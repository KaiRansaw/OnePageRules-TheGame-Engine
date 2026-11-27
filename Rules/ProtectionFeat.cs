using System;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using FDG.Rules.Definitions;

namespace FDG
{
    /// <summary>
    /// Protection Feat (Army Forge 3.6.0, every system that has it - GF, Firefight, AoF, Skirmish and both Quests print the
    /// same words): "Once per game, when this unit takes wounds and most of its models have this rule, you may use this
    /// rule and roll one die per wound, and on a 5+ it is ignored."
    ///
    /// Asked at the moment the wounds land, the way the book reads - not braced in advance at the unit's own activation
    /// (the reading before 2026-11-27, which covered every attack until the unit's next activation). One prompt per
    /// batch of wounds while the feat is unspent; saying yes spends it, whatever the dice show.
    /// </summary>
    public static class ProtectionFeat
    {
        public const string Name = "Protection Feat";
        public const int IgnoreOn = 5;

        /// <summary>"Most of its models have this rule" - on the model, granted to the unit (Protection Feat Aura), or
        /// static on the unit for a native model (a joined hero does not inherit the host's rules).</summary>
        public static bool MostModelsHave(IUnit unit)
        {
            bool Named(Rules.Dispatch.ResolvedRule r) => string.Equals(r.Definition.Name, Name, StringComparison.OrdinalIgnoreCase);
            bool granted = RuleGrantQueries.UnitHasGrantedRule(unit, Name);
            bool unitStatic = unit.RuleDefinitions.Any(Named);
            int living = 0, carriers = 0;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                living++;
                bool joinedHero = unit.JoinedHeroModelId is ModelID hid && model.ID == hid;
                if (granted || model.RuleDefinitions.Any(Named) || (!joinedHero && unitStatic)) carriers++;
            }
            return living > 0 && carriers * 2 > living;
        }

        public static bool Spent(IUnit unit) => unit.Tokens.HasToken(TokenType.ProtectionFeatSpent);

        /// <summary>Can this unit use it on wounds now?</summary>
        public static bool Available(IUnit unit) => unit.GetIsAlive() && !Spent(unit) && MostModelsHave(unit);

        /// <summary>
        /// A bot keeps it for a blow worth stopping: 3 or more wounds at once, or enough to wipe the unit out.
        /// </summary>
        public static bool BotUses(IUnit unit, float wounds) =>
            wounds >= 3f - 0.001f || wounds >= unit.RemainingWounds - 0.001f;

        /// <summary>Asks whether to spend the feat on <paramref name="wounds"/> about to land on <paramref name="unit"/>;
        /// a yes spends it.</summary>
        public static async Task<bool> Ask(IGameContext c, IUnit unit, float wounds)
        {
            if (wounds <= 0f || !Available(unit)) return false;
            string count = $"{wounds:0.#} wound{(MathF.Abs(wounds - 1f) < 0.001f ? "" : "s")}";
            bool use = await c.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(unit.PlayerID,
                $"{unit.Name} takes {count}. Use Protection Feat (once per game) - one die per wound, {IgnoreOn}+ ignored?",
                defaultAnswer: BotUses(unit, wounds), displayName: Name));
            if (use) unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ProtectionFeatSpent));
            return use;
        }

        /// <summary>One die per wound, each <see cref="IgnoreOn"/>+ ignored. Returns how many were ignored.</summary>
        public static async Task<float> Roll(IGameContext c, IUnit unit, float wounds, string context)
        {
            if (wounds <= 0f) return 0f;
            IDiceResults roll = c.DiceRoller.Roll(wounds);
            float ignored = MathF.Min(wounds, roll.AtOrAbove(IgnoreOn));
            await c.Presenter.Present(DiceRolledBeat.From(roll, IgnoreOn, c.Settings.RandomnessType, Name,
                $"{ignored:0.##} ignored", category: ERollBeatCategory.Defense, context: context));
            c.Log($"Protection Feat: {context} ignores {ignored:0.##} of {wounds:0.##} wound(s).");
            return ignored;
        }

        /// <summary>Offers the feat against a plain pool of <paramref name="wounds"/> (dangerous terrain, a spell's
        /// self-inflicted wounds, ...). Returns the wounds left.</summary>
        public static async Task<float> Offer(IGameContext c, IUnit unit, float wounds)
        {
            if (!await Ask(c, unit, wounds)) return wounds;
            return MathF.Max(0f, wounds - await Roll(c, unit, wounds, unit.Name));
        }
    }
}
