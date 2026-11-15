
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    public class ChooseMeleeDefenderStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnDefenderChosen;
        public StageBinding BackToChooseAction;

        public ChooseMeleeDefenderStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent)
            : base(gameContext, parent)
        {
            OnDefenderChosen = new StageBinding(this);
            BackToChooseAction = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            GameContext.LogDebug("Entered Choose Melee Defender.");

            List<ActionChoice> choices = new List<ActionChoice>();

            PlayerID attackingPlayer = context.AttackingUnit.PlayerID();
            
            List<CancellableSelectionRequest<UnitData>.ValidOption> validDefenders = new List<CancellableSelectionRequest<UnitData>.ValidOption>();
            List<CancellableSelectionRequest<UnitData>.InvalidOption> invalidDefenders = new List<CancellableSelectionRequest<UnitData>.InvalidOption>();

            //There has GOT to be a better way to look up what team a player is on.
            TeamData? playerTeam = GameContext.GameDataStore.GetAllValues<TeamData>()
                .FirstOrDefault(teamData => teamData.IsPlayerOnTeam(attackingPlayer));

            IReadOnlyList<PlayerID> alliedPlayers;

            if (playerTeam == null)
            {
                alliedPlayers = new List<PlayerID>() { attackingPlayer }; 
            }
            else
            {
                alliedPlayers = playerTeam.Players;
            }


            foreach(ArmyData army in GameContext.GameDataStore.GetAllValues<ArmyData>()
                .Where(a => alliedPlayers.Contains(a.PlayerID) == false))
            {
                foreach(DataBinding<UnitData> potentialDefender in army.UnitBindings)
                {
                    // #029: an Aircraft can't be charged / moved into base contact with, so it's never a valid
                    // melee defender even if a model strayed within the melee cylinder.
                    if (Rules.Dispatch.AircraftRules.IsAircraft(potentialDefender.GetValue()))
                    {
                        invalidDefenders.Add(new CancellableSelectionRequest<UnitData>.InvalidOption(potentialDefender,
                            potentialDefender.GetValue().Name, "Aircraft can't be charged."));
                        continue;
                    }

                    // Star Quest Infiltrating Step: this hero can't be targeted this round.
                    if (QuestFlags.CannotBeTargeted(GameContext.TableState.Progress.Quest, potentialDefender.GetValue(), context.AttackingUnit.GetValue()))
                    {
                        invalidDefenders.Add(new CancellableSelectionRequest<UnitData>.InvalidOption(potentialDefender,
                            potentialDefender.GetValue().Name, QuestFlags.HiddenReason));
                        continue;
                    }

                    // #022: a unit is engageable only if some model pair is within the melee cylinder
                    // (2" horizontal AND 4" vertical) — the same gate the post-pile-in strike check uses,
                    // so we never offer a defender no model could actually strike.
                    // Extra Actions - Heavy Charge: the charge is at "a target that is between 3" and 6" away",
                    // so only the enemies that were that far when it was declared may be picked.
                    if (context is CombatActionContext { ActivationContext.HeavyChargeTargets: { } heavyTargets }
                        && context.ChargingUnit != null && !heavyTargets.Contains(potentialDefender.GetValue().ID))
                    {
                        invalidDefenders.Add(new CancellableSelectionRequest<UnitData>.InvalidOption(potentialDefender,
                            potentialDefender.GetValue().Name, "Heavy Charge - this unit was not 3\" to 6\" away."));
                        continue;
                    }

                    if(SolidBuildings.InMeleeRange(context.AttackingUnit.GetValue(), potentialDefender.GetValue(),
                           GameContext.TableState.Terrain.Objects.ToList()))
                    {
                        validDefenders.Add(new CancellableSelectionRequest<UnitData>.ValidOption(potentialDefender, potentialDefender.GetValue().Name));
                    }
                    else
                    {
                        invalidDefenders.Add(new CancellableSelectionRequest<UnitData>.InvalidOption(potentialDefender, potentialDefender.GetValue().Name,
                            "This unit is too far away."));
                    }
                }
            }

            // #197 Instinctive: a compelled unit's charge must land on the closest valid defender. The
            // narrowing happens here - before the request is issued - so every resolver, human or AI,
            // simply has no non-compliant option (the same shape as the ranged chooser's gating). Ties
            // within the float tolerance all stay valid; the choice among equals is the player's.
            string? compelSource = Rules.Dispatch.CapabilityRuleQueries.IsCompelledToAttackClosest(
                context.AttackingUnit.GetValue(), GameContext.RuleEvaluator);
            if (compelSource != null && validDefenders.Count > 1)
            {
                float closest = float.MaxValue;
                Dictionary<DataBinding<UnitData>, float> distances = new Dictionary<DataBinding<UnitData>, float>();
                foreach (CancellableSelectionRequest<UnitData>.ValidOption option in validDefenders)
                {
                    float distance = UnitCompareUtilities.MinDistanceBetweenUnits(
                        context.AttackingUnit.GetValue(), option.Option.GetValue(), out _, out _,
                        includeVertical: false);
                    distances[option.Option] = distance;
                    if (distance < closest) closest = distance;
                }

                for (int i = validDefenders.Count - 1; i >= 0; i--)
                {
                    if (distances[validDefenders[i].Option] > closest + 0.001f)
                    {
                        invalidDefenders.Add(new CancellableSelectionRequest<UnitData>.InvalidOption(
                            validDefenders[i].Option, validDefenders[i].Name,
                            $"{compelSource} - must attack the closest target."));
                        validDefenders.RemoveAt(i);
                    }
                }
            }

            async Task ChooseDefender(DataBinding<UnitData> defender)
            {
                //Set the defender on the context.
                GameContext.Log($"Chose {defender.GetValue().Name} as defender.");
                KingOfTheHill.OnTargetPicked(GameContext, attackingPlayer, defender.GetValue());
                context.SetDefender(defender);
                await OnDefenderChosen.Activate(context);
            }

            if (validDefenders.Count == 0)
            {
                GameContext.Log("No potential melee units found.");
                await BackToChooseAction.Activate(context);
                return;
            }

            // Ask even when there is exactly one valid defender. This prompt carries the charge's only Back
            // button, and nothing has been mutated yet (impact hits and the pile-in move both come later),
            // so auto-selecting the sole defender used to commit the player to a charge they may have
            // clicked by mistake - with no way back.
            // #191 A4-3: its own request TYPE so AI resolvers replace exactly this decision.
            ChooseMeleeDefenderRequest request = new ChooseMeleeDefenderRequest(attackingPlayer, "Choose defending unit",
                validDefenders, invalidDefenders);

            CancellableResult<DataBinding<UnitData>> defenderResult
                = await GameContext.PlayerRequester
                .RequestDecision<ChooseMeleeDefenderRequest, CancellableResult<DataBinding<UnitData>>>(request);

            if (defenderResult is Cancelled<DataBinding<UnitData>>)
            {
                await BackToChooseAction.Activate(context);
                return;
            }

            DataBinding<UnitData> picked = ((Selected<DataBinding<UnitData>>)defenderResult).Value;
            await ChooseDefender(await DefenderPicksInsideBuilding(context, picked, validDefenders));
        }

        /// <summary>
        /// #460 Simplified Terrain, Solid Buildings: "When charging units inside a building, only one unit inside of
        /// it is targeted, and the defender may choose which." The attacker picked a unit inside a building; when
        /// its side has more than one unit in there, the defending player names the one that fights.
        /// </summary>
        private async Task<DataBinding<UnitData>> DefenderPicksInsideBuilding(ICombatActionContext context,
            DataBinding<UnitData> picked, List<CancellableSelectionRequest<UnitData>.ValidOption> valid)
        {
            var terrain = GameContext.TableState.Terrain.Objects.ToList();
            UnitData attacker = context.AttackingUnit.GetValue();
            if (SolidBuildings.EngagedThrough(attacker, picked.GetValue(), terrain) is not { } building) return picked;
            if (!ReferenceEquals(SolidBuildings.Holding(picked.GetValue(), terrain), building)) return picked;

            var inside = valid.Select(v => v.Option)
                .Where(u => ReferenceEquals(SolidBuildings.Holding(u.GetValue(), terrain), building)
                            && u.GetValue().PlayerID.Equals(picked.GetValue().PlayerID))
                .ToList();
            if (inside.Count < 2) return picked;

            var names = inside.Select((u, i) => $"{u.GetValue().Name} ({i + 1})").ToList();
            var request = new StringSelectionRequest(picked.GetValue().PlayerID,
                $"{attacker.Name} charges your building. Pick which unit inside fights it.", names,
                Array.Empty<StringSelectionRequest.InvalidOption>(), displayName: "Choosing who defends the building")
            {
                Recommended = names[inside.IndexOf(picked)],
            };
            string answer = await GameContext.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
            int index = names.IndexOf(answer);
            DataBinding<UnitData> chosen = index >= 0 ? inside[index] : picked;
            if (!ReferenceEquals(chosen, picked))
                GameContext.Log($"The defender sends {chosen.GetValue().Name} to hold the building against {attacker.Name}.");
            return chosen;
        }
    }
}
