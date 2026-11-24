using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;

namespace FDG
{
    /// <summary>Advanced Casting, saved with the game: each caster's spell book, who suffered a backlash this round,
    /// and how many caster levels a Drained backlash has cost.</summary>
    [Serializable]
    public sealed class AdvancedCastingState
    {
        /// <summary>The round <see cref="Backlashed"/> belongs to.</summary>
        public int Round { get; set; }
        /// <summary>Caster unit id -> the spells in its book.</summary>
        public Dictionary<string, List<string>> Books { get; set; } = new();
        /// <summary>Caster unit id -> its name, for the Mission panel.</summary>
        public Dictionary<string, string> Names { get; set; } = new();
        /// <summary>Casters that suffered a backlash this round: "can't cast any more spells until the end of the round".</summary>
        public List<string> Backlashed { get; set; } = new();
        /// <summary>Caster unit id -> caster levels lost to Heavy Backlash's Drained.</summary>
        public Dictionary<string, int> Drained { get; set; } = new();
        /// <summary>Magic Pools: player id -> spell dice in that player's pool.</summary>
        public Dictionary<string, int> Pools { get; set; } = new();
        /// <summary>The last few backlashes, newest last, for the Mission panel.</summary>
        public List<string> Recent { get; set; } = new();
    }

    /// <summary>
    /// Advanced Casting - Currents of Power (GF p34, GFF p31) / Winds of Power (AoF p34, AoFS p31), Advanced Rulebooks
    /// 3.6.0. "The Caster(X) rule is heavily modified, with players agreeing to use any or all of the following systems:
    /// Spell Books, 2D6 Casting, Magic Pools, Heavy Backlash." Build 3.34: Spell Books, 2D6 Casting and Heavy Backlash.
    /// The four books print different wound counts (see <see cref="TwoD6BacklashWounds"/> and the Heavy Backlash tables),
    /// each followed as printed. Judgement calls: house rules C, "Advanced Casting readings".
    /// </summary>
    public static class AdvancedCasting
    {
        public const string SpellBooks = "Spell Books", TwoD6Casting = "2D6 Casting", MagicPools = "Magic Pools",
            HeavyBacklash = "Heavy Backlash";

        public static readonly IReadOnlyList<(string Name, EAdvancedRules Flag)> All = new (string, EAdvancedRules)[]
        {
            (SpellBooks, EAdvancedRules.SpellBooks),
            (TwoD6Casting, EAdvancedRules.TwoD6Casting),
            (MagicPools, EAdvancedRules.MagicPools),
            (HeavyBacklash, EAdvancedRules.HeavyBacklash),
        };

        public static bool Fantasy(EGameSystem s) => s is EGameSystem.AgeOfFantasy or EGameSystem.AoFSkirmish;
        /// <summary>GFF and AoFS print the bigger Heavy Backlash table (3D3 / 3 wounds, the closest unit within 6").</summary>
        public static bool Skirmish(EGameSystem s) => s is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;
        public static string PowerName(EGameSystem s) => Fantasy(s) ? "Winds of Power" : "Currents of Power";

        /// <summary>2D6 Casting's backlash: "losing 1 spell token and taking 3 wounds" (GF, GFF) / "1 wound" (AoF, AoFS).</summary>
        public static int TwoD6BacklashWounds(EGameSystem s) => s is EGameSystem.GrimdarkFuture or EGameSystem.GFFirefight ? 3 : 1;

        public const int TwoD6InBook = 7, TwoD6OffBook = 10;

        /// <summary>Magic Pools' backlash: "losing 1 spell die and taking 1 wound" (GF, AoF) / "3 wounds" (GFF, AoFS).</summary>
        public static int PoolBacklashWounds(EGameSystem s) => Skirmish(s) ? 3 : 1;

        public const int PoolInBook = 4, PoolOffBook = 6;

        // ---------------------------------------------------------------- lobby text

        public const string Intro = "Advanced Casting rules add more complexity to how casters work in the game, making them more important to the battle.";

