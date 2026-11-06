using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;

namespace FDG.StateMachine
{
    /// <summary>
    /// Horde Mode experience and levelling up (OPR Horde Mode v3.6.0):
    /// <list type="bullet">
    ///   <item>Defending units earn XP for kills: +3 for a unit, +5 for a hero unit, +8 for a hero and its unit.</item>
    ///   <item>Every 5 XP the unit rolls a trait: 1 Agile, 2 Headstrong, 3 Specialist (melee or shooting),
    ///   4 Resilient, 5 Elite, 6 Fast Learner (pick any other trait).</item>
    ///   <item>Each trait except Specialist can only be gained once - a duplicate is re-rolled. XP caps at
    ///   30, so a unit levels up at most six times.</item>
    /// </list>
    /// XP is a <see cref="TokenType.HordeXP"/> count and each trait a permanent rule grant, so both are
    /// saved with the unit, sync to every player, and show on the unit's tooltip.
    /// </summary>
    public static class HordeExperience
    {
        public const int XpPerLevel = 5;
        public const int MaxXp = 30;

        public const string Agile = "Trait: Agile";
        public const string Headstrong = "Trait: Headstrong";
        public const string MeleeSpecialist = "Trait: Melee Specialist";
        public const string ShootingSpecialist = "Trait: Shooting Specialist";
        public const string Resilient = "Trait: Resilient";
        public const string Elite = "Trait: Elite";
        public const string FastLearner = "Trait: Fast Learner";

        /// <summary>The die faces of the trait table.</summary>
        public enum ETraitRoll { Agile = 1, Headstrong = 2, Specialist = 3, Resilient = 4, Elite = 5, FastLearner = 6 }

        /// <summary>
        /// XP for destroying a unit: +8 for a hero together with its unit, +5 for a hero on its own,
        /// else +3.
        /// <para>
        /// #416: the top tier exists only in Grimdark Future and Age of Fantasy. Both skirmish Horde
        /// books stop after "+5 XP: Killed a Hero unit", so there a hero's squad is worth the same as the
        /// hero - see <see cref="HordeRules.AwardsHeroAndUnitXp"/>. Read off <see cref="HordeRules"/>
        /// rather than taken as a parameter because every caller would have to pass the same thing, and
        /// the sizes for the game in play already live there.
        /// </para>
        /// </summary>
        public static int XpForKill(bool hadJoinedHero, bool wasSoloHero) =>
            hadJoinedHero && HordeRules.AwardsHeroAndUnitXp ? 8
            : hadJoinedHero || wasSoloHero ? 5
            : 3;

        /// <summary>How many 5-XP thresholds were crossed going from <paramref name="before"/> to <paramref name="after"/>.</summary>
        public static int LevelUps(int before, int after) =>
            Math.Max(0, Math.Min(after, MaxXp) / XpPerLevel - Math.Min(before, MaxXp) / XpPerLevel);

        /// <summary>The traits (rule names) this unit already has.</summary>
        public static HashSet<string> OwnedTraits(IUnit unit) =>
            unit.Tokens.GetAllTokens(TokenType.RuleGrant)
                .Select(t => t.Payload)
                .OfType<TokenPayload.RuleGrant>()
                .Select(g => g.RuleName)
                .Where(n => n.StartsWith("Trait: ", StringComparison.Ordinal))
                .ToHashSet();

        /// <summary>The trait rule names a unit can still gain (Specialist expands to whichever picks are left).</summary>
        public static List<string> AvailableTraits(IReadOnlySet<string> owned, bool includeFastLearner)
        {
            var all = new List<string> { Agile, Headstrong, MeleeSpecialist, ShootingSpecialist, Resilient, Elite };
            if (includeFastLearner) all.Add(FastLearner);
            return all.Where(t => !owned.Contains(t)).ToList();
        }

