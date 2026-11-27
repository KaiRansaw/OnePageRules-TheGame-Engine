using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;

namespace FDG
{
    /// <summary>One Hero's relic item.</summary>
    [Serializable]
    public sealed class RelicPick
    {
        /// <summary>The owning player's id.</summary>
        public string Owner { get; set; } = "";
        public string OwnerName { get; set; } = "";
        /// <summary>The unit the Hero is part of (its own, or the one it joined).</summary>
        public string Unit { get; set; } = "";
        public string UnitName { get; set; } = "";
        /// <summary>The Hero's model.</summary>
        public string Model { get; set; } = "";
        public string Hero { get; set; } = "";
        public string Item { get; set; } = "";
        /// <summary>The weapon a relic weapon went on (Relic Weapons), by name.</summary>
        public string? Weapon { get; set; }
        /// <summary>Ranger's Standard: "Ambush" or "Scout".</summary>
        public string? Choice { get; set; }
        /// <summary>Ranger's Standard in the skirmish books: the other units ("up to half of its army") that took the same
        /// rule and "must deploy within 3\" of the Hero" - by unit key.</summary>
        public List<string> RangerUnits { get; set; } = new();
        /// <summary>A once-per-game item has been used.</summary>
        public bool Used { get; set; }
        /// <summary>Power Wand: used, waiting for the Hero's next cast.</summary>
        public bool Armed { get; set; }
    }

    /// <summary>Relic Wargear, saved with the game.</summary>
    [Serializable]
    public sealed class RelicWargearState
    {
        /// <summary>The selection method in force: 1 Free, 2 Limited, 3 Randomized (0 until settled).</summary>
        public int Method { get; set; }
        public List<RelicPick> Picks { get; set; } = new();
        /// <summary>Spellthieving Weapon: unit id -> spell tokens the caster gets fewer of next round.</summary>
        public Dictionary<string, int> Thieved { get; set; } = new();
        /// <summary>The players who have been asked (so a resumed game doesn't ask again).</summary>
        public List<string> Settled { get; set; } = new();
        /// <summary>Recent events for the Mission panel, newest last.</summary>
        public List<string> Recent { get; set; } = new();
    }

    /// <summary>
    /// Advanced Casting - Relic Wargear (GF p36-37, GFF p33-34) / Magic Items (AoF p36-37, AoFS p33-34). "Before the
    /// game begins, each Hero may equip one relic item. Pick one of the following selection methods, or roll a D3 to
    /// randomly get one: 1. Free 2. Limited 3. Randomized ... Each army may only have one copy of each item, and if you
    /// randomly get a duplicate, you must re-roll to get another item." Six groups of six items; the two Skirmish books
    /// (GFF, AoFS) reword the banners, Terrifying Mask and the two once-per-game gems; the two Fantasy books rename three
    /// Enchanted Items. Picked for each Hero before deployment (bots roll). Readings: house rules C, "Relic Wargear".
    /// </summary>
    public static class RelicWargear
    {
        public enum EKind { Plain, Weapon, MeleeWeapon, OncePerGame, Ranger }

        public sealed record Def(int Category, int Number, string Name, string Text, EKind Kind);

        public const int Free = 1, Limited = 2, Randomized = 3;
        public const int PickShift = 44;
        public const EAdvancedRules PickMask = (EAdvancedRules)(0x3UL << PickShift);

        public static readonly string[] Categories =
            { "Relic Weapons", "Holy Armour", "Sorcerous Talismans", "Mystic Banners", "Arcane Items", "Enchanted Items" };

        // Item names the engine reads.
        public const string Bloodshed = "Weapon of Bloodshed", Obsidian = "Obsidian Weapon", Kingslayer = "Kingslayer Weapon",
            Biting = "Biting Weapon", Spellthieving = "Spellthieving Weapon", Fencer = "Fencer's Blade",
            Destiny = "Armour of Destiny", Fortune = "Helmet of Fortune", SilverSteel = "Silver-Steel Shield",
            Chainmail = "Glittering Chainmail", DragonShield = "Dragon Shield", CharmedHelmet = "Charmed Helmet",
            Preservation = "Talisman of Preservation", Dawnstone = "Dawnstone", ProtectiveGem = "Protective Gem",
            OpalAmulet = "Opal Amulet", Luckstone = "Luckstone", SeedOfRebirth = "Seed of Rebirth",
            Swiftness = "Banner of Swiftness", Rampager = "Rampager's Standard", EternalFlame = "Banner of Eternal Flame",
            Ranger = "Ranger's Standard", Discipline = "Standard of Discipline", Wailing = "Wailing Banner",
            Tome = "Tome of Magic", HexScroll = "Hex Scroll", PowerWand = "Power Wand", DispelScroll = "Dispel Scroll",
            ChannelingStaff = "Channeling Staff", Shielding = "Scroll of Shielding",
            TrickstersShard = "Trickster's Shard", TerrifyingMask = "Terrifying Mask", RingOfRuin = "Ring of Ruin";

        public const string Ambush = "Ambush", Scout = "Scout", NoRelic = "No relic";

        private static bool Fantasy(EGameSystem s) => s is EGameSystem.AgeOfFantasy or EGameSystem.AoFSkirmish;
        private static bool Skirmish(EGameSystem s) => s is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;

        /// <summary>Enchanted Item 1 (Caster), 2 (Flying) and 6 (place within 6"), by book.</summary>
        public static string Harness(EGameSystem s) => Fantasy(s) ? "Wizarding Hat" : "Psychic Harness";
        public static string Wings(EGameSystem s) => Fantasy(s) ? "Flying Carpet" : "Conjured Wings";
        public static string Displacement(EGameSystem s) => Fantasy(s) ? "Potion of Displacement" : "Displacement Device";

        public static string OptionName(EGameSystem s) => Fantasy(s) ? "Magic Items" : "Relic Wargear";

        private static string Banner(EGameSystem s, string what) => Skirmish(s)
            ? $"The Hero and up to 3 friendly units that are within 12\" at the beginning of the round {what}. This effect lasts until the end of the round."
            : $"The Hero and its unit {what}.";

