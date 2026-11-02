using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Quest;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// GF: Star Quest hero rules (Core Rules **v2.1.0**): power, stat tests, resting, skill actions, the
    /// common skills (Push, Scurry, revive) and the class skills that the engine can resolve.
    ///
    /// <para>Power: a hero starts the mission at full power. The costs are
    /// <see cref="ExtraActionPower"/>, <see cref="RevivePower"/>, <see cref="PassStatTestPower"/>,
    /// <see cref="IgnoreShakenPower"/> and <see cref="FailedSkillPower"/>. Spending more than it has costs
    /// the hero a wound per missing point (exhaustion).</para>
    /// </summary>
    public static class QuestActions
    {
        // -- Power costs ---------------------------------------------------------------------------------
        // Named rather than written as literals at each site because the cost appears BOTH in the rule and
        // in the player-facing label, and under v2.0.1 those were separate 2s in five files. #414.
        //
        // #418: and now per SYSTEM, because the two Quest books are a generation apart - Star Quest's
        // 2.1.0 charges 3 power for an extra action and a revive where AoF: Quest's 2.0.1 charges 2.
        // The numbers live in QuestRulesProfile; these members are the Star Quest values, kept for the
        // places that have no game context to ask (a catalog built at class-load time) and as the
        // documented default. ANY SITE THAT HAS A CONTEXT SHOULD ASK THE PROFILE.

        /// <summary>Star Quest: "Heroes may spend 3 Power to use any one additional action."
        /// AoF: Quest charges 2 - see <see cref="Rules.Dispatch.QuestRulesProfile"/>.</summary>
        public const int ExtraActionPower = 3;

        /// <summary>Star Quest: "Heroes may spend 3 Power to revive other Unconscious Heroes."
        /// AoF: Quest charges 2.</summary>
        public const int RevivePower = 3;

        /// <summary>"Heroes may spend 2 Power to count failed Str/Dex/Wil tests as passed." The same in
        /// both books.</summary>
        public const int PassStatTestPower = 2;

        /// <summary>"Heroes may spend 2 Power to ignore being Shaken from failed Morale Tests." The same
        /// in both books.</summary>
        public const int IgnoreShakenPower = 2;

        /// <summary>
        /// "Heroes using Hero Skill actions may spend 1 Power if they fail their stat test to count it as
        /// passed." One power in both books - but 2.1.0 made the payment OPTIONAL and the skill FAIL if
        /// declined, where 2.0.1 forces it and resolves the skill either way. #418 implements both; which
        /// one applies is <c>QuestRulesProfile.FailedSkillPaymentIsOptional</c>.
        /// </summary>
        public const int FailedSkillPower = 1;

        /// <summary>The Quest rules this game is played by (#418).</summary>
        public static Rules.Dispatch.QuestRulesProfile Profile(IGameContext context) =>
            Rules.Dispatch.QuestRulesProfile.For(context);

        public const string RestOption = "Rest";
        public const string SkillOption = "Skill";
        public static readonly string ExtraActionOption = $"Extra Action ({ExtraActionPower} power)";
        public const string FreeActionOption = "Free Action";
        public static readonly string ExtraSkillOption = $"Extra Skill ({ExtraActionPower} power)";
        public const string FeatSuffix = " (once per game)";
        /// <summary>Internal: the hero went down at the start of its activation.</summary>
        public const string EndActivationOption = "__end_activation";

        private static readonly TextColor Blue = new(130, 200, 255, 255);
        private static readonly TextColor Gold = new(255, 215, 0, 255);

        public static bool IsQuestGame(IGameContext context) => GameSystemInfo.IsQuestMode(context.Settings.GameMode);

        public static QuestHero? Sheet(UnitData unit) => QuestHero.FromJson(unit.QuestHeroJson);

        /// <summary>Star Quest movement multiplier: Crippled heroes and Bad weather halve movement (they stack).</summary>
        public static float MoveFactor(IGameContext context, IUnit unit)
        {
            if (!IsQuestGame(context)) return 1f;
            float factor = 1f;
            if (unit is UnitData { IsQuestHero: true } hero && QuestTreasure.Has(hero, QuestTreasure.Crippled)) factor *= 0.5f;
            if (QuestChallenges.HalvesMovement(context)) factor *= 0.5f;
            return factor;
        }

        public static string StatName(int type) => type switch { 0 => "Str", 1 => "Dex", 2 => "Wil", _ => "" };

        public static int StatValue(QuestHero hero, int type) => type switch
        {
            0 => hero.Strength, 1 => hero.Dexterity, 2 => hero.Willpower, _ => 7,
        };

        // ── Power ────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Spends power. What the hero can't pay comes off as wounds (exhaustion).</summary>
        public static async Task SpendPower(IGameContext context, UnitData hero, int amount, string why)
        {
            if (amount <= 0) return;
            int paid = Math.Min(hero.QuestPower, amount);
            hero.QuestPower -= paid;
            int missing = amount - paid;
            context.Log($"{hero.Name} spends {amount} power ({why}) - {hero.QuestPower} left.");
            if (paid > 0 && QuestTreasure.Has(hero, QuestTreasure.Diseased))
            {
                await context.Announce($"{hero.Name} is Diseased - takes {paid} wound{(paid == 1 ? "" : "s")} for the power lost",
                    new TextColor(255, 120, 90, 255), EBannerTier.Toast);
                await CasualtyPresentation.ApplyUnitWounds(context, hero, paid);
            }
            if (missing > 0)
            {
                await context.Announce($"{hero.Name} is exhausted - takes {missing} wound{(missing == 1 ? "" : "s")}",
                    new TextColor(255, 120, 90, 255), EBannerTier.Toast);
                await CasualtyPresentation.ApplyUnitWounds(context, hero, missing);
            }
        }

        public static void RestorePower(UnitData hero, int amount)
        {
            QuestHero? sheet = Sheet(hero);
            int max = sheet?.Power ?? hero.QuestPower;
            hero.QuestPower = Math.Clamp(hero.QuestPower + amount, 0, Math.Max(max, hero.QuestPower));
        }

        // ── Tests ────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Game Master testing tool: a hero whose ID is in here has its next Str/Dex/Wil test forced to a
        /// pass (1) or a fail (-1); the entry is used up by that one test. Only the Game Master panel ever
        /// writes to it, and only from the engine side, so no lock is needed.
        /// </summary>
        public static readonly Dictionary<UnitID, int> ForcedNextTest = new();

        /// <summary>One stat test (Str/Dex/Wil): a die needing the stat, plus any stored "+X to the next stat
        /// roll". A failed test may be turned into a pass for 2 power when <paramref name="offerPower"/>.</summary>
        public static async Task<bool> StatTest(IGameContext context, UnitData hero, int statType, string what, bool offerPower)
        {
            QuestHero? sheet = Sheet(hero);
            int needed = sheet == null ? 4 : StatValue(sheet, statType);

            // #407: each contribution is collected with the thing that caused it, so the test's hover can
            // say "Impaired -1   Broken Arm -1" instead of one anonymous "penalty -2". A hero failing Str
            // tests could not previously tell a condition from an injury from a random event, which is the
            // same complaint ("i am unsure if the modifiers are correctly being applied") that drove the
            // roll-modifier work - stat tests are simply where a Star Quest hero spends most of its time.
            // Sign convention matches the roll chips: positive helps (it lowers the target below).
            var modifiers = new List<Rules.Dispatch.GrantedModifier>();

            int stored = hero.QuestStatBonus;
            hero.QuestStatBonus = 0;
            if (stored != 0)
            {
                // Deliberately generic: QuestStatBonus is a bare int on the hero with no room for a
                // source, written by Concentrate, Guidance, Inspire and one treasure item. Naming it would
                // need a field on UnitData (and so a save-format change) to carry what granted it; the
                // unit card already shows "(next stat roll +X)" separately. Recorded on #407.
                modifiers.Add(new Rules.Dispatch.GrantedModifier("skill bonus", stored));
            }

            if (QuestTreasure.Has(hero, QuestTreasure.Impaired))
            {
                modifiers.Add(new Rules.Dispatch.GrantedModifier(QuestTreasure.Impaired, -1));
            }

            (int injuryPenalty, string? injury) = QuestMission.InjuryPenaltyNamed(hero, statType);
            if (injuryPenalty != 0)
            {
                modifiers.Add(new Rules.Dispatch.GrantedModifier(injury, -injuryPenalty));
            }

            (int eventDelta, string? eventName) =
                QuestChallenges.EventStatBonusNamed(context, hero, statType);
            if (eventDelta != 0)
            {
                modifiers.Add(new Rules.Dispatch.GrantedModifier(eventName, eventDelta));
            }

            int bonus = modifiers.Sum(modifier => modifier.Delta);
            int target = Math.Clamp(needed - bonus, 2, 6);
            IDiceResults roll = context.DiceRoller.RollDecisive(6);
            bool passed = roll.AtOrAbove(target) >= 1f;
            string? forcedNote = null;
            if (ForcedNextTest.Remove(hero.ID, out int forced) && forced != 0)
            {
                passed = forced > 0;
                forcedNote = passed ? "Passed (Game Master)" : "Failed (Game Master)";
            }
            await context.Presenter.Present(DiceRolledBeat.FromDecisive(roll, target,
                $"{hero.Name} - {StatName(statType)} test ({what})", forcedNote ?? (passed ? "Passed" : "Failed"),
                // #407: one chip per source, headed by the base stat - the same shape the hit, save and
                // morale rolls use, so a player reads every roll in the game the same way.
                modifierTags: modifiers.Count > 0
                    ? new List<string> { $"{StatName(statType)} {needed}+" }
                        .Concat(modifiers.Select(modifier => modifier.Describe())).ToList()
                    : null));
            if (passed) return true;
            if (!offerPower) return false;

            bool spend = await context.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(hero.PlayerID,
                $"{hero.Name} failed the {StatName(statType)} test. Spend {Profile(context).PassStatTestPower} power to pass it anyway? ({hero.QuestPower} power left)",
                defaultAnswer: hero.QuestPower >= 2, displayName: $"{hero.Name}: spend power?"));
            if (!spend) return false;
            await SpendPower(context, hero, Profile(context).PassStatTestPower,
                $"to pass the {StatName(statType)} test");
            return true;
        }

        /// <summary>
        /// A hero skill: test its stat. Returns whether the skill's effect should resolve.
        /// <para>
        /// **v2.1.0 changed this, and it is a real gameplay change.** The book now reads: "If passed the
        /// effect is resolved, and if failed you **may** spend 1 Power to count as having passed it
        /// instead." Under v2.0.1 the payment was forced and the skill worked either way, so a failed
        /// roll cost a hero power it had no say over and could never fizzle.
        /// </para>
        /// <para>
        /// So a decline now returns false and the caller must NOT resolve the effect. This is also the
        /// rules-level answer to QA 3.10 ("never auto-spend power, always ask") - which was filed as a
        /// preference and turns out to be what the book requires.
        /// </para>
        /// <para>
        /// The default answer is yes only while the hero can actually afford it. Spending power it does
        /// not have costs a wound per missing point, and under the old forced payment that could put a
        /// hero down for failing a roll it was never asked about. Same shape as the stat-test offer above.
        /// </para>
        /// </summary>
        public static async Task<bool> HeroSkillTest(IGameContext context, UnitData hero, int statType, string skill)
        {
            if (statType is < 0 or > 2) return true;
            if (await StatTest(context, hero, statType, skill, offerPower: false)) return true;

            Rules.Dispatch.QuestRulesProfile profile = Profile(context);
            int cost = profile.FailedSkillPower;

            // #418: AoF: Quest is on the 2.0.1 wording - "if failed you must first spend 1 power and
            // then the effect is resolved" - so there is nothing to ask and the skill cannot fail. Star
            // Quest's 2.1.0 reversed that (#414, Q3): the payment is optional and declining it means the
            // skill does nothing, which is what lets a hero skill fail at all.
            if (!profile.FailedSkillPaymentIsOptional)
            {
                await SpendPower(context, hero, cost, $"failed {skill} test");
                return true;
            }

            bool spend = await context.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(
                hero.PlayerID,
                $"{hero.Name} failed the {skill} test. Spend {cost} power to count it as passed? "
                + $"({hero.QuestPower} power left)",
                defaultAnswer: hero.QuestPower >= cost,
                displayName: $"{hero.Name}: spend power?"));
            if (!spend)
            {
                await Toast(context, $"{hero.Name}: {skill} fails.");
                return false;
            }
            await SpendPower(context, hero, cost, $"failed {skill} test");
            return true;
        }

        // ── Shared effects ───────────────────────────────────────────────────────────────────────────────

        public static float Distance(IUnit a, IUnit b) =>
            UnitCompareUtilities.MinDistanceBetweenUnits(a, b, out _, out _, includeVertical: false);

        public static List<UnitData> Units(IGameContext context) =>
            context.GameDataStore.GetAllValues<ArmyData>().SelectMany(a => a.UnitBindings.Select(b => b.GetValue())).ToList();

        public static bool AreFriends(IGameContext context, IUnit a, IUnit b)
        {
            if (a.PlayerID.Equals(b.PlayerID)) return true;
            TeamData? team = context.GameDataStore.GetAllValues<TeamData>().FirstOrDefault(t => t.IsPlayerOnTeam(a.PlayerID));
            return team != null && team.IsPlayerOnTeam(b.PlayerID);
        }

        /// <summary>Restores up to <paramref name="wounds"/> wounds to a unit's living models.</summary>
        public static void Heal(IUnit unit, float wounds, IGameContext? context = null)
        {
            // Devoted Aura: the next time the unit restores wounds, it restores two more.
            if (context != null && wounds > 0f && QuestFlags.Take(context, unit, QuestFlags.DevotedAura) != null) wounds += 2f;
            foreach (IModel model in unit.Models)
            {
                if (wounds <= 0f) break;
                if (!model.GetIsAlive() || model.WoundsDealt <= 0f) continue;
                float h = MathF.Min(wounds, model.WoundsDealt);
                ((ModelData)model).DealWounds(-h);
                wounds -= h;
            }
        }

        /// <summary>Revives an unconscious hero: it restores 3 wounds where it fell.</summary>
        public static void Revive(UnitData hero, int wounds = 3)
        {
            foreach (DataBinding<ModelData> binding in hero.ModelBindings)
            {
                ModelData model = binding.GetValue();
                int total = (int)MathF.Round(model.TotalWounds);
                model.SetMaxWounds(total);
                int left = Math.Min(total, wounds);
                if (total - left > 0) model.DealWounds(total - left);
            }
            foreach (TokenType t in new[] { TokenType.Shaken, TokenType.Fatigued })
                if (hero.Tokens.GetTokenCount(t) > 0) hero.Tokens.RemoveTokens(t, hero.Tokens.GetTokenCount(t));
        }

        /// <summary>
        /// A forced shove: every living model of <paramref name="target"/> slides <paramref name="inches"/>
        /// straight away from (or straight toward) <paramref name="pusher"/>.
        ///
        /// <para>A push is not a move the player steers - the rule says "directly away" - so this moves the
        /// models itself instead of raising a movement request. That is also what makes it work at all: a
        /// pushed unit put through the normal move checks has to end outside 1" of every enemy and obeys the
        /// forced-charge rule, and a unit being shoved by a hero standing right next to it has no legal spot
        /// left, so every square answered "moves through an enemy unit" and the push could never be
        /// confirmed.</para>
        /// </summary>
        public static async Task Shove(IGameContext context, IUnit pusher, UnitData target, float inches, bool away = true)
        {
            if (inches <= 0f || !target.GetIsAlive()) return;

            (float fx, float fz) = Centre(pusher);
            (float tx, float tz) = Centre(target);
            float dx = tx - fx, dz = tz - fz;
            float len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < 0.01f) { dx = 1f; dz = 0f; len = 1f; }   // exactly on top of each other: shove along +x
            if (!away) { dx = -dx; dz = -dz; }
            dx = dx / len * inches;
            dz = dz / len * inches;

            float w = HordePlacement.TableWidth, h = HordePlacement.TableHeight;
            foreach (IModel model in target.Models)
            {
                if (model is not ModelData m || !m.GetIsAlive()) continue;
                float r = m.BaseShape.CircumscribedRadiusInches;
                m.SetPosition(new Position(
                    Math.Clamp(m.Position.x + dx, r, MathF.Max(r, w - r)),
                    Math.Clamp(m.Position.z + dz, r, MathF.Max(r, h - r))));
            }

            await Toast(context, $"{target.Name} is pushed {inches:0.#}\" {(away ? "away from" : "toward")} {pusher.Name}.");
        }

        /// <summary>The average position of a unit's living models.</summary>
        private static (float x, float z) Centre(IUnit unit)
        {
            float x = 0f, z = 0f;
            int n = 0;
            foreach (IModel m in unit.Models)
            {
                if (!m.GetIsAlive()) continue;
                x += m.Position.x;
                z += m.Position.z;
                n++;
            }
            return n == 0 ? (0f, 0f) : (x / n, z / n);
        }

        /// <summary>A buff or debuff on one roll, through the engine's granted roll modifiers.</summary>
        /// <summary>
        /// Grants a roll buff or debuff outside the rules engine - the Star Quest skills, items, injuries
        /// and random events, and the Firefight campaign skills, none of which are
        /// <see cref="SpecialRuleDefinition"/>s and so have no rule name of their own to read.
        ///
        /// <para>#407: <paramref name="source"/> is what the player should see credited beside the roll
        /// ("Rage +2"). Optional only so no caller breaks; a site that omits it renders as a bare "buff",
        /// which is what every granted buff looked like before this. Pass the skill, item or event name.
        /// </para>
        /// </summary>
        public static void Grant(IUnit bearer, IUnit target, ERollKind roll, int delta, ELifetime lifetime,
            IGameContext context, string? source = null)
        {
            OperationApplier.ApplyTokenOperations(new List<RuleOperation>
            {
                new RuleOperation.GrantTokenToUnit(target,
                    Effect.BuildStatModifierToken(roll, delta, lifetime, source,
                        // Cross-unit provenance, matching RuleInvocation.OwnerForEffectiveTarget: stamp
                        // the granter only when the buff lands on someone else.
                        ownerUnitID: target.ID != bearer.ID ? bearer.ID : null)),
            });
        }

        /// <summary>X hits on a unit: it rolls Defense for each (AP lowers the roll); each fail is a wound.</summary>
        public static async Task DealHits(IGameContext context, UnitData attacker, UnitData target, int hits, int ap, string source)
        {
            if (hits <= 0 || !target.GetIsAlive()) return;
            int needed = Math.Clamp(target.Defense + ap, 2, 6);
            IDiceResults roll = context.DiceRoller.Roll(6, hits);
            float saved = roll.AtOrAbove(needed);
            float wounds = hits - saved;
            await context.Presenter.Present(DiceRolledBeat.From(roll, needed, context.Settings.RandomnessType,
                $"{target.Name} - Defense vs {source}", $"{wounds:0.#} wound{(wounds == 1f ? "" : "s")}",
                category: ERollBeatCategory.Defense, context: $"{attacker.Name} -> {target.Name}"));
            if (wounds > 0f) await CasualtyPresentation.ApplyUnitWounds(context, target, wounds);
        }

        /// <summary>
        /// A morale test; true when passed.
        /// <para>
        /// Fixed at <see cref="EMoraleTrigger.OtherSource"/> because every caller is a non-combat
        /// event - an advanced skill's nerve check, a Dread or Enchantment flag, a treasure's effect -
        /// and none of them is shooting or melee, so Banner's +1 must not apply (audit finding 3).
        /// <b>A Quest caller that IS shooting or melee must not use this helper</b>; it should call
        /// <c>MoraleUtilities.TakeMoraleTest</c> with <see cref="EMoraleTrigger.ShootingOrMelee"/>, the
        /// way the melee and wound-driven paths do.
        /// </para>
        /// </summary>
        public static async Task<bool> Morale(IGameContext context, UnitData unit) =>
            (await MoraleUtilities.TakeMoraleTest(context, unit,
                HeroStatRules.GetMoraleQuality(unit, context.TableState),
                EMoraleTrigger.OtherSource)).Passed;

        public static Task Toast(IGameContext context, string text) =>
            context.Announce(text, Blue, EBannerTier.Toast);

        // ── Skill choices ────────────────────────────────────────────────────────────────────────────────

        /// <summary>The skills the hero can use right now, with a reason for the ones it can't.</summary>
        public static (List<QuestSkill> Usable, List<(string Name, string Why)> Unusable) SkillsFor(IGameContext context, UnitData hero)
        {
            var usable = new List<QuestSkill>();
            var unusable = new List<(string, string)>();
            QuestHero? sheet = Sheet(hero);
            var known = new List<string> { QuestSkill.DiscoverName, QuestSkill.PushName, QuestSkill.ScurryName, QuestSkill.ReviveName };
            if (QuestMission.IsMission(context)) known.Insert(0, QuestMission.ObjectiveSkillName);
            if (sheet != null) known.AddRange(sheet.Skills);
            if (sheet != null) known.AddRange(sheet.Items.Distinct().Select(i => QuestTreasure.ItemPrefix + i));
            foreach (string name in known.Distinct())
            {
                QuestSkill? skill = QuestSkill.Find(name);
                if (skill == null)
                {
                    // #414: an item the engine can't play now says WHY, the way a skill always has. The
                    // generic line below is the fallback for one that simply isn't wired up yet - a
                    // different thing from one that cannot work at all.
                    unusable.Add((name, name.StartsWith(QuestTreasure.ItemPrefix)
                        ? QuestItems.NotPossible.TryGetValue(name[QuestTreasure.ItemPrefix.Length..],
                            out string? whyItem) ? whyItem
                          : "This item isn't in the game yet - it stays in your pack."
                        : QuestAdvancedSkills.NotPossible.TryGetValue(name, out string? why0) ? why0
                        : "Not in the game yet - use it by hand if you like."));
                    continue;
                }
                string? why = skill.Unavailable(context, hero);
                if (why == null) usable.Add(skill); else unusable.Add((name, why));
            }
            return (usable, unusable);
        }

        /// <summary>The class feature, if the hero has one the engine can use and it isn't spent.</summary>
        public static QuestSkill? AvailableFeat(IGameContext context, UnitData hero)
        {
            QuestHero? sheet = Sheet(hero);
            if (sheet == null || string.IsNullOrEmpty(sheet.ClassFeat)) return null;
            if (hero.QuestFeatsUsed.Contains(sheet.ClassFeat)) return null;
            QuestSkill? feat = QuestSkill.Find(sheet.ClassFeat);
            return feat != null && feat.Unavailable(context, hero) == null ? feat : null;
        }

        /// <summary>
        /// Lets the player pick up to <paramref name="max"/> units from <paramref name="candidates"/>.
        ///
        /// <para>The one remaining candidate is still offered rather than chosen for the player: a grenade
        /// that goes off on its own because only one enemy happened to be in range reads as the game taking
        /// the shot, and the player never sees what it was aimed at.</para>
        /// </summary>
        public static async Task<List<UnitData>> PickUnits(IGameContext context, UnitData hero, string skill,
            List<UnitData> candidates, int max)
        {
            var chosen = new List<UnitData>();
            for (int i = 0; i < max; i++)
            {
                var remaining = candidates.Where(c => !chosen.Contains(c)).ToList();
                if (remaining.Count == 0) break;
                var bindings = context.GameDataStore.GetAllDataBindings<UnitData>()
                    .Where(b => remaining.Contains(b.GetValue())).ToList();
                var request = new SelectionRequest<UnitData>(hero.PlayerID,
                    $"{skill}: choose a unit ({chosen.Count + 1} of up to {max})",
                    bindings.Select(b => new SelectionRequest<UnitData>.ValidOption(b, b.GetValue().Name)).ToList(),
                    Array.Empty<SelectionRequest<UnitData>.InvalidOption>(), allowCancel: chosen.Count > 0,
                    displayName: $"Choosing a target for {skill}");
                DataBinding<UnitData>? pick = await context.PlayerRequester
                    .RequestDecision<SelectionRequest<UnitData>, DataBinding<UnitData>>(request);
                if (pick == null) break;
                chosen.Add(pick.GetValue());
            }
            return chosen;
        }

        public static List<UnitData> Enemies(IGameContext context, UnitData hero, float range) =>
            Units(context).Where(u => !AreFriends(context, hero, u) && u.GetIsAlive() && u.GetIsOnBattlefield()
                && Distance(hero, u) <= range).ToList();

        public static List<UnitData> Friends(IGameContext context, UnitData hero, float range, bool includeSelf, bool heroesOnly) =>
            Units(context).Where(u => AreFriends(context, hero, u) && u.GetIsAlive() && u.GetIsOnBattlefield()
                && (includeSelf || !ReferenceEquals(u, hero)) && (!heroesOnly || u.IsQuestHero)
                && (ReferenceEquals(u, hero) || Distance(hero, u) <= range)).ToList();

        public static List<UnitData> UnconsciousHeroes(IGameContext context, UnitData hero, float range) =>
            Units(context).Where(u => u.IsQuestHero && !ReferenceEquals(u, hero) && AreFriends(context, hero, u)
                && !u.GetIsAlive() && u.Models.Any(m => m.Position.x != 0f || m.Position.z != 0f)
                && DistanceToDownedHero(hero, u) <= range).ToList();

        /// <summary>
        /// Distance from a standing hero to a DOWNED one, measured to the downed hero's models even
        /// though they are not alive.
        /// <para>
        /// <see cref="Distance"/> (UnitCompareUtilities.MinDistanceBetweenUnits) deliberately skips dead
        /// models, so that a corpse left where it fell cannot put a unit in shooting or melee range. That
        /// is right for every combat gate - and exactly wrong here: an unconscious Star Quest hero has NO
        /// living models, so the standard measure returned +infinity and
        /// <see cref="UnconsciousHeroes"/> was always empty. Revive, Resurrect, Opulent Revival and the
        /// Reviving Kit could therefore never find anyone, and always answered "No unconscious hero
        /// within X" even with a hero down at the reviver's feet (QA 3.24, 9.20/9.21).
        /// </para>
        /// Measured from the reviver's LIVING models (it has to be standing to revive anyone) to the
        /// downed hero's models wherever they lie.
        /// </summary>
        private static float DistanceToDownedHero(IUnit reviver, IUnit downed)
        {
            float closest = float.PositiveInfinity;
            foreach (IModel a in reviver.Models)
            {
                if (!a.GetIsAlive()) continue;
                foreach (IModel b in downed.Models)
                {
                    // Models parked at the origin are off-table (never deployed, embarked), not lying
                    // on the floor where someone could reach them.
                    if (b.Position.x == 0f && b.Position.z == 0f) continue;
                    float distance = a.BaseDistanceToOtherModel_2D(b);
                    if (distance < closest) closest = distance;
                }
            }
            return closest;
        }
    }

    /// <summary>
    /// One Star Quest skill the engine resolves. <see cref="Stat"/> is the test it takes (0 Str, 1 Dex, 2 Wil,
    /// -1 none); a common skill only works on a pass and 2 power can buy that pass, a hero skill may buy it
    /// for 1 and otherwise fails (#414, v2.1.0); a class feature is once per game, free, no test.
    /// </summary>
    public sealed class QuestSkill
    {
        public const string PushName = "Push";
        public const string ScurryName = "Scurry";
        /// <summary>#418 - the price left this label when AoF: Quest arrived charging 2 power where
        /// Star Quest charges 3. The skill catalog is built at class-load time with no game to ask, so a
        /// priced name here could only ever be one system's. The cost is on the description line
        /// instead, which IS built per game, and the spend itself reads the profile.</summary>
        public static readonly string ReviveName = "Revive a hero";
        public const string DiscoverName = "Discover";

        public string Name { get; }
        public int Stat { get; }
        public bool Common { get; }
        public bool Feat { get; }
        public string Description { get; }
        private readonly Func<IGameContext, UnitData, string?> _unavailable;
        private readonly Func<IGameContext, UnitData, Task> _resolve;

        internal QuestSkill(string name, int stat, string description, Func<IGameContext, UnitData, string?> unavailable,
            Func<IGameContext, UnitData, Task> resolve, bool common = false, bool feat = false)
        {
            Name = name; Stat = stat; Description = description; _unavailable = unavailable; _resolve = resolve;
            Common = common; Feat = feat;
        }

        /// <summary>
        /// #418 - the skill's description for the game being played. A catalog entry is built at
        /// class-load time, so a price written into its text could only ever be one Quest system's;
        /// revive costs 3 power in Star Quest and 2 in AoF: Quest. Entries whose text mentions a cost
        /// write <c>{power}</c> and this fills it in, so the number a player reads is the number the
        /// spend will take.
        /// </summary>
        public string DescriptionIn(IGameContext context) =>
            Description.Contains("{power}", StringComparison.Ordinal)
                ? Description.Replace("{power}",
                    FDG.Rules.Dispatch.QuestRulesProfile.For(context).RevivePower.ToString())
                : Description;

        public string? Unavailable(IGameContext context, UnitData hero) => _unavailable(context, hero);

        /// <summary>Takes the test the skill calls for, then resolves it.</summary>
        public async Task Use(IGameContext context, UnitData hero)
        {
            if (Common && Stat >= 0)
            {
                if (!await QuestActions.StatTest(context, hero, Stat, Name, offerPower: true))
                {
                    await QuestActions.Toast(context, $"{hero.Name}: {Name} fails.");
                    return;
                }
            }
            else if (!Feat && Stat >= 0)
            {
                // #414 (v2.1.0): a failed hero-skill test used to resolve anyway for a forced 1 power.
                // It can now fizzle, so this result has to be honored like the common-skill one above -
                // HeroSkillTest already said so in a toast, hence no second one here.
                if (!await QuestActions.HeroSkillTest(context, hero, Stat, Name)) return;
            }
            if (!hero.GetIsAlive()) return;
            // #407 wording: name the skill that is speaking, for the whole of its resolution. Every
            // skill's effect text was written as a bare rules fragment ("one extra hit on its next
            // charge"), so the report said WHAT changed but never WHICH skill did it - Kai could not
            // tell a working skill from a silent one. Setting it here covers all ~120 at once rather
            // than editing every line.
            string? previous = Speaking;
            Speaking = Name;
            try
            {
                await _resolve(context, hero);
            }
            finally
            {
                Speaking = previous;   // restore, since a skill may resolve another (free actions)
            }
        }

        /// <summary>
        /// The skill currently resolving, or null outside one. Read by the wording helpers
        /// (<c>SetFlag</c> / <c>GrantSelf</c> / <c>Flag</c>) so a skill's message names itself.
        /// <para>Per-call rather than per-game on purpose: skills resolve one at a time on the engine
        /// thread, and the nesting a free action creates is handled by save/restore above.</para>
        /// </summary>
        public static string? Speaking { get; private set; }

        /// <summary>
        /// The standard form for a skill's effect message: "Hero: Skill - what it does".
        /// Falls back to "Hero: what it does" when no skill is resolving, so a non-skill caller reads
        /// the same as it always did.
        /// </summary>
        public static string Line(string heroName, string text) =>
            Speaking is { Length: > 0 } skill ? $"{heroName}: {skill} - {text}." : $"{heroName}: {text}.";

        public static QuestSkill? Find(string name) =>
            All.FirstOrDefault(s => s.Name == name)
            ?? QuestTreasure.SearchSkills.FirstOrDefault(s => s.Name == name)
            ?? QuestTreasure.ItemSkills.FirstOrDefault(s => s.Name == name)
            ?? QuestMission.MissionSkills.FirstOrDefault(s => s.Name == name)
            ?? QuestAdvancedSkills.All.FirstOrDefault(s => s.Name == name);

        private static string? Always(IGameContext c, UnitData h) => null;

        private static Func<IGameContext, UnitData, string?> NeedsEnemy(float range) =>
            (c, h) => QuestActions.Enemies(c, h, range).Count > 0 ? null : $"No enemy within {range:0}\".";

        private static Func<IGameContext, UnitData, string?> NeedsFriend(float range, bool includeSelf, bool heroesOnly) =>
            (c, h) => QuestActions.Friends(c, h, range, includeSelf, heroesOnly).Count > 0 ? null
                : $"No {(heroesOnly ? "hero" : "friendly unit")} within {range:0}\".";

        private static async Task HealFriends(IGameContext c, UnitData h, string skill, float range, int count,
            bool includeSelf, bool heroesOnly, int wounds)
        {
            var picks = await QuestActions.PickUnits(c, h, skill, QuestActions.Friends(c, h, range, includeSelf, heroesOnly), count);
            foreach (UnitData u in picks) QuestActions.Heal(u, wounds, c);
            await QuestActions.Toast(c, $"{skill}: {string.Join(", ", picks.Select(p => p.Name))} restore{(picks.Count == 1 ? "s" : "")} {wounds} wound{(wounds == 1 ? "" : "s")}.");
        }

        private static async Task BuffFriends(IGameContext c, UnitData h, string skill, float range, int count,
            bool includeSelf, ERollKind roll, int delta, ELifetime life, string text)
        {
            var picks = await QuestActions.PickUnits(c, h, skill, QuestActions.Friends(c, h, range, includeSelf, false), count);
            foreach (UnitData u in picks) QuestActions.Grant(h, u, roll, delta, life, c);
            await QuestActions.Toast(c, $"{skill}: {string.Join(", ", picks.Select(p => p.Name))} {text}.");
        }

        private static async Task StatBonusFriends(IGameContext c, UnitData h, string skill, float range, int count, int bonus)
        {
            var picks = await QuestActions.PickUnits(c, h, skill, QuestActions.Friends(c, h, range, false, false), count);
            foreach (UnitData u in picks) if (u.IsQuestHero) u.QuestStatBonus += bonus;
            await QuestActions.Toast(c, $"{skill}: {string.Join(", ", picks.Select(p => p.Name))} get +{bonus} to their next stat roll.");
        }

        private static int D3(IGameContext c) => (HordePlacement.RollD6(c) + 1) / 2;

        private static async Task SetFlag(IGameContext c, UnitData h, string flag, string text)
        {
            QuestFlags.Set(c, h, flag);
            await QuestActions.Toast(c, QuestSkill.Line(h.Name, text));
        }

        private static async Task GrantSelf(IGameContext c, UnitData h, string rule, ELifetime life, string text)
        {
            QuestTreasure.GrantRule(c, h, h, rule, life);
            await QuestActions.Toast(c, QuestSkill.Line(h.Name, text));
        }

        public static readonly IReadOnlyList<QuestSkill> All = new List<QuestSkill>
        {
            // ── Common skills (every hero) ──
            new(PushName, 0, "Pick one enemy unit within 3\" and take a Str test. If passed, the target is pushed directly away by 2\".",
                NeedsEnemy(3f), async (c, h) =>
                {
                    var t = await QuestActions.PickUnits(c, h, PushName, QuestActions.Enemies(c, h, 3f), 1);
                    if (t.Count == 0) return;
                    await QuestActions.Shove(c, h, t[0], 2f);
                }, common: true),
            new(ScurryName, 1, "The hero may move by up to D3\" by passing a Dex test.", Always, async (c, h) =>
                {
                    int d = D3(c);
                    await QuestActions.Toast(c, $"{h.Name} scurries up to {d}\".");
                    await new GameOperationServices(c).MoveUnit(h, d, isOptional: true, controller: h.PlayerID);
                }, common: true),
            new(ReviveName, -1, "Heroes within 3\" of an unconscious hero may spend {power} power to "
                + "revive it; it restores 3 wounds.",
                (c, h) => QuestActions.UnconsciousHeroes(c, h, 3f).Count > 0 ? null : "No unconscious hero within 3\".",
                async (c, h) =>
                {
                    var down = QuestActions.UnconsciousHeroes(c, h, 3f);
                    // Always ask, even with one hero down - the player should see who they are reviving.
                    var request = new StringSelectionRequest(h.PlayerID, "Which hero do you revive?",
                        down.Select(d => d.Name).ToList(), Array.Empty<StringSelectionRequest.InvalidOption>(),
                        displayName: "Choosing a hero to revive");
                    string pick = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
                    UnitData target = down.FirstOrDefault(d => d.Name == pick) ?? down[0];
                    await QuestActions.SpendPower(c, h, QuestActions.Profile(c).RevivePower,
                        $"to revive {target.Name}");
                    QuestActions.Revive(target);
                    await c.Announce($"{h.Name} revives {target.Name}!", new TextColor(120, 230, 140, 255), EBannerTier.Notice);
                }, common: true),

            // ── Skills every class may learn (tier 0) ──
            new("Concentrate", 1, "The hero takes 1 wound and gets +2 to its next stat roll.", Always, async (c, h) =>
                {
                    await CasualtyPresentation.ApplyUnitWounds(c, h, 1f);
                    h.QuestStatBonus += 2;
                    await QuestActions.Toast(c, $"{h.Name} concentrates: +2 to the next stat roll.");
                }),
            new("Healing Touch", 2, "The hero restores D3 wounds, or one other hero within 3\" does instead.",
                NeedsFriend(3f, true, true), async (c, h) =>
                {
                    int d = D3(c);
                    await HealFriends(c, h, "Healing Touch", 3f, 1, true, true, d);
                }),
            new("Battle Rest", 0, "The hero restores D3+1 power.", Always, async (c, h) =>
                {
                    int d = D3(c) + 1;
                    QuestActions.RestorePower(h, d);
                    await QuestActions.Toast(c, $"{h.Name} restores {d} power ({h.QuestPower}).");
                }),
            new("Protector", 0, "The hero gets +1 to Def rolls until the end of the round.", Always, async (c, h) =>
                {
                    QuestActions.Grant(h, h, ERollKind.Save, 1, ELifetime.ThisRound, c, "Protector");
                    await QuestActions.Toast(c, $"{h.Name}: +1 to Defense rolls this round.");
                }),

            // ── Class starter skills ──
            new("Rally", 2, "Heroes may use this skill action even whilst Shaken. The hero stops being Shaken.", Always, async (c, h) =>
                {
                    h.Tokens.RemoveTokens(TokenType.Shaken);
                    await QuestActions.Toast(c, $"{h.Name} rallies and is no longer Shaken.");
                }),
            new("Healing Word", 2, "Pick two friendly heroes within 6\", which restore 1 wound each.", NeedsFriend(6f, true, true),
                (c, h) => HealFriends(c, h, "Healing Word", 6f, 2, true, true, 1)),
            new("Song of Health", 2, "Pick two other friendly heroes within 9\", which restore 1 wound each.", NeedsFriend(9f, false, true),
                (c, h) => HealFriends(c, h, "Song of Health", 9f, 2, false, true, 1)),
            new("Healing Ointment", 2, "Pick one friendly unit within 3\", which restores 1 wound.", NeedsFriend(3f, true, false),
                (c, h) => HealFriends(c, h, "Healing Ointment", 3f, 1, true, false, 1)),
            new("Shield of Faith", 1, "Pick one other friendly unit within 6\", which gets +2 to Def rolls next time it takes hits.",
                NeedsFriend(6f, false, false), (c, h) => BuffFriends(c, h, "Shield of Faith", 6f, 1, false, ERollKind.Save, 2, ELifetime.NextTrigger, "get +2 to Defense the next time they take hits")),
            new("Ancient Weapon", 2, "Pick one other friendly unit within 6\", which gets +2 to hit rolls next time it attacks.",
                NeedsFriend(6f, false, false), (c, h) => BuffFriends(c, h, "Ancient Weapon", 6f, 1, false, ERollKind.Hit, 2, ELifetime.NextTrigger, "get +2 to hit the next time they attack")),
            new("Barkskin", 1, "The hero gets +1 to Def rolls next time it takes hits until the end of the round.", Always, async (c, h) =>
                {
                    QuestActions.Grant(h, h, ERollKind.Save, 1, ELifetime.NextTrigger, c, "Barkskin");
                    await QuestActions.Toast(c, $"{h.Name}: +1 to Defense the next time it takes hits.");
                }),
            new("Dark Warding", 2, "The hero takes 1 wound, and gets +2 to Defense rolls until the end of the round.", Always, async (c, h) =>
                {
                    await CasualtyPresentation.ApplyUnitWounds(c, h, 1f);
                    if (!h.GetIsAlive()) return;
                    QuestActions.Grant(h, h, ERollKind.Save, 2, ELifetime.ThisRound, c, "Dark Warding");
                    await QuestActions.Toast(c, $"{h.Name}: +2 to Defense rolls this round.");
                }),
            new("Rage", 0, "The hero takes 1 wound and gets +2 to hit in melee when charging until the end of the round.", Always, async (c, h) =>
                {
                    await CasualtyPresentation.ApplyUnitWounds(c, h, 1f);
                    if (!h.GetIsAlive()) return;
                    QuestActions.Grant(h, h, ERollKind.Hit, 2, ELifetime.ThisRound, c, "Rage");
                    await QuestActions.Toast(c, $"{h.Name} rages: +2 to hit this round (charge into melee to use it).");
                }),
            new("Guidance", 0, "Pick two other friendly units within 6\", which get +1 to their next stat roll each.",
                NeedsFriend(6f, false, true), (c, h) => StatBonusFriends(c, h, "Guidance", 6f, 2, 1)),
            new("Inspire", 1, "Pick two other friendly units within 9\", which get +1 to their next stat roll each.",
                NeedsFriend(9f, false, true), (c, h) => StatBonusFriends(c, h, "Inspire", 9f, 2, 1)),
            new("Methodical Affinity", 1, "The hero may spend X power (max. 3), and gets +X+1 to its next stat roll.", Always, async (c, h) =>
                {
                    var options = new List<string> { "0", "1", "2", "3" };
                    var request = new StringSelectionRequest(h.PlayerID, $"Methodical Affinity: spend how much power? ({h.QuestPower} left)",
                        options, Array.Empty<StringSelectionRequest.InvalidOption>(), displayName: "Methodical Affinity");
                    int x = int.TryParse(await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request), out int v) ? v : 0;
                    await QuestActions.SpendPower(c, h, x, "Methodical Affinity");
                    h.QuestStatBonus += x + 1;
                    await QuestActions.Toast(c, $"{h.Name}: +{x + 1} to the next stat roll.");
                }),
            new("Hunting Beast", 0, "Pick one enemy unit within 6\", which takes 2 hits.", NeedsEnemy(6f), async (c, h) =>
                {
                    var t = await QuestActions.PickUnits(c, h, "Hunting Beast", QuestActions.Enemies(c, h, 6f), 1);
                    if (t.Count > 0) await QuestActions.DealHits(c, h, t[0], 2, 0, "Hunting Beast");
                }),
            new("Thorn Whip", 0, "Pick one enemy unit within 9\", which takes one hit and is then pulled up to 6\" directly toward the hero.",
                NeedsEnemy(9f), async (c, h) =>
                {
                    var t = await QuestActions.PickUnits(c, h, "Thorn Whip", QuestActions.Enemies(c, h, 9f), 1);
                    if (t.Count == 0) return;
                    await QuestActions.DealHits(c, h, t[0], 1, 0, "Thorn Whip");
                    if (!t[0].GetIsAlive()) return;
                    await QuestActions.Toast(c, $"Pull {t[0].Name} up to 6\" directly toward {h.Name}.");
                    await new GameOperationServices(c).MoveUnit(t[0], 6f, isOptional: true, controller: h.PlayerID);
                }),
            new("Challenge", 2, "Pick two enemy units within 12\", which must take a morale test. If failed, they are pulled up to 6\" directly toward the hero.",
                NeedsEnemy(12f), async (c, h) =>
                {
                    var t = await QuestActions.PickUnits(c, h, "Challenge", QuestActions.Enemies(c, h, 12f), 2);
                    foreach (UnitData e in t)
                    {
                        if (await QuestActions.Morale(c, e)) continue;
                        await QuestActions.Toast(c, $"Pull {e.Name} up to 6\" directly toward {h.Name}.");
                        await new GameOperationServices(c).MoveUnit(e, 6f, isOptional: true, controller: h.PlayerID);
                    }
                }),
            new("Distracting Tune", 0, "Pick one enemy unit within 9\", which must take a morale test. If failed, it gets -2 to Defense rolls next time it is attacked.",
                NeedsEnemy(9f), async (c, h) =>
                {
                    var t = await QuestActions.PickUnits(c, h, "Distracting Tune", QuestActions.Enemies(c, h, 9f), 1);
                    if (t.Count == 0 || await QuestActions.Morale(c, t[0])) return;
                    QuestActions.Grant(h, t[0], ERollKind.Save, -2, ELifetime.NextTrigger, c, "Distracting Tune");
                    await QuestActions.Toast(c, $"{t[0].Name}: -2 to Defense the next time it is attacked.");
                }),

            new("Flurry of Blows", 0, "The next time the hero attacks in melee or shoots from within 12\" this round, if it deals at least one hit, it deals one extra hit.",
                Always, (c, h) => GrantSelf(c, h, "Extra hit in melee or within 12\"", ELifetime.NextTrigger, "one extra hit on its next close attack")),
            new("Combat Superiority", 0, "The next time the hero attacks in melee or shoots from within 12\" this round, if it deals at least one hit, it deals one extra hit.",
                Always, (c, h) => GrantSelf(c, h, "Extra hit in melee or within 12\"", ELifetime.NextTrigger, "one extra hit on its next close attack")),
            new("Evasive", 1, "Until the end of the round, the next time an enemy attacks the hero, it gets -1 to hit.",
                Always, (c, h) => GrantSelf(c, h, "Evasive", ELifetime.NextTrigger, "the next enemy attack gets -1 to hit")),
            new("Lightning Reflexes", 1, "Until the end of the round, when taking a hit, roll one die. On a 6+ it is ignored.",
                Always, (c, h) => GrantSelf(c, h, "Protected", ELifetime.ThisRound, "ignores wounds on a 6+ this round")),
            new("Vicious Strike", 2, "The hero's weapons get AP(+1) until the end of the round.", Always, async (c, h) =>
                {
                    QuestTreasure.GrantRule(c, h, h, "AP(+1) in melee", ELifetime.ThisRound);
                    QuestTreasure.GrantRule(c, h, h, "AP(1) when shooting", ELifetime.ThisRound);
                    await QuestActions.Toast(c, $"{h.Name}: AP(+1) this round.");
                }),
            new("Devoted Warding", 1, "Pick one friendly unit within 6\". Until the end of the round, hits it takes get AP(-1), to a min. of AP(0).",
                NeedsFriend(6f, true, false), async (c, h) =>
                {
                    var t = await QuestActions.PickUnits(c, h, "Devoted Warding", QuestActions.Friends(c, h, 6f, true, false), 1);
                    foreach (UnitData u in t) QuestTreasure.GrantRule(c, h, u, "Fortified", ELifetime.ThisRound);
                    if (t.Count > 0) await QuestActions.Toast(c, $"{t[0].Name}: incoming hits get AP(-1) this round.");
                }),
            new("Fog Cloud", 2, "Place a fog cloud marker within 9\". The area within 3\" of it counts as cover until the end of the round.",
                Always, async (c, h) =>
                {
                    Position p = await QuestTreasure.PickPoint(c, h, "Fog Cloud", 9f);
                    QuestTreasure.PlaceMarkerAt(c, "smoke", p, 1);
                    await QuestActions.Toast(c, "A fog cloud rises: cover within 3\" until the end of the round.");
                }),
            new("Trickster", 0, "Place a trickster marker within 3\". Enemies treat it as a hero until the end of the round (it goes if an enemy moves within 1\").",
                Always, async (c, h) =>
                {
                    Position p = await QuestTreasure.PickPoint(c, h, "Trickster", 3f);
                    QuestTreasure.PlaceMarkerAt(c, "phantasm", p, 1);
                    await QuestActions.Toast(c, "A decoy appears - enemies that see it go for it.");
                }),
            new("Curing Touch", 2, "The hero loses one status condition, or one other hero within 3\" does instead.",
                (c, h) => QuestActions.Friends(c, h, 3f, true, true).Any(u => QuestActions.Sheet(u)?.Conditions.Count > 0) ? null : "No hero within 3\" has a condition.",
                async (c, h) =>
                {
                    var hurt = QuestActions.Friends(c, h, 3f, true, true).Where(u => QuestActions.Sheet(u)?.Conditions.Count > 0).ToList();
                    var t = await QuestActions.PickUnits(c, h, "Curing Touch", hurt, 1);
                    if (t.Count == 0) return;
                    string cond = QuestActions.Sheet(t[0])!.Conditions[0];
                    QuestTreasure.RemoveCondition(t[0], cond);
                    await QuestActions.Toast(c, $"{t[0].Name} is no longer {cond}.");
                }),
            new("Arcana", 2, "The hero spends D3 power, and gets +1 to spell casting rolls until the end of the round.",
                QuestTreasure.IsCaster, async (c, h) =>
                {
                    int d = D3(c);
                    await QuestActions.SpendPower(c, h, d, "Arcana");
                    QuestActions.Grant(h, h, ERollKind.Cast, 1, ELifetime.ThisRound, c, "Arcana");
                    await QuestActions.Toast(c, $"{h.Name}: +1 to casting rolls this round.");
                }),
            new("Fiendish Respite", 1, "Until the end of the round, the next time the hero destroys an enemy unit, it restores D3+1 power.",
                Always, async (c, h) =>
                {
                    QuestProgress q = GameProgressUtilities.GetQuestProgress(c.GameDataStore);
                    q.Respite.Add($"{h.ID.ID}:{QuestTreasure.EnemiesDestroyed(c)}");
                    GameProgressUtilities.SetQuestProgress(c.GameDataStore, q);
                    await QuestActions.Toast(c, $"{h.Name}: destroy an enemy unit this round to restore D3+1 power.");
                }),

            new("Reckless", 1, "Until the end of the round, whenever the hero attacks in melee or shoots from within 12\" and deals at least one wound, it deals one extra wound - but whenever it takes at least one wound, it takes one extra wound.",
                // Kai's own wording (QA 3.13b): name the skill, then say what it does in a sentence
                // anyone can read. The old text - "one extra wound dealt (melee / within 12") and
                // taken this round" - never said which skill it was, and read as a rules fragment.
                Always, (c, h) => SetFlag(c, h, QuestFlags.Reckless,
                    "deals +1 wound in melee or within 12\", but takes +1 to each wound")),
            new("Vengeful Strike", 0, "Until the end of the round, whenever the hero attacks a Shaken enemy in melee or shoots at it from within 12\", it gets +1 to hit.",
                Always, (c, h) => SetFlag(c, h, QuestFlags.Vengeful, "+1 to hit Shaken enemies (melee / within 12\") this round")),
            new("Dread Attack", 1, "Until the end of the round, the next time the hero attacks an enemy in melee or shoots at it from over 12\" away, that enemy must take a morale test at the end of that activation. If failed, it is Shaken.",
                Always, (c, h) => SetFlag(c, h, QuestFlags.Dread, "its next melee attack or shot from over 12\" makes the target test morale")),
            new("Martial Strike", 2, "Until the end of the round, the next time the hero attacks an enemy in melee, after combat is resolved, the enemy unit is pushed 6\" directly away.",
                Always, (c, h) => SetFlag(c, h, QuestFlags.Martial, "its next melee target is pushed 6\" away")),
            new("Conjuration", 0, "Until the end of the round, the hero gets +6\" range either when casting its next spell, or the next time it spends tokens to modify enemy casting rolls.",
                QuestTreasure.IsCaster, async (c, h) =>
                {
                    QuestFlags.AddSpellRange(c, h, 6);
                    await QuestActions.Toast(c, $"{h.Name}: +6\" range on its next spell or counter-spell.");
                }),
            new("Distant Spell", 2, "The hero may spend X power (max. 3), and until the end of the round gets +X*6\" range either when casting its next spell, or the next time it spends tokens to modify enemy casting rolls.",
                QuestTreasure.IsCaster, async (c, h) =>
                {
                    var request = new StringSelectionRequest(h.PlayerID, $"Distant Spell: spend how much power? Each point is +6\" range. ({h.QuestPower} left)",
                        new List<string> { "0", "1", "2", "3" }, Array.Empty<StringSelectionRequest.InvalidOption>(), displayName: "Distant Spell");
                    int x = int.TryParse(await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request), out int v) ? Math.Clamp(v, 0, 3) : 0;
                    if (x == 0) { await QuestActions.Toast(c, $"{h.Name} spends no power - no extra range."); return; }
                    await QuestActions.SpendPower(c, h, x, "Distant Spell");
                    QuestFlags.AddSpellRange(c, h, 6 * x);
                    await QuestActions.Toast(c, $"{h.Name}: +{6 * x}\" range on its next spell or counter-spell.");
                }),
            new("Pact of Strength", 0, "The hero takes 1 wound, and the next spell it casts until the end of the round gets AP(+2).",
                QuestTreasure.IsCaster, async (c, h) =>
                {
                    await CasualtyPresentation.ApplyUnitWounds(c, h, 1f);
                    if (!h.GetIsAlive()) return;
                    await SetFlag(c, h, QuestFlags.Pact, "its next spell this round gets AP(+2)");
                }),
            new("Enchantment", 1, "Until the end of the round, the next time the hero successfully casts a spell that targets enemies, pick one of them to take a morale test. If failed, it is Shaken.",
                QuestTreasure.IsCaster, (c, h) => SetFlag(c, h, QuestFlags.Enchant, "its next spell on enemies makes one test morale")),
            new("Tides of Chaos", 0, "Until the end of the round, the hero restores D3+2 power the next time it successfully casts a spell, or the next time it counters an enemy spell and that spell fails.",
                QuestTreasure.IsCaster, (c, h) => SetFlag(c, h, QuestFlags.Tides, "restores D3+2 power on its next successful spell or counter")),

            // ── Class features (once per game, free, when activated) ──
            new("Infiltrating Step", -1, "Once per game, when activated: until the end of the round, enemies can't target the hero as long as it hasn't targeted any enemies.",
                Always, (c, h) => SetFlag(c, h, QuestFlags.Infiltrating, "enemies can't target it this round (until it targets one)"), feat: true),
            new("Soldier's Might", -1, "Once per game, when activated: the next time the hero deals at least one hit in melee this round, it deals one extra hit.",
                Always, (c, h) => GrantSelf(c, h, "Extra hit in melee", ELifetime.NextTrigger, "one extra hit on its next melee attack"), feat: true),
            new("Pathfinder's Precision", -1, "Once per game, when activated: the next time the hero deals at least one hit when shooting this round, it deals one extra hit.",
                Always, (c, h) => GrantSelf(c, h, "Extra hit when shooting", ELifetime.NextTrigger, "one extra hit on its next shooting attack"), feat: true),
            new("Bio-Trap", -1, "Once per game, when activated: the area within 3\" of the hero is dangerous terrain for enemy units until the end of the round.",
                Always, async (c, h) =>
                {
                    Position p = h.Models.FirstOrDefault(m => m.GetIsAlive())?.Position ?? new Position(0f, 0f);
                    QuestTreasure.PlaceMarkerAt(c, "biotrap", new Position(p.x, p.z), 1);
                    await QuestActions.Toast(c, $"A bio-trap springs up around {h.Name}: dangerous for enemies within 3\" this round.");
                }, feat: true),
            new("Psychic Focus", -1, "Once per game, when activated, the hero gets one spell token.",
                QuestTreasure.IsCaster, (c, h) => QuestTreasure.AddSpellTokens(c, h, 1), feat: true),
            new("Berserker Frenzy", -1, "Once per game, when activated, the hero restores D3 power.", Always, async (c, h) =>
                {
                    int d = D3(c);
                    QuestActions.RestorePower(h, d);
                    await QuestActions.Toast(c, $"{h.Name}'s frenzy restores {d} power.");
                }, feat: true),
            new("Sentinel Meditation", -1, "Once per game, when activated, the hero restores D3 wounds.", Always, async (c, h) =>
                {
                    int d = D3(c);
                    QuestActions.Heal(h, d, c);
                    await QuestActions.Toast(c, $"{h.Name} meditates and restores {d} wounds.");
                }, feat: true),
            new("Guarding Blessing", -1, "Once per game, when activated, two heroes within 6\" restore 1 wound each.",
                NeedsFriend(6f, true, true), (c, h) => HealFriends(c, h, "Guarding Blessing", 6f, 2, true, true, 1), feat: true),
            new("Instigated Restoration", -1, "Once per game, when activated, two other heroes within 9\" restore 1 power each.",
                NeedsFriend(9f, false, true), async (c, h) =>
                {
                    var picks = await QuestActions.PickUnits(c, h, "Instigated Restoration", QuestActions.Friends(c, h, 9f, false, true), 2);
                    foreach (UnitData u in picks) QuestActions.RestorePower(u, 1);
                    await QuestActions.Toast(c, $"{string.Join(", ", picks.Select(p => p.Name))} restore 1 power.");
                }, feat: true),
            new("Commanding Shout", -1, "Once per game, when activated, two heroes within 3\" may move by up to 6\" each.",
                NeedsFriend(3f, true, true), async (c, h) =>
                {
                    var picks = await QuestActions.PickUnits(c, h, "Commanding Shout", QuestActions.Friends(c, h, 3f, true, true), 2);
                    foreach (UnitData u in picks)
                        await new GameOperationServices(c).MoveUnit(u, 6f, isOptional: true, controller: u.PlayerID);
                }, feat: true),
            new("Ritual Pact", -1, "Once per game, when activated, the hero takes 1 wound, and then deals 1 wound to one enemy unit within 6\".",
                NeedsEnemy(6f), async (c, h) =>
                {
                    await CasualtyPresentation.ApplyUnitWounds(c, h, 1f);
                    var t = await QuestActions.PickUnits(c, h, "Ritual Pact", QuestActions.Enemies(c, h, 6f), 1);
                    if (t.Count > 0) await CasualtyPresentation.ApplyUnitWounds(c, t[0], 1f);
                }, feat: true),
            new("Techno-Bolts", -1, "Once per game, when activated, the hero may spend X power (max. 3). Pick one enemy unit within 12\", which takes X+1 hits.",
                NeedsEnemy(12f), async (c, h) =>
                {
                    var request = new StringSelectionRequest(h.PlayerID, $"Techno-Bolts: spend how much power? ({h.QuestPower} left)",
                        new List<string> { "0", "1", "2", "3" }, Array.Empty<StringSelectionRequest.InvalidOption>(), displayName: "Techno-Bolts");
                    int x = int.TryParse(await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request), out int v) ? v : 0;
                    await QuestActions.SpendPower(c, h, x, "Techno-Bolts");
                    var t = await QuestActions.PickUnits(c, h, "Techno-Bolts", QuestActions.Enemies(c, h, 12f), 1);
                    if (t.Count > 0) await QuestActions.DealHits(c, h, t[0], x + 1, 0, "Techno-Bolts");
                }, feat: true),
        };
    }

    /// <summary>
    /// The Star Quest actions a hero takes from the action menu: Rest, a Skill, an extra action bought with
    /// power, or its class feature. Returns to Choose Action afterwards (Rest ends the activation).
    /// </summary>
    public class QuestActionStage : StageBase<IUnitActionContext>
    {
        public StageBinding OnFinished;
        public StageBinding ToEndActivation;

        public QuestActionStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent)
            : base(gameContext, parent)
        {
            OnFinished = new StageBinding(this);
            ToEndActivation = new StageBinding(this);
        }

        public override async Task Enter(IUnitActionContext context)
        {
            UnitData hero = context.ActivatingUnit.GetValue();
            string action = context.PendingQuestAction ?? "";
            context.PendingQuestAction = null;

            if (action == QuestActions.EndActivationOption)
            {
                await ToEndActivation.Activate(context);
                return;
            }

            if (action == QuestActions.RestOption)
            {
                QuestHero? sheet = QuestActions.Sheet(hero);
                hero.QuestPower = Math.Max(hero.QuestPower, sheet?.Power ?? hero.QuestPower);
                await GameContext.Announce($"{hero.Name} rests and restores all power ({hero.QuestPower}).",
                    new TextColor(130, 200, 255, 255), EBannerTier.Toast);
                await ToEndActivation.Activate(context);
                return;
            }

            if (action == QuestActions.FreeActionOption)
            {
                string why = QuestFlags.Take(GameContext, hero, QuestFlags.BonusAction) ?? "";
                await GameContext.Announce($"{hero.Name}: {why}.", new TextColor(130, 200, 255, 255), EBannerTier.Toast);
                context.BeginQuestExtraAction();
                await OnFinished.Activate(context);
                return;
            }

            Rules.Dispatch.QuestRulesProfile rules = QuestActions.Profile(GameContext);
            if (action == rules.ExtraActionOption || action == rules.ExtraSkillOption)
            {
                await QuestActions.SpendPower(GameContext, hero,
                    QuestActions.Profile(GameContext).ExtraActionPower, "extra action");
                context.QuestExtraUsed = true;
                if (action == rules.ExtraActionOption) context.BeginQuestExtraAction();
                else context.QuestSkillsUsed--;
                if (!hero.GetIsAlive()) { await ToEndActivation.Activate(context); return; }
                await OnFinished.Activate(context);
                return;
            }

            if (action.EndsWith(QuestActions.FeatSuffix))
            {
                string name = action[..^QuestActions.FeatSuffix.Length];
                QuestSkill? feat = QuestSkill.Find(name);
                if (feat != null)
                {
                    hero.QuestFeatsUsed.Add(name);
                    context.MarkIrreversibleAction();
                    await feat.Use(GameContext, hero);
                }
                await (hero.GetIsAlive() ? OnFinished : ToEndActivation).Activate(context);
                return;
            }

            // Skill: pick one.
            var (usable, unusable) = QuestActions.SkillsFor(GameContext, hero);
            var request = new StringSelectionRequest(hero.PlayerID, $"{hero.Name}: choose a skill ({hero.QuestPower} power)",
                usable.Select(s => s.Name).ToList(),
                unusable.Select(u => new StringSelectionRequest.InvalidOption(u.Name, u.Why)).ToList(),
                optionDescriptions: usable.ToDictionary(s => s.Name,
                    s => (s.Stat >= 0 ? $"({QuestActions.StatName(s.Stat)} test) " : "")
                        + s.DescriptionIn(GameContext)),
                allowCancel: true, displayName: $"{hero.Name}: Skill");
            string? choice = usable.Count == 0 ? null
                : await GameContext.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
            QuestSkill? skill = choice == null ? null : usable.FirstOrDefault(s => s.Name == choice);
            if (skill != null)
            {
                context.QuestSkillsUsed++;
                context.MarkIrreversibleAction();
                GameContext.Log($"{hero.Name} uses {skill.Name}.");
                await skill.Use(GameContext, hero);
            }
            await (hero.GetIsAlive() ? OnFinished : ToEndActivation).Activate(context);
        }
    }
}
