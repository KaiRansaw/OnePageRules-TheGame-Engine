using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// A unit's special rules must survive a single network update, not just the catch-up snapshot. Found
    /// in a two-player network play-through (2026-11-06): on the client, Horse Marksmen lost Fast, Musician
    /// and every other rule the moment they moved - the host's per-unit update replaced the client's copy
    /// with a freshly deserialized one whose rule list was empty (the rules travel as a [JsonIgnore]'d list
    /// plus a blob, and only the snapshot path replayed the blob).
    /// </summary>
    [TestFixture]
    public class NetworkRuleSyncTests
    {
        [Test]
        public void UnitAndModelRules_SurviveASingleUpdate()
        {
            var host = GameDataStore.GameDataStoreBuilder.GetDefault();
            var client = GameDataStore.GameDataStoreBuilder.GetDefault();
            host.OnDataAddedAsJson += (r, json) => client.CreateFromReferenceAndJson(r, json);
            host.OnDataUpdatedAsJson += (r, json) => client.SetValueWithJson(r, json);

            var model = new ModelData(0.5f, new List<Weapon>(), new Position(1f, 1f), host);
            model.AttachRuleDefinition(new ResolvedRule("Fast", CoreRuleCatalog.Fast));
            DataReference modelRef = host.Create(model);
            var modelBinding = host.GetDataBinding<ModelData>(modelRef);

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Horse Marksmen", quality: 5, defense: 5,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            unit.AttachRuleDefinition(new ResolvedRule("Fast", CoreRuleCatalog.Fast));
            DataReference unitRef = host.Create(unit);

            // An ordinary update - the kind every move, wound and token change sends.
            host.SetValue(unitRef, host.GetValue<UnitData>(unitRef));
            host.SetValue(modelRef, host.GetValue<ModelData>(modelRef));

            Assert.That(client.GetValue<UnitData>(unitRef).RuleDefinitions.Select(r => r.RequestedName),
                Does.Contain("Fast"), "the client's unit keeps its rules after an update");
            Assert.That(client.GetValue<ModelData>(modelRef).RuleDefinitions.Select(r => r.RequestedName),
                Does.Contain("Fast"), "and so does the client's model");
        }

        /// <summary>
        /// The end-of-game +1 VP per objective is added to the Mission Cards state after the last per-turn
        /// snapshot. It changed only the host's copy, so a joined player's Game Over card showed the winner
        /// on 0 VP (network play-through, 2026-11-06). Storing the state must reach a synced store.
        /// </summary>
        [Test]
        public void MissionCardVp_SetAfterTheLastSnapshot_ReachesAJoinedPlayer()
        {
            var host = GameDataStore.GameDataStoreBuilder.GetDefault();
            var client = GameDataStore.GameDataStoreBuilder.GetDefault();
            host.OnDataAddedAsJson += (r, json) => client.CreateFromReferenceAndJson(r, json);
            host.OnDataUpdatedAsJson += (r, json) => client.SetValueWithJson(r, json);

            GameProgressUtilities.WriteProgress(host, new GameProgressData(EResumeStage.MainPhase, 4,
                new List<int>(), new List<int>(), 0, new Dictionary<int, int>(),
                new List<DataBinding<UnitData>>(), GameSettings.GetDefault()));
            var state = new FDG.Missions.MissionCardState { Game = FDG.Missions.EMissionCardGame.TotalDomination };
            GameProgressUtilities.SetMissionCards(host, state);

            // What ScoreFinalObjectives does: change the live state, then store it.
            state.Vp[1] = 1;
            GameProgressUtilities.SetMissionCards(host, state);

            Assert.That(GameProgressUtilities.MissionCards(client)?.Vp.GetValueOrDefault(1), Is.EqualTo(1),
                "the joined player sees the final victory points");
        }
    }
}
