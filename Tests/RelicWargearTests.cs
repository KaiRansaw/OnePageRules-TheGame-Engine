using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Advanced Casting - Relic Wargear (GF p36-37, GFF p33-34) / Magic Items (AoF p36-37, AoFS p33-34). The
    /// pick before deployment, and each item's effect - the combat ones through the real fire stages.</summary>
    [TestFixture]
    public class RelicWargearTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _me, _them;
        private readonly List<StringSelectionRequest> _asked = new();
        private readonly List<YesNoRequest> _yesNo = new();
        private Func<StringSelectionRequest, string>? _answer;
        private bool _yes = true;

        private sealed class Answer : IPlayerRequestByID
        {
            private readonly Func<object, object> _answer;
            public Answer(Func<object, object> answer) => _answer = answer;
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply> =>
                Task.FromResult((TReply)_answer(request!));
        }

        private sealed class Faces : IDiceRoller
        {
            private readonly int[] _faces;
            private int _i;
            public Faces(params int[] faces) => _faces = faces;
            public IDiceResults Roll(int sideCount, float rollCount) => new FixedDiceResults(_i < _faces.Length ? _faces[_i++] : 1);
        }

        private sealed class Seat : IPlayerController
        {
            public Seat(string name, PlayerID id) { Name = name; ID = id; }
            public string Name { get; }
            public PlayerID ID { get; }
            public bool IsReady => true;
            public Presentation.IPresentationSink? PresentationSink => null;
#pragma warning disable CS0067
            public event Action<bool>? OnReadyStateChanged;
            public event Action<PlayerID, EChatMessageType, string>? OnMessageSentByPlayer;
#pragma warning restore CS0067
            public Task WaitUntilReadyAsync() => Task.CompletedTask;
            public void SendLogMessage(string logMessage, TextColor color) { }
            public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) { }
        }

        private void Build(EGameSystem system, int method, IDiceRoller? dice = null)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.AdvancedRules = RelicWargear.WithPick(EAdvancedRules.RelicWargear, method);
            _asked.Clear();
            _yesNo.Clear();
            _answer = null;
            _yes = true;
            _context = new TestGameContext(_store, dice ?? new FixedDiceRoller(4), settings: settings, playerRequester: new Answer(r => r switch
            {
                StringSelectionRequest q => Record(q),
                YesNoRequest y => RecordYes(y),
                AssignWoundsRequest w => AutoFill(w),
                PlaceObjectsRequest<ModelData> => new Cancelled<List<PlacedObjectEntry<ModelData>>>(),
                _ => throw new InvalidOperationException(r.GetType().Name),
            }));
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        private object Record(StringSelectionRequest q)
        {
            _asked.Add(q);
            return _answer?.Invoke(q) ?? q.Recommended ?? q.ValidOptions[0];
        }

        private object RecordYes(YesNoRequest y)
        {
            _yesNo.Add(y);
            return _yes;
        }

        private static object AutoFill(AssignWoundsRequest w)
        {
            var results = new AssignWoundsResults(w.UnitReceivingWounds, w.Packets);
            results.AutoFill();
            return results;
        }

        private void Seats()
        {
            new PlayerSlot(0, teamNumber: 0, _me, new ArmyListFile(), _store).AssignPlayerController(new Seat("Kai", _me));
            new PlayerSlot(1, teamNumber: 1, _them, new ArmyListFile(), _store).AssignPlayerController(new Seat("Bot", _them));
        }

        private DataBinding<UnitData> Unit(PlayerID owner, Position at, int models, string name = "Squad", bool hero = false,
            int tough = 1, int defense = 4, int casterLevel = 0, params Weapon[] weapons)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(new CircleBase(0.5f), tough,
                    _store.GetDataBinding<float>(_store.Create((float)tough)),
                    _store.GetDataBinding<Position>(_store.Create(new Position(at.x + i * 1.5f, at.z))),
                    _store.GetDataBinding<Float2>(_store.Create(new Float2(0f, 1f))),
                    weapons.Select(w => new Weapon(w)).ToList());
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, name, quality: 4, defense: defense, modelBindings: bindings);
            if (hero) unit.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            if (casterLevel > 0)
                unit.AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster, new RuleArgument[] { new RuleArgument.Int(casterLevel) }));
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private static Weapon Blade(int attacks = 2, int ap = 0) => new Weapon("Blade", rangeInches: 0f, attacks: attacks, armorPenetration: ap);
        private static Weapon Pistol() => new Weapon("Pistol", rangeInches: 12f, attacks: 1, armorPenetration: 0);

        /// <summary>Puts a relic on a Hero directly (what Settle records), for the effect tests.</summary>
        private RelicPick Give(DataBinding<UnitData> unit, string item, string? weapon = null)
        {
            RelicWargearState s = RelicWargear.State(_store) ?? new RelicWargearState { Method = RelicWargear.Free };
            UnitData u = unit.GetValue();
            var pick = new RelicPick
            {
                Owner = u.PlayerID.ToString(), OwnerName = "P", Unit = AdvancedCasting.Id(u), UnitName = u.Name,
                Model = u.Models[0].ID.ToString(), Hero = u.Name, Item = item, Weapon = weapon,
            };
            s.Picks.Add(pick);
            RelicWargear.Save(_context, s);
            return pick;
        }

        private static async Task Run<TResult, TSelf>(CombatStage<TResult, TSelf, ICombatMetadata> stage, CombatMetadata metadata)
            where TSelf : CombatStage<TResult, TSelf, ICombatMetadata>
        {
            stage.NextStage.Bind("done");
            await stage.Enter(metadata);
        }

        private async Task<CombatMetadata> HitAndSave(DataBinding<UnitData> attacker, DataBinding<UnitData> defender, Weapon weapon, bool melee)
        {
            var metadata = new CombatMetadata(_context, attacker, defender, weapon, weaponCount: 1, attackerMoved: false, isMelee: melee);
            metadata.AddResult(new CoverCheckResults(0));
            await Run(new DetermineHitRollStage<ICombatMetadata>(_context, new NoOpLayer<ICombatMetadata>()), metadata);
            await Run(new RollToHitStage<ICombatMetadata>(_context, new NoOpLayer<ICombatMetadata>()), metadata);
            await Run(new DetermineSaveRollsNeededStage<ICombatMetadata>(_context, new NoOpLayer<ICombatMetadata>()), metadata);
            return metadata;
        }

        private static int SaveNeeded(CombatMetadata m)
        {
            Assert.That(m.QueryForResult(out DetermineSaveRollNeededResults saves), Is.True);
            return saves.PendingSaveRollsList[0].SaveNeeded;
        }

        // ================================================================= the book

        [Test]
        public void Table_ThirtySixItems_SixGroupsOfSix_InAllFourBooks()
        {
            foreach (EGameSystem s in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish })
            {
                var table = RelicWargear.Table(s);
                Assert.That(table, Has.Count.EqualTo(36));
                for (int c = 0; c < 6; c++)
                    Assert.That(table.Where(d => d.Category == c).Select(d => d.Number).OrderBy(n => n), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }), $"{s} group {c}");
                Assert.That(table.Select(d => d.Name).Distinct().Count(), Is.EqualTo(36));
            }
            // The two Fantasy books rename three Enchanted Items; every book numbers Ring of Ruin 3 and Terrifying Mask 5.
            Assert.That(RelicWargear.Table(EGameSystem.AgeOfFantasy).Where(d => d.Category == 5).Select(d => d.Name),
                Is.EquivalentTo(new[] { "Wizarding Hat", "Flying Carpet", "Ring of Ruin", "Trickster's Shard", "Terrifying Mask", "Potion of Displacement" }));
            foreach (EGameSystem s in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight })
            {
                Assert.That(RelicWargear.Find(s, RelicWargear.RingOfRuin)!.Number, Is.EqualTo(3));
                Assert.That(RelicWargear.Find(s, RelicWargear.TerrifyingMask)!.Number, Is.EqualTo(5));
            }
            Assert.That(RelicWargear.OptionName(EGameSystem.AoFSkirmish), Is.EqualTo("Magic Items"));
            Assert.That(RelicWargear.OptionName(EGameSystem.GrimdarkFuture), Is.EqualTo("Relic Wargear"));
        }

        [Test]
        public void Table_SkirmishBooks_RewordTheBannersMaskAndGems()
        {
            Assert.That(RelicWargear.Find(EGameSystem.GFFirefight, RelicWargear.Swiftness)!.Text, Does.Contain("up to 3 friendly units"));
            Assert.That(RelicWargear.Find(EGameSystem.GrimdarkFuture, RelicWargear.Swiftness)!.Text, Is.EqualTo("The Hero and its unit get +2\" when using Advance actions."));
            Assert.That(RelicWargear.Find(EGameSystem.AoFSkirmish, RelicWargear.TerrifyingMask)!.Text, Does.Contain("within 6\""));
            Assert.That(RelicWargear.Find(EGameSystem.AgeOfFantasy, RelicWargear.TerrifyingMask)!.Text, Does.Contain("in melee with the Hero"));
            Assert.That(RelicWargear.Find(EGameSystem.GFFirefight, RelicWargear.ProtectiveGem)!.Text, Does.EndWith("attacking the Hero."));
            Assert.That(RelicWargear.Find(EGameSystem.GrimdarkFuture, RelicWargear.OpalAmulet)!.Text, Does.EndWith("the Hero and its unit."));
            Assert.That(RelicWargear.Find(EGameSystem.GFFirefight, RelicWargear.Ranger)!.Text, Does.Contain("up to half of its army"));
        }

        [Test]
        public void Lobby_MethodBits_RoundTrip_AndLeaveTheOtherPicksAlone()
        {
            EAdvancedRules rules = EAdvancedRules.RelicWargear | CommandPoints.WithPick(EAdvancedRules.CommandPoints, 5);
            for (int m = 0; m <= 3; m++)
            {
                rules = RelicWargear.WithPick(rules, m);
                var settings = GameSettings.GetDefault();
                settings.AdvancedRules = rules;
                Assert.That(RelicWargear.Picked(settings), Is.EqualTo(m));
                Assert.That(CommandPoints.Picked(settings), Is.EqualTo(5), "the playstyle bits are untouched");
                Assert.That(rules & EAdvancedRules.RelicWargear, Is.EqualTo(EAdvancedRules.RelicWargear));
            }
            Assert.That(AdvancedRules.In(AdvancedRules.AdvancedCastingChapter, EGameSystem.GrimdarkFuture).Select(o => o.Name), Does.Contain("Relic Wargear"));
            Assert.That(AdvancedRules.In(AdvancedRules.AdvancedCastingChapter, EGameSystem.AgeOfFantasy).Select(o => o.Name), Does.Contain("Magic Items"));
        }

        // ================================================================= before the game

        [Test]
        public async Task Settle_Free_EachHeroPicks_OneCopyPerArmy_BotsRoll()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free, new Faces(3, 5, 6, 6));
            Seats();
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true, weapons: Blade());
            var psychic = Unit(_me, new Position(14, 10), 1, "Psychic", hero: true, weapons: Pistol());
            var squad = Unit(_me, new Position(20, 10), 5, "Squad");
            var warlord = Unit(_them, new Position(40, 40), 1, "Warlord", hero: true, weapons: Blade());
            _answer = q => q.TargetPlayerID == _me && q.ValidOptions.Contains(RelicWargear.Luckstone) ? RelicWargear.Luckstone
                : q.Recommended!;

            await RelicWargear.Settle(_context);

            var mine = _asked.Where(q => q.TargetPlayerID == _me && q.DisplayName == "Relic Wargear").ToList();
            Assert.That(mine, Has.Count.EqualTo(2), "a slot per Hero, none for the squad");
            Assert.That(mine[0].ValidOptions, Has.Count.EqualTo(37), "Free: all 36, and No relic");
            Assert.That(mine[1].ValidOptions, Does.Not.Contain(RelicWargear.Luckstone), "one copy per army");
            Assert.That(mine[1].InvalidOptions.Single(i => i.Option == RelicWargear.Luckstone).Reason, Is.EqualTo("on Captain"));
            RelicWargearState s = RelicWargear.State(_store)!;
            Assert.That(s.Method, Is.EqualTo(RelicWargear.Free));
            Assert.That(s.Picks.Count(p => p.Owner == _me.ToString()), Is.EqualTo(2));
            Assert.That(s.Picks.Single(p => p.Owner == _them.ToString()).Item, Is.Not.Empty, "the bot gets one too");
            Assert.That(RelicWargear.ItemOf(_store, captain.GetValue(), captain.GetValue().Models[0]), Is.EqualTo(RelicWargear.Luckstone));
            int asked = _asked.Count;
            await RelicWargear.Settle(_context);
            Assert.That(_asked, Has.Count.EqualTo(asked), "settled once");
        }

        [Test]
        public async Task Settle_Limited_OnlyTheRolledGroup()
        {
            // The category die rolls 3: Sorcerous Talismans.
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Limited, new Faces(3, 2));
            Seats();
            Unit(_me, new Position(10, 10), 1, "Captain", hero: true, weapons: Blade());
            await RelicWargear.Settle(_context);
            StringSelectionRequest q = _asked.Single();
            Assert.That(q.ValidOptions.Where(o => o != RelicWargear.NoRelic),
                Is.EquivalentTo(RelicWargear.Table(EGameSystem.GrimdarkFuture).Where(d => d.Category == 2).Select(d => d.Name)));
            Assert.That(q.Instructions, Does.Contain("Rolled 3: Sorcerous Talismans"));
            Assert.That(q.InvalidOptions, Has.Count.EqualTo(30));
        }

        [Test]
        public async Task Settle_Randomized_TwoItems_DuplicateReRolled()
        {
            // Group 2 (Holy Armour), then 5, 5 (the same item - re-rolled), 1: Dragon Shield and Armour of Destiny.
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Randomized, new Faces(2, 5, 5, 1, 2));
            Seats();
            Unit(_me, new Position(10, 10), 1, "Captain", hero: true, weapons: Blade());
            await RelicWargear.Settle(_context);
            StringSelectionRequest q = _asked.Single();
            Assert.That(q.ValidOptions, Is.EqualTo(new[] { RelicWargear.DragonShield, RelicWargear.Destiny, RelicWargear.NoRelic }));
        }

        [Test]
        public async Task Settle_RollForIt_TheD3PicksTheMethod()
        {
            // D3 off a 5 = 3: Randomized.
            Build(EGameSystem.GrimdarkFuture, 0, new Faces(5, 1, 1, 2, 1));
            Seats();
            Unit(_me, new Position(10, 10), 1, "Captain", hero: true, weapons: Blade());
            await RelicWargear.Settle(_context);
            Assert.That(RelicWargear.State(_store)!.Method, Is.EqualTo(RelicWargear.Randomized));
        }

        [Test]
        public async Task Settle_NoRelic_LeavesTheHeroWithout()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            Seats();
            Unit(_me, new Position(10, 10), 1, "Captain", hero: true, weapons: Blade());
            _answer = q => RelicWargear.NoRelic;
            await RelicWargear.Settle(_context);
            Assert.That(RelicWargear.State(_store)!.Picks, Is.Empty);
        }

        [Test]
        public async Task Settle_RelicWeapon_AsksWhichWeapon()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            Seats();
            Unit(_me, new Position(10, 10), 1, "Captain", hero: true, weapons: new[] { Blade(), Pistol() });
            _answer = q => q.DisplayName == "Relic Wargear" ? (q.TargetPlayerID == _me ? RelicWargear.Bloodshed : q.Recommended!)
                : q.DisplayName == RelicWargear.Bloodshed ? "Pistol" : q.Recommended!;
            await RelicWargear.Settle(_context);
            Assert.That(_asked.Any(q => q.DisplayName == RelicWargear.Bloodshed && q.ValidOptions.SequenceEqual(new[] { "Blade", "Pistol" })));
            Assert.That(RelicWargear.State(_store)!.Picks.Single().Weapon, Is.EqualTo("Pistol"));
        }

        [Test]
        public async Task Settle_StandingRules_GoOnTheHero()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            Seats();
            var a = Unit(_me, new Position(10, 10), 1, "Psychic", hero: true, weapons: Blade());
            var b = Unit(_me, new Position(14, 10), 1, "Champion", hero: true, weapons: Blade());
            var c = Unit(_me, new Position(18, 10), 1, "Chaplain", hero: true, weapons: Blade());
            var d = Unit(_me, new Position(22, 10), 1, "Ranger", hero: true, weapons: Blade());
            var queue = new Queue<string>(new[] { "Psychic Harness", "Conjured Wings", RelicWargear.TrickstersShard, RelicWargear.Discipline });
            _answer = q => q.DisplayName == "Relic Wargear" && q.TargetPlayerID == _me ? queue.Dequeue() : q.Recommended!;
            await RelicWargear.Settle(_context);
            Assert.That(a.GetValue().RuleDefinitions.Any(r => r.Definition.Name == "Caster" && r.Arguments[0] is RuleArgument.Int { Value: 1 }), "Caster(+1)");
            Assert.That(b.GetValue().RuleDefinitions.Any(r => r.Definition.Name == "Flying"));
            Assert.That(c.GetValue().Models[0].Weapons.All(w => w.RuleDefinitions.Any(r => r.Definition.Name == RelicRuleCatalog.TricksterName)));
            Assert.That(RelicWargear.PassesOnTwo(d.GetValue()), "the Standard of Discipline's grant");
        }

        // ================================================================= weapons, through the real stages

        [Test]
        public async Task WeaponOfBloodshed_PlusThreeAttacks_OnThePickedWeaponOnly()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true, weapons: new[] { Blade(2), Pistol() });
            var target = Unit(_them, new Position(11.2f, 10), 5);
            Give(captain, RelicWargear.Bloodshed, "Blade");
            CombatMetadata m = await HitAndSave(captain, target, captain.GetValue().Models[0].Weapons[0], melee: true);
            Assert.That(m.QueryForResult(out DetermineHitRollResults hit) && hit.AttackCount == 5f, "A2 + 3");
            CombatMetadata pistol = await HitAndSave(captain, target, captain.GetValue().Models[0].Weapons[1], melee: false);
            Assert.That(pistol.QueryForResult(out DetermineHitRollResults shot) && shot.AttackCount == 1f, "not the other weapon");
        }

        [Test]
        public async Task ObsidianWeapon_AP4_AndBitingWeapon_Quality2()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true, weapons: Blade(2, ap: 1));
            var target = Unit(_them, new Position(11.2f, 10), 5, defense: 3);
            Assert.That(SaveNeeded(await HitAndSave(captain, target, captain.GetValue().Models[0].Weapons[0], true)), Is.EqualTo(4), "D3 + AP1");
            RelicPick p = Give(captain, RelicWargear.Obsidian, "Blade");
            Assert.That(SaveNeeded(await HitAndSave(captain, target, captain.GetValue().Models[0].Weapons[0], true)), Is.EqualTo(7), "D3 + AP4");
            p.Item = RelicWargear.Biting;
            RelicWargear.Save(_context, RelicWargear.State(_store)!);
            CombatMetadata m = await HitAndSave(captain, target, captain.GetValue().Models[0].Weapons[0], true);
            Assert.That(m.QueryForResult(out DetermineHitRollResults hit) && hit.HitRollNeeded == 2, $"Quality 2+ (was {hit.HitRollNeeded})");
        }

        [Test]
        public async Task KingslayerWeapon_OnlyAgainstHeroes()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true, weapons: Blade(2));
            var squad = Unit(_them, new Position(11.2f, 10), 5, defense: 3);
            var warlord = Unit(_them, new Position(8.8f, 10), 1, "Warlord", hero: true, defense: 3);
            Give(captain, RelicWargear.Kingslayer, "Blade");
            CombatMetadata vsSquad = await HitAndSave(captain, squad, captain.GetValue().Models[0].Weapons[0], true);
            Assert.That(vsSquad.QueryForResult(out DetermineHitRollResults a) && a.AttackCount == 2f);
            Assert.That(SaveNeeded(vsSquad), Is.EqualTo(3));
            CombatMetadata vsHero = await HitAndSave(captain, warlord, captain.GetValue().Models[0].Weapons[0], true);
            Assert.That(vsHero.QueryForResult(out DetermineHitRollResults b) && b.AttackCount == 5f);
            Assert.That(SaveNeeded(vsHero), Is.EqualTo(7));
        }

        [Test]
        public async Task FencersBlade_IgnoresFatigue()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true, weapons: Blade(2));
            var target = Unit(_them, new Position(11.2f, 10), 5);
            captain.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Fatigued));
            CombatMetadata tired = await HitAndSave(captain, target, captain.GetValue().Models[0].Weapons[0], true);
            Assert.That(tired.QueryForResult(out DetermineHitRollResults a) && a.HitRollNeeded == 6, "fatigued: 6s only");
            Give(captain, RelicWargear.Fencer, "Blade");
            CombatMetadata fresh = await HitAndSave(captain, target, captain.GetValue().Models[0].Weapons[0], true);
            Assert.That(fresh.QueryForResult(out DetermineHitRollResults b) && b.HitRollNeeded == 4, $"Quality 4+ (was {b.HitRollNeeded})");
        }

        // ================================================================= being attacked

        [Test]
        public async Task HelmetOfFortune_AndSilverSteelShield_WhileTheHeroStandsAlone()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            var shooter = Unit(_them, new Position(20, 10), 1, weapons: Pistol());
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true, defense: 4);
            RelicPick p = Give(captain, RelicWargear.Fortune);
            Assert.That(SaveNeeded(await HitAndSave(shooter, captain, shooter.GetValue().Models[0].Weapons[0], false)), Is.EqualTo(3), "+1 Defense");
            p.Item = RelicWargear.SilverSteel;
            RelicWargear.Save(_context, RelicWargear.State(_store)!);
            Assert.That(SaveNeeded(await HitAndSave(shooter, captain, shooter.GetValue().Models[0].Weapons[0], false)), Is.EqualTo(2), "Defense 2+ when shot at");
            var brawler = Unit(_them, new Position(11.2f, 10), 1, weapons: Blade());
            Assert.That(SaveNeeded(await HitAndSave(brawler, captain, brawler.GetValue().Models[0].Weapons[0], true)), Is.EqualTo(4), "Silver-Steel is shooting only");
            p.Item = RelicWargear.Chainmail;
            RelicWargear.Save(_context, RelicWargear.State(_store)!);
            Assert.That(SaveNeeded(await HitAndSave(shooter, captain, shooter.GetValue().Models[0].Weapons[0], false)), Is.EqualTo(4), "Chainmail is melee only");
        }

        [Test]
        public async Task DragonShield_TakesTwoOffTheAP()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            var shooter = Unit(_them, new Position(20, 10), 1, weapons: new Weapon("Cannon", 24f, 1, 3));
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true, defense: 3);
            Give(captain, RelicWargear.DragonShield);
            Assert.That(SaveNeeded(await HitAndSave(shooter, captain, shooter.GetValue().Models[0].Weapons[0], false)), Is.EqualTo(4), "D3 + AP(3-2)");
        }

        [Test]
        public async Task OpalAmulet_MinusOneToHit_OncePerGame()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            var shooter = Unit(_them, new Position(20, 10), 1, weapons: Pistol());
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true);
            RelicPick p = Give(captain, RelicWargear.OpalAmulet);
            CombatMetadata first = await HitAndSave(shooter, captain, shooter.GetValue().Models[0].Weapons[0], false);
            Assert.That(first.QueryForResult(out DetermineHitRollResults a) && a.HitRollNeeded == 5, $"-1 to hit (was {a.HitRollNeeded})");
            Assert.That(p.Used);
            CombatMetadata second = await HitAndSave(shooter, captain, shooter.GetValue().Models[0].Weapons[0], false);
            Assert.That(second.QueryForResult(out DetermineHitRollResults b) && b.HitRollNeeded == 4, "once per game");
            Assert.That(_yesNo, Has.Count.EqualTo(1));
        }

        // Lane 2, 2026-11-27: a bot used its once-per-game gem on the first attack of the game, a single pistol shot.
        [Test]
        public async Task ABot_KeepsTheAmulet_ForAnAttackWorthStopping()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            var pistol = Unit(_them, new Position(20, 10), 1, weapons: Pistol());
            var guns = Unit(_them, new Position(20, 20), 5, "Gunners", weapons: new Weapon("Rifle", 24f, 2, 0));
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true);
            Give(captain, RelicWargear.OpalAmulet);
            _yes = false;   // the player says no, so the item stays for the next question
            await HitAndSave(pistol, captain, pistol.GetValue().Models[0].Weapons[0], false);
            await HitAndSave(guns, captain, guns.GetValue().Models[0].Weapons[0], false);
            Assert.That(_yesNo.Select(y => y.DefaultAnswer), Is.EqualTo(new[] { false, true }),
                "the bot's answer: not for one pistol shot, yes for ten rifle shots");
        }

        private async Task<float> ApplyToHero(DataBinding<UnitData> attacker, DataBinding<UnitData> hero, float wounds, bool spell = false)
        {
            var weapon = new Weapon("Gun", 24f, 1, 0);
            var metadata = new CombatMetadata(_context, attacker, hero, weapon, weaponCount: 1, attackerMoved: false, isMelee: false, isSpell: spell);
            var results = new AssignWoundsResults(hero, new List<WoundPacket> { WoundPacket.Unconfined(wounds) });
            results.AutoFill();
            metadata.AddResult(results);
            float before = hero.GetValue().RemainingWounds;
            await Run(new ApplyWoundsStage<ICombatMetadata>(_context, new NoOpLayer<ICombatMetadata>()), metadata);
            return before - hero.GetValue().RemainingWounds;
        }

        [Test]
        public async Task ArmourOfDestiny_FivePlusIgnoresEachWound()
        {
            // Two wounds: a 5 (ignored) and a 2.
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free, new Faces(5, 2));
            var shooter = Unit(_them, new Position(20, 10), 1);
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true, tough: 3);
            Give(captain, RelicWargear.Destiny);
            Assert.That(await ApplyToHero(shooter, captain, 2f), Is.EqualTo(1f));
        }

        [Test]
        public async Task Dawnstone_OnlyAgainstSpells()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free, new Faces(2, 2));
            var shooter = Unit(_them, new Position(20, 10), 1);
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true, tough: 6);
            Give(captain, RelicWargear.Dawnstone);
            Assert.That(await ApplyToHero(shooter, captain, 2f), Is.EqualTo(2f), "a shot, not a spell");
            Assert.That(await ApplyToHero(shooter, captain, 2f, spell: true), Is.EqualTo(0f), "2+ each against a spell");
        }

        [Test]
        public async Task Luckstone_IgnoresUpToD3_Once()
        {
            // D3 off a 6 = 3: all three wounds ignored; the next three land.
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free, new Faces(6));
            var shooter = Unit(_them, new Position(20, 10), 1);
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true, tough: 6);
            RelicPick p = Give(captain, RelicWargear.Luckstone);
            Assert.That(await ApplyToHero(shooter, captain, 3f), Is.EqualTo(0f));
            Assert.That(p.Used);
            Assert.That(await ApplyToHero(shooter, captain, 3f), Is.EqualTo(3f), "once per game");
        }

        [Test]
        public async Task TalismanOfPreservation_SixIgnores_AndTakesAWoundBack()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free, new Faces(1, 6));
            var shooter = Unit(_them, new Position(20, 10), 1);
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true, tough: 6);
            Give(captain, RelicWargear.Preservation);
            ((ModelData)captain.GetValue().Models[0]).DealWounds(2f);
            Assert.That(await ApplyToHero(shooter, captain, 2f), Is.EqualTo(0f), "one lands, one ignored, and a wound removed");
        }

        [Test]
        public async Task SpellthievingWeapon_EnemyCasterLosesTokens_AndGetsFewerNextRound()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true, weapons: Blade());
            var wizard = Unit(_them, new Position(11.2f, 10), 3, "Wizard", casterLevel: 2);
            wizard.GetValue().Tokens.AddToken(new Token(TokenType.SpellTokens, 3, new TokenClearTrigger.ManualOnly()));
            Give(captain, RelicWargear.Spellthieving, "Blade");
            var weapon = captain.GetValue().Models[0].Weapons[0];
            var metadata = new CombatMetadata(_context, captain, wizard, weapon, weaponCount: 1, attackerMoved: false, isMelee: true);
            var results = new AssignWoundsResults(wizard, new List<WoundPacket> { WoundPacket.Unconfined(2f) });
            results.AutoFill();
            metadata.AddResult(results);
            await Run(new ApplyWoundsStage<ICombatMetadata>(_context, new NoOpLayer<ICombatMetadata>()), metadata);
            Assert.That(wizard.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(1), "loses one per wound");
            wizard.GetValue().Tokens.AddToken(new Token(TokenType.SpellTokens, 2, new TokenClearTrigger.ManualOnly()));
            RelicWargear.AfterRoundStartGrant(_context, wizard.GetValue());
            Assert.That(wizard.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(1), "two fewer next round");
        }

        [Test]
        public async Task ScrollOfShielding_FourPlusAgainstSpellWounds_FriendlyWithin12()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free, new FixedDiceRoller(4));
            var priest = Unit(_me, new Position(10, 10), 1, "Priest", hero: true);
            var squad = Unit(_me, new Position(16, 10), 5);
            Give(priest, RelicWargear.Shielding);
            Assert.That(await RelicWargear.ShieldFromSpell(_context, squad.GetValue(), 3f), Is.EqualTo(0f));
            Assert.That(await RelicWargear.ShieldFromSpell(_context, squad.GetValue(), 3f), Is.EqualTo(3f), "once per game");
        }

        // ================================================================= morale, activation, rounds, casting

        [Test]
        public async Task TerrifyingMask_EnemyInMelee_FailsItsMoraleTest()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free, new FixedDiceRoller(6));
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true);
            var foe = Unit(_them, new Position(11.2f, 10), 5);
            var far = Unit(_them, new Position(30, 30), 5);
            Give(captain, RelicWargear.TerrifyingMask);
            MoraleUtilities.MoraleTestOutcome distant = await MoraleUtilities.TakeMoraleTest(_context, far.GetValue(), 4, EMoraleTrigger.Shooting);
            Assert.That(distant.Passed, Is.True, "not in melee with the Hero");
            MoraleUtilities.MoraleTestOutcome near = await MoraleUtilities.TakeMoraleTest(_context, foe.GetValue(), 4, EMoraleTrigger.Shooting);
            Assert.That(near.Passed, Is.False, "the mask - despite a 6");
        }

        [Test]
        public async Task StandardOfDiscipline_PassesOnTwo()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free, new FixedDiceRoller(2));
            var squad = Unit(_me, new Position(10, 10), 5);
            MoraleUtilities.MoraleTestOutcome before = await MoraleUtilities.TakeMoraleTest(_context, squad.GetValue(), 4, EMoraleTrigger.Shooting);
            Assert.That(before.Passed, Is.False);
            squad.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.RuleGrant(RelicRuleCatalog.DisciplineName, ELifetime.UntilEndOfGame)));
            MoraleUtilities.MoraleTestOutcome after = await MoraleUtilities.TakeMoraleTest(_context, squad.GetValue(), 4, EMoraleTrigger.Shooting);
            Assert.That(after.Passed, Is.True);
        }

        [Test]
        public async Task SeedOfRebirth_AndTomeOfMagic_AtActivation()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free, new FixedDiceRoller(6));
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true, tough: 6);
            var psychic = Unit(_me, new Position(20, 10), 1, "Psychic", hero: true, casterLevel: 1);
            Give(captain, RelicWargear.SeedOfRebirth);
            Give(psychic, RelicWargear.Tome);
            ((ModelData)captain.GetValue().Models[0]).DealWounds(4f);
            await RelicWargear.OnActivation(_context, captain.GetValue());
            Assert.That(captain.GetValue().RemainingWounds, Is.EqualTo(5f), "D3 = 3 removed");
            await RelicWargear.OnActivation(_context, psychic.GetValue());
            Assert.That(psychic.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(3));
            await RelicWargear.OnActivation(_context, psychic.GetValue());
            Assert.That(psychic.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(3), "once per game");
        }

        [Test]
        public void CharmedHelmet_PlusOneTokenEachRound()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            var psychic = Unit(_me, new Position(20, 10), 1, "Psychic", hero: true, casterLevel: 1);
            Give(psychic, RelicWargear.CharmedHelmet);
            RelicWargear.AfterRoundStartGrant(_context, psychic.GetValue());
            RelicWargear.AfterRoundStartGrant(_context, psychic.GetValue());
            Assert.That(psychic.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(2));
        }

        [Test]
        public async Task SkirmishBanner_HeroUnitAndTheThreeNearestWithin12_ForTheRound()
        {
            Build(EGameSystem.GFFirefight, RelicWargear.Free);
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true);
            var near = Enumerable.Range(0, 4).Select(i => Unit(_me, new Position(12 + i * 2, 10), 1)).ToList();
            var far = Unit(_me, new Position(40, 10), 1);
            Give(captain, RelicWargear.Swiftness);
            await RelicWargear.RoundStart(_context);
            bool Has(DataBinding<UnitData> u) => u.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant)
                .Any(t => t.Payload is TokenPayload.RuleGrant g && g.RuleName == RelicRuleCatalog.SwiftnessName);
            Assert.That(Has(captain));
            Assert.That(near.Take(3).All(Has), "the three nearest");
            Assert.That(Has(near[3]), Is.False, "only three");
            Assert.That(Has(far), Is.False);
        }

        [Test]
        public async Task SkirmishBanner_OnlyWithin12()
        {
            Build(EGameSystem.AoFSkirmish, RelicWargear.Free);
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true);
            var close = Unit(_me, new Position(20, 10), 1);
            var beyond = Unit(_me, new Position(24, 10), 1);
            Give(captain, RelicWargear.Discipline);
            await RelicWargear.RoundStart(_context);
            Assert.That(RelicWargear.PassesOnTwo(close.GetValue()), "10 inches away (9 edge to edge)");
            Assert.That(RelicWargear.PassesOnTwo(beyond.GetValue()), Is.False, "14 inches away");
        }

        [Test]
        public async Task RangersStandard_GivesTheUnitScoutOrAmbush_AndSaysSoForTheScoutPass()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            Seats();
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true);
            _answer = q => q.TargetPlayerID != _me ? RelicWargear.NoRelic
                : q.DisplayName == RelicWargear.Ranger ? RelicWargear.Scout : RelicWargear.Ranger;
            Assert.That(await RelicWargear.Settle(_context), Is.True, "a Scout was handed out");
            Assert.That(captain.GetValue().RuleDefinitions.Any(r => r.Definition.Name == "Scout"));
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            Seats();
            var other = Unit(_me, new Position(10, 10), 1, "Captain", hero: true);
            _answer = q => q.TargetPlayerID != _me ? RelicWargear.NoRelic
                : q.DisplayName == RelicWargear.Ranger ? RelicWargear.Ambush : RelicWargear.Ranger;
            Assert.That(await RelicWargear.Settle(_context), Is.False, "Ambush waits in the normal pool");
            Assert.That(other.GetValue().RuleDefinitions.Any(r => r.Definition.Name == "Ambush"));
        }

        // Lane 1 (2026-11-27): the skirmish books' Ranger's Standard - "The Hero and up to half of its army get Ambush or
        // Scout (pick one). The models must deploy within 3" of the Hero." Five units: up to two others join the Hero.
        [Test]
        public async Task RangersStandard_InSkirmish_UpToHalfTheArmy_DeployWithin3OfTheHero()
        {
            Build(EGameSystem.GFFirefight, RelicWargear.Free);
            Seats();
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true);
            var others = Enumerable.Range(1, 4).Select(i => Unit(_me, new Position(20 + 3 * i, 10), 2, $"Squad {i}")).ToList();
            _answer = q => q.TargetPlayerID != _me ? RelicWargear.NoRelic
                : q.Instructions.Contains("pick up to") ? q.ValidOptions[0]
                : q.DisplayName == RelicWargear.Ranger ? RelicWargear.Scout : RelicWargear.Ranger;

            Assert.That(await RelicWargear.Settle(_context), Is.True);
            var tied = others.Where(u => u.GetValue().RuleDefinitions.Any(r => r.Definition.Name == "Scout")).ToList();
            Assert.That(tied, Has.Count.EqualTo(2), "half of a five-unit army, rounded down");
            Assert.That(RelicWargear.RangerHeroModel(_context, captain.GetValue()), Is.Null, "the Hero's own unit isn't tied");

            CircularZone? zone = RelicWargear.RangerZone(_context, tied[0].GetValue());
            Assert.That(zone, Is.Not.Null);
            Assert.That(zone!.IsPointWithinZone(new Float2(13.5f, 10f)), Is.True, "2.5\" from the Hero's base edge");
            Assert.That(zone.IsPointWithinZone(new Float2(15f, 10f)), Is.False, "4\" from the Hero's base edge");
            Assert.That(RelicWargear.RangerZone(_context, others.Except(tied).First().GetValue()), Is.Null, "not tied");
        }

        [Test]
        public async Task GrimdarkBanner_IsForTheWholeGame_OnTheHerosUnit()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            Seats();
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true);
            _answer = q => q.TargetPlayerID == _me ? RelicWargear.Rampager : q.Recommended!;
            await RelicWargear.Settle(_context);
            Assert.That(captain.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant)
                .Any(t => t.Payload is TokenPayload.RuleGrant g && g.RuleName == RelicRuleCatalog.RampagerName && t.ClearTrigger is TokenClearTrigger.ManualOnly));
        }

        [Test]
        public async Task DispelScroll_StopsAnEnemySpell_HexScrollWoundsTheCaster()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free, new FixedDiceRoller(6));
            var priest = Unit(_me, new Position(10, 10), 1, "Priest", hero: true);
            var wizard = Unit(_them, new Position(18, 10), 5, "Wizard", casterLevel: 1);
            var farWizard = Unit(_them, new Position(40, 40), 5, "Far Wizard", casterLevel: 1);
            Give(priest, RelicWargear.DispelScroll);
            Assert.That(await RelicWargear.AfterEnemyCast(_context, farWizard.GetValue(), "Bolt"), Is.False, "over 12\"");
            Assert.That(await RelicWargear.AfterEnemyCast(_context, wizard.GetValue(), "Bolt"), Is.True);
            Assert.That(await RelicWargear.AfterEnemyCast(_context, wizard.GetValue(), "Bolt"), Is.False, "once per game");
            var hexer = Unit(_me, new Position(12, 12), 1, "Hexer", hero: true);
            Give(hexer, RelicWargear.HexScroll);
            float before = wizard.GetValue().RemainingWounds;
            Assert.That(await RelicWargear.AfterEnemyCast(_context, wizard.GetValue(), "Bolt"), Is.False);
            Assert.That(before - wizard.GetValue().RemainingWounds, Is.EqualTo(3f), "D3 off a 6");
        }

        [Test]
        public async Task PowerWand_Once_AndChannelingStaff_DoublesRangeUntilSpent()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            var psychic = Unit(_me, new Position(10, 10), 1, "Psychic", hero: true, casterLevel: 1);
            var other = Unit(_me, new Position(30, 10), 1, "Other", casterLevel: 1);
            Give(psychic, RelicWargear.PowerWand);
            Assert.That(await RelicWargear.PowerWandOffer(_context, psychic.GetValue(), "Bolt"), Is.True);
            Assert.That(await RelicWargear.PowerWandOffer(_context, psychic.GetValue(), "Bolt"), Is.False);
            var keeper = Unit(_me, new Position(50, 10), 1, "Keeper", hero: true);
            Give(keeper, RelicWargear.ChannelingStaff);
            Assert.That(RelicWargear.StaffRange(_context, other.GetValue(), 12f), Is.EqualTo(12f), "any friendly caster");
            var enemy = Unit(_them, new Position(60, 60), 1, "Enemy", casterLevel: 1);
            Assert.That(RelicWargear.StaffRange(_context, enemy.GetValue(), 12f), Is.EqualTo(0f));
            await RelicWargear.StaffUsed(_context, other.GetValue(), "Bolt");
            Assert.That(RelicWargear.StaffRange(_context, other.GetValue(), 12f), Is.EqualTo(0f), "spent");
        }

        [Test]
        public void Off_NothingHappens()
        {
            Build(EGameSystem.GrimdarkFuture, RelicWargear.Free);
            var captain = Unit(_me, new Position(10, 10), 1, "Captain", hero: true, weapons: Blade());
            Give(captain, RelicWargear.Bloodshed, "Blade");
            GameSettings off = GameSettings.GetDefault();
            _context = new TestGameContext(_store, new FixedDiceRoller(4), settings: off);
            Assert.That(RelicWargear.AttackBonus(_context, captain.GetValue(), captain.GetValue(), captain.GetValue().Models[0].Weapons[0], false), Is.EqualTo(0));
        }
    }
}