        /// <summary>
        /// Rolls the trait table until a result the unit can still take: a trait it already has is re-rolled;
        /// Specialist stays on the table while either pick is left; Fast Learner can be used once.
        /// Returns null only if nothing at all is left to gain.
        /// </summary>
        public static ETraitRoll? RollTrait(IReadOnlySet<string> owned, Func<int> rollD6)
        {
            if (AvailableTraits(owned, includeFastLearner: true).Count == 0) return null;

            for (int attempt = 0; attempt < 200; attempt++)
            {
                var result = (ETraitRoll)Math.Clamp(rollD6(), 1, 6);
                bool takeable = result switch
                {
                    ETraitRoll.Agile => !owned.Contains(Agile),
                    ETraitRoll.Headstrong => !owned.Contains(Headstrong),
                    ETraitRoll.Specialist => !owned.Contains(MeleeSpecialist) || !owned.Contains(ShootingSpecialist),
                    ETraitRoll.Resilient => !owned.Contains(Resilient),
                    ETraitRoll.Elite => !owned.Contains(Elite),
                    ETraitRoll.FastLearner => !owned.Contains(FastLearner)
                        && AvailableTraits(owned, includeFastLearner: false).Count > 0,
                    _ => false,
                };
                if (takeable) return result;
            }
            return null;
        }

        /// <summary>
        /// Horde Mode kill hook (called from the unit-destroyed seam when the death has a killer): a
        /// defending unit that destroys an enemy unit earns XP and rolls a trait for every 5 XP crossed.
        /// </summary>
        public static async Task OnUnitDestroyed(IGameContext gameContext, IUnit dead, IUnit killer)
        {
            if (gameContext.Settings.GameMode != EGameMode.Horde) return;
            if (killer is not UnitData unit || !unit.GetIsAlive()) return;

            HashSet<PlayerID> defenders = Stages.HordePlacement.DefenderPlayers(gameContext.TableState);
            if (!defenders.Contains(unit.PlayerID) || defenders.Contains(dead.PlayerID)) return;

            bool joinedHero = dead is UnitData deadData && deadData.HasHero;
            bool soloHero = !joinedHero && dead.Models.Count == 1
                && dead.RuleDefinitions.Any(rule => rule.Definition == CoreRuleCatalog.Hero);
            int gain = XpForKill(joinedHero, soloHero);

            string what = joinedHero ? "a hero and its unit" : soloHero ? "a hero" : "a unit";
            gameContext.Log($"Horde Mode: {unit.Name} destroyed {what} ({dead.Name}).");
            await AddXp(gameContext, unit, gain, askForChoices: true);
        }

        /// <summary>
        /// Adds XP (capped at 30) and rolls a trait for every 5 XP crossed. With
        /// <paramref name="askForChoices"/> false, a Specialist / Fast Learner pick is made automatically
        /// (the Game Master's XP button uses that, so it never stacks a prompt on top of the current one).
        /// </summary>
        public static async Task AddXp(IGameContext gameContext, UnitData unit, int amount, bool askForChoices)
        {
            int before = unit.Tokens.GetTokenCount(TokenType.HordeXP);
            int after = Math.Min(MaxXp, before + amount);
            if (after <= before) return;   // already at the cap

            unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.HordeXP, after - before));
            gameContext.Log($"Horde Mode: {unit.Name} +{after - before} XP ({after} XP).");
            await gameContext.Announce($"{unit.Name} +{after - before} XP", new TextColor(130, 220, 255, 255));

