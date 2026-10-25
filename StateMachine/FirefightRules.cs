using System.Threading.Tasks;
using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Utilities;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;


namespace FDG.Stages
{
    /// <summary>
    /// GF: Firefight (v3.5.1) rules that differ from the full game. Firefight has no morale from wounds or
    /// lost melees: instead, at the end of every round, an army down to half or fewer of its starting units
    /// tests morale with every unit. A failure makes the unit Shaken, a failure while already Shaken routs it.
    /// </summary>
    public static class FirefightRules
    {
        /// <summary>True when the current game is a skirmish game (GF: Firefight / AoF: Skirmish).</summary>
        public static bool IsSkirmishGame(IGameContext c) => GameSystemInfo.IsSkirmish(c.Settings.GameSystem);

        /// <summary>Wound markers on a unit's last model (Firefight wound effects). Kept on the unit.</summary>
        public static readonly TokenType WoundMarker = new("FirefightWoundMarker");

        /// <summary>
        /// Firefight wound effects. Each wound kills one model until one is left; the last model then gets a
        /// wound marker per wound. Once it has at least X markers (X = its Tough value, 1 if none), roll one die
        /// and add the markers (+1 if Shaken, +Fear(X) of a melee attacker): 5+X or more Knocks it Out, anything
        /// lower makes the unit Shaken - and the attacker then rolls, on a 4+ it may push the model 2".
        /// <paramref name="order"/> is which models die first (the wound assignment's choice). Returns models killed.
        /// </summary>
        public static async Task<int> ApplyWounds(IGameContext c, IUnit unit, float woundsIn, IUnit? attacker,
            bool melee, IEnumerable<IModel>? order = null)
        {
            int wounds = (int)MathF.Round(woundsIn, MidpointRounding.AwayFromZero);
            if (wounds <= 0 || !unit.GetIsAlive()) return 0;
            wounds -= FirefightCampaign.HerbalistIgnores(c, unit, wounds);
            if (wounds <= 0) return 0;

            int killed = 0;
            List<IModel> living = (order ?? Enumerable.Empty<IModel>()).Concat(unit.Models)
                .Distinct().Where(m => m.GetIsAlive()).ToList();
            while (wounds > 0 && living.Count > 1)
            {
                IModel model = living[0];
                living.RemoveAt(0);
                model.DealWounds(model.TotalWounds - model.WoundsDealt);
                killed++;
                wounds--;
                await c.Presenter.Present(new ModelDiedBeat(model.ID, unit.ID, unit.Name, model.Position, overlap: wounds > 0));
            }
            if (wounds == 0) { c.Log($"{unit.Name} loses {killed} model(s)."); return killed; }

            IModel last = living[0];
            unit.Tokens.AddToken(new Token(WoundMarker, wounds, new TokenClearTrigger.ManualOnly()));
            int markers = unit.Tokens.GetTokenCount(WoundMarker);
            int tough = Math.Max(1, (int)MathF.Round(last.TotalWounds));
            await c.Presenter.Present(new ModelWoundedBeat(last.ID, last.Position, overlap: false));
            if (markers < tough)
            {
                c.Log($"{unit.Name}: {markers} wound marker(s), Tough({tough}) - no wound effect roll yet.");
                return killed;
            }

            bool shaken = unit.Tokens.HasToken(TokenType.Shaken);
            int fear = melee && attacker != null ? FearValue(attacker) : 0;
            int campaign = FirefightCampaign.WoundRollModifier(c, unit, attacker, melee);
            int die = c.DiceRoller.RollDecisiveFace();
            int total = die + markers + (shaken ? 1 : 0) + fear + campaign;
            int needed = 5 + tough;
            string parts = $"rolled {die} + {markers} marker(s)" + (shaken ? " + 1 Shaken" : "") + (fear > 0 ? $" + {fear} Fear" : "")
                + (campaign != 0 ? $" {campaign:+0;-0} campaign skills" : "");
            if (total >= needed)
            {
                c.Log($"Wound effect: {unit.Name} {parts} = {total} (out on {needed}+) - Knocked Out.");
                last.DealWounds(last.TotalWounds - last.WoundsDealt);
                killed++;
                await c.Presenter.Present(new ModelDiedBeat(last.ID, unit.ID, unit.Name, last.Position, overlap: false));
                return killed;
            }

            c.Log($"Wound effect: {unit.Name} {parts} = {total} (out on {needed}+) - Shaken.");
            DataBinding<UnitData>? binding = c.GameDataStore.GetAllDataBindings<UnitData>()
                .FirstOrDefault(b => ReferenceEquals(b.GetValue(), unit));
            if (binding != null) await MoraleUtilities.ApplyShakenWithPresentation(c, binding);

            if (attacker != null && attacker.GetIsAlive())
            {
                int push = c.DiceRoller.RollDecisiveFace();
                if (push >= 4)
                {
                    c.Log($"Pushing: {attacker.Name} rolled {push} - may push {unit.Name} up to 2\".");
                    await c.Announce($"Pushing: move {unit.Name} up to 2\".");
                    await new GameOperationServices(c).MoveUnit(unit, 2f, isOptional: true, controller: attacker.PlayerID);
                }
                else c.Log($"Pushing: {attacker.Name} rolled {push} - no push.");
            }
            return killed;
        }