        public static string BookText(string name, EGameSystem s) => name switch
        {
            SpellBooks => "Before the game begins, for each Caster(X) roll to get X+1 random spells from their spell list, and then pick X of those "
                + "spells to be part of that caster's spell book for the battle.\n2D6 Casting: Spells from a caster's spell books are cast on a 7+ as "
                + "usual, whilst spells that are not part of their book are only successfully cast on a 10+ instead.\nMagic Pools: Spells from a "
                + "caster's spell books are cast on a 4+ as usual, whilst spells that are not part of their book are only successfully cast on a 6+ instead.",
            TwoD6Casting => "This system can't be used at the same time as Magic Pools.\nCasting Spells: When casting spells, instead of rolling a single "
                + "die trying to score 4+ to resolve spell effects, players roll two dice trying to score 7+ to resolve spell effects. If the result on "
                + $"both dice is the same, then the caster suffers a backlash, losing 1 spell token and taking {TwoD6BacklashWounds(s)} wound"
                + $"{(TwoD6BacklashWounds(s) == 1 ? "" : "s")}, and after resolving any spell effects, the caster can't cast any more spells until the end "
                + "of the round. Note that players may spend spell tokens as usual to give the caster +1/-1 to the roll.\nModifiers: When using 2D6 "
                + "Casting, regardless of modifiers, results of 12 always succeed, and results of 2 always fail (instead of always succeeding on 6 and "
                + "always failing on 1).",
            MagicPools => "This system can't be used at the same time as 2D6 Casting.\nThe Magic Pool: At the beginning of each round, each "
                + "player gets 2*X spell dice in their magic pool, where X is the total number of spell tokens all their casters would have "
                + "generated that round. At the end of each round, each player must discard unspent spell dice down to 6*X dice, where X is the "
                + "total number of casters in their army.\nCasting Spells: When casting spells, instead of rolling a single die and spending spell "
                + "tokens to get +1 to the roll, players may take any number of spell dice from their magic pool, and roll them instead. If you "
                + "roll as many 4+ as the spell's casting value, then the spell is cast successfully. If any of the dice has an unmodified result "
                + "of 6, then the spell can't be blocked. If any of the dice has an unmodified result of 1, then the caster suffers a backlash, "
                + $"losing 1 spell die and taking {PoolBacklashWounds(s)} wound{(PoolBacklashWounds(s) == 1 ? "" : "s")}, and after resolving any "
                + "spell effects, the caster can't cast any more spells until the end of the round.\nBlocking Spells: When blocking spells, instead "
                + "of spending spell tokens to give -1 to the roll, players may take any number of spell dice from their magic pool, and roll them "
                + "at the same time as the caster instead. Then, sum together the total result of all casting dice, and the total result of all "
                + "blocking dice. If the total blocking result is equal or higher than the casting result, then the spell is blocked, and its "
                + "effects are not resolved. Note that if any of the casting dice has an unmodified result of 6, then the spell can't be blocked",
            HeavyBacklash => "When using 2D6 Casting or Magic Pools, when a caster suffers a backlash, roll 2D6 to see what happens, instead of losing "
                + $"1 spell token/die and taking {(Skirmish(s) ? "3 wounds" : "1 wound")}:\n" + string.Join("\n", Table(s).Select(r => $"{r.Range} {r.Name} - {r.Text}"))
                + "\nNote that casters that suffer a backlash can't cast any more spells until the end of the round.",
            _ => "",
        };

        public static string GameText(string name) => name switch
        {
            SpellBooks => "Before deployment each caster rolls its X+1 spells and you drop one (bots drop the dearest). With 2D6 Casting, spells "
                + "outside the book need 10+. On its own (no 2D6 Casting) the book prints no effect, so the books are made but change nothing. "
                + "Books show on the Mission panel.",
            TwoD6Casting => "Every cast rolls two dice: 7+ to cast (10+ for a spell outside a Spell Book), tokens still +1/-1, 12 always casts and 2 "
                + "always fails. Doubles are a backlash, win or lose: the token and wounds after the spell resolves, then no more casting this round.",
            MagicPools => "Casters stop getting spell tokens: each round you get two pool dice for every token they would have made, kept "
                + "up to 6 per caster at the end of the round. To cast you pick how many dice to roll (at least the spell's value); a spell "
                + "outside a Spell Book needs 6s instead of 4+. An enemy with a caster within 18\" may block with pool dice rolled at the same "
                + "time. Any 1 is a backlash (after the spell). Ticking it unticks 2D6 Casting. Bots never block.",
            HeavyBacklash => "With 2D6 Casting or Magic Pools. A backlash rolls on the table instead. \"Caster level\" lost to Drained means one fewer spell "
                + "token every round.",
            _ => "",
        };

