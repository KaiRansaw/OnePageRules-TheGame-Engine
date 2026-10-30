using System.Collections.Generic;
using System.Linq;
using FDG.Utilities;

namespace FDG.Rules.Dispatch;

/// <summary>
/// The stat-divergence seams for a joined Hero (#006). A merged unit is one <see cref="UnitData"/> for
/// all the per-unit machinery; these three helpers are the only points where the hero's own stats diverge
/// from the rank and file, each read by exactly one resolution stage:
/// <list type="bullet">
///   <item><see cref="GetMoraleQuality"/> — <c>DetermineMoraleSaveNeededStage</c> / wound-driven morale
///         (slice C): the unit tests morale at the hero's Quality while the hero lives.</item>
///   <item><see cref="GetSaveDefense"/> — <c>DetermineSaveRollsNeededStage</c> (slice D): the hero uses
///         the unit's Defense until it is the sole survivor, then its own.</item>
///   <item><see cref="GetAttackQuality"/> — <c>DetermineHitRollNeededStage</c> (slice E): a weapon batch
///         owned solely by the hero fires at the hero's Quality.</item>
/// </list>
///
/// All three helpers are implemented and wired into their stages; HeroStatRulesTests pins the behavior.
/// </summary>
public static class HeroStatRules
{
    /// <summary>
    /// The Quality a unit tests morale at. With a living joined hero this is the hero's Quality (the rule's
    /// "may take morale tests on behalf of the unit"); otherwise the unit's own.
    /// <para>
    /// This overload sees only the unit itself, so it answers for the joined-hero half of the rule alone.
    /// Prefer <see cref="GetMoraleQuality(UnitData, ITableState)"/> wherever the table is in scope: OPR
    /// 3.6.0 added a second, relational half (the big-Hero aura) that a unit-only read cannot see. Kept for
    /// callers with no table - and for the tests that pin the joined-hero behaviour on its own.
    /// </para>
    /// </summary>
    public static int GetMoraleQuality(UnitData unit)
    {
        if (unit.HeroAttachment != null)
        {
            IModel? hero = unit.GetHeroModel();
            if (hero != null && hero.GetIsAlive())
            {
                return unit.HeroAttachment.Quality;
            }
        }

        return unit.Quality;
    }

    /// <summary>
    /// The Quality a unit tests morale at, including the OPR 3.6.0 big-Hero aura (#409):
    /// "<i>The Hero's unit, and units within 6" of friendly Heroes with Tough(9) or higher, may use the
    /// Hero's Quality for Morale Tests as long as the Hero isn't Shaken.</i>"
    ///
    /// <para>The joined-hero half is unchanged and still comes from the unit's own attachment. The aura is
    /// new: a nearby big Hero - one on the table in its own right, since Tough(9)+ is far past
    /// <c>HeroJoinResolver.MaxHeroToughForJoin</c> and such a Hero can never join a unit - lends its
    /// Quality to everything friendly within 6".</para>
    ///
    /// <para><b>"May" is resolved as "takes the better of the two."</b> Quality is a roll-at-or-above
    /// target, so a lower number is strictly better and there is no situation in which a unit would decline
    /// the Hero's Quality. Making it a prompt would add a decision point to every morale test in the game
    /// to buy an option nobody takes. Recorded in WorkItems/409 rather than cut silently.</para>
    ///
    /// <para>Measured unit-edge to unit-edge like every other distance in the engine, ignoring height, and
    /// only from living models - a dead Hero lends nothing, and a Shaken one is explicitly excluded by the
    /// rule. A Hero in reserve has no table presence and is skipped.</para>
    /// </summary>
    public static int GetMoraleQuality(UnitData unit, ITableState? tableState)
    {
        int quality = GetMoraleQuality(unit);
        if (tableState == null) return quality;

        foreach (IUnit candidate in tableState.Units.Objects)
        {
            if (ReferenceEquals(candidate, unit)) continue;
            if (!candidate.GetIsAlive()) continue;
            if (candidate.Tokens.HasToken(Foundation.TokenType.Shaken)) continue;
            if (ReserveRules.IsInReserve(candidate)) continue;
            if (!ITeamExtensions.AreAllied(tableState.Teams.Objects, unit.PlayerID, candidate.PlayerID)) continue;
            if (!IsBigHero(candidate)) continue;

            // A better (lower) Quality is the only reason to look further.
            if (candidate.Quality >= quality) continue;

            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(
                unit, candidate, out _, out _, includeVertical: false);
            if (distance <= BigHeroMoraleAuraInches)
            {
                quality = candidate.Quality;
            }
        }

        return quality;
    }

    /// <summary>How far a Tough(9)+ Hero lends its Quality for Morale Tests (OPR 3.6.0).</summary>
    public const float BigHeroMoraleAuraInches = 6f;

    /// <summary>The Tough value at which a Hero starts projecting the 3.6.0 morale aura.</summary>
    public const int BigHeroMinTough = 9;