        /// <summary>"Wound markers: 2 (rolls at 3, Knocked Out on 8+)" for the unit cards; null when it has none.</summary>
        public static string? MarkerSummary(IUnit unit)
        {
            int markers = unit.Tokens.GetTokenCount(WoundMarker);
            if (markers <= 0) return null;
            IModel? last = unit.Models.FirstOrDefault(m => m.GetIsAlive());
            int tough = last == null ? 1 : Math.Max(1, (int)MathF.Round(last.TotalWounds));
            return markers < tough
                ? $"Wound markers: {markers} (rolls from {tough}, Knocked Out on {5 + tough}+)"
                : $"Wound markers: {markers} (next wound rolls, Knocked Out on {5 + tough}+)";
        }

        /// <summary>Removes one wound marker (Game Master heal). True if there was one.</summary>
        public static bool RemoveMarker(IUnit unit) => unit.Tokens.RemoveTokens(WoundMarker, 1) > 0;

        /// <summary>The attacker's Fear(X) value (0 if it has no Fear).</summary>
        public static int FearValue(IUnit attacker)
        {
            foreach (var rule in attacker.RuleDefinitions.Concat(attacker.Models.SelectMany(m => m.RuleDefinitions)))
            {
                if (!string.Equals(rule.Definition.Name, "Fear", StringComparison.OrdinalIgnoreCase)) continue;
                return rule.Arguments.Count > 0 && rule.Arguments[0] is RuleArgument.Int x ? x.Value : 1;
            }
            return 0;
        }

        /// <summary>The end-of-round army morale check. Does nothing outside skirmish games.</summary>
        public static async Task ArmyMoraleAtRoundEnd(IGameContext c)
        {
            if (!IsSkirmishGame(c)) return;
            foreach (ArmyData army in c.GameDataStore.GetAllValues<ArmyData>().ToList())
            {
                int starting = army.UnitBindings.Count;
                var alive = army.UnitBindings.Where(b => b.GetValue().GetIsAlive()).ToList();
                if (starting == 0 || alive.Count == 0 || alive.Count * 2 > starting) continue;

                c.Log($"Army morale: {army.ArmyName} is down to {alive.Count} of {starting} units - every unit tests.");
                foreach (DataBinding<UnitData> binding in alive)
                {
                    UnitData unit = binding.GetValue();
                    if (!unit.GetIsAlive()) continue;
                    bool wasShaken = unit.Tokens.HasToken(TokenType.Shaken);
                    MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                        c, unit, HeroStatRules.GetMoraleQuality(unit), failureCausesShakenOrRout: true);
                    if (outcome.Passed) { c.Log($"  {unit.Name} passes."); continue; }
                    if (wasShaken)
                    {
                        c.Log($"  {unit.Name} fails while Shaken and routs.");
                        await MoraleUtilities.RoutWithPresentation(c, binding);
                    }
                    else
                    {
                        c.Log($"  {unit.Name} fails and is Shaken.");
                        await MoraleUtilities.ApplyShakenWithPresentation(c, binding);
                    }
                }
            }
        }
    }
}