        /// <summary>The 36 items as this book prints them. The numbers are the printed ones (GF and AoF print the Enchanted
        /// Items in the order 1, 2, 5, 4, 3, 6, but each item keeps the same number as in the other two books).</summary>
        public static IReadOnlyList<Def> Table(EGameSystem s)
        {
            string theHero = Skirmish(s) ? "the Hero" : "the Hero and its unit";
            return new[]
            {
                new Def(0, 1, Bloodshed, "Pick one of the Hero's weapons, which gets +3 Attacks.", EKind.Weapon),
                new Def(0, 2, Obsidian, "Pick one of the Hero's weapons, which gets AP(4).", EKind.Weapon),
                new Def(0, 3, Kingslayer, "Pick one of the Hero's weapons, which gets +3 Attacks and AP(4) when fighting against Heroes and their unit.", EKind.Weapon),
                new Def(0, 4, Biting, "Pick one of the Hero's weapons, which hits at Quality 2+.", EKind.Weapon),
                new Def(0, 5, Spellthieving, "Pick one of the Hero's weapons. When an enemy Caster takes wounds from the Hero, for each wound it loses 1 spell token, and gets 1 less spell token next round.", EKind.Weapon),
                new Def(0, 6, Fencer, "Pick one of the Hero's melee weapons, which ignores Fatigue.", EKind.MeleeWeapon),
                new Def(1, 1, Destiny, "When the Hero takes a wound, roll one die. On a 5+ the wound is ignored.", EKind.Plain),
                new Def(1, 2, Fortune, "The Hero gets +1 to Defense rolls.", EKind.Plain),
                new Def(1, 3, SilverSteel, "The Hero counts as having Defense 2+ when shot at.", EKind.Plain),
                new Def(1, 4, Chainmail, "The Hero counts as having Defense 2+ in melee.", EKind.Plain),
                new Def(1, 5, DragonShield, "When the Hero takes hits, they count as having AP(-2), to a min. of AP(0).", EKind.Plain),
                new Def(1, 6, CharmedHelmet, "The Hero gets +1 spell token at the beginning of each round.", EKind.Plain),
                new Def(2, 1, Preservation, "When the Hero takes a wound, roll one die. On a 6+ the wound is ignored, and you may remove one wound from it.", EKind.Plain),
                new Def(2, 2, Dawnstone, "When the Hero takes a wound from a spell, roll one die. On a 2+ the wound is ignored.", EKind.Plain),
                new Def(2, 3, ProtectiveGem, $"Once per game, you may give an enemy unit -1 to hit rolls in melee when attacking {theHero}.", EKind.OncePerGame),
                new Def(2, 4, OpalAmulet, $"Once per game, you may give an enemy unit -1 to hit rolls when shooting at {theHero}.", EKind.OncePerGame),
                new Def(2, 5, Luckstone, "Once per game, when the Hero takes one or more wounds, you may ignore up to D3 of those wounds.", EKind.OncePerGame),
                new Def(2, 6, SeedOfRebirth, "Once per game, when the Hero is activated, you may remove up to D3 wounds from it.", EKind.OncePerGame),
                new Def(3, 1, Swiftness, Banner(s, "get +2\" when using Advance actions"), EKind.Plain),
                new Def(3, 2, Rampager, Banner(s, "get +4\" when using Charge actions"), EKind.Plain),
                new Def(3, 3, EternalFlame, Banner(s, "may ignore all terrain effects"), EKind.Plain),
                new Def(3, 4, Ranger, Skirmish(s)
                    ? "The Hero and up to half of its army get Ambush or Scout (pick one). The models must deploy within 3\" of the Hero."
                    : "The Hero and its unit get Ambush or Scout (pick one).", EKind.Ranger),
                new Def(3, 5, Discipline, Banner(s, "pass Morale Tests on rolls of 2+"), EKind.Plain),
                new Def(3, 6, Wailing, "The Hero gets Fear(+2).", EKind.Plain),
                new Def(4, 1, Tome, "Once per game, the Hero gets 3 spell tokens.", EKind.OncePerGame),
                new Def(4, 2, HexScroll, "Once per game, after an enemy Caster within 12\" of the Hero successfully casts a spell, it takes D3 wounds.", EKind.OncePerGame),
                new Def(4, 3, PowerWand, "Once per game, the Hero casts its next spell on a roll of 2+.", EKind.OncePerGame),
                new Def(4, 4, DispelScroll, "Once per game, after an enemy caster within 12\" of the Hero successfully casts a spell, it may not resolve the spell's effects.", EKind.OncePerGame),
                new Def(4, 5, ChannelingStaff, "Once per game, after a friendly Caster successfully casts a spell, it may double the spell's range.", EKind.OncePerGame),
                new Def(4, 6, Shielding, "Once per game, after a friendly unit within 12\" of the Hero takes wounds from a spell, it may roll one die for each wound. On a 4+ that wound is ignored.", EKind.OncePerGame),
                new Def(5, 1, Harness(s), "The Hero gets Caster(+1).", EKind.Plain),
                new Def(5, 2, Wings(s), "The Hero gets Flying.", EKind.Plain),
                new Def(5, 3, RingOfRuin, "The Hero gets Impact(+3).", EKind.Plain),
                new Def(5, 4, TrickstersShard, "The Hero's weapons ignore Regeneration.", EKind.Plain),
                new Def(5, 5, TerrifyingMask, Skirmish(s)
                    ? "Once per game, enemy units within 6\" of the Hero automatically fail all Morale Tests."
                    : "Once per game, enemy units in melee with the Hero automatically fail all Morale Tests.", EKind.OncePerGame),
                new Def(5, 6, Displacement(s), "Once per game, when the Hero is activated, you may place it anywhere within 6\" of its position.", EKind.OncePerGame),
            };
        }

        /// <summary>The Hero a relic question is about, from its wording ("... - Captain (with Squad): ...").</summary>
        public static string HeroInQuestion(string instructions)
        {
            int dash = instructions.IndexOf(" - ", StringComparison.Ordinal);
            if (dash < 0) return "";
            int colon = instructions.IndexOf(": ", dash + 3, StringComparison.Ordinal);
            return colon < 0 ? instructions[(dash + 3)..] : instructions[(dash + 3)..colon];
        }

        public static Def? Find(EGameSystem s, string name) => Table(s).FirstOrDefault(d => d.Name == name);

        // ---------------------------------------------------------------- lobby

        public static string MethodName(int m) => m switch { Free => "Free", Limited => "Limited", Randomized => "Randomized", _ => "Roll for it" };

        public static string MethodText(int m) => m switch
        {
            Free => "Players may freely pick any one item from any of the categories.",
            Limited => "Players must roll one die to get a category, and may then freely pick any one item from it.",
            Randomized => "Players must roll one die to get a category, then roll two dice to get two random items from it, and then pick one of them to keep.",
            _ => "Roll a D3 when the game starts: 1 Free, 2 Limited, 3 Randomized.",
        };

        public static string BookText(EGameSystem s)
        {
            string items = Fantasy(s) ? "Magic Items" : "Relic Wargear";
            return $"{items} can be anything from holy artifacts, to relic swords, to lost rings of might, and much more. These items are "
                + "assigned Heroes to make them more powerful.\nBefore the game begins, each Hero may equip one " + (Fantasy(s) ? "magic" : "relic")
                + " item. Pick one of the following selection methods, or roll a D3 to randomly get one: 1. Free 2. Limited 3. Randomized\n"
                + string.Join("\n", Enumerable.Range(1, 3).Select(m => $"{m}. {MethodName(m)} - {MethodText(m)}"))
                + "\nEach army may only have one copy of each item, and if you randomly get a duplicate, you must re-roll to get another item.\n"
                + string.Join("\n", Table(s).OrderBy(d => d.Category).ThenBy(d => d.Number).Select(d => $"{Categories[d.Category]} {d.Number}. {d.Name}: {d.Text}"));
        }

        public const string GameText = "Pick the selection method below (or roll for it). Before deployment every player gets a slot for each "
            + "Hero: pick, or roll then pick, by that method - or leave a Hero without one. Bots roll theirs. The relic shows on the Hero's "
            + "card and by its name on the table; once-per-game items are offered when they can be used.";

        public static bool On(IGameContext c) => AdvancedRules.IsOn(c, EAdvancedRules.RelicWargear);

        /// <summary>The method picked in the lobby (0 = roll for it).</summary>
        public static int Picked(GameSettings s) => (int)(((ulong)s.AdvancedRules >> PickShift) & 0x3);