    /// <summary>
    /// Whether a unit is a Hero carrying Tough(<see cref="BigHeroMinTough"/>) or higher. Reads the Tough
    /// rule's argument directly, the same argument-only read <c>HeroJoinResolver.TryGetToughValue</c>
    /// makes - the aura is about the Hero's printed Tough, not a mid-game wound total.
    /// </summary>
    private static bool IsBigHero(IUnit unit)
    {
        bool isHero = false;
        int tough = 0;

        foreach (ResolvedRule rule in unit.RuleDefinitions)
        {
            if (rule.Definition == CoreRuleCatalog.Hero)
            {
                isHero = true;
            }
            else if (rule.Definition == CoreRuleCatalog.Tough
                && rule.Arguments.Count > 0
                && rule.Arguments[0] is Foundation.RuleArgument.Int toughArg)
            {
                tough = toughArg.Value;
            }
        }

        return isHero && tough >= BigHeroMinTough;
    }

    /// <summary>
    /// The Defense a unit's models save at. A joined hero uses the unit's Defense until every other model
    /// is dead; once the hero is the sole survivor the unit saves at the hero's own Defense.
    /// </summary>
    public static int GetSaveDefense(UnitData unit)
    {
        if (unit.HeroAttachment != null)
        {
            List<IModel> living = unit.Models.Where(model => model.GetIsAlive()).ToList();
            if (living.Count == 1 && living[0].ID == unit.HeroAttachment.HeroModelId)
            {
                return unit.HeroAttachment.Defense;
            }
        }

        return unit.Defense;
    }

    /// <summary>
    /// The Quality a weapon batch hits at. A batch whose living owners are exactly the hero fires at the
    /// hero's Quality; a batch the rank and file (also) carry stays at the unit's Quality (the deferred
    /// same-weapon-collision case).
    /// </summary>
    public static int GetAttackQuality(UnitData unit, IWeapon weaponType)
    {
        if (unit.HeroAttachment != null)
        {
            IReadOnlyList<IModel> owners = LivingWeaponBatchOwners(unit, weaponType);

            // Hero's own Quality only when this batch is owned by the hero alone. If a rank-and-file model
            // also carries a matching weapon (the deferred same-weapon-pooling case), it stays unit Quality.
            if (owners.Count > 0 && owners.All(model => model.ID == unit.HeroAttachment.HeroModelId))
            {
                return unit.HeroAttachment.Quality;
            }
        }

        return unit.Quality;
    }

    /// <summary>
    /// The living models in <paramref name="unit"/> carrying a weapon matching <paramref name="weaponType"/>
    /// (by <see cref="WeaponComparer"/>) — the owners of a weapon batch. The hit stages pass these as the
    /// batch's per-model rule contributors under <see cref="EModelRuleScope.AllOwners"/>, so a per-model rule
    /// fires only when every owner shares it (#093): a hero-only batch fires the hero's own rules; a
    /// homogeneous squad's shared per-model rule fires once; a weapon the rank and file also carry doesn't
    /// leak the hero's rules onto their pooled shots. Also the source of the slice-E attack-Quality gate.
    /// </summary>
    public static IReadOnlyList<IModel> LivingWeaponBatchOwners(UnitData unit, IWeapon weaponType)
    {
        // #191 search perf: this was Where(...).ToList() with a fresh comparer, an outer closure and a
        // LINQ Any() over every model's weapons - ~2.5 KB of garbage per call, and the Tactician's
        // volley estimate calls it twice per volley on units of twenty-plus models (measured: 55% of
        // the combat estimate's allocation). Same models, same order; the comparer is stateless, and
        // the empty answer - a batch nobody living carries - needs no list at all.
        IReadOnlyList<IModel> models = unit.Models;
        List<IModel>? owners = null;
        for (int i = 0; i < models.Count; i++)
        {
            IModel model = models[i];
            if (!model.GetIsAlive()) continue;

            IReadOnlyList<Weapon> weapons = model.Weapons;
            bool carriesBatch = false;
            for (int w = 0; w < weapons.Count; w++)
            {
                if (!s_weaponComparer.Equals(weapons[w], weaponType)) continue;
                carriesBatch = true;
                break;
            }
            if (!carriesBatch) continue;

            owners ??= new List<IModel>(models.Count - i);
            owners.Add(model);
        }
        return owners ?? (IReadOnlyList<IModel>)Array.Empty<IModel>();
    }

    /// <summary>Stateless (it only reads the two weapons), so one instance serves every comparison.</summary>
    private static readonly WeaponComparer s_weaponComparer = new WeaponComparer();

    /// <summary>
    /// The living models of a defending unit — the Subject-seat counterpart to
    /// <see cref="LivingWeaponBatchOwners"/>. The defensive dispatch sites (#183) pass these as the
    /// per-model rule contributors under <see cref="EModelRuleScope.AnyOwner"/>, so a joined hero's
    /// relocated defensive rules (Evasive, Resistance, Fortified, ...) become VISIBLE to the evaluator —
    /// and the #163 trace — after the merge. The rule's own <see cref="Definitions.Condition.AllModelsHaveThisRule"/>
    /// gate then decides whether it actually applies (only when every living model has it — i.e. the hero
    /// is the sole survivor, or the whole unit shares it). For a non-hero unit no model carries per-model
    /// rules, so unioning them adds nothing and behaviour is unchanged.
    /// </summary>
    public static IReadOnlyList<IModel> LivingModels(IUnit unit)
    {
        // Pre-sized loop: called per unit per Tactician evaluation, and the Where().ToList() growth
        // showed in the profile (#191 step 3, 2026-09-03).
        IReadOnlyList<IModel> models = unit.Models;
        var living = new List<IModel>(models.Count);
        for (int i = 0; i < models.Count; i++)
        {
            if (models[i].GetIsAlive()) living.Add(models[i]);
        }
        return living;
    }
}