        public static bool BooksOn(IGameContext c) => AdvancedRules.IsOn(c, EAdvancedRules.SpellBooks);
        public static bool TwoD6On(IGameContext c) => AdvancedRules.IsOn(c, EAdvancedRules.TwoD6Casting);
        /// <summary>Magic Pools, unless 2D6 Casting is on too ("can't be used at the same time" - 2D6 wins).</summary>
        public static bool PoolsOn(IGameContext c) => AdvancedRules.IsOn(c, EAdvancedRules.MagicPools) && !TwoD6On(c);

        /// <summary>The lobby's tick: 2D6 Casting and Magic Pools can't both be on, so ticking one clears the other.</summary>
        public static EAdvancedRules Tick(EAdvancedRules now, EAdvancedRules flag, bool on)
        {
            if (!on) return now & ~flag;
            if (flag == EAdvancedRules.TwoD6Casting) now &= ~EAdvancedRules.MagicPools;
            if (flag == EAdvancedRules.MagicPools) now &= ~EAdvancedRules.TwoD6Casting;
            return now | flag;
        }
        public static bool HeavyOn(IGameContext c) => AdvancedRules.IsOn(c, EAdvancedRules.HeavyBacklash);

        // ---------------------------------------------------------------- Heavy Backlash table

        public sealed record BacklashRow(int Low, int High, string Name, string Text)
        {
            public string Range => Low == High ? $"{Low}" : $"{Low}-{High}";
        }

        public static IReadOnlyList<BacklashRow> Table(EGameSystem s) => Skirmish(s)
            ? new[]
            {
                new BacklashRow(2, 4, "Cascade", "The caster loses 1 spell token/die, takes 3D3 wounds, and the closest unit within 6\" takes 2D3 wounds."),
                new BacklashRow(5, 6, "Detonation", "The caster loses 1 spell token/die and takes 3D3 wounds."),
                new BacklashRow(7, 7, "Boom!", "The caster loses 1 spell token/die and takes 3 wounds."),
                new BacklashRow(8, 9, "Feedback", "The caster loses D3 spell tokens/dice and takes 3 wounds."),
                new BacklashRow(10, 12, "Drained", "The caster loses D3 spell tokens/dice, takes 3 wounds, and lowers its caster level by 1."),
            }
            : new[]
            {
                new BacklashRow(2, 4, "Cascade", "The caster loses 1 spell token/die, takes D3 wounds, and the caster's unit takes 2D3 wounds, or if the caster is not part of a unit the closest unit within 6\" takes 2D3 wounds."),
                new BacklashRow(5, 6, "Detonation", "The caster loses 1 spell token/die and takes D3 wounds."),
                new BacklashRow(7, 7, "Boom!", "The caster loses 1 spell token/die and takes 1 wound."),
                new BacklashRow(8, 9, "Feedback", "The caster loses D3 spell tokens/dice and takes 1 wound."),
                new BacklashRow(10, 12, "Drained", "The caster loses D3 spell tokens/dice, takes 1 wound, and lowers its caster level by 1."),
            };

        public static BacklashRow Row(EGameSystem s, int total) => Table(s).First(r => total >= r.Low && total <= r.High);

        // ---------------------------------------------------------------- state

        private static readonly ConditionalWeakTable<IReadableGameDataStore, AdvancedCastingState> Pending = new();

        public static AdvancedCastingState? State(IReadableGameDataStore store) =>
            GameProgressUtilities.TryGetProgress(store)?.AdvancedCasting
            ?? (Pending.TryGetValue(store, out AdvancedCastingState? s) ? s : null);

        internal static void Save(IGameContext c, AdvancedCastingState s)
        {
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(c.GameDataStore);
            if (progress == null)
            {
                Pending.Remove(c.GameDataStore);
                Pending.Add(c.GameDataStore, s);
                return;
            }
            progress.AdvancedCasting = s;
            GameProgressUtilities.WriteProgress(c.GameDataStore, progress);
        }

        public static string Id(IUnit u) => u.ID.ID.ToString();

        private static int Round(IGameContext c) => GameProgressUtilities.TryGetProgress(c.GameDataStore)?.RoundCount ?? 0;

        private static int D3(IGameContext c) => (c.DiceRoller.RollDecisiveFace() + 1) / 2;

