using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Kai, 2026-11-02, on the 2x2 seat grid: "I'd like to be able to have a bot also run seat 1 as an
    /// option." Seat 1 is the host's own seat, and until now the only thing that could change who plays
    /// a seat was <see cref="ILobbyViewModel.SetSavedSlotPlayerType"/>, which the lobby called only when
    /// resuming a save. <see cref="ILobbyViewModel.SetPlayerType"/> is the live-lobby version.
    ///
    /// <para>
    /// The load-bearing case is <see cref="EverySeatABot_StillBuildsAGameModelToWatchItWith"/>. Handing
    /// every seat to a bot is the whole point of the feature - it is how you watch two armies fight -
    /// and the launch path built the front end's game model only as a side effect of finding a
    /// <see cref="EPlayerType.Local"/> slot. With no Local slot it built none, never raised
    /// <c>OnLaunched</c>, and the game would have run to a result with the lobby still on screen and
    /// nothing drawn. Nothing about the seat controls would have looked broken; the game just would not
    /// have appeared.
    /// </para>
    /// </summary>
    [TestFixture]
    public class LobbySeatCrewTests
    {
        private static LobbyViewModel_Host NewHost() =>
            new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());

        private static PlayerID OwnSeat(LobbyViewModel_Host host) => host.PlayerInfos[0].PlayerID;

        // Host-only no-op network double, same shape as the one in LobbyPlayerRemovalTests: these
        // cases never join anything, so broadcasts go nowhere.
        private sealed class NullNetworkHost : INetworkHost
        {
            public event System.Action<ConnectionID>? OnNewClientConnected { add { } remove { } }
            public event System.Action<ConnectionID>? OnClientDisconnected { add { } remove { } }
            public event System.Action<System.ArraySegment<byte>, ConnectionID>? OnMessageReceived { add { } remove { } }

            public System.Threading.Tasks.Task StartAsync() => System.Threading.Tasks.Task.CompletedTask;
            public System.Threading.Tasks.Task SendCommandToAllAsync(System.ArraySegment<byte> data, bool isPooled)
                => System.Threading.Tasks.Task.CompletedTask;
            public System.Threading.Tasks.Task SendCommandToSingleClientAsync(ConnectionID clientId,
                System.ArraySegment<byte> data, bool isPooled) => System.Threading.Tasks.Task.CompletedTask;
            public void DisconnectClient(ConnectionID clientId) { }
            public void MarkClientAuthenticated(ConnectionID clientId) { }
            public void Stop() { }
        }

        /// <summary>
        /// Kai, 2026-11-05: AoF: Quest could not be launched at all - "Can't launch yet: Pick a party of 2-4
        /// heroes for your row" with a two-hero party loaded. The gate looked for Star Quest's party faction
        /// only. Asserted through the real gate with a real party, for both Quest systems.
        /// </summary>
        [TestCase(EGameSystem.GFStarQuest)]
        [TestCase(EGameSystem.AoFQuest)]
        public void AQuestLobby_WithAPartyAndAnEnemyArmy_CanLaunch(EGameSystem system)
        {
            LobbyViewModel_Host host = NewHost();
            host.SetGameSystem(system);
            host.SetGameMode(EGameMode.StarQuestHorde);
            host.AddAiPlayer(EAiProfile.StarQuest);
            PlayerID seat1 = OwnSeat(host), seat2 = host.PlayerInfos[1].PlayerID;

            static Quest.QuestHero Hero(string name) => new Quest.QuestHero
            {
                Name = name, Faction = "Test", BaseHeroName = "Leader", BaseCost = 60, Class = "Soldier",
                Quality = 4, Defense = 4, Tough = 6, Power = 4, Strength = 4,
            };
            host.UpdateArmyListFile(seat1, Quest.QuestArmy.Build(new[] { Hero("Ozlozk"), Hero("Ygloth") }, _ => null, system));
            SaveLoad.ArmyListFile enemies = MakeArmy("Orcs");
            enemies.GameSystem = GameSystemInfo.Slug(system);
            host.UpdateArmyListFile(seat2, enemies);
            host.SetPlayerTeam(seat2, ETeamOption.Team2);

            Assert.That(host.BlockingLaunchProblems(), Is.Empty);
        }

        [Test]
        public void TheHostsOwnSeatCanBeHandedToABot()
        {
            LobbyViewModel_Host host = NewHost();
            PlayerID seat1 = OwnSeat(host);

            Assert.That(host.CheckCanSetPlayerType(seat1), Is.True,
                "seat 1 is the one Kai asked for by name.");
            Assert.That(host.PlayerInfos[0].PlayerType, Is.EqualTo(EPlayerType.Local));

            host.SetPlayerType(seat1, EPlayerType.AI, EAiProfile.SoloCoOp);

            Assert.That(host.PlayerInfos[0].PlayerType, Is.EqualTo(EPlayerType.AI));
            // And back, because a toggle that only goes one way is a trap.
            host.SetPlayerType(seat1, EPlayerType.Local);
            Assert.That(host.PlayerInfos[0].PlayerType, Is.EqualTo(EPlayerType.Local));
        }

        [Test]
        public void ReSeatingKeepsEverythingElseAboutTheSeat()
        {
            LobbyViewModel_Host host = NewHost();
            host.AddAiPlayer(EAiProfile.Tactician);
            PlayerID seat2 = host.PlayerInfos[1].PlayerID;

            host.SetPlayerTeam(seat2, ETeamOption.Team2);
            host.SetPlayerColor(seat2, 3);
            string nameBefore = host.PlayerInfos[1].PlayerName;

            host.SetPlayerType(seat2, EPlayerType.Local);

            LobbyPlayerInfoSummary after = host.PlayerInfos[1];
            Assert.That(after.PlayerType, Is.EqualTo(EPlayerType.Local));
            Assert.That(after.TeamNumber, Is.EqualTo(ETeamOption.Team2), "the side is the seat's, not the crew's.");
            Assert.That(after.ColorIndex, Is.EqualTo(3));
            Assert.That(after.PlayerName, Is.EqualTo(nameBefore));
            Assert.That(host.PlayerInfos, Has.Count.EqualTo(2), "re-seating is not adding or removing.");
        }

        [Test]
        public void ASeatCannotBeMadeNetworked()
        {
            // A networked seat is created by somebody connecting to it. Letting the host conjure one
            // would produce a slot with no connection behind it, which Launch would hand a
            // NetworkPlayerController pointed at nothing.
            LobbyViewModel_Host host = NewHost();
            PlayerID seat1 = OwnSeat(host);

            host.SetPlayerType(seat1, EPlayerType.Network);

            Assert.That(host.PlayerInfos[0].PlayerType, Is.EqualTo(EPlayerType.Local));
        }

        [Test]
        public void AClientCannotReSeatAnybody()
        {
            // Same policy as removal: the roster belongs to the host, so a client offers the Player/Bot
            // control on no seat at all - not even the one it is sitting in.
            var net = new LoopbackNetworkHost();
            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", net);
            var clientVm = new LobbyViewModel_Client("Alpha", net.Connect("Alpha"), "");

            foreach (LobbyPlayerInfoSummary info in hostVm.PlayerInfos)
                Assert.That(clientVm.CheckCanSetPlayerType(info.PlayerID), Is.False);
        }

        /// <summary>
        /// The one that matters, and it goes through <see cref="ILobbyViewModel.TryLaunchGame"/> rather
        /// than calling the spectator hook directly.
        ///
        /// <para>
        /// That is deliberate and it is the whole point of this test. The first version of it called
        /// <c>MakeSpectatorGameModelIfNobodyIsPlaying()</c> itself, and with that version in place the
        /// <b>entire 4,690-test suite still passed after Launch's call to the hook was deleted</b> - the
        /// test proved the hook worked and proved nothing about whether the game ever reached it. This
        /// is the fifth or sixth time that shape of gap has bitten this project, so: launch the lobby,
        /// the way a human clicking LAUNCH does, and assert on what the front end receives.
        /// </para>
        /// </summary>
        [Test]
        public async Task EverySeatABot_LaunchStillHandsTheFrontEndAGameToDrawWith()
        {
            LobbyViewModel_Host host = NewHost();
            host.AddAiPlayer(EAiProfile.SoloCoOp);

            PlayerID seat1 = OwnSeat(host), seat2 = host.PlayerInfos[1].PlayerID;
            host.UpdateArmyListFile(seat1, MakeArmy("First Guard"));
            host.UpdateArmyListFile(seat2, MakeArmy("Second Guard"));
            host.SetPlayerTeam(seat2, ETeamOption.Team2);

            // Nobody is playing: both seats are bots. This is Kai's "watch the game play itself".
            host.SetPlayerType(seat1, EPlayerType.AI, EAiProfile.SoloCoOp);
            Assert.That(host.PlayerInfos.All(i => i.PlayerType == EPlayerType.AI), Is.True);

            Assert.That(host.BlockingLaunchProblems(), Is.Empty,
                "the lobby must be launchable before this test means anything - an all-bot lobby is "
                + "not itself a launch blocker, and if that ever changes this assert says so.");

            EngineInterface.IFDGGame? launched = null;
            host.OnLaunched += game => launched = game;

            Assert.That(host.TryLaunchGame(out string? failReason), Is.True, failReason);
            await WaitFor(() => launched != null,
                "Launch never handed the front end a game to draw with.");

            Assert.That(launched, Is.Not.Null,
                "with every seat a bot there is no Local slot, so Launch's own loop builds no game "
                + "model - and without the spectator one the game runs to a result with the lobby "
                + "still on screen and nothing drawn.");
            Assert.That(launched!.LocalPlayerIDs, Is.Empty,
                "a spectator owns no player: AssignInterfaces makes one request receiver per local ID, "
                + "so an empty list is what keeps the watcher from being asked to decide anything.");
            Assert.That(launched.TableState, Is.Not.Null,
                "and it must still carry the table, or there is nothing to watch.");

            host.StopGame();
        }

        /// <summary>The ordinary case, also through a real launch: a human at seat 1 gets the game model
        /// Launch's own loop built, and exactly one of them.</summary>
        [Test]
        public async Task WithSomebodyPlaying_LaunchHandsOverOneGameOwningThatSeat()
        {
            LobbyViewModel_Host host = NewHost();
            host.AddAiPlayer(EAiProfile.SoloCoOp);

            PlayerID seat1 = OwnSeat(host), seat2 = host.PlayerInfos[1].PlayerID;
            host.UpdateArmyListFile(seat1, MakeArmy("First Guard"));
            host.UpdateArmyListFile(seat2, MakeArmy("Second Guard"));
            host.SetPlayerTeam(seat2, ETeamOption.Team2);

            var launched = new List<EngineInterface.IFDGGame>();
            host.OnLaunched += game => launched.Add(game);

            Assert.That(host.TryLaunchGame(out string? failReason), Is.True, failReason);
            await WaitFor(() => launched.Count > 0, "Launch never raised OnLaunched.");
            // And give a second one time to show up, so "exactly one" means it rather than "not yet".
            await Task.Delay(250);

            Assert.That(launched, Has.Count.EqualTo(1),
                "two game models for one watcher would double every log line it draws.");
            Assert.That(launched[0].LocalPlayerIDs, Is.EqualTo(new[] { seat1 }),
                "the human's seat, and only it.");

            host.StopGame();
        }

        /// <summary>
        /// A headless host with nobody subscribed - the real "dedicated server" the old null check was
        /// written for. Asserted on the hook directly because there is no front end here to observe:
        /// the point is that no model is built, and a model nothing reads is invisible by definition.
        /// </summary>
        [Test]
        public void WithNobodyWatching_NoSpectatorModelIsInvented()
        {
            LobbyViewModel_Host host = NewHost();
            host.SetPlayerType(OwnSeat(host), EPlayerType.AI, EAiProfile.SoloCoOp);

            Assert.That(host.MakeSpectatorGameModelIfNobodyIsPlaying(), Is.Null);
        }

        private static async Task WaitFor(System.Func<bool> condition, string message)
        {
            System.DateTime deadline = System.DateTime.UtcNow + System.TimeSpan.FromSeconds(10);
            while (System.DateTime.UtcNow < deadline)
            {
                if (condition()) return;
                await Task.Delay(25);
            }
            Assert.Fail(message);
        }

        private static SaveLoad.ArmyListFile MakeArmy(string name) => new SaveLoad.ArmyListFile
        {
            Name = name,
            Faction = name,
            GameSystem = ArmyBuilding.GameSystems.GrimdarkFuture,
            PointsLimit = 2000,
            Units = new()
            {
                new SaveLoad.UnitFileEntry
                {
                    Name = "Riflemen", ModelCount = 5, Quality = 4, Defense = 4,
                    Weapons = new() { new SaveLoad.WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                },
                new SaveLoad.UnitFileEntry
                {
                    Name = "Brawlers", ModelCount = 3, Quality = 4, Defense = 4,
                    Weapons = new() { new SaveLoad.WeaponFileEntry { Name = "Blade", RangeInches = 0, Attacks = 2 } },
                },
            },
        };
    }
}
