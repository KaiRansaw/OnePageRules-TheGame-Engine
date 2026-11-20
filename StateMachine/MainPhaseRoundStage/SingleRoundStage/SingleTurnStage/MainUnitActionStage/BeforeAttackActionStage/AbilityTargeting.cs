using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #100 #2 — resolves an ability's <see cref="TargetSelector"/> into the units the acting unit may
    /// pick: applies the affinity, range, line-of-sight, and required-token filters. This is the first code
    /// to actually READ a TargetSelector (the record was inert before #2). <see cref="BeforeAttackActionStage"/>
    /// uses it both to decide whether an ability has enough valid targets to offer and to populate the
    /// target-selection request.
    /// </summary>
    public static class AbilityTargeting
    {
        /// <summary>
        /// The units <paramref name="actingUnit"/> may pick for an ability with <paramref name="selector"/>.
        /// Self short-circuits to the bearer. Friend/Foe/Any scan armies by allegiance, then filter by
        /// closest-model range, an optional line-of-sight requirement, and an optional required token.
        /// Off-battlefield units (reserves / embarked / wiped out) are never candidates.
        /// </summary>
        public static List<DataBinding<UnitData>> EligibleTargets(
            DataBinding<UnitData> actingUnit, TargetSelector selector, IGameContext gameContext)
        {
            if (selector.TargetAffinity == ETargetAffinity.Self)
            {
                return new List<DataBinding<UnitData>> { actingUnit };
            }

            PlayerID actingPlayer = actingUnit.PlayerID();
            IReadOnlyList<PlayerID> allied = AlliedPlayers(actingPlayer, gameContext);
            IReadOnlyList<ITerrain>? terrain = selector.RequireLineOfSight
                ? gameContext.TableState.Terrain.Objects.ToList()
                : null;

            IReadOnlyList<IUnit> relays = BuffRelaysInReach(actingUnit, selector, allied, gameContext);
            IReadOnlyList<(IUnit Relay, Rules.Definitions.RuleOperation.EnableBuffRelay Offer)> heroRelays =
                HeroBuffRelays(actingUnit, selector, allied, gameContext);

            List<DataBinding<UnitData>> eligible = new List<DataBinding<UnitData>>();

            foreach (ArmyData army in gameContext.GameDataStore.GetAllValues<ArmyData>())
            {
                if (!AffinityMatches(selector.TargetAffinity, allied.Contains(army.PlayerID)))
                {
                    continue;
                }

                foreach (DataBinding<UnitData> candidateBinding in army.UnitBindings)
                {
                    UnitData candidate = candidateBinding.GetValue();

                    if (!candidate.GetIsOnBattlefield())
                    {
                        continue;
                    }

                    // #197 Extended Buff Range: a candidate out of the ability's own reach may still be
                    // picked "as if the user were in the relay's position" - the relay relaxes RANGE only;
                    // every other filter below applies to the candidate unchanged.
                    float distance = UnitCompareUtilities.MinDistanceBetweenUnits(actingUnit.GetValue(),
                        candidate, out IModel? nearActing, out IModel? nearCandidate, includeVertical: false);
                    if (distance > selector.RangeInches
                        && !ReachableViaRelay(relays, candidate, selector.RangeInches)
                        && HeroRelayFor(heroRelays, candidate, gameContext) == null)
                    {
                        continue;
                    }

                    if (selector.RequiredToken != null
                        && !candidate.Tokens.HasToken(selector.RequiredToken.Value))
                    {
                        continue;
                    }

                    // The candidate qualifies if IT or any of its MODELS carries the rule. A joined hero
                    // keeps its own rules on its model (#006/#093) - Caster is the common case, and the
                    // round-start token grant already scans models the same way - so a unit-only scan would
                    // make "pick an enemy with Caster" (#197 P6's Casting Debuff) blind to every hero caster.
                    if (selector.RequiredRule != null && !HasRule(candidate, selector.RequiredRule))
                    {
                        continue;
                    }

                    if (selector.RequireLineOfSight && nearActing != null && nearCandidate != null
                        && !LineOfSightUtilities.HasLineOfSight(SightEnd.Of(nearActing), SightEnd.Of(nearCandidate), terrain))
                    {
                        continue;
                    }

                    eligible.Add(candidateBinding);
                }
            }

            return eligible;
        }

        /// <summary>
        /// #197 Extended Buff Range: the OTHER friendly units whose <see cref="Rules.Definitions.RuleOperation.EnableBuffRelay"/>
        /// offer reaches the acting unit right now - each is a position the pick may be measured from.
        /// Only a FRIENDLY pick can be relayed (the rule relays "buffs"), and only a sight-free one: a
        /// relay lends position, not eyes, and no corpus Friend-pick requires line of sight, so the
        /// combination is gated out rather than guessed at (grow on demand).
        /// </summary>
        private static IReadOnlyList<IUnit> BuffRelaysInReach(DataBinding<UnitData> actingUnit,
            TargetSelector selector, IReadOnlyList<PlayerID> allied, IGameContext gameContext)
        {
            if (selector.TargetAffinity != ETargetAffinity.Friend || selector.RequireLineOfSight)
            {
                return Array.Empty<IUnit>();
            }

            List<IUnit>? relays = null;
            UnitData actor = actingUnit.GetValue();

            foreach (ArmyData army in gameContext.GameDataStore.GetAllValues<ArmyData>())
            {
                if (!allied.Contains(army.PlayerID))
                {
                    continue;
                }

                foreach (DataBinding<UnitData> binding in army.UnitBindings)
                {
                    UnitData other = binding.GetValue();

                    // "Another friendly unit": never the acting unit itself, and never one off the table.
                    if (ReferenceEquals(other, actor) || other.ID.Equals(actor.ID)) continue;
                    if (!other.GetIsAlive() || !other.GetIsOnBattlefield()) continue;

                    float distance = UnitCompareUtilities.MinDistanceBetweenUnits(actor, other,
                        out _, out _, includeVertical: false);

                    foreach (Rules.Definitions.RuleOperation.EnableBuffRelay offer in
                             Rules.Dispatch.CapabilityRuleQueries.BuffRelayOffers(other, gameContext.RuleEvaluator))
                    {
                        if (offer.TargetWithinInches <= 0f && distance <= offer.RangeInches)
                        {
                            (relays ??= new List<IUnit>()).Add(other);
                            break;
                        }
                    }
                }
            }

            return (IReadOnlyList<IUnit>?)relays ?? Array.Empty<IUnit>();
        }

        /// <summary>The token a Quest "Only one use per game." Extended Buff Range leaves on the relay unit once used.</summary>
        public static readonly TokenType BuffRelayUsed = new("ExtendedBuffRangeUsed");

        /// <summary>
        /// #468 Extended Buff Range, 3.6.0: when the acting unit's pick lands on <paramref name="candidate"/> only
        /// through a relay, the relay unit used (so a once-per-game relay can be spent); null when the candidate
        /// is in the ability's own range or no 3.6.0 relay reaches it.
        /// </summary>
        public static IUnit? HeroRelayUsedFor(DataBinding<UnitData> actingUnit, TargetSelector selector,
            UnitData candidate, IGameContext gameContext)
        {
            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(actingUnit.GetValue(), candidate,
                out _, out _, includeVertical: false);
            if (distance <= selector.RangeInches) return null;

            IReadOnlyList<PlayerID> allied = AlliedPlayers(actingUnit.PlayerID(), gameContext);
            (IUnit Relay, Rules.Definitions.RuleOperation.EnableBuffRelay Offer)? hit =
                HeroRelayFor(HeroBuffRelays(actingUnit, selector, allied, gameContext), candidate, gameContext);
            return hit?.Relay;
        }

        /// <summary>
        /// #468 Extended Buff Range, 3.6.0 wording. The acting unit must be (GF / AoF) or be within the offer's
        /// <c>HeroWithinInches</c> of (Firefight, Skirmish, Quest) a friendly unit R with the rule, and must hold a
        /// Hero ("then that Hero may use special rules that allow it to pick friendly units within 12\""). Only
        /// friendly, sight-free 12" picks are relayed - "except for spells" holds by construction, spells never
        /// come through here. Returns each such R with its offer; the far unit is checked per candidate.
        /// </summary>
        private static IReadOnlyList<(IUnit Relay, Rules.Definitions.RuleOperation.EnableBuffRelay Offer)> HeroBuffRelays(
            DataBinding<UnitData> actingUnit, TargetSelector selector, IReadOnlyList<PlayerID> allied,
            IGameContext gameContext)
        {
            if (selector.TargetAffinity != ETargetAffinity.Friend || selector.RequireLineOfSight
                || Math.Abs(selector.RangeInches - 12f) > 0.01f)
            {
                return Array.Empty<(IUnit, Rules.Definitions.RuleOperation.EnableBuffRelay)>();
            }

            UnitData actor = actingUnit.GetValue();
            if (!HoldsAHero(actor))
            {
                return Array.Empty<(IUnit, Rules.Definitions.RuleOperation.EnableBuffRelay)>();
            }

            List<(IUnit, Rules.Definitions.RuleOperation.EnableBuffRelay)>? found = null;
            foreach (ArmyData army in gameContext.GameDataStore.GetAllValues<ArmyData>())
            {
                if (!allied.Contains(army.PlayerID)) continue;

                foreach (DataBinding<UnitData> binding in army.UnitBindings)
                {
                    UnitData relay = binding.GetValue();
                    // The acting unit is the relay itself in GF / AoF ("has a Hero in it") - it is acting, so it
                    // is on the table whatever its bookkeeping says.
                    if (!relay.ID.Equals(actor.ID) && (!relay.GetIsAlive() || !relay.GetIsOnBattlefield())) continue;

                    foreach (Rules.Definitions.RuleOperation.EnableBuffRelay offer in
                             Rules.Dispatch.CapabilityRuleQueries.BuffRelayOffers(relay, gameContext.RuleEvaluator))
                    {
                        if (offer.TargetWithinInches <= 0f) continue;
                        if (offer.OncePerGame && relay.Tokens.HasToken(BuffRelayUsed)) continue;

                        bool heroInReach = offer.HeroWithinInches <= 0f
                            ? relay.ID.Equals(actor.ID)
                            : relay.ID.Equals(actor.ID) || UnitCompareUtilities.MinDistanceBetweenUnits(actor, relay,
                                out _, out _, includeVertical: false) <= offer.HeroWithinInches;
                        if (!heroInReach) continue;

                        (found ??= new()).Add((relay, offer));
                        break;
                    }
                }
            }

            return (IReadOnlyList<(IUnit, Rules.Definitions.RuleOperation.EnableBuffRelay)>?)found
                ?? Array.Empty<(IUnit, Rules.Definitions.RuleOperation.EnableBuffRelay)>();
        }

        // "If THIS unit [the far one, which has the rule] is within 24\" of ANOTHER friendly unit with this rule".
        private static (IUnit Relay, Rules.Definitions.RuleOperation.EnableBuffRelay Offer)? HeroRelayFor(
            IReadOnlyList<(IUnit Relay, Rules.Definitions.RuleOperation.EnableBuffRelay Offer)> relays,
            UnitData candidate, IGameContext gameContext)
        {
            if (relays.Count == 0) return null;
            if (Rules.Dispatch.CapabilityRuleQueries.BuffRelayOffers(candidate, gameContext.RuleEvaluator)
                    .All(o => o.TargetWithinInches <= 0f))
            {
                return null;
            }

            foreach ((IUnit relay, Rules.Definitions.RuleOperation.EnableBuffRelay offer) in relays)
            {
                if (relay.ID.Equals(candidate.ID)) continue;
                if (UnitCompareUtilities.MinDistanceBetweenUnits(relay, candidate, out _, out _,
                        includeVertical: false) <= offer.TargetWithinInches)
                {
                    return (relay, offer);
                }
            }

            return null;
        }

        // A joined hero, or a lone Hero unit.
        private static bool HoldsAHero(UnitData unit) =>
            unit.HeroAttachment != null
            || unit.RuleDefinitions.Any(r => r.Definition == Rules.Dispatch.CoreRuleCatalog.Hero)
            || unit.Models.Any(m => m.RuleDefinitions.Any(r => r.Definition == Rules.Dispatch.CoreRuleCatalog.Hero));

        private static bool ReachableViaRelay(IReadOnlyList<IUnit> relays, UnitData candidate,
            float rangeInches)
        {
            foreach (IUnit relay in relays)
            {
                if (UnitCompareUtilities.MinDistanceBetweenUnits(relay, candidate,
                        out _, out _, includeVertical: false) <= rangeInches)
                {
                    return true;
                }
            }

            return false;
        }

        // Matches on the canonical name OR the name the book asked for, so a book alias ("Wizard" for
        // Caster) still satisfies the filter - the same pair the rule-resolution path compares.
        private static bool HasRule(IUnit unit, string ruleName)
        {
            bool Matches(IEnumerable<Rules.Dispatch.ResolvedRule> rules) => rules.Any(r =>
                string.Equals(r.Definition.Name, ruleName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.RequestedName, ruleName, StringComparison.OrdinalIgnoreCase));

            return Matches(unit.RuleDefinitions) || unit.Models.Any(m => Matches(m.RuleDefinitions));
        }

        private static bool AffinityMatches(ETargetAffinity affinity, bool isAllied) => affinity switch
        {
            ETargetAffinity.Friend => isAllied,
            ETargetAffinity.Foe => !isAllied,
            ETargetAffinity.Any => true,
            _ => false, // Self is handled by the caller before we get here.
        };

        private static IReadOnlyList<PlayerID> AlliedPlayers(PlayerID player, IGameContext gameContext)
        {
            TeamData? team = gameContext.GameDataStore.GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(player));
            return team?.Players ?? new List<PlayerID> { player };
        }
    }
}