        // ---------------------------------------------------------------- caster level

        /// <summary>X in the unit's Caster(X) - its own rule, or a joined hero's model's. 0 when it has none.</summary>
        public static int CasterLevel(IUnit unit)
        {
            int own = RatingOf(unit.RuleDefinitions);
            if (own > 0) return own;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                int r = RatingOf(model.RuleDefinitions);
                if (r > 0) return r;
            }
            return 0;
        }

        private static int RatingOf(IEnumerable<ResolvedRule> rules)
        {
            foreach (ResolvedRule rule in rules)
            {
                bool isCaster = string.Equals(rule.Definition.Name, "Caster", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rule.RequestedName, "Caster", StringComparison.OrdinalIgnoreCase);
                if (isCaster && rule.Arguments.Count > 0 && rule.Arguments[0] is RuleArgument.Int rating) return rating.Value;
            }
            return 0;
        }

        /// <summary>Caster levels Drained has taken from this unit.</summary>
        public static int DrainedLevels(IReadableGameDataStore store, IUnit unit) =>
            State(store)?.Drained.GetValueOrDefault(Id(unit)) ?? 0;

        /// <summary>After the round-start grant: a Drained caster ("lowers its caster level by 1") gets one fewer token per level lost.</summary>
        public static void AfterRoundStartGrant(IGameContext c, IUnit unit, int gained)
        {
            int drained = DrainedLevels(c.GameDataStore, unit);
            if (drained > 0 && gained > 0)
            {
                int take = Math.Min(drained, gained);
                unit.Tokens.RemoveTokens(TokenType.SpellTokens, take);
                gained -= take;
                c.Log($"{unit.Name} is Drained: {take} fewer spell token{(take == 1 ? "" : "s")} this round.");
            }
            // Magic Pools: "2*X spell dice, where X is the total number of spell tokens all their casters would have
            // generated that round" - the tokens become dice in the player's pool.
            if (PoolsOn(c) && gained > 0)
            {
                unit.Tokens.RemoveTokens(TokenType.SpellTokens, gained);
                AddPool(c, unit.PlayerID, 2 * gained);
                c.Log($"Magic Pools: {unit.Name} adds {2 * gained} spell dice to its player's pool.");
            }
        }

        // ---------------------------------------------------------------- Magic Pools

        public static int PoolDice(IReadableGameDataStore store, PlayerID player) =>
            State(store)?.Pools.GetValueOrDefault(player.ID.ToString()) ?? 0;

        public static void AddPool(IGameContext c, PlayerID player, int dice)
        {
            AdvancedCastingState s = State(c.GameDataStore) ?? new AdvancedCastingState();
            string key = player.ID.ToString();
            s.Pools[key] = Math.Max(0, s.Pools.GetValueOrDefault(key) + dice);
            if (c.TableState.Players.Objects.FirstOrDefault(p => p.IsFilled && p.PlayerID == player)?.Name is { } name)
                s.Names["player:" + key] = name;
            Save(c, s);
        }

        /// <summary>Takes up to <paramref name="dice"/> from the pool; returns how many were taken.</summary>
        public static int SpendPool(IGameContext c, PlayerID player, int dice)
        {
            int have = PoolDice(c.GameDataStore, player);
            int take = Math.Clamp(dice, 0, have);
            if (take > 0) AddPool(c, player, -take);
            return take;
        }

        /// <summary>What each casting die needs: 4+, or 6+ for a spell outside the caster's Spell Book.</summary>
        public static int PoolDieNeeded(IGameContext c, IUnit caster, string spell) => InBook(c, caster, spell) ? PoolInBook : PoolOffBook;

        /// <summary>End of round: "discard unspent spell dice down to 6*X dice, where X is the total number of casters in their army".</summary>
        public static void RoundEnd(IGameContext c)
        {
            if (!PoolsOn(c)) return;
            AdvancedCastingState? s = State(c.GameDataStore);
            if (s == null) return;
            bool changed = false;
            foreach (ArmyData army in c.GameDataStore.GetAllValues<ArmyData>())
            {
                string key = army.PlayerID.ID.ToString();
                int have = s.Pools.GetValueOrDefault(key);
                int casters = army.UnitBindings.Select(b => b.GetValue()).Count(u => u.GetIsAlive() && CasterLevel(u) > 0);
                int cap = 6 * casters;
                if (have <= cap) continue;
                s.Pools[key] = cap;
                changed = true;
                c.Log($"Magic Pools: {have - cap} unspent spell dice discarded (kept {cap}).");
            }
            if (changed) Save(c, s);
        }

