using System.Text.Json;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;
using FDG.StateMachine;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// Horde Mode's shop (OPR v3.6.0, End of Round): after a point-reward round pays out, the defenders
    /// may spend points to recruit new units (deployed fully within 9" of an objective they seized), to
    /// buy upgrades for units in range, and - new in 3.6 (#409) - to REINFORCE units in range that are
    /// down to half strength. Each defending player is offered the shop in turn; bots keep their points.
    /// </summary>
    public static class HordeShop
    {
        private static readonly TextColor ShopColor = new TextColor(255, 215, 0, 255);

        /// <summary>Offers the shop to every defending player. <paramref name="seized"/> are the objectives
        /// the defenders held this wave; with none there is nowhere to deploy, so the shop stays closed.</summary>
        public static async Task Run(IGameContext gameContext, IReadOnlyList<ObjectiveData> seized, int wave)
        {
            if (seized.Count == 0)
            {
                gameContext.Log("Horde Mode: no objective was seized this wave, so there is nowhere to deploy recruits - the shop stays closed.");
                return;
            }

            var objectives = seized
                .Select(o => new HordeShopObjective(NumberOf(o), o.Position.x, o.Position.z, o.IsHordePriority))
                .OrderBy(o => o.Number)
                .ToList();

            HashSet<PlayerID> defenders = HordePlacement.DefenderPlayers(gameContext.TableState);
            foreach (ArmyData army in gameContext.GameDataStore.GetAllValues<ArmyData>()
                         .Where(a => defenders.Contains(a.PlayerID)).ToList())
            {
                int points = GameProgressUtilities.TryGetProgress(gameContext.GameDataStore)?.Horde?.PointsAvailable ?? 0;
                List<HordeShopUnit> upgradable = UpgradableUnits(gameContext, army, objectives);
                // Points buy recruits and reinforcements; an upgrade can also be worth less than nothing
                // (it pays points back), so a unit in range keeps the shop open even with an empty purse.
                if (points <= 0 && !upgradable.Any(u => u.NotEligibleReason == null))
                {
                    gameContext.Log("Horde Mode: no points to spend and no unit near a seized objective - the shop stays closed.");
                    continue;
                }

                var request = new HordeShopRequest(army.PlayerID, wave, points, army.Faction, army.ArmyName, objectives)
                {
                    AllowedGameSystems = gameContext.Settings.AllowedGameSystems,
                    Upgradable = upgradable,
                };
                HordeShopReply reply = await gameContext.PlayerRequester
                    .RequestDecision<HordeShopRequest, HordeShopReply>(request);

                await ApplyOrder(gameContext, army.PlayerID, reply, objectives);
            }
        }

        /// <summary>Spends the points and deploys the recruits a shop reply names. Anything that doesn't add
        /// up (too expensive, unreadable, an objective that wasn't seized) is refused with a log line.</summary>
        internal static async Task ApplyOrder(IGameContext gameContext, PlayerID player, HordeShopReply reply,
            IReadOnlyList<HordeShopObjective> objectives)
        {
            ArmyListFile? recruits = ReadList(gameContext, reply.RecruitsJson);
            ArmyListFile? upgrades = ReadList(gameContext, reply.UpgradesJson);
            bool hasRecruits = recruits != null && recruits.Units.Count > 0;
            bool hasUpgrades = upgrades != null && upgrades.Units.Count > 0;
            bool hasReinforcements = reply.ReinforceUnitIds.Count > 0;
            if (!hasRecruits && !hasUpgrades && !hasReinforcements)
            {
                gameContext.Log($"Horde Mode: {gameContext.GetPlayerName(player)} buys nothing.");
                return;
            }

            DataBinding<ArmyData>? armyBinding = gameContext.GameDataStore.GetAllDataBindings<ArmyData>()
                .FirstOrDefault(b => b.GetValue().PlayerID == player);
            if (armyBinding == null) return;
            ArmyData army = armyBinding.GetValue();

            // Upgrades only for units still in range right now; each costs the difference to its current price.
            HashSet<Guid> eligible = UpgradableUnits(gameContext, army, objectives)
                .Where(u => u.NotEligibleReason == null).Select(u => u.UnitId).ToHashSet();
            var upgradeJobs = new List<(DataBinding<UnitData> Binding, UnitFileEntry Entry, string? Loadout)>();
            if (hasUpgrades)
            {
                for (int i = 0; i < upgrades!.Units.Count && i < reply.UpgradeUnitIds.Count; i++)
                {
                    Guid id = reply.UpgradeUnitIds[i];
                    DataBinding<UnitData>? binding = army.UnitBindings.FirstOrDefault(b => b.GetValue().ID.ID == id);
                    if (binding == null || !eligible.Contains(id))
                    {
                        gameContext.Log($"Horde Mode: {upgrades.Units[i].Name} isn't within {FDG.StateMachine.HordeRules.ShopRangeInches:0}\" of a seized objective any more - upgrade skipped.");
                        continue;
                    }
                    // A loadout can't shrink the unit on the table (a combined unit priced as a single one
                    // would refund points while keeping every model).
                    if (upgrades.Units[i].ModelCount < OwnModels(binding.GetValue()).Count)
                    {
                        gameContext.Log($"Horde Mode: the new loadout for {binding.GetValue().Name} has fewer models than the unit - upgrade skipped.");
                        continue;
                    }
                    string? loadout = i < reply.UpgradeLoadouts.Count ? reply.UpgradeLoadouts[i] : null;
                    upgradeJobs.Add((binding, upgrades.Units[i], loadout));
                }
            }

            // #409 Reinforcing Units: collect the units the reply named, re-checking eligibility against
            // the live table exactly as upgrades do - the shop's list was built before the player spent any
            // time in it, and a reply is untrusted input either way.
            var reinforceJobs = new List<(DataBinding<UnitData> Binding, int Cost)>();
            if (hasReinforcements)
            {
                Dictionary<Guid, HordeShopUnit> byId = UpgradableUnits(gameContext, army, objectives)
                    .ToDictionary(u => u.UnitId);
                foreach (Guid id in reply.ReinforceUnitIds.Distinct())
                {
                    DataBinding<UnitData>? binding = army.UnitBindings.FirstOrDefault(b => b.GetValue().ID.ID == id);
                    byId.TryGetValue(id, out HordeShopUnit? offer);
                    if (binding == null || offer == null || offer.NotReinforceableReason != null)
                    {
                        string name = binding?.GetValue().Name ?? "A unit";
                        gameContext.Log($"Horde Mode: {name} can't be reinforced right now"
                            + (offer?.NotReinforceableReason is string why ? $" - {why}" : ".")
                            + " - reinforcement skipped.");
                        continue;
                    }
                    reinforceJobs.Add((binding, offer.ReinforceCost));
                }
            }

            int recruitCost = hasRecruits ? recruits!.Units.Sum(u => Math.Max(0, u.PointCost)) : 0;
            int upgradeCost = upgradeJobs.Sum(job => job.Entry.PointCost - OwnCost(job.Binding.GetValue()));
            int reinforceCost = reinforceJobs.Sum(job => job.Cost);
            int cost = recruitCost + upgradeCost + reinforceCost;
            int available = GameProgressUtilities.TryGetProgress(gameContext.GameDataStore)?.Horde?.PointsAvailable ?? 0;
            if (cost > available)
            {
                gameContext.Log($"Horde Mode: the order costs {cost} points but only {available} are available - nothing bought.");
                return;
            }

            // The new units' and loadouts' rules and spells come from the faction book: make sure the game
            // knows every one of them (and that a resumed game will too), without disturbing rules in play.
            IRuleResolver resolver = gameContext.RuleEvaluator.RuleResolver ?? new RuleResolver();
            if (hasRecruits) MergeRuleData(gameContext, army, recruits!, resolver);
            if (hasUpgrades) MergeRuleData(gameContext, army, upgrades!, resolver);

            GameProgressUtilities.SpendHordePoints(gameContext.GameDataStore, cost);
            gameContext.Log($"Horde Mode: {gameContext.GetPlayerName(player)} spends {cost} points " +
                $"({available - cost} left).");

            foreach ((DataBinding<UnitData> binding, UnitFileEntry entry, string? loadout) in upgradeJobs)
            {
                await ApplyUpgrade(gameContext, binding, entry, loadout, upgrades!, resolver);
            }

            // #409: reinforcements land after upgrades, so a unit that bought bigger models in the same
            // order gets its bodies back at the new profile rather than the old one.
            foreach ((DataBinding<UnitData> binding, int unitCost) in reinforceJobs)
            {
                UnitData unit = binding.GetValue();
                string restored = HordeReinforce.Apply(unit, OwnModels(unit));
                gameContext.Log($"Horde Mode: {unit.Name} reinforced for {unitCost} points ({restored}).");
                await gameContext.Announce($"{unit.Name} reinforced ({restored})", ShopColor);
            }

            if (!hasRecruits) return;

            // Build every recruit the way a deploying unit is built, then let heroes join their units.
            var built = new List<(UnitFileEntry Entry, UnitData Unit)>();
            var objectiveFor = new Dictionary<UnitData, HordeShopObjective>();
            for (int i = 0; i < recruits!.Units.Count; i++)
            {
                UnitFileEntry entry = recruits.Units[i];
                int number = i < reply.ObjectiveNumbers.Count ? reply.ObjectiveNumbers[i] : objectives[0].Number;
                HordeShopObjective objective = objectives.FirstOrDefault(o => o.Number == number) ?? objectives[0];

                var unit = new UnitData(player, entry, gameContext.GameDataStore, resolver,
                    recruits.DefaultRangedEffectSet, recruits.DefaultMeleeEffectSet);
                GameBootstrap.AttachRulesFromArmyList(unit, entry, resolver);
                unit.HordeLoadoutJson = i < reply.RecruitLoadouts.Count ? reply.RecruitLoadouts[i] : null;
                built.Add((entry, unit));
                objectiveFor[unit] = objective;
            }

            IReadOnlyList<UnitData> survivors = HeroJoinResolver.Apply(built, message => gameContext.Log("  " + message),
                CoreRulesProfile.For(gameContext).HeroJoinToughCap);
            gameContext.Log($"  Recruited: {string.Join(", ", recruits.Units.Select(u => $"{u.Name} ({u.PointCost} pts)"))}.");

            foreach (UnitData unit in survivors)
            {
                UnitCreationRules.Apply(unit, gameContext.RuleEvaluator);
                ReserveRules.PlaceInReserve(unit); // off the table until it is placed below
                DataReference reference = gameContext.GameDataStore.Create(unit);
                DataBinding<UnitData> binding = gameContext.GameDataStore.GetDataBinding<UnitData>(reference);
                army.UnitBindings.Add(binding);
            }
            gameContext.GameDataStore.SetValue(armyBinding.Reference, army);

            foreach (UnitData unit in survivors)
            {
                HordeShopObjective objective = objectiveFor[unit];
                await gameContext.Announce($"{unit.Name} recruited!", ShopColor);
                var anchor = new Float2(objective.X, objective.Z);
                Func<float, IBoundedZone> zone = radius => new TableCornerZone(anchor, radius,
                    HordePlacement.TableWidth, HordePlacement.TableHeight);
                string task = $"Deploy {unit.Name} within {FDG.StateMachine.HordeRules.ShopRangeInches:0}\" of objective {objective.Number}";
                float radius = await HordePlacement.RoomyRadius(gameContext, unit, zone, task);
                await HordePlacement.PlaceUnitNear(gameContext, unit, r => zone(Math.Max(r, radius)), task, task);
            }
        }

        private static ArmyListFile? ReadList(IGameContext gameContext, string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<ArmyListFile>(json, RuleJson.Options);
            }
            catch (Exception ex)
            {
                gameContext.Log($"Horde Mode: part of the shop order could not be read ({ex.Message}) - it is ignored.");
                return null;
            }
        }

        /// <summary>
        /// The player's units in play, each marked with whether it may buy upgrades now: alive, on the
        /// table, with a model within 9" of a seized objective. Ineligible ones carry the reason, so the
        /// shop can say why rather than leave them out.
        /// </summary>
        internal static List<HordeShopUnit> UpgradableUnits(IGameContext gameContext, ArmyData army,
            IReadOnlyList<HordeShopObjective> objectives)
        {
            var result = new List<HordeShopUnit>();
            foreach (UnitData unit in army.UnitBindings.Select(b => b.GetValue()))
            {
                if (!unit.GetIsAlive()) continue;

                string? reason = null;
                if (ReserveRules.IsInReserve(unit)) reason = "It isn't on the table.";
                else if (unit.Tokens.HasToken(Rules.Foundation.TokenType.EmbarkedIn)) reason = "It is inside a transport.";
                else
                {
                    bool near = unit.ModelBindings.Select(b => b.GetValue())
                        .Where(m => m.GetIsAlive())
                        // Measured from the closest point of the model's base, like every other distance
                        // in the game (rules accuracy pass; was model centre).
                        .Any(m => objectives.Any(o =>
                            BaseShapeGeometry.SurfaceDistanceToPoint2D(m.BaseShape, m.Position, m.Facing,
                                new Position(o.X, o.Z))
                            <= HordeRules.ShopRangeInches));
                    if (!near)
                        reason = $"It is more than {HordeRules.ShopRangeInches:0}\" from every objective your side seized this wave.";
                }

                // #409 Reinforcing Units (OPR 3.6.0): the unit must be down to half or less of its total
                // size (or Tough, for a single-model unit) AND within reach of a seized objective.
                // #437: that reach is its OWN band - 9" in all four books - not the upgrade/recruit range,
                // which the skirmish books scale to 6". Re-tested here rather than reusing `reason`.
                List<ModelData> own = OwnModels(unit);
                string? reinforceReason = ReinforceRangeReason(unit, objectives) ?? reason switch
                {
                    // Keep the non-spatial refusals (in reserve, inside a transport); drop the
                    // upgrade-range one, which does not apply to reinforcing.
                    null => null,
                    string why when why.StartsWith("It is more than", StringComparison.Ordinal) => null,
                    string why => why,
                };
                if (reinforceReason == null && !HordeReinforce.IsAtHalfStrength(
                        own.Count, own.Count(m => m.GetIsAlive()),
                        own.Sum(m => m.TotalWounds), own.Sum(m => m.RemainingWoundsBinding.GetValue())))
                {
                    reinforceReason = own.Count > 1
                        ? "It still has more than half its models."
                        : "It still has more than half its wounds.";
                }

                result.Add(new HordeShopUnit(unit.ID.ID, unit.Name, own.Count, OwnCost(unit),
                    unit.HordeLoadoutJson, DescribeGear(unit))
                {
                    NotEligibleReason = reason,
                    JoinedHeroName = unit.HeroAttachment?.Name,
                    ReinforceCost = HordeReinforce.Cost(OwnCost(unit)),
                    NotReinforceableReason = reinforceReason,
                    ReinforceRestores = reinforceReason != null ? null : DescribeRestore(own),
                });
            }
            return result;
        }

        /// <summary>
        /// #409: what reinforcing this unit would give back, in words, for the shop to show before the
        /// player commits. Mirrors what <see cref="HordeReinforce.Apply"/> will actually do.
        /// </summary>
        private static string DescribeRestore(IReadOnlyList<ModelData> own)
        {
            if (own.Count <= 1)
            {
                int wounds = HordeReinforce.WoundsRestored(
                    own[0].TotalWounds, own[0].RemainingWoundsBinding.GetValue());
                return $"+{wounds} wound{(wounds == 1 ? "" : "s")}";
            }

            int models = HordeReinforce.ModelsRestored(own.Count, own.Count(m => m.GetIsAlive()));
            return $"+{models} model{(models == 1 ? "" : "s")}";
        }

        /// <summary>The unit's own models - a joined hero's model left out.</summary>
        private static List<ModelData> OwnModels(UnitData unit) => unit.ModelBindings.Select(b => b.GetValue())
            .Where(m => unit.JoinedHeroModelId == null || m.ID != unit.JoinedHeroModelId.Value)
            .ToList();

        /// <summary>The unit's own points - a joined hero's cost (folded in at the join) left out.</summary>
        private static int OwnCost(UnitData unit) => unit.PointCost - (unit.HeroAttachment?.PointCost ?? 0);

        private static string DescribeGear(UnitData unit)
        {
            var weapons = OwnModels(unit)
                .SelectMany(m => m.Weapons)
                .GroupBy(w => w.Name)
                .Select(g => g.Count() > 1 ? $"{g.Count()}x {g.Key}" : g.Key);
            var rules = unit.RuleDefinitions.Select(r => r.Definition.Name)
                .Where(n => n != CoreRuleCatalog.DisembarkRuleName && n != CoreRuleCatalog.EmbarkRuleName)
                .Distinct();
            string text = string.Join(", ", weapons);
            string ruleText = string.Join(", ", rules);
            if (ruleText.Length > 0) text += $". Rules: {ruleText}";
            if (unit.HeroAttachment != null)
                text += $". Joined by {unit.HeroAttachment.Name ?? "a hero"} (keeps its own gear)";
            return text;
        }

        /// <summary>
        /// Gives a unit on the table a new loadout in place: same unit, same models where it already had
        /// them (positions, damage and casualties kept), new weapons, rules and stats; any extra models the
        /// upgrade adds are placed beside it. XP and traits stay - they are tokens on the unit.
        /// </summary>
        private static async Task ApplyUpgrade(IGameContext gameContext, DataBinding<UnitData> unitBinding,
            UnitFileEntry entry, string? loadout, ArmyListFile source, IRuleResolver resolver)
        {
            UnitData unit = unitBinding.GetValue();
            string oldName = unit.Name;

            // Build the new loadout off to the side (a scratch store), so nothing half-made touches the table.
            GameDataStore scratch = GameDataStore.GameDataStoreBuilder.GetDefault();
            var fresh = new UnitData(unit.PlayerID, entry, scratch, resolver,
                source.DefaultRangedEffectSet, source.DefaultMeleeEffectSet);
            GameBootstrap.AttachRulesFromArmyList(fresh, entry, resolver);
            List<ModelData> freshModels = fresh.ModelBindings.Select(b => b.GetValue()).ToList();

            int heroCost = unit.HeroAttachment?.PointCost ?? 0;
            List<ModelData> oldModels = OwnModels(unit);
            List<float> dealt = oldModels.Select(m => m.WoundsDealt).ToList();
            List<bool> dead = oldModels.Select(m => !m.GetIsAlive()).ToList();
            ModelData? heroModel = unit.ModelBindings.Select(b => b.GetValue())
                .FirstOrDefault(m => unit.JoinedHeroModelId != null && m.ID == unit.JoinedHeroModelId.Value);
            float heroDealt = heroModel?.WoundsDealt ?? 0f;
            bool heroDead = heroModel != null && !heroModel.GetIsAlive();

            int shared = Math.Min(oldModels.Count, freshModels.Count);
            for (int i = 0; i < shared; i++)
            {
                oldModels[i].Weapons = new List<Weapon>(freshModels[i].Weapons);
                oldModels[i].SetMaxWounds((int)MathF.Max(1f, freshModels[i].TotalWounds));
            }

            // Models the upgrade adds join the unit off-table, then get placed below.
            var added = new List<DataBinding<ModelData>>();
            for (int i = shared; i < freshModels.Count; i++)
            {
                ModelData template = freshModels[i];
                var model = new ModelData(template.BaseShape, new List<Weapon>(template.Weapons), new Position(),
                    gameContext.GameDataStore);
                DataBinding<ModelData> binding = gameContext.GameDataStore.GetDataBinding<ModelData>(
                    gameContext.GameDataStore.Create(model));
                unit.ModelBindings.Add(binding);
                added.Add(binding);
            }

            unit.Name = entry.Name;
            unit.Quality = entry.Quality;
            unit.Defense = entry.Defense;
            unit.PointCost = entry.PointCost + heroCost;
            unit.HordeLoadoutJson = loadout;
            unit.ReplaceRuleDefinitions(fresh.RuleDefinitions);

            // Creation-time effects (Tough, auras, Armor) come from the new rules: clear the old auras first
            // so they don't double up, then apply - and put the damage and casualties back on top.
            foreach (Rules.Tokens.Token token in unit.Tokens.GetAllTokens(Rules.Foundation.TokenType.RuleGrant).ToList())
            {
                if (token.Payload is Rules.Tokens.TokenPayload.RuleGrant { Lifetime: Rules.Foundation.ELifetime.Aura })
                    unit.Tokens.RemoveTokensWithPayload(Rules.Foundation.TokenType.RuleGrant, token.OwnerUnitID,
                        token.Payload, token.Count);
            }
            UnitCreationRules.Apply(unit, gameContext.RuleEvaluator);
            for (int i = 0; i < shared; i++)
            {
                ModelData model = oldModels[i];
                if (dead[i]) model.DealWounds(model.TotalWounds);
                else if (dealt[i] > 0f) model.DealWounds(MathF.Min(dealt[i], model.TotalWounds - 1f));
            }
            // The creation pass re-sets a joined hero's wounds too: put its damage back.
            if (heroModel != null)
            {
                if (heroDead) heroModel.DealWounds(heroModel.TotalWounds);
                else if (heroDealt > 0f) heroModel.DealWounds(MathF.Min(heroDealt, heroModel.TotalWounds - 1f));
            }
            unit.RewireModelWoundSubscriptions();

            gameContext.GameDataStore.SetValue(unitBinding.Reference, unit);
            foreach (DataBinding<ModelData> binding in unit.ModelBindings)
                gameContext.GameDataStore.SetValue(binding.Reference, binding.GetValue());

            gameContext.Log($"  {oldName} upgraded: {DescribeGear(unit)}.");
            await gameContext.Announce($"{unit.Name} upgraded!", ShopColor);

            if (added.Count > 0)
            {
                // The unit grew: let its owner set the whole unit down again where it stands.
                var alive = unit.ModelBindings.Select(b => b.GetValue())
                    .Where(m => m.GetIsAlive() && (m.Position.x != 0f || m.Position.z != 0f)).ToList();
                float cx = alive.Count > 0 ? alive.Average(m => m.Position.x) : HordePlacement.TableWidth / 2f;
                float cz = alive.Count > 0 ? alive.Average(m => m.Position.z) : HordePlacement.TableHeight / 2f;
                var anchor = new Float2(cx, cz);
                string task = $"Place {unit.Name} with its new models";
                List<DataBinding<ModelData>> living = unit.ModelBindings.Where(b => b.GetValue().GetIsAlive()).ToList();
                await HordePlacement.PlaceUnitNear(gameContext, unit,
                    radius => new TableCornerZone(anchor, radius * 2f / 3f, HordePlacement.TableWidth, HordePlacement.TableHeight),
                    task, task, living);
            }
        }

        private static void MergeRuleData(IGameContext gameContext, ArmyData army, ArmyListFile recruits,
            IRuleResolver resolver)
        {
            ArmyRuleDataPersistence.PersistedArmyRuleData? persisted = army.RestoreRuleData();
            var definitions = new List<SpecialRuleDefinition>(persisted?.RuleDefinitions ?? new List<SpecialRuleDefinition>());
            var spells = new List<SpellDefinition>(persisted?.Spells ?? new List<SpellDefinition>());
            var auxiliary = new List<UnitFileEntry>(persisted?.AuxiliaryUnits ?? new List<UnitFileEntry>());

            foreach (SpecialRuleDefinition definition in ArmyListRuleResolution.EffectiveDefinitions(recruits))
            {
                if (definitions.Any(d => string.Equals(d.Name, definition.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                definitions.Add(definition);
                if (resolver is RuleResolver concrete && !concrete.TryResolve(definition.Name, out _))
                    concrete.Register(definition);
            }

            bool spellsChanged = false;
            foreach (SpellDefinition spell in recruits.Spells)
            {
                if (spells.Any(s => string.Equals(s.Name, spell.Name, StringComparison.OrdinalIgnoreCase))) continue;
                spells.Add(spell);
                spellsChanged = true;
            }

            foreach (UnitFileEntry aux in recruits.AuxiliaryUnits ?? new List<UnitFileEntry>())
            {
                if (auxiliary.Any(a => string.Equals(a.Id, aux.Id, StringComparison.OrdinalIgnoreCase))) continue;
                auxiliary.Add(aux);
            }

            army.PersistRuleData(definitions, spells, auxiliary,
                persisted?.DefaultRangedEffectSet ?? recruits.DefaultRangedEffectSet,
                persisted?.DefaultMeleeEffectSet ?? recruits.DefaultMeleeEffectSet);
            if (spellsChanged) army.SetSpells(ArmyListSpellResolution.ResolveSpells(spells, resolver));
        }

        private static int NumberOf(IObjective objective) =>
            objective.HordeNumber
            ?? HordeRules.ObjectiveNumber(objective.Position, HordePlacement.TableWidth, HordePlacement.TableHeight);

        /// <summary>
        /// #437: the reinforce band is 9" in all four Horde books, even where upgrades and recruiting are
        /// 6". Returns the refusal text, or null when the unit is close enough to a seized objective.
        /// Measured from the closest point of a living model's base, like every other distance.
        /// </summary>
        private static string? ReinforceRangeReason(UnitData unit, IReadOnlyList<HordeShopObjective> objectives)
        {
            bool near = unit.ModelBindings.Select(b => b.GetValue())
                .Where(m => m.GetIsAlive())
                .Any(m => objectives.Any(o =>
                    BaseShapeGeometry.SurfaceDistanceToPoint2D(m.BaseShape, m.Position, m.Facing,
                        new Position(o.X, o.Z))
                    <= HordeRules.ReinforceRangeInches));
            return near ? null
                : $"It is more than {HordeRules.ReinforceRangeInches:0}\" from every objective your side "
                  + "seized this wave.";
        }
    }
}