            for (int level = 0; level < LevelUps(before, after); level++)
                await LevelUp(gameContext, unit, askForChoices);
        }

        private static async Task LevelUp(IGameContext gameContext, UnitData unit, bool askForChoices)
        {
            HashSet<string> owned = OwnedTraits(unit);
            ETraitRoll? roll = RollTrait(owned, () =>
            {
                int face = Stages.HordePlacement.RollD6(gameContext);
                gameContext.Log($"  {unit.Name} rolls for a trait: {face}.");
                return face;
            });
            if (roll == null)
            {
                gameContext.Log($"  {unit.Name} has every trait already.");
                return;
            }

            string trait;
            switch (roll.Value)
            {
                case ETraitRoll.Agile: trait = Agile; break;
                case ETraitRoll.Headstrong: trait = Headstrong; break;
                case ETraitRoll.Resilient: trait = Resilient; break;
                case ETraitRoll.Elite: trait = Elite; break;
                case ETraitRoll.Specialist:
                    trait = await ChooseTrait(gameContext, unit,
                        new[] { MeleeSpecialist, ShootingSpecialist }.Where(t => !owned.Contains(t)).ToList(),
                        $"{unit.Name} rolled Specialist - pick melee or shooting.", askForChoices);
                    break;
                default: // Fast Learner: pick any other trait still available.
                    Grant(unit, FastLearner);
                    trait = await ChooseTrait(gameContext, unit,
                        AvailableTraits(OwnedTraits(unit), includeFastLearner: false),
                        $"{unit.Name} rolled Fast Learner - pick any trait.", askForChoices);
                    break;
            }

            Grant(unit, trait);
            string shortName = trait.Substring("Trait: ".Length);
            gameContext.Log($"Horde Mode: {unit.Name} levels up and gains {shortName}.");
            await gameContext.Announce($"{unit.Name} levels up: {shortName}!", new TextColor(130, 220, 255, 255),
                EBannerTier.Headline);
        }

        // Owner picks from the remaining options; a single option needs no question.
        private static async Task<string> ChooseTrait(IGameContext gameContext, UnitData unit,
            IReadOnlyList<string> options, string instructions, bool ask)
        {
            if (options.Count == 1 || !ask) return options[0];

            List<string> labels = options.Select(o => o.Substring("Trait: ".Length)).ToList();
            var descriptions = new Dictionary<string, string>();
            for (int i = 0; i < options.Count; i++)
                descriptions[labels[i]] = CoreRuleCatalog.All.FirstOrDefault(d => d.Name == options[i])?.Description ?? "";

            var request = new StringSelectionRequest(unit.PlayerID, instructions, labels,
                Array.Empty<StringSelectionRequest.InvalidOption>(), descriptions,
                displayName: $"Choosing a trait for {unit.Name}");
            string picked = await gameContext.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
            int index = labels.IndexOf(picked);
            return options[index >= 0 ? index : 0];
        }

        /// <summary>Gives the unit a trait as a permanent rule grant.</summary>
        public static void Grant(UnitData unit, string traitRuleName) =>
            unit.Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.RuleGrant(traitRuleName, ELifetime.UntilEndOfGame),
                OwnerUnitID: unit.ID));

        /// <summary>
        /// The Elite trait's re-roll: true (and marks it spent) if <paramref name="unit"/> has Elite and
        /// has not used it yet.
        /// <para>
        /// #409 (OPR 3.6.0): once per GAME, not once per round - the trait was halved in strength. The
        /// HordeEliteUsed token's clear trigger is ManualOnly for exactly that reason, so nothing sweeps
        /// it at end of round.
        /// </para>
        /// <para>
        /// Still narrower than the book in one respect, which 3.6 did not change: the rulebook says
        /// "re-roll one ROLL of any kind", and this is wired only into the morale test. Tracked on #409
        /// rather than cut silently; widening it needs a general per-unit re-roll offer the engine does
        /// not have yet.
        /// </para>
        /// </summary>
        public static bool TryUseElite(IUnit unit)
        {
            if (!CanUseElite(unit)) return false;
            SpendElite(unit);
            return true;
        }

        /// <summary>
        /// Whether <paramref name="unit"/> has Elite and has not spent it yet - WITHOUT spending it.
        /// <para>
        /// Split out from <see cref="TryUseElite"/> so the re-roll can be OFFERED before it is paid for.
        /// Kai's standing rule is that the game never auto-picks, and a once-per-game resource spent
        /// without asking is the sharpest version of that: the unit's single re-roll could be burned on
        /// a morale test the player would happily have failed. Checking and spending in one call made
        /// the prompt impossible - declining would already have cost the use.
        /// </para>
        /// </summary>
        public static bool CanUseElite(IUnit unit) =>
            !unit.Tokens.HasToken(TokenType.HordeEliteUsed) && OwnedTraits(unit).Contains(Elite);

        /// <summary>Marks Elite spent. Only call after the player has agreed to use it.</summary>
        public static void SpendElite(IUnit unit, bool perRound = false) =>
            // #463: once per ROUND in Firefight and Skirmish - the same marker, swept at round end.
            unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.HordeEliteUsed,
                clearOverride: perRound ? new TokenClearTrigger.RoundEnd() : null));
    }
}