        // ---------------------------------------------------------------- Spell Books

        /// <summary>Before deployment: each caster rolls X+1 different spells from its army's list, and its owner picks X
        /// (asked which one to leave out). Bots leave out the dearest.</summary>
        public static async Task Settle(IGameContext c)
        {
            if (!BooksOn(c)) return;
            AdvancedCastingState s = State(c.GameDataStore) ?? new AdvancedCastingState();
            bool changed = false;
            foreach (ArmyData army in c.GameDataStore.GetAllValues<ArmyData>().ToList())
            {
                List<RuntimeSpell> spells = army.Spells.Where(sp => sp.Threshold > 0).ToList();
                if (spells.Count == 0) continue;
                foreach (DataBinding<UnitData> binding in army.UnitBindings.ToList())
                {
                    UnitData unit = binding.GetValue();
                    if (!unit.GetIsAlive() || s.Books.ContainsKey(Id(unit))) continue;
                    int x = CasterLevel(unit);
                    if (x <= 0) continue;
                    List<RuntimeSpell> rolled = RollSpells(c, spells, x + 1);
                    List<RuntimeSpell> book = rolled;
                    if (rolled.Count > x)
                    {
                        RuntimeSpell bot = rolled.OrderByDescending(sp => sp.Threshold).ThenBy(sp => rolled.IndexOf(sp)).First();
                        List<string> labels = rolled.Select(sp => $"Leave out {sp.Name} ({sp.Threshold})").ToList();
                        string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(
                            unit.PlayerID,
                            $"Spell Books: {unit.Name} (Caster({x})) rolled {string.Join(", ", rolled.Select(sp => sp.Name))}. Pick {x} for its book - which one is left out?",
                            labels, new List<StringSelectionRequest.InvalidOption>(), displayName: "Spell Books")
                        { Recommended = labels[rolled.IndexOf(bot)] });
                        int drop = labels.IndexOf(answer);
                        if (drop < 0) drop = rolled.IndexOf(bot);
                        book = rolled.Where((_, i) => i != drop).ToList();
                    }
                    s.Books[Id(unit)] = book.Select(sp => sp.Name).ToList();
                    s.Names[Id(unit)] = unit.Name;
                    changed = true;
                    c.Log($"Spell Books: {unit.Name}'s book is {string.Join(", ", book.Select(sp => sp.Name))}.");
                }
            }
            if (changed) Save(c, s);
        }

        /// <summary>"Roll to get X+1 random spells": distinct spells, a die per spell number, re-rolling repeats.</summary>
        internal static List<RuntimeSpell> RollSpells(IGameContext c, IReadOnlyList<RuntimeSpell> spells, int count)
        {
            if (count >= spells.Count) return spells.ToList();
            var picked = new List<int>();
            int guard = 0;
            while (picked.Count < count && guard++ < 500)
            {
                int n = c.DiceRoller.RollDecisiveFace(spells.Count) - 1;
                if (n >= 0 && n < spells.Count && !picked.Contains(n)) picked.Add(n);
            }
            for (int i = 0; picked.Count < count && i < spells.Count; i++)
                if (!picked.Contains(i)) picked.Add(i);
            return picked.OrderBy(i => i).Select(i => spells[i]).ToList();
        }

        /// <summary>Whether a spell is in this caster's book. True when Spell Books is off, or the caster has no book
        /// (a caster that appeared after the game began).</summary>
        public static bool InBook(IGameContext c, IUnit caster, string spell)
        {
            if (!BooksOn(c)) return true;
            List<string>? book = State(c.GameDataStore)?.Books.GetValueOrDefault(Id(caster));
            return book == null || book.Contains(spell);
        }

        // ---------------------------------------------------------------- 2D6 Casting

        /// <summary>What the cast needs before modifiers: 4+ on one die normally, 7+ (10+ off-book) on two with 2D6 Casting.</summary>
        public static int BaseNeeded(IGameContext c, IUnit caster, string spell, int normal) =>
            !TwoD6On(c) ? normal : InBook(c, caster, spell) ? TwoD6InBook : TwoD6OffBook;

        /// <summary>The 2D6 total needed after modifiers: "results of 12 always succeed, and results of 2 always fail".</summary>
        public static int ClampTwoD6(int needed) => Math.Clamp(needed, 3, 12);

        // ---------------------------------------------------------------- backlash

        /// <summary>Casters that suffered a backlash this round can't cast again until it ends.</summary>
        public static bool IsBacklashed(IGameContext c, IUnit unit)
        {
            AdvancedCastingState? s = State(c.GameDataStore);
            return s != null && s.Round == Round(c) && s.Backlashed.Contains(Id(unit));
        }

        public const string BacklashedReason = "Suffered a backlash - no more casting this round.";

        private static readonly TextColor BacklashColor = new TextColor(220, 90, 220, 255);

        /// <summary>A backlash on rolled doubles, applied after the spell resolves: the plain one ("losing 1 spell token
        /// and taking N wounds"), or with Heavy Backlash a 2D6 roll on the table. Either way the caster is done casting
        /// for the round.</summary>
        public static async Task Backlash(IGameContext c, IUnit caster)
        {
            AdvancedCastingState s = State(c.GameDataStore) ?? new AdvancedCastingState();
            int round = Round(c);
            if (s.Round != round) { s.Round = round; s.Backlashed.Clear(); }
            if (!s.Backlashed.Contains(Id(caster))) s.Backlashed.Add(Id(caster));
            s.Names[Id(caster)] = caster.Name;

            EGameSystem system = c.Settings.GameSystem;
            string line;
            int tokensLost, wounds;
            int splash = 0;
            bool pool = PoolsOn(c);
            string what = pool ? "spell die" : "spell token";
            if (!HeavyOn(c))
            {
                tokensLost = 1;
                wounds = pool ? PoolBacklashWounds(system) : TwoD6BacklashWounds(system);
                line = $"Backlash! {caster.Name} loses a {what} and takes {wounds} wound{(wounds == 1 ? "" : "s")}";
            }
            else
            {
                int a = c.DiceRoller.RollDecisiveFace(), b = c.DiceRoller.RollDecisiveFace();
                BacklashRow row = Row(system, a + b);
                bool big = Skirmish(system);
                (tokensLost, wounds, splash) = row.Name switch
                {
                    "Cascade" => (1, big ? D3(c) + D3(c) + D3(c) : D3(c), D3(c) + D3(c)),
                    "Detonation" => (1, big ? D3(c) + D3(c) + D3(c) : D3(c), 0),
                    "Boom!" => (1, big ? 3 : 1, 0),
                    "Feedback" => (D3(c), big ? 3 : 1, 0),
                    _ => (D3(c), big ? 3 : 1, 0),   // Drained
                };
                line = $"Heavy Backlash ({a}+{b}={a + b}, {row.Name}): {caster.Name} loses {tokensLost} {(pool ? "spell " + (tokensLost == 1 ? "die" : "dice") : "spell token" + (tokensLost == 1 ? "" : "s"))} "
                    + $"and takes {wounds} wound{(wounds == 1 ? "" : "s")}";
                if (row.Name == "Drained")
                {
                    s.Drained[Id(caster)] = s.Drained.GetValueOrDefault(Id(caster)) + 1;
                    line += ", and its caster level drops by 1";
                }
            }

            if (pool)
            {
                string key = caster.PlayerID.ID.ToString();
                s.Pools[key] = Math.Max(0, s.Pools.GetValueOrDefault(key) - tokensLost);
            }
            else
            {
                int held = caster.Tokens.GetTokenCount(TokenType.SpellTokens);
                if (held > 0) caster.Tokens.RemoveTokens(TokenType.SpellTokens, Math.Min(held, tokensLost));
            }

            // Cascade's 2D3: GF / AoF - "the caster's unit takes 2D3 wounds, or if the caster is not part of a unit the
            // closest unit within 6""; GFF / AoFS - always the closest unit within 6".
            IUnit? splashed = null;
            if (splash > 0)
            {
                bool partOfUnit = !Skirmish(system) && caster.Models.Count(m => m.GetIsAlive()) > 1;
                splashed = partOfUnit ? caster : ClosestUnitWithin(c, caster, 6f);
                line += splashed == null ? $"; no unit within 6\" for the other {splash}"
                    : splashed == caster ? $", and its unit takes {splash} more"
                    : $", and {splashed.Name} (closest within 6\") takes {splash}";
            }

            s.Recent.Add(line);
            while (s.Recent.Count > 4) s.Recent.RemoveAt(0);
            Save(c, s);

            c.Log(line + ". No more casting for it this round.");
            await c.Announce(line + ".", BacklashColor, EBannerTier.Notice);
            if (wounds > 0 && caster.GetIsAlive()) await CasualtyPresentation.ApplyUnitWounds(c, caster, wounds);
            if (splashed != null && splashed.GetIsAlive()) await CasualtyPresentation.ApplyUnitWounds(c, splashed, splash);
        }

        private static IUnit? ClosestUnitWithin(IGameContext c, IUnit from, float inches)
        {
            IUnit? best = null;
            float bestDistance = float.MaxValue;
            foreach (IUnit u in c.TableState.Units.Objects)
            {
                if (u == from || u.ID.Equals(from.ID) || !u.GetIsAlive() || !u.GetIsOnBattlefield()) continue;
                float d = UnitCompareUtilities.MinDistanceBetweenUnits(from, u, out _, out _, includeVertical: true);
                if (d <= inches && d < bestDistance) { best = u; bestDistance = d; }
            }
            return best;
        }

        // ---------------------------------------------------------------- Mission panel

        /// <summary>The panel's lines: the systems on, each spell book, who is locked out this round, Drained casters, the
        /// last backlashes.</summary>
        public static List<(string Text, string Hover, bool Warn)> PanelLines(IReadableGameDataStore store, GameSettings settings, int round)
        {
            var lines = new List<(string, string, bool)>();
            bool books = AdvancedRules.IsOn(settings, EAdvancedRules.SpellBooks), twoD6 = AdvancedRules.IsOn(settings, EAdvancedRules.TwoD6Casting),
                heavy = AdvancedRules.IsOn(settings, EAdvancedRules.HeavyBacklash);
            bool pools = !twoD6 && AdvancedRules.IsOn(settings, EAdvancedRules.MagicPools);
            if (!books && !twoD6 && !heavy && !pools) return lines;
            EGameSystem system = settings.GameSystem;
            var on = new List<string>();
            if (books) on.Add(SpellBooks);
            if (twoD6) on.Add(TwoD6Casting);
            if (pools) on.Add(MagicPools);
            if (heavy) on.Add(HeavyBacklash);
            string hover = string.Join("\n\n", on.Select(n => $"{n}: {GameText(n)}"));
            lines.Add(($"{PowerName(system)}: {string.Join(", ", on)}", hover, false));
            if (twoD6) lines.Add((books ? "Casting: 7+ on 2D6 (10+ outside the spell book)" : "Casting: 7+ on 2D6", BookText(TwoD6Casting, system), false));
            if (pools) lines.Add((books ? "Casting: a 4+ per die from the pool (6+ outside the spell book)" : "Casting: a 4+ per die from the pool",
                BookText(MagicPools, system), false));
            AdvancedCastingState? s = State(store);
            if (s == null) return lines;
            if (pools)
                foreach (ArmyData army in store.GetAllValues<ArmyData>())
                {
                    string name = s.Names.GetValueOrDefault("player:" + army.PlayerID.ID, "Player");
                    lines.Add(($"{name}: {s.Pools.GetValueOrDefault(army.PlayerID.ID.ToString())} spell dice", BookText(MagicPools, system), false));
                }
            foreach ((string id, List<string> book) in s.Books)
                lines.Add(($"{s.Names.GetValueOrDefault(id, "Caster")}: {string.Join(", ", book)}", BookText(SpellBooks, system), false));
            if (s.Round == round)
                foreach (string id in s.Backlashed)
                    lines.Add(($"{s.Names.GetValueOrDefault(id, "Caster")}: backlash - no more casting this round", BacklashedReason, true));
            foreach ((string id, int levels) in s.Drained.Where(d => d.Value > 0))
                lines.Add(($"{s.Names.GetValueOrDefault(id, "Caster")}: Drained, caster level -{levels}", "One fewer spell token every round per level lost.", true));
            if (heavy)
                foreach (string recent in s.Recent)
                    lines.Add((recent, BookText(HeavyBacklash, system), true));
            else
                foreach (string recent in s.Recent)
                    lines.Add((recent, BookText(pools ? MagicPools : TwoD6Casting, system), true));
            return lines;
        }
    }
}