        public static EAdvancedRules WithPick(EAdvancedRules rules, int pick) =>
            (rules & ~PickMask) | (EAdvancedRules)(((ulong)Math.Clamp(pick, 0, 3)) << PickShift);

        // ---------------------------------------------------------------- state

        private static readonly ConditionalWeakTable<IReadableGameDataStore, RelicWargearState> Pending = new();

        public static RelicWargearState? State(IReadableGameDataStore store) =>
            GameProgressUtilities.TryGetProgress(store)?.RelicWargear
            ?? (Pending.TryGetValue(store, out RelicWargearState? s) ? s : null);

        internal static void Save(IGameContext c, RelicWargearState s)
        {
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(c.GameDataStore);
            if (progress == null)
            {
                Pending.Remove(c.GameDataStore);
                Pending.Add(c.GameDataStore, s);
                return;
            }
            progress.RelicWargear = s;
            GameProgressUtilities.WriteProgress(c.GameDataStore, progress);
        }

        public static readonly TextColor Gold = new TextColor(217, 178, 74, 255);

        private static int D6(IGameContext c) => c.DiceRoller.RollDecisiveFace();
        private static int D3(IGameContext c) => (D6(c) + 1) / 2;

        private static string UnitKey(IUnit u) => AdvancedCasting.Id(u);
        private static string ModelKey(IModel m) => m.ID.ToString();

        private static void Note(RelicWargearState s, string line)
        {
            s.Recent.Add(line);
            if (s.Recent.Count > 6) s.Recent.RemoveAt(0);
        }

        // ---------------------------------------------------------------- the Heroes

        /// <summary>A Hero in play: its unit and model, and what to call it.</summary>
        public sealed record HeroSlot(UnitData Unit, IModel Model, string Name);

        private static bool HasHeroRule(IEnumerable<ResolvedRule> rules) =>
            rules.Any(r => CoreRuleCatalog.IsHeroRuleName(r.Definition.Name) || CoreRuleCatalog.IsHeroRuleName(r.RequestedName));

        /// <summary>Every Hero of a player: a joined Hero (its host unit and its own model), or a Hero on its own.</summary>
        public static List<HeroSlot> HeroesOf(IGameContext c, PlayerID player) => HeroesOf(c.GameDataStore, player);

        public static List<HeroSlot> HeroesOf(IReadableGameDataStore store, PlayerID player)
        {
            var list = new List<HeroSlot>();
            foreach (ArmyData army in store.GetAllValues<ArmyData>().Where(a => a.IsOwnedBy(player)))
                foreach (DataBinding<UnitData> binding in army.UnitBindings)
                {
                    UnitData u = binding.GetValue();
                    if (!u.GetIsAlive()) continue;
                    if (u.HeroAttachment is { } joined)
                    {
                        IModel? m = u.Models.FirstOrDefault(x => x.ID.Equals(joined.HeroModelId));
                        if (m != null) list.Add(new HeroSlot(u, m, joined.Name ?? "Hero"));
                    }
                    else if (u.Models.Count == 1 && HasHeroRule(u.RuleDefinitions))
                        list.Add(new HeroSlot(u, u.Models[0], u.Name));
                }
            return list;
        }

        /// <summary>The living Hero in this unit with a relic, and its pick.</summary>
        private static IEnumerable<(RelicPick Pick, IModel Model)> Carried(IGameContext c, IUnit? unit)
        {
            if (unit == null || !On(c) || State(c.GameDataStore) is not { } s) yield break;
            string key = UnitKey(unit);
            foreach (RelicPick p in s.Picks)
            {
                if (p.Unit != key) continue;
                IModel? m = unit.Models.FirstOrDefault(x => ModelKey(x) == p.Model);
                if (m != null && m.GetIsAlive()) yield return (p, m);
            }
        }

        private static (RelicPick Pick, IModel Model)? Carrying(IGameContext c, IUnit? unit, string item) =>
            Carried(c, unit).Where(x => x.Pick.Item == item).Select(x => ((RelicPick, IModel)?)x).FirstOrDefault();

        /// <summary>The relic item a model carries (the unit card / the table label), or null.</summary>
        public static string? ItemOf(IReadableGameDataStore store, IUnit unit, IModel model) =>
            State(store)?.Picks.FirstOrDefault(p => p.Unit == UnitKey(unit) && p.Model == ModelKey(model))?.Item;

        /// <summary>The relic picks of a unit, for its card.</summary>
        public static IReadOnlyList<RelicPick> PicksOf(IReadableGameDataStore store, IUnit unit) =>
            State(store)?.Picks.Where(p => p.Unit == UnitKey(unit)).ToList() ?? new List<RelicPick>();

        /// <summary>Whether the Hero stands on its own - a Hero unit, or the last model of the unit it joined: the moments
        /// its own Defense (and so its relic armour) is what the unit saves with.</summary>
        private static bool Alone(IUnit unit, IModel hero)
        {
            var living = unit.Models.Where(m => m.GetIsAlive()).ToList();
            return living.Count == 1 && living[0].ID.Equals(hero.ID);
        }

        private static bool Friendly(IGameContext c, PlayerID a, PlayerID b) =>
            a == b || ITeamExtensions.AreAllied(c.TableState.Teams.Objects, a, b);

        private static float Gap(IModel m, IUnit unit) =>
            unit.Models.Where(x => x.GetIsAlive()).Select(x => Position.GetDistance2D(m.Position, x.Position) - m.BaseRadiusInches - x.BaseRadiusInches)
                .DefaultIfEmpty(float.MaxValue).Min();

        private static IEnumerable<IUnit> LiveUnits(IGameContext c) =>
            c.TableState.Units.Objects.Where(u => u.GetIsAlive() && u.GetIsOnBattlefield());

        private static IEnumerable<(RelicPick Pick, IUnit Unit, IModel Model)> AllCarried(IGameContext c)
        {
            if (!On(c) || State(c.GameDataStore) is not { } s) yield break;
            foreach (IUnit u in c.TableState.Units.Objects.Where(u => u.GetIsAlive()))
                foreach ((RelicPick p, IModel m) in Carried(c, u))
                    yield return (p, u, m);
        }

        private static async Task<bool> Ask(IGameContext c, PlayerID player, string question, bool bot, string title) =>
            await c.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(player, question, defaultAnswer: bot, displayName: title));

        private static async Task Use(IGameContext c, RelicPick p, string line)
        {
            p.Used = true;
            if (State(c.GameDataStore) is { } s)
            {
                Note(s, line);
                Save(c, s);
            }
            await c.Announce(line, Gold, EBannerTier.Toast,
                terms: Find(c.Settings.GameSystem, p.Item) is { } d ? new[] { new ReportTerm(d.Name, d.Text) } : null);
        }

        // ---------------------------------------------------------------- before the game

