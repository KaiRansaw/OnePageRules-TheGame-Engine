using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.ArmyBuilding;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Every seat's FULL army list reaches every joined player's lobby, not just the roster's
    /// name/faction/points summary. Kai: every player in the lobby sees every seat's list - the lobby's
    /// seat window (portrait cards, hovers, unit detail) used to work on the host only, and a joined
    /// friend saw "Only the host's screen can show other players' lists" for every seat but their own.
    ///
    /// <para>End to end through the REAL host and client view models over the in-process
    /// <see cref="LoopbackNetworkHost"/>, as <see cref="LobbyTeamSyncTests"/> and
    /// <see cref="MultiClientLobbyTests"/> do. Covers a pick, a change, a late joiner, bots, a Quest hero
    /// party with its embedded book data, a removed seat, and a client's own pick going round the host to
    /// a second client.</para>
    /// </summary>
    [TestFixture]
    public class LobbySeatArmySyncTests
    {
        [Test]
        public async Task HostsPick_ReachesTheClient_AsTheFullList()
        {
            var (host, loopback, clients) = await StandUpLobby(1);
            PlayerID hostSeat = SeatOf(host, "Host");

            host.UpdateArmyListFile(hostSeat, MakeArmy("Host Guard", "Riflemen", "Brawlers"));

            SeatArmyView seen = clients[0].GetSeatArmy(hostSeat);
            Assert.That(seen.Status, Is.EqualTo(ESeatArmyStatus.Ready));
            Assert.That(seen.Army!.Name, Is.EqualTo("Host Guard"));
            Assert.That(seen.Army.Units.Select(u => u.Name), Is.EqualTo(new[] { "Riflemen", "Brawlers" }),
                "the units themselves arrive, not just the summary");
            Assert.That(seen.Army.Units[0].Weapons.Single().Name, Is.EqualTo("Rifle"), "weapons too");
        }

        [Test]
        public async Task ASeatsChangedList_ReplacesTheOldOne()
        {
            var (host, _, clients) = await StandUpLobby(1);
            PlayerID hostSeat = SeatOf(host, "Host");

            host.UpdateArmyListFile(hostSeat, MakeArmy("First Pick", "Riflemen"));
            host.UpdateArmyListFile(hostSeat, MakeArmy("Second Pick", "Snipers"));

            SeatArmyView seen = clients[0].GetSeatArmy(hostSeat);
            Assert.That(seen.Army!.Name, Is.EqualTo("Second Pick"));
            Assert.That(seen.Army.Units.Single().Name, Is.EqualTo("Snipers"));
        }

        [Test]
        public async Task ALateJoiner_GetsEveryListAlreadyPicked()
        {
            var (host, loopback, clients) = await StandUpLobby(1);
            PlayerID hostSeat = SeatOf(host, "Host");
            PlayerID firstClientSeat = SeatOf(host, "Alpha");
            host.AddAiPlayer(EAiProfile.SoloRules);
            PlayerID botSeat = SeatOf(host, "DerpBot 1");

            host.UpdateArmyListFile(hostSeat, MakeArmy("Host Guard", "Riflemen"));
            clients[0].UpdateArmyListFile(firstClientSeat, MakeArmy("Alpha Raiders", "Bikers"));
            host.UpdateArmyListFile(botSeat, MakeArmy("Bot Horde", "Grunts"));

            LobbyViewModel_Client late = await Join(loopback, "Bravo");

            Assert.That(late.GetSeatArmy(hostSeat).Army!.Name, Is.EqualTo("Host Guard"));
            Assert.That(late.GetSeatArmy(firstClientSeat).Army!.Name, Is.EqualTo("Alpha Raiders"));
            Assert.That(late.GetSeatArmy(botSeat).Army!.Name, Is.EqualTo("Bot Horde"), "bot seats too");
            Assert.That(late.GetSeatArmy(SeatOf(host, "Bravo")).Status, Is.EqualTo(ESeatArmyStatus.NoArmy),
                "its own empty seat is simply empty");
        }

        [Test]
        public async Task ALateJoiner_SeesLaterChangesToo()
        {
            var (host, loopback, _) = await StandUpLobby(1);
            PlayerID hostSeat = SeatOf(host, "Host");
            host.UpdateArmyListFile(hostSeat, MakeArmy("Before", "Riflemen"));

            LobbyViewModel_Client late = await Join(loopback, "Bravo");
            host.UpdateArmyListFile(hostSeat, MakeArmy("After", "Snipers"));

            Assert.That(late.GetSeatArmy(hostSeat).Army!.Name, Is.EqualTo("After"));
        }

        [Test]
        public async Task AClientsOwnPick_GoesRoundTheHost_ToTheOtherClient()
        {
            var (host, _, clients) = await StandUpLobby(2);
            PlayerID alphaSeat = SeatOf(host, "Alpha");

            clients[0].UpdateArmyListFile(alphaSeat, MakeArmy("Alpha Raiders", "Bikers"));

            Assert.That(clients[1].GetSeatArmy(alphaSeat).Army!.Name, Is.EqualTo("Alpha Raiders"));
            Assert.That(clients[0].GetSeatArmy(alphaSeat).Army!.Name, Is.EqualTo("Alpha Raiders"),
                "the picker's own lobby reads it back the same way");
            Assert.That(host.GetSeatArmy(alphaSeat).Army!.Name, Is.EqualTo("Alpha Raiders"));
        }

        [Test]
        public async Task AQuestHeroParty_ArrivesWithItsBuiltData()
        {
            var (host, _, clients) = await StandUpLobby(1);
            PlayerID hostSeat = SeatOf(host, "Host");

            static Quest.QuestHero Hero(string name) => new Quest.QuestHero
            {
                Name = name, Faction = "Test", BaseHeroName = "Leader", BaseCost = 60, Class = "Soldier",
                Quality = 4, Defense = 4, Tough = 6, Power = 4, Strength = 4,
            };
            BuiltArmyFile party = Quest.QuestArmy.Build(new[] { Hero("Ozlozk"), Hero("Ygloth") }, _ => null,
                EGameSystem.GFStarQuest);
            host.UpdateArmyListFile(hostSeat, party);

            SeatArmyView seen = clients[0].GetSeatArmy(hostSeat);
            Assert.That(seen.Army, Is.InstanceOf<BuiltArmyFile>(), "the runtime type survives the trip");
            Assert.That(seen.Army!.Units.Select(u => u.Name), Is.EquivalentTo(party.Units.Select(u => u.Name)));
            Assert.That(seen.Army.GameSystem, Is.EqualTo(party.GameSystem));
        }

        [Test]
        public async Task AForgeBuiltList_KeepsItsEmbeddedBookAndSelections()
        {
            var (host, _, clients) = await StandUpLobby(1);
            PlayerID hostSeat = SeatOf(host, "Host");
            var list = new BuilderList { Name = "L", PointsLimit = 100000 };
            list.Units.Add(new BuilderUnit { RosterUnitId = "warriors" });
            BuiltArmyFile forged = ListCompiler.Compile(DemoBook.Build(), list);

            host.UpdateArmyListFile(hostSeat, forged);

            var seen = (BuiltArmyFile)clients[0].GetSeatArmy(hostSeat).Army!;
            Assert.That(seen.Book, Is.Not.Null, "the unit detail needs the book's rules and upgrades");
            Assert.That(seen.Selections!.Units.Single().RosterUnitId, Is.EqualTo("warriors"));
        }

        [Test]
        public async Task ARemovedSeat_TakesItsListWithIt()
        {
            var (host, _, clients) = await StandUpLobby(1);
            host.AddAiPlayer(EAiProfile.SoloRules);
            PlayerID botSeat = SeatOf(host, "DerpBot 1");
            host.UpdateArmyListFile(botSeat, MakeArmy("Bot Horde", "Grunts"));
            Assert.That(clients[0].GetSeatArmy(botSeat).Status, Is.EqualTo(ESeatArmyStatus.Ready));

            host.RemovePlayer(botSeat);

            Assert.That(clients[0].GetSeatArmy(botSeat).Status, Is.EqualTo(ESeatArmyStatus.NoArmy));
        }

        [Test]
        public async Task ARosterThatSaysAListIsOnItsWay_ReadsAsLoading()
        {
            var (host, _, clients) = await StandUpLobby(1);
            PlayerID hostSeat = SeatOf(host, "Host");
            Assert.That(clients[0].GetSeatArmy(hostSeat).Status, Is.EqualTo(ESeatArmyStatus.NoArmy),
                "no pick yet is not 'loading'");

            // Feed the client a roster that claims a list, with no list behind it - the moment between the
            // two messages on a real network.
            var loopback = new LoopbackNetworkHost();
            var wire = loopback.Connect("Solo");
            var hostVm = new LobbyViewModel_Host("Host2", "T", "", loopback);
            var lone = new LobbyViewModel_Client("Solo", wire, "");
            await lone.JoinResultTask;
            PlayerID seat = SeatOf(hostVm, "Host2");
            wire.DropNextOfType = "LobbySeatArmyMessage";
            hostVm.UpdateArmyListFile(seat, MakeArmy("In Flight", "Riflemen"));

            Assert.That(lone.GetSeatArmy(seat).Status, Is.EqualTo(ESeatArmyStatus.Loading));
        }

        [Test]
        public async Task OnlyAChangedSeat_IsResent()
        {
            var (host, loopback, clients) = await StandUpLobby(1);
            PlayerID hostSeat = SeatOf(host, "Host");
            host.UpdateArmyListFile(hostSeat, MakeArmy("Host Guard", "Riflemen"));
            LoopbackNetworkClient wire = loopback.Clients[0];
            int before = wire.DeliveredCount("LobbySeatArmyMessage");

            host.SetPlayerTeam(hostSeat, ETeamOption.Team2); // a roster change that is not a list change

            Assert.That(wire.DeliveredCount("LobbySeatArmyMessage"), Is.EqualTo(before),
                "a team or colour change must not resend every list");
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────

        private static readonly string[] ClientNames = { "Alpha", "Bravo", "Charlie" };

        private static PlayerID SeatOf(ILobbyViewModel vm, string name) =>
            vm.PlayerInfos.Single(p => p.PlayerName == name).PlayerID;

        private static async Task<(LobbyViewModel_Host, LoopbackNetworkHost, IReadOnlyList<LobbyViewModel_Client>)>
            StandUpLobby(int clientCount)
        {
            var loopback = new LoopbackNetworkHost();
            var host = new LobbyViewModel_Host("Host", "The Table", "", loopback);
            var clients = new List<LobbyViewModel_Client>();
            for (int i = 0; i < clientCount; i++)
                clients.Add(await Join(loopback, ClientNames[i]));
            return (host, loopback, clients);
        }

        private static async Task<LobbyViewModel_Client> Join(LoopbackNetworkHost loopback, string name)
        {
            var client = new LobbyViewModel_Client(name, loopback.Connect(name), "");
            Task<string?> joinTask = client.JoinResultTask;
            Task winner = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(winner, Is.SameAs(joinTask), "Join handshake did not complete.");
            Assert.That(await joinTask, Is.Null, "open lobby join must be accepted");
            return client;
        }

        private static ArmyListFile MakeArmy(string name, params string[] unitNames) => new ArmyListFile
        {
            Name = name,
            Faction = name,
            GameSystem = GameSystems.GrimdarkFuture,
            PointsLimit = 2000,
            Units = unitNames.Select(unit => new UnitFileEntry
            {
                Name = unit, ModelCount = 5, Quality = 4, Defense = 4, PointCost = 100,
                Weapons = new() { new WeaponFileEntry { Name = unit == "Riflemen" ? "Rifle" : "Blade", RangeInches = 24, Attacks = 1 } },
            }).ToList(),
        };
    }
}