        /// <summary>"Before the game begins, each Hero may equip one relic item." The method is settled (rolled if the lobby
        /// left it to the dice), then each player picks for each Hero in turn; bots take what the dice give them.</summary>
        public static async Task<bool> Settle(IGameContext c)
        {
            if (!On(c)) return false;
            bool scouts = false;
            RelicWargearState s = State(c.GameDataStore) ?? new RelicWargearState();
            EGameSystem system = c.Settings.GameSystem;
            if (s.Method == 0)
            {
                int m = Picked(c.Settings);
                if (m == 0)
                {
                    m = D3(c);
                    c.Log($"{OptionName(system)}: rolled {m} for the selection method - {MethodName(m)}.");
                }
                s.Method = m;
                await c.Announce($"{OptionName(system)} - {MethodName(m)}: {MethodText(m)}", Gold, EBannerTier.Notice);
            }
            var defs = Table(system);
            foreach (IPlayerSlotInfo seat in c.TableState.Players.Objects.Where(p => p.IsFilled).OrderBy(p => p.SlotID))
            {
                string owner = seat.PlayerID.ToString();
                if (s.Settled.Contains(owner)) continue;
                foreach (HeroSlot hero in HeroesOf(c, seat.PlayerID))
                {
                    if (s.Picks.Any(p => p.Model == ModelKey(hero.Model))) continue;
                    var taken = s.Picks.Where(p => p.Owner == owner).ToDictionary(p => p.Item, p => p.Hero);
                    bool Usable(Def d) => !taken.ContainsKey(d.Name) && (d.Kind != EKind.MeleeWeapon || MeleeWeapons(hero.Model).Any())
                        && (d.Kind != EKind.Weapon || Weapons(hero.Model).Any());
                    string unitNote = hero.Unit.HeroAttachment != null ? $" (with {hero.Unit.Name})" : "";
                    List<Def> offered;
                    string rolled;
                    Def bot;
                    if (s.Method == Free)
                    {
                        offered = defs.Where(Usable).ToList();
                        bot = RollItem(c, defs, Usable);
                        rolled = "Pick any item";
                    }
                    else
                    {
                        // Limited / Randomized: a die for the category (re-rolled if the army already has all six).
                        int cat = -1;
                        for (int guard = 0; guard < 50 && cat < 0; guard++)
                        {
                            int die = D6(c) - 1;
                            if (defs.Any(d => d.Category == die && Usable(d))) cat = die;
                        }
                        if (cat < 0) cat = defs.First(Usable).Category;
                        if (s.Method == Limited)
                        {
                            offered = defs.Where(d => d.Category == cat && Usable(d)).ToList();
                            bot = offered[(D6(c) - 1) % offered.Count];
                            rolled = $"Rolled {cat + 1}: {Categories[cat]}. Pick any item from it";
                        }
                        else
                        {
                            // Two dice for two items; a duplicate (of the army's, or of the first die) is re-rolled.
                            offered = new List<Def>();
                            var dice = new List<int>();
                            for (int guard = 0; guard < 60 && offered.Count < 2; guard++)
                            {
                                int die = D6(c);
                                Def d = defs.First(x => x.Category == cat && x.Number == die);
                                if (!Usable(d) || offered.Contains(d)) continue;
                                offered.Add(d);
                                dice.Add(die);
                            }
                            if (offered.Count == 0) offered.Add(defs.First(Usable));
                            bot = offered[(D6(c) - 1) % offered.Count];
                            rolled = $"Rolled {cat + 1}: {Categories[cat]}, then {string.Join(" and ", dice)}. Keep one";
                        }
                    }
                    var options = offered.Select(d => d.Name).Append(NoRelic).ToList();
                    var invalid = defs.Where(d => !offered.Contains(d))
                        .Select(d => new StringSelectionRequest.InvalidOption(d.Name,
                            taken.TryGetValue(d.Name, out string? on) ? $"on {on}" : !Usable(d) ? "no weapon for it" : "not rolled"))
                        .ToList();
                    var texts = defs.ToDictionary(d => d.Name, d => $"{Categories[d.Category]} {d.Number}: {d.Text}");
                    texts[NoRelic] = "This Hero goes without a relic.";
                    string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(
                        seat.PlayerID, $"{OptionName(system)} ({MethodName(s.Method)}) - {hero.Name}{unitNote}: {rolled}, or none.",
                        options, invalid, optionDescriptions: texts, displayName: OptionName(system)) { Recommended = bot.Name });
                    Def? chosen = offered.FirstOrDefault(d => d.Name == answer);
                    if (answer != NoRelic && chosen == null) chosen = bot;
                    if (chosen == null)
                    {
                        c.Log($"{OptionName(system)}: {seat.Name}'s {hero.Name} goes without.");
                        continue;
                    }
                    var pick = new RelicPick
                    {
                        Owner = owner, OwnerName = seat.Name, Unit = UnitKey(hero.Unit), UnitName = hero.Unit.Name,
                        Model = ModelKey(hero.Model), Hero = hero.Name, Item = chosen.Name,
                    };
                    if (chosen.Kind is EKind.Weapon or EKind.MeleeWeapon)
                    {
                        var weapons = (chosen.Kind == EKind.MeleeWeapon ? MeleeWeapons(hero.Model) : Weapons(hero.Model)).ToList();
                        string best = weapons.OrderByDescending(w => w.Attacks).First().Name;
                        var names = weapons.Select(w => w.Name).Distinct().ToList();
                        pick.Weapon = names.Count == 1 ? names[0] : await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(
                            new StringSelectionRequest(seat.PlayerID, $"{chosen.Name}: which of {hero.Name}'s weapons gets it?", names,
                                new List<StringSelectionRequest.InvalidOption>(),
                                optionDescriptions: weapons.GroupBy(w => w.Name).ToDictionary(g => g.Key, g => g.First().GetWeaponNameAndStats()),
                                displayName: chosen.Name) { Recommended = best });
                        if (!names.Contains(pick.Weapon)) pick.Weapon = best;
                    }
                    if (chosen.Kind == EKind.Ranger)
                    {
                        pick.Choice = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(
                            seat.PlayerID, $"{chosen.Name}: {(Skirmish(system) ? "the Hero" : "the Hero and its unit")} get Ambush or Scout?",
                            new List<string> { Scout, Ambush }, new List<StringSelectionRequest.InvalidOption>(), displayName: chosen.Name)
                        { Recommended = Scout });
                        if (pick.Choice != Ambush) pick.Choice = Scout;
                        scouts |= pick.Choice == Scout;
                        if (Skirmish(system)) await PickRangerUnits(c, seat.PlayerID, hero, pick);
                    }
                    s.Picks.Add(pick);
                    Apply(c, pick, hero);
                    Save(c, s);   // the picker shows each Hero's item as soon as it is chosen
                    c.Log($"{OptionName(system)}: {seat.Name}'s {hero.Name}{unitNote} takes the {chosen.Name}"
                        + (pick.Weapon != null ? $" on its {pick.Weapon}" : "") + (pick.Choice != null ? $" ({pick.Choice})" : "") + ".");
                }
                s.Settled.Add(owner);
                Save(c, s);
            }
            Save(c, s);
            return scouts;
        }

        /// <summary>
        /// Ranger's Standard in GF: Firefight / AoF: Skirmish (Advanced p34): "The Hero and up to half of its army get Ambush
        /// or Scout (pick one). The models must deploy within 3" of the Hero." Half of the army's units, rounded down; the
        /// player picks them one at a time (bots take none). They deploy - or arrive from Ambush - within 3" of the Hero
        /// (see <see cref="RangerHeroModel"/>). Lane 1, 2026-11-27.
        /// </summary>
        private static async Task PickRangerUnits(IGameContext c, PlayerID player, HeroSlot hero, RelicPick pick)
        {
            const string done = "Done";
            List<UnitData> army = c.GameDataStore.GetAllValues<ArmyData>().Where(a => a.IsOwnedBy(player))
                .SelectMany(a => a.UnitBindings).Select(b => b.GetValue()).Where(u => u.GetIsAlive()).ToList();
            int max = army.Count / 2;
            var candidates = army.Where(u => !ReferenceEquals(u, hero.Unit)).ToList();
            while (pick.RangerUnits.Count < max)
            {
                var left = candidates.Where(u => !pick.RangerUnits.Contains(UnitKey(u))).ToList();
                if (left.Count == 0) break;
                var labels = new Dictionary<string, UnitData>();
                foreach (UnitData u in left)
                {
                    string label = u.Name;
                    for (int n = 2; labels.ContainsKey(label); n++) label = $"{u.Name} ({n})";
                    labels[label] = u;
                }
                var options = labels.Keys.Append(done).ToList();
                string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(player,
                    $"{Ranger}: pick up to {max - pick.RangerUnits.Count} more unit(s) to get {pick.Choice} with {hero.Name} "
                    + "(they must deploy within 3\" of the Hero), or Done.",
                    options, new List<StringSelectionRequest.InvalidOption>(), displayName: Ranger) { Recommended = done });
                if (!labels.TryGetValue(answer, out UnitData? chosen)) break;
                pick.RangerUnits.Add(UnitKey(chosen));
                c.Log($"{Ranger}: {chosen.Name} gets {pick.Choice} with {hero.Name}.");
            }
        }

        /// <summary>
        /// The Hero model a unit given Ranger's Standard by another unit's Hero must deploy within 3" of (the skirmish
        /// books' "The models must deploy within 3\" of the Hero"), or null when the unit isn't tied to one. The Hero's
        /// own unit is never tied.
        /// </summary>
        public static IModel? RangerHeroModel(IGameContext c, IUnit unit)
        {
            if (State(c.GameDataStore) is not { } s) return null;
            string key = UnitKey(unit);
            RelicPick? p = s.Picks.FirstOrDefault(x => x.Item == Ranger && x.RangerUnits.Contains(key));
            if (p == null) return null;
            UnitData? heroUnit = c.GameDataStore.GetAllValues<UnitData>().FirstOrDefault(u => UnitKey(u) == p.Unit);
            return heroUnit?.Models.FirstOrDefault(m => m.ID.ToString() == p.Model);
        }

        /// <summary>The area a Ranger-tied unit's models may be placed in: every model's centre within 3" of the Hero's
        /// base edge (plus the two base radii, centre to centre). Null when the Hero is dead or not on the table.</summary>
        public static FDG.CircularZone? RangerZone(IGameContext c, IUnit unit)
        {
            IModel? hero = RangerHeroModel(c, unit);
            if (hero == null || !hero.GetIsAlive()) return null;
            IUnit? heroUnit = c.TableState.Units.Objects.FirstOrDefault(u => u.Models.Contains(hero));
            if (heroUnit == null || Rules.Dispatch.ReserveRules.IsInReserve(heroUnit)) return null;
            float r = unit.Models.Count == 0 ? 0.5f : unit.Models.Max(m => m.BaseRadiusInches);
            return new FDG.CircularZone(new Float2(hero.Position.x, hero.Position.z), 3f + hero.BaseRadiusInches + r);
        }

        private static Def RollItem(IGameContext c, IReadOnlyList<Def> defs, Func<Def, bool> usable)
        {
            for (int guard = 0; guard < 80; guard++)
            {
                int cat = D6(c) - 1, n = D6(c);
                Def d = defs.First(x => x.Category == cat && x.Number == n);
                if (usable(d)) return d;
            }
            return defs.First(usable);
        }

        private static IEnumerable<IWeapon> Weapons(IModel m) => m.Weapons;
        private static IEnumerable<IWeapon> MeleeWeapons(IModel m) => m.Weapons.Where(w => w.IsMelee());

        /// <summary>The items that are standing rules: put on the Hero (its model if it joined a unit, else its unit), on its
        /// unit (the banners), or on its weapons (Trickster's Shard).</summary>
        private static void Apply(IGameContext c, RelicPick p, HeroSlot hero)
        {
            EGameSystem system = c.Settings.GameSystem;
            bool joined = hero.Unit.HeroAttachment != null;
            void OnHero(string rule, int? arg)
            {
                ResolvedRule? r = Resolve(c, rule, arg);
                if (r == null) return;
                if (joined && hero.Model is ModelData md) md.AttachRuleDefinition(r);
                else hero.Unit.AttachRuleDefinition(r);
            }
            if (p.Item == Harness(system)) OnHero("Caster", 1);
            else if (p.Item == Wings(system)) OnHero("Flying", null);
            else if (p.Item == RingOfRuin) OnHero("Impact", 3);
            else if (p.Item == Wailing) OnHero("Fear", 2);
            else if (p.Item == TrickstersShard)
            {
                foreach (IWeapon w in hero.Model.Weapons)
                    if (w is Weapon weapon) weapon.AttachRuleDefinition(new ResolvedRule(RelicRuleCatalog.TricksterName, RelicRuleCatalog.Trickster));
            }
            else if (p.Item == Ranger && Resolve(c, p.Choice ?? Scout, null) is { } ranger)
            {
                hero.Unit.AttachRuleDefinition(ranger);
                foreach (string key in p.RangerUnits)
                {
                    UnitData? tied = c.GameDataStore.GetAllValues<UnitData>().FirstOrDefault(u => UnitKey(u) == key);
                    if (tied != null && Resolve(c, p.Choice ?? Scout, null) is { } rule) tied.AttachRuleDefinition(rule);
                }
            }
            else if (!Skirmish(system) && BannerRule(p.Item) is { } banner)
                hero.Unit.Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.ManualOnly(),
                    Payload: new TokenPayload.RuleGrant(banner, ELifetime.UntilEndOfGame)));
        }

        private static string? BannerRule(string item) => item switch
        {
            Swiftness => RelicRuleCatalog.SwiftnessName,
            Rampager => RelicRuleCatalog.RampagerName,
            EternalFlame => RelicRuleCatalog.EternalFlameName,
            Discipline => RelicRuleCatalog.DisciplineName,
            _ => null,
        };

        private static ResolvedRule? Resolve(IGameContext c, string name, int? arg)
        {
            SpecialRuleDefinition? def = null;
            if (c.RuleEvaluator.RuleResolver is { } resolver && resolver.TryResolve(name, out ResolvedRule found)) def = found.Definition;
            def ??= name switch
            {
                "Caster" => CoreRuleCatalog.Caster, "Flying" => CoreRuleCatalog.Flying, "Impact" => CoreRuleCatalog.Impact,
                "Fear" => CoreRuleCatalog.Fear, "Ambush" => CoreRuleCatalog.Ambush, "Scout" => CoreRuleCatalog.Scout, _ => null,
            };
            if (def == null) return null;
            return arg is int x ? new ResolvedRule(name, def, new RuleArgument[] { new RuleArgument.Int(x) }) : new ResolvedRule(name, def);
        }

        // ---------------------------------------------------------------- the Skirmish banners

        /// <summary>The Skirmish banners (GFF, AoFS): "The Hero and up to 3 friendly units that are within 12" at the
        /// beginning of the round ... This effect lasts until the end of the round." At the start of each round the Hero's
        /// unit and the three nearest friendly units within 12" get it (house rules C: the nearest, not picked).</summary>
        public static async Task RoundStart(IGameContext c)
        {
            if (!On(c) || !Skirmish(c.Settings.GameSystem)) return;
            foreach ((RelicPick p, IUnit unit, IModel hero) in AllCarried(c).ToList())
            {
                if (BannerRule(p.Item) is not { } rule || !unit.GetIsOnBattlefield()) continue;
                var others = LiveUnits(c).Where(u => !ReferenceEquals(u, unit) && Friendly(c, u.PlayerID, unit.PlayerID))
                    .Select(u => (Unit: u, Gap: Gap(hero, u))).Where(x => x.Gap <= 12.01f).OrderBy(x => x.Gap).Take(3).Select(x => x.Unit);
                var buffed = new[] { unit }.Concat(others).ToList();
                foreach (IUnit u in buffed)
                    u.Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.RoundEnd(),
                        Payload: new TokenPayload.RuleGrant(rule, ELifetime.ThisRound)));
                c.Log($"{p.Item}: {string.Join(", ", buffed.Select(u => u.Name))} for this round.");
            }
            await Task.CompletedTask;
        }

        /// <summary>Standard of Discipline: the unit passes Morale Tests on rolls of 2+.</summary>
        public static bool PassesOnTwo(IUnit unit) =>
            unit.Tokens.GetAllTokens(TokenType.RuleGrant).Any(t => t.Payload is TokenPayload.RuleGrant g && g.RuleName == RelicRuleCatalog.DisciplineName);

        // ---------------------------------------------------------------- weapons

        /// <summary>The attacking unit's relic weapon pick for this weapon, if the Hero carries this weapon.</summary>
        private static (RelicPick Pick, IModel Model)? WeaponPick(IGameContext c, IUnit attacker, IWeapon weapon) =>
            Carried(c, attacker).Where(x => x.Pick.Weapon != null && x.Pick.Weapon == weapon.Name
                && x.Model.Weapons.Any(w => w.Name == weapon.Name)).Select(x => ((RelicPick, IModel)?)x).FirstOrDefault();

        /// <summary>Only the Hero carries this weapon batch (its stats are the Hero's own).</summary>
        private static bool HeroOnlyBatch(IUnit attacker, IModel hero, IWeapon weapon) =>
            attacker is not UnitData data
            || HeroStatRules.LivingWeaponBatchOwners(data, weapon).All(m => m.ID.Equals(hero.ID));

        private static bool FightsHero(IUnit defender) => FDG.Missions.MissionCardGame.UnitHasHero(defender);

        /// <summary>Weapon of Bloodshed (+3 Attacks), and Kingslayer Weapon against Heroes and their unit.</summary>
        public static int AttackBonus(IGameContext c, IUnit attacker, IUnit defender, IWeapon weapon, bool spell)
        {
            if (spell || WeaponPick(c, attacker, weapon) is not { } x) return 0;
            if (x.Pick.Item == Bloodshed) return 3;
            if (x.Pick.Item == Kingslayer && FightsHero(defender)) return 3;
            return 0;
        }

        /// <summary>Obsidian Weapon (AP(4)), and Kingslayer Weapon against Heroes and their unit: the weapon's AP is raised to
        /// 4. Only for a batch the Hero fires alone (a weapon it shares with its unit stays as it is - house rules C).</summary>
        public static int ArmorPenetration(IGameContext c, IUnit attacker, IUnit defender, IWeapon weapon, int ap, bool spell)
        {
            if (spell || WeaponPick(c, attacker, weapon) is not { } x || !HeroOnlyBatch(attacker, x.Model, weapon)) return ap;
            if (x.Pick.Item == Obsidian || (x.Pick.Item == Kingslayer && FightsHero(defender))) return Math.Max(ap, 4);
            return ap;
        }

        /// <summary>Biting Weapon: "hits at Quality 2+".</summary>
        public static int? Quality(IGameContext c, IUnit attacker, IWeapon weapon, bool spell) =>
            !spell && WeaponPick(c, attacker, weapon) is { } x && x.Pick.Item == Biting && HeroOnlyBatch(attacker, x.Model, weapon) ? 2 : null;

        /// <summary>Fencer's Blade: the melee weapon "ignores Fatigue".</summary>
        public static bool IgnoresFatigue(IGameContext c, IUnit attacker, IWeapon weapon) =>
            WeaponPick(c, attacker, weapon) is { } x && x.Pick.Item == Fencer && HeroOnlyBatch(attacker, x.Model, weapon);

        // ---------------------------------------------------------------- being attacked

        /// <summary>Protective Gem (melee) / Opal Amulet (shooting): once per game the defender's player may give the attacking
        /// unit -1 to hit against the Hero's unit (the Skirmish books: against the Hero, so only while it stands alone).</summary>
        public static async Task<IReadOnlyList<GrantedModifier>> HitOffers(IGameContext c, IUnit attacker, IUnit defender, bool melee, bool spell)
        {
            if (spell) return Array.Empty<GrantedModifier>();
            string item = melee ? ProtectiveGem : OpalAmulet;
            if (Carrying(c, defender, item) is not { } x || x.Pick.Used) return Array.Empty<GrantedModifier>();
            if (Skirmish(c.Settings.GameSystem) && !Alone(defender, x.Model)) return Array.Empty<GrantedModifier>();
            if (!await Ask(c, defender.PlayerID, $"{x.Pick.Hero}: use the {item} (once per game) - {attacker.Name} gets -1 to hit {(melee ? "in this melee" : "with this shooting")}?",
                    true, item))
                return Array.Empty<GrantedModifier>();
            await Use(c, x.Pick, $"{x.Pick.Hero} uses the {item}: {attacker.Name} gets -1 to hit.");
            return new[] { new GrantedModifier(item, -1) };
        }

        /// <summary>The Hero's relic armour, while its own Defense is what the unit saves with (it stands alone): Helmet of
        /// Fortune +1, Silver-Steel Shield (shot at) and Glittering Chainmail (melee) Defense 2+, Dragon Shield AP -2.</summary>
        public static (int DefenseBonus, int? DefenseOverride, int ApReduction, string? Source) SaveChanges(IGameContext c, IUnit defender, bool melee)
        {
            foreach ((RelicPick p, IModel m) in Carried(c, defender))
            {
                if (!Alone(defender, m)) continue;
                switch (p.Item)
                {
                    case Fortune: return (1, null, 0, Fortune);
                    case SilverSteel when !melee: return (0, 2, 0, SilverSteel);
                    case Chainmail when melee: return (0, 2, 0, Chainmail);
                    case DragonShield: return (0, null, 2, DragonShield);
                }
            }
            return (0, null, 0, null);
        }

        /// <summary>Wounds about to land on the Hero's model: Armour of Destiny (5+ each), Talisman of Preservation (6+ each,
        /// and a wound back), Dawnstone (2+ each, from a spell), and Luckstone (once: up to D3 ignored). Returns what lands.</summary>
        public static async Task<float> HeroWounds(IGameContext c, IUnit defender, ModelData model, float wounds, bool spell)
        {
            if (wounds < 1f || Carried(c, defender).Where(x => x.Model.ID.Equals(model.ID)).Select(x => x.Pick).FirstOrDefault() is not { } p)
                return wounds;
            int whole = (int)Math.Floor(wounds + 0.0001f);
            int threshold = p.Item switch { Destiny => 5, Preservation => 6, Dawnstone when spell => 2, _ => 0 };
            if (threshold > 0)
            {
                int ignored = 0;
                for (int i = 0; i < whole; i++) if (D6(c) >= threshold) ignored++;
                if (ignored > 0)
                {
                    wounds -= ignored;
                    c.Log($"{p.Item}: {p.Hero} ignores {ignored} of {whole} wound{(whole == 1 ? "" : "s")} ({threshold}+).");
                    if (p.Item == Preservation && model.WoundsDealt > 0.999f)
                    {
                        model.DealWounds(-1f);
                        c.Log($"{p.Item}: {p.Hero} removes a wound.");
                    }
                    await c.Announce($"{p.Hero}'s {p.Item} turns aside {ignored} wound{(ignored == 1 ? "" : "s")}.", Gold, EBannerTier.Toast);
                }
                return wounds;
            }
            if (p.Item == Luckstone && !p.Used)
            {
                float left = model.TotalWounds - model.WoundsDealt;
                if (!await Ask(c, defender.PlayerID, $"{p.Hero} is taking {wounds:0} wound{(wounds == 1f ? "" : "s")}. Use the Luckstone (once per game) to ignore up to D3 of them?",
                        wounds >= left - 0.0001f, Luckstone))
                    return wounds;
                int d3 = D3(c);
                float ignored = Math.Min(d3, wounds);
                await Use(c, p, $"{p.Hero} uses the Luckstone: rolled {d3}, ignores {ignored:0}.");
                return wounds - ignored;
            }
            return wounds;
        }

        /// <summary>Scroll of Shielding: once per game, after a friendly unit within 12" of the Hero takes wounds from a spell,
        /// a die per wound, each 4+ ignored. Offered before the wounds land (the same moment Regeneration rolls).</summary>
        public static async Task<float> ShieldFromSpell(IGameContext c, IUnit defender, float wounds)
        {
            if (wounds < 1f) return wounds;
            foreach ((RelicPick p, IUnit unit, IModel hero) in AllCarried(c).ToList())
            {
                if (p.Item != Shielding || p.Used || !Friendly(c, unit.PlayerID, defender.PlayerID)) continue;
                if (!ReferenceEquals(unit, defender) && Gap(hero, defender) > 12.01f) continue;
                if (!await Ask(c, unit.PlayerID, $"{defender.Name} takes {wounds:0} wound{(wounds == 1f ? "" : "s")} from a spell. Use {p.Hero}'s Scroll of Shielding (once per game) - a die each, 4+ ignored?",
                        true, Shielding))
                    continue;
                int whole = (int)Math.Floor(wounds + 0.0001f), ignored = 0;
                for (int i = 0; i < whole; i++) if (D6(c) >= 4) ignored++;
                await Use(c, p, $"{p.Hero}'s Scroll of Shielding: {defender.Name} ignores {ignored} of {whole}.");
                return wounds - ignored;
            }
            return wounds;
        }

        /// <summary>Spellthieving Weapon: "When an enemy Caster takes wounds from the Hero, for each wound it loses 1 spell
        /// token, and gets 1 less spell token next round."</summary>
        public static void AfterWounds(IGameContext c, IUnit attacker, IUnit defender, IWeapon? weapon, float wounds, bool spell)
        {
            if (spell || weapon == null || wounds < 1f || WeaponPick(c, attacker, weapon) is not { } x || x.Pick.Item != Spellthieving) return;
            if (Friendly(c, attacker.PlayerID, defender.PlayerID) || !defender.Models.Any(m => FDG.Missions.MissionCardGame.IsCasterModel(defender, m))) return;
            int n = (int)Math.Floor(wounds + 0.0001f);
            int lost = Math.Min(n, defender.Tokens.GetTokenCount(TokenType.SpellTokens));
            if (lost > 0) defender.Tokens.RemoveTokens(TokenType.SpellTokens, lost);
            if (State(c.GameDataStore) is { } s)
            {
                s.Thieved[UnitKey(defender)] = s.Thieved.GetValueOrDefault(UnitKey(defender)) + n;
                Note(s, $"{x.Pick.Hero}'s Spellthieving Weapon: {defender.Name} loses {lost} spell token{(lost == 1 ? "" : "s")}, {n} fewer next round.");
                Save(c, s);
            }
            c.Log($"Spellthieving Weapon: {defender.Name} loses {lost} spell token(s) and gets {n} fewer next round.");
        }

        // ---------------------------------------------------------------- morale

        /// <summary>Terrifying Mask: once per game, enemy units in melee with the Hero (the Skirmish books: within 6") fail
        /// their Morale Tests. Offered when such a unit is about to test; the use covers that test (house rules C).</summary>
        public static async Task<bool> MaskFails(IGameContext c, IUnit testing)
        {
            float reach = Skirmish(c.Settings.GameSystem) ? 6f : 1f;
            foreach ((RelicPick p, IUnit unit, IModel hero) in AllCarried(c).ToList())
            {
                if (p.Item != TerrifyingMask || p.Used || Friendly(c, unit.PlayerID, testing.PlayerID)) continue;
                if (!unit.GetIsOnBattlefield() || Gap(hero, testing) > reach + 0.01f) continue;
                if (!await Ask(c, unit.PlayerID, $"{testing.Name} must take a Morale Test. Use {p.Hero}'s Terrifying Mask (once per game) so it fails automatically?",
                        true, TerrifyingMask))
                    continue;
                await Use(c, p, $"{p.Hero}'s Terrifying Mask: {testing.Name} fails its Morale Test.");
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- activation and rounds

        /// <summary>The Hero activates: Seed of Rebirth (remove up to D3 wounds), Tome of Magic (3 spell tokens), and the
        /// Displacement Device / Potion of Displacement (place it within 6" - for a Hero that joined a unit, the unit moves
        /// with it, house rules C).</summary>
        public static async Task OnActivation(IGameContext c, IUnit unit)
        {
            foreach ((RelicPick p, IModel hero) in Carried(c, unit).ToList())
            {
                if (p.Used) continue;
                if (p.Item == SeedOfRebirth && hero is ModelData md && md.WoundsDealt > 0.999f
                    && await Ask(c, unit.PlayerID, $"{p.Hero} activates with {md.WoundsDealt:0} wound{(md.WoundsDealt < 1.5f ? "" : "s")}. Use the Seed of Rebirth (once per game) to remove up to D3?", true, SeedOfRebirth))
                {
                    int d3 = D3(c);
                    int healed = (int)Math.Min(d3, Math.Floor(md.WoundsDealt + 0.0001f));
                    md.DealWounds(-healed);
                    await Use(c, p, $"{p.Hero} uses the Seed of Rebirth: rolled {d3}, removes {healed} wound{(healed == 1 ? "" : "s")}.");
                }
                else if (p.Item == Tome && unit.Models.Any(m => m.GetIsAlive() && FDG.Missions.MissionCardGame.IsCasterModel(unit, m))
                    && unit.Tokens.GetTokenCount(TokenType.SpellTokens) < GameWideConstants.MAX_SPELL_TOKENS
                    && await Ask(c, unit.PlayerID, $"{p.Hero} activates with {unit.Tokens.GetTokenCount(TokenType.SpellTokens)} spell tokens. Use the Tome of Magic (once per game) for 3 more?",
                        unit.Tokens.GetTokenCount(TokenType.SpellTokens) <= 1, Tome))
                {
                    int room = GameWideConstants.MAX_SPELL_TOKENS - unit.Tokens.GetTokenCount(TokenType.SpellTokens);
                    unit.Tokens.AddToken(new Token(TokenType.SpellTokens, Math.Min(3, room), new TokenClearTrigger.ManualOnly()));
                    await Use(c, p, $"{p.Hero} reads the Tome of Magic: +3 spell tokens.");
                }
                else if (p.Item == Displacement(c.Settings.GameSystem) && unit is UnitData data
                    && await Ask(c, unit.PlayerID, $"{p.Hero} activates. Use the {p.Item} (once per game) to place it anywhere within 6\"?", false, p.Item))
                {
                    var living = data.ModelBindings.Where(m => m.GetValue().GetIsAlive()).ToList();
                    var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, $"{p.Item}: place {p.Hero} within 6\" of where it stands",
                        new CircularZone(new Float2(hero.Position.x, hero.Position.z), 6f + hero.BaseRadiusInches), living,
                        minDistanceFromEnemiesInches: 1f, allowCancel: true, cancelHint: "Stay where it is.");
                    var result = await c.PlayerRequester
                        .RequestDecision<PlaceObjectsRequest<ModelData>, CancellableResult<List<PlacedObjectEntry<ModelData>>>>(request);
                    if (result is not Selected<List<PlacedObjectEntry<ModelData>>> selected) continue;
                    foreach (PlacedObjectEntry<ModelData> placement in selected.Value)
                    {
                        placement.Binding.GetValue().SetPosition(placement.Position);
                        if (placement.Facing.HasValue) placement.Binding.GetValue().SetFacing(placement.Facing.Value);
                    }
                    await Use(c, p, $"{p.Hero} uses the {p.Item}.");
                }
            }
        }

        /// <summary>After the round's spell tokens are handed out: Charmed Helmet (+1), and a Spellthieving Weapon's toll
        /// (fewer tokens this round). Before the 6-token cap.</summary>
        public static void AfterRoundStartGrant(IGameContext c, IUnit unit)
        {
            if (!On(c) || State(c.GameDataStore) is not { } s) return;
            if (Carried(c, unit).Any(x => x.Pick.Item == CharmedHelmet))
            {
                unit.Tokens.AddToken(new Token(TokenType.SpellTokens, 1, new TokenClearTrigger.ManualOnly()));
                c.Log($"Charmed Helmet: {unit.Name} gets +1 spell token.");
            }
            if (s.Thieved.Remove(UnitKey(unit), out int fewer))
            {
                int lose = Math.Min(fewer, unit.Tokens.GetTokenCount(TokenType.SpellTokens));
                if (lose > 0) unit.Tokens.RemoveTokens(TokenType.SpellTokens, lose);
                c.Log($"Spellthieving Weapon: {unit.Name} gets {lose} fewer spell token{(lose == 1 ? "" : "s")} this round.");
                Save(c, s);
            }
        }

        // ---------------------------------------------------------------- casting

        /// <summary>Power Wand: once per game, "the Hero casts its next spell on a roll of 2+". Offered as the Hero's unit
        /// rolls to cast.</summary>
        public static async Task<bool> PowerWandOffer(IGameContext c, IUnit caster, string spell)
        {
            if (Carrying(c, caster, PowerWand) is not { } x || x.Pick.Used) return false;
            if (!await Ask(c, caster.PlayerID, $"{x.Pick.Hero} casts {spell}. Use the Power Wand (once per game) - it casts on a roll of 2+?", true, PowerWand))
                return false;
            await Use(c, x.Pick, $"{x.Pick.Hero} uses the Power Wand: {spell} casts on a 2+.");
            return true;
        }

        /// <summary>After an enemy caster successfully casts: a Hero within 12" may use its Dispel Scroll (the spell does
        /// nothing) or Hex Scroll (the caster takes D3 wounds), once per game each. True when the spell was dispelled.</summary>
        public static async Task<bool> AfterEnemyCast(IGameContext c, IUnit caster, string spell)
        {
            bool dispelled = false;
            foreach (string item in new[] { DispelScroll, HexScroll })
                foreach ((RelicPick p, IUnit unit, IModel hero) in AllCarried(c).ToList())
                {
                    if (p.Item != item || p.Used || Friendly(c, unit.PlayerID, caster.PlayerID) || !unit.GetIsOnBattlefield()) continue;
                    if (!caster.GetIsAlive() || Gap(hero, caster) > 12.01f) continue;
                    if (item == DispelScroll && dispelled) continue;
                    string what = item == DispelScroll ? "so it doesn't take effect" : "so the caster takes D3 wounds";
                    if (!await Ask(c, unit.PlayerID, $"{caster.Name} cast {spell}. Use {p.Hero}'s {item} (once per game) {what}?", true, item)) continue;
                    if (item == DispelScroll)
                    {
                        dispelled = true;
                        await Use(c, p, $"{p.Hero}'s Dispel Scroll: {spell} does nothing.");
                    }
                    else
                    {
                        int d3 = D3(c);
                        await Use(c, p, $"{p.Hero}'s Hex Scroll: {caster.Name} takes {d3} wound{(d3 == 1 ? "" : "s")}.");
                        await CasualtyPresentation.ApplyUnitWounds(c, caster, d3);
                    }
                    break;
                }
            return dispelled;
        }

        /// <summary>Channeling Staff: a friendly caster may reach twice a spell's range while the army's staff is unused.</summary>
        public static float StaffRange(IGameContext c, IUnit caster, float range) =>
            StaffOf(c, caster) != null ? range : 0f;

        private static RelicPick? StaffOf(IGameContext c, IUnit caster) =>
            AllCarried(c).Where(x => x.Pick.Item == ChannelingStaff && !x.Pick.Used && Friendly(c, x.Unit.PlayerID, caster.PlayerID))
                .Select(x => x.Pick).FirstOrDefault();

        /// <summary>A cast reached past the spell's own range: the staff is spent.</summary>
        public static async Task StaffUsed(IGameContext c, IUnit caster, string spell)
        {
            if (StaffOf(c, caster) is { } p)
                await Use(c, p, $"{p.Hero}'s Channeling Staff: {caster.Name}'s {spell} reaches double its range.");
        }

        // ---------------------------------------------------------------- panel

        /// <summary>The Mission panel's lines: the method, then each player's Heroes and their items.</summary>
        public static List<(string Text, string Hover, bool Warn)> PanelLines(IReadableGameDataStore store, GameSettings settings)
        {
            var list = new List<(string, string, bool)>();
            if (State(store) is not { Method: > 0 } s) return list;
            EGameSystem system = settings.GameSystem;
            list.Add(($"{OptionName(system)} - {MethodName(s.Method)}", MethodText(s.Method), false));
            foreach (var owner in s.Picks.GroupBy(p => p.OwnerName))
                foreach (RelicPick p in owner)
                {
                    Def? d = Find(system, p.Item);
                    string extra = p.Weapon != null ? $" on {p.Weapon}" : p.Choice != null ? $" ({p.Choice})" : "";
                    list.Add(($"{owner.Key}: {p.Hero} - {p.Item}{extra}{(p.Used ? " (used)" : "")}", d?.Text ?? "", !p.Used && d?.Kind == EKind.OncePerGame));
                }
            foreach (string line in s.Recent) list.Add((line, "", false));
            return list;
        }
    }
}
