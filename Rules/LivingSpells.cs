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
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;

namespace FDG
{
    /// <summary>One living spell on the table.</summary>
    [Serializable]
    public sealed class LivingSpellPiece
    {
        /// <summary>The owning player's id.</summary>
        public string Owner { get; set; } = "";
        public string OwnerName { get; set; } = "";
        public string Name { get; set; } = "";
        public float X { get; set; }
        public float Z { get; set; }
        /// <summary>Second copy (Linked Jumpgates / Voidgates, Twin Shock-Spheres).</summary>
        public float? X2 { get; set; }
        public float? Z2 { get; set; }
        /// <summary>The way its front faces (a unit vector) - spells that move straight, and the long ones' orientation.</summary>
        public float Fx { get; set; }
        public float Fz { get; set; } = 1f;
        /// <summary>The caster unit's id (platforms).</summary>
        public string Caster { get; set; } = "";
    }

    /// <summary>Living Spells, saved with the game: each army's three spells and what is on the table.</summary>
    [Serializable]
    public sealed class LivingSpellsState
    {
        /// <summary>Player id -> the army's living spells (one per category).</summary>
        public Dictionary<string, List<string>> Picks { get; set; } = new();
        public Dictionary<string, string> Names { get; set; } = new();
        public List<LivingSpellPiece> OnTable { get; set; } = new();
        public List<string> Recent { get; set; } = new();
    }

    /// <summary>
    /// Advanced Casting - Living Spells (GF p35, GFF p32, AoF p35, AoFS p32), the same in all four books apart from
    /// Linked Jumpgates (GF, GFF) / Linked Voidgates (AoF, AoFS). "Living spells are cast like regular spells, and if
    /// successful, they are placed anywhere within 12" of the caster. Each army may only have one living spell in play at
    /// a time ... Enemy casters that are within 12" of a living spell at any point of their activation before attacking,
    /// may try to unbind them." They join the caster's spell picker as "Living Spell: X" and "Unbind: X". Build 3.39: the
    /// framework and the six Primitive Spells. House rules C, "Living Spells readings".
    /// </summary>
    public static class LivingSpells
    {
        public enum ESize { Small, Large, Long }
        public enum EMove { None, Corner, Straight, CasterMoves, Platform }

        public sealed record Def(string Category, int Number, string Name, int Value, ESize Size, EMove Move, string Text);

        public const string Primitive = "Primitive Spells", Intricate = "Intricate Spells", Forbidden = "Forbidden Spells";
        public static readonly string[] Categories = { Primitive, Intricate, Forbidden };

        public const string BurningPyre = "Burning Pyre", SuffocatingCloud = "Suffocating Cloud", CogsOfTime = "Cogs of Time",
            DeathlyPendulum = "Deathly Pendulum", ShardOfMist = "Shard of Mist";

        public static string Gates(EGameSystem s) => s is EGameSystem.AgeOfFantasy or EGameSystem.AoFSkirmish ? "Linked Voidgates" : "Linked Jumpgates";

        public static IReadOnlyList<Def> Table(EGameSystem s) => new[]
        {
            new Def(Primitive, 1, BurningPyre, 1, ESize.Small, EMove.None, "At the end of each round, all units within 6\" take D3 wounds."),
            new Def(Primitive, 2, SuffocatingCloud, 1, ESize.Long, EMove.Straight, "This spell needs to have a clearly marked front. This spell moves 12\" straight at the end of each round. If it gets to a table edge, it is removed. Units the spell moves through must take a Dangerous Terrain Test."),
            new Def(Primitive, 3, CogsOfTime, 1, ESize.Small, EMove.Corner, "This spell moves 12\" towards a random table corner at the end of each round. If it gets to a table edge, it is removed. Units within 6\" get +1 to hit rolls when shooting."),
            new Def(Primitive, 4, DeathlyPendulum, 1, ESize.Small, EMove.Corner, "This spell moves 12\" towards a random table corner at the end of each round, and then units within 6\" of it take 2D3 hits. If it gets to a table edge, it is removed."),
            new Def(Primitive, 5, ShardOfMist, 1, ESize.Small, EMove.None, "Units within 6\" get -1 to hit rolls when shooting."),
            new Def(Primitive, 6, Gates(s), 1, ESize.Small, EMove.None, "Place one additional copy of this spell within 24\" of it. When a unit ends its activation within 3\" of one of the copies of this spell, it may be placed anywhere within 3\" of another copy of this spell."),
            new Def(Intricate, 1, "Hungering Jaws", 2, ESize.Large, EMove.Straight, "This spell needs to have a clearly marked front. This spell moves 12\" straight at the end of each round. If it gets to a table edge, it is removed. Units it moves through take 2D3 hits."),
            new Def(Intricate, 2, "Magic Shackles", 2, ESize.Small, EMove.Corner, "This spell moves 12\" towards a random table corner at the end of each round. If it gets to a table edge, it is removed. Units within 6\" count as being in Difficult terrain."),
            new Def(Intricate, 3, "Healing Swarm", 2, ESize.Small, EMove.CasterMoves, "At the end of each round, one random caster within 6\" may move this spell by up to 12\" in any direction, and then units within 6\" of it restore D3 models/wounds."),
            new Def(Intricate, 4, "Soul-Drain Platform", 2, ESize.Large, EMove.Platform, "The caster must be placed on top of this spell. As long as the caster is on top of this spell, the spell moves with it. When activated, one random unit (including the caster) within 6\" takes D3 wounds."),
            new Def(Intricate, 5, "Screaming Horror", 2, ESize.Small, EMove.Corner, "This spell moves 12\" towards a random table corner at the end of each round. If it gets to a table edge, it is removed. Units within 6\" get -1 to Morale Test rolls."),
            new Def(Intricate, 6, "Arcane Palisade", 2, ESize.Long, EMove.None, "This spell counts as Impassable and Sight Blocking terrain."),
            new Def(Forbidden, 1, "Killing Sun", 3, ESize.Large, EMove.CasterMoves, "At the end of each round, one random caster within 6\" may move this spell by up to 12\" in any direction. Units it moves through take 2D3 hits."),
            new Def(Forbidden, 2, "Divine Maelstrom", 3, ESize.Small, EMove.None, "Units within 6\" count as being in Cover terrain."),
            new Def(Forbidden, 3, "Sword Swarm", 3, ESize.Small, EMove.None, "Units within 6\" get +1 to hit rolls in melee."),
            new Def(Forbidden, 4, "Twin Shock-Spheres", 3, ESize.Small, EMove.None, "Place one additional copy of this spell within 12\" of it, and draw a straight line between all copies of it. When a unit moves through the line, it takes 2D3 hits."),
            new Def(Forbidden, 5, "Spirit Seeker", 3, ESize.Small, EMove.Straight, "This spell needs to have a clearly marked front. This spell moves 12\" straight at the end of each round. If it gets to a table edge, it is removed. Units within 6\" get -1 to hit rolls in melee."),
            new Def(Forbidden, 6, "Vortex Platform", 3, ESize.Large, EMove.Platform, "The caster must be placed on top of this spell. As long as the caster is on top of this spell, the spell moves with it, and the caster gets +1 to casting rolls."),
        };

        /// <summary>The spells in the game so far (build 3.39: the Primitive Spells).</summary>
        public static bool Built(Def d) => d.Category == Primitive;

        public static Def? Find(EGameSystem s, string name) => Table(s).FirstOrDefault(d => d.Name == name);

        // ---------------------------------------------------------------- sizes

        /// <summary>Small = 32mm round, Large = 50mm round (the smaller of each pair the book allows), Long = 175x32mm.</summary>
        public const float SmallRadius = 32f / 25.4f / 2f, LargeRadius = 50f / 25.4f / 2f, LongLength = 175f / 25.4f, LongWidth = 32f / 25.4f;

        public static float Radius(ESize size) => size switch { ESize.Large => LargeRadius, ESize.Long => LongWidth / 2f, _ => SmallRadius };

        // ---------------------------------------------------------------- lobby text

        public const string Option = "Living Spells";

        public static string BookText(EGameSystem s) =>
            "Living Spells are physical representations of spells on the battlefield, which add a new dimension to casting. Small Spells "
            + "= 32mm/40mm Round Base, Large Spells = 50mm/60mm Round Base, Long Spells = 175x32mm Oval/Rectangle Base.\nLiving spells are "
            + "cast like regular spells, and if successful, they are placed anywhere within 12\" of the caster. Each army may only have one "
            + "living spell in play at a time, and if a new one is cast, the previous one is removed from play.\nEnemy casters that are "
            + "within 12\" of a living spell at any point of their activation before attacking, may try to unbind them. Unbinding spells "
            + "works just like casting them, but if successful, the spell is removed from the table.\nBefore the game begins, each army gets "
            + "one spell from each of these categories (all players either pick freely or roll to get a random spell from each category): "
            + "Primitive Spells, Intricate Spells, Forbidden Spells.\n"
            + string.Join("\n", Table(s).Select(d => $"{d.Category} {d.Number}. {d.Name} ({d.Value}) - {d.Size} Spell: {d.Text}"));

        public const string GameText = "Before deployment each army picks (or rolls) one spell per category - only the Primitive Spells are "
            + "in the game so far. A caster casts one from its spell list as \"Living Spell: X\" and places it within 12\"; casting another "
            + "removes the old one. An enemy caster within 12\" can pick \"Unbind: X\" the same way. Spells that move do so at the end of "
            + "the round. They show on the table as plain pieces and on the Mission panel.";

        public static bool On(IGameContext c) => AdvancedRules.IsOn(c, EAdvancedRules.LivingSpells);

        // ---------------------------------------------------------------- state

        private static readonly ConditionalWeakTable<IReadableGameDataStore, LivingSpellsState> Pending = new();

        public static LivingSpellsState? State(IReadableGameDataStore store) =>
            GameProgressUtilities.TryGetProgress(store)?.LivingSpells
            ?? (Pending.TryGetValue(store, out LivingSpellsState? s) ? s : null);

        internal static void Save(IGameContext c, LivingSpellsState s)
        {
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(c.GameDataStore);
            if (progress == null)
            {
                Pending.Remove(c.GameDataStore);
                Pending.Add(c.GameDataStore, s);
                return;
            }
            progress.LivingSpells = s;
            GameProgressUtilities.WriteProgress(c.GameDataStore, progress);
        }

        private static string Key(PlayerID p) => p.ID.ToString();

        private static bool Friendly(IGameContext c, PlayerID a, string owner) =>
            Guid.TryParse(owner, out Guid g) && (a.ID == g || ITeamExtensions.AreAllied(c.TableState.Teams.Objects, a, new PlayerID(g)));

        private static readonly TextColor Violet = new TextColor(190, 120, 255, 255);

        // ---------------------------------------------------------------- before the game

        public const string Roll = "Roll for it";

        /// <summary>"Before the game begins, each army gets one spell from each of these categories (all players either pick
        /// freely or roll)." Asked of every player, a category at a time; bots roll.</summary>
        public static async Task Settle(IGameContext c)
        {
            if (!On(c)) return;
            LivingSpellsState s = State(c.GameDataStore) ?? new LivingSpellsState();
            if (s.Picks.Count > 0) return;
            EGameSystem system = c.Settings.GameSystem;
            foreach (IPlayerSlotInfo seat in c.TableState.Players.Objects.Where(p => p.IsFilled).OrderBy(p => p.SlotID))
            {
                var picks = new List<string>();
                foreach (string category in Categories)
                {
                    var defs = Table(system).Where(d => d.Category == category).ToList();
                    if (!defs.Any(Built))
                    {
                        c.Log($"Living Spells: the {category} aren't in the game yet.");
                        continue;
                    }
                    var options = new List<string> { Roll };
                    options.AddRange(defs.Where(Built).Select(d => $"{d.Name} ({d.Value}): {d.Text}"));
                    string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(seat.PlayerID,
                        $"Living Spells: pick your army's spell from the {category}, or roll for it.", options,
                        new List<StringSelectionRequest.InvalidOption>(), displayName: "Living Spells") { Recommended = Roll });
                    int idx = options.IndexOf(answer);
                    Def picked;
                    if (idx > 0) picked = defs.Where(Built).ElementAt(idx - 1);
                    else
                    {
                        // A die per category; a result that isn't in the game yet is re-rolled.
                        Def? rolled = null;
                        for (int guard = 0; guard < 50 && rolled == null; guard++)
                        {
                            Def d = defs[c.DiceRoller.RollDecisiveFace() - 1];
                            if (Built(d)) rolled = d;
                        }
                        picked = rolled ?? defs.First(Built);
                    }
                    picks.Add(picked.Name);
                }
                s.Picks[Key(seat.PlayerID)] = picks;
                s.Names[Key(seat.PlayerID)] = seat.Name;
                c.Log($"Living Spells: {seat.Name} has {string.Join(", ", picks)}.");
            }
            Save(c, s);
        }

        // ---------------------------------------------------------------- the spell picker

        public const string CastPrefix = "Living Spell: ", UnbindPrefix = "Unbind: ";

        public static bool IsOption(string spell) =>
            spell.StartsWith(CastPrefix, StringComparison.Ordinal) || spell.StartsWith(UnbindPrefix, StringComparison.Ordinal);

        /// <summary>The extra picker entries for this caster: its army's living spells, and "Unbind: X" for each enemy living
        /// spell within 12".</summary>
        public static IReadOnlyList<RuntimeSpell> OfferSpells(IGameContext c, IUnit caster, PlayerID player)
        {
            if (!On(c) || State(c.GameDataStore) is not { } s) return Array.Empty<RuntimeSpell>();
            EGameSystem system = c.Settings.GameSystem;
            var list = new List<RuntimeSpell>();
            foreach (string name in s.Picks.GetValueOrDefault(Key(player)) ?? new List<string>())
                if (Find(system, name) is { } d) list.Add(Make(CastPrefix + d.Name, d.Value));
            foreach (LivingSpellPiece p in s.OnTable.Where(p => !Friendly(c, player, p.Owner)))
                if (Find(system, p.Name) is { } d && Gap(caster, p, d) <= 12f + 0.01f)
                    list.Add(Make($"{UnbindPrefix}{d.Name} ({p.OwnerName})", d.Value));
            return list;
        }

        private static RuntimeSpell Make(string name, int value) => new RuntimeSpell(
            new SpellDefinition(name, value, new TargetSelector(0f, 1, 1, ETargetAffinity.Self, RequireLineOfSight: false),
                new Effect.AddRule(name, ELifetime.NextTrigger)), Array.Empty<ResolvedRule>());

        /// <summary>The picker's description for a living spell or an unbind.</summary>
        public static string? Describe(IGameContext c, string option)
        {
            EGameSystem system = c.Settings.GameSystem;
            if (option.StartsWith(CastPrefix, StringComparison.Ordinal) && Find(system, option[CastPrefix.Length..]) is { } d)
                return $"{d.Size} Spell, placed within 12\" of the caster (your other living spell is removed). {d.Text}";
            if (option.StartsWith(UnbindPrefix, StringComparison.Ordinal))
                return "Cast like a spell; if it works, that living spell is removed from the table.";
            return null;
        }

        /// <summary>A living spell or unbind was cast successfully.</summary>
        public static async Task Resolve(IGameContext c, IUnit caster, string option)
        {
            LivingSpellsState s = State(c.GameDataStore) ?? new LivingSpellsState();
            EGameSystem system = c.Settings.GameSystem;
            if (option.StartsWith(UnbindPrefix, StringComparison.Ordinal))
            {
                string rest = option[UnbindPrefix.Length..];
                LivingSpellPiece? target = s.OnTable.FirstOrDefault(p => !Friendly(c, caster.PlayerID, p.Owner)
                    && rest == $"{p.Name} ({p.OwnerName})");
                if (target == null) return;
                Remove(c, s, target, $"{caster.Name} unbinds the {target.Name}");
                Save(c, s);
                await c.Announce($"{caster.Name} unbinds {target.OwnerName}'s {target.Name}.", Violet, EBannerTier.Toast);
                return;
            }
            if (Find(system, option[CastPrefix.Length..]) is not { } d) return;
            // "Each army may only have one living spell in play at a time ... the previous one is removed from play."
            foreach (LivingSpellPiece old in s.OnTable.Where(p => p.Owner == Key(caster.PlayerID)).ToList())
                Remove(c, s, old, $"{old.Name} makes way for the {d.Name}");
            Position at = await PlaceNear(c, caster.PlayerID, $"Living Spell: place the {d.Name} within 12\" of {caster.Name}",
                Centre(caster), 12f + CasterRadius(caster) + Radius(d.Size), d.Size);
            var piece = new LivingSpellPiece
            {
                Owner = Key(caster.PlayerID),
                OwnerName = PlayerName(c, caster.PlayerID),
                Name = d.Name,
                X = at.x,
                Z = at.z,
                Caster = AdvancedCasting.Id(caster),
            };
            if (d.Move == EMove.Straight || d.Size == ESize.Long)
                (piece.Fx, piece.Fz) = await AskFacing(c, caster, at, d);
            if (d.Name == Gates(system))
            {
                // "Place one additional copy of this spell within 24\" of it" - edge to edge.
                Position second = await PlaceNear(c, caster.PlayerID, $"Living Spell: place the second {d.Name} within 24\" of the first",
                    at, 24f + 2f * Radius(d.Size), d.Size);
                piece.X2 = second.x;
                piece.Z2 = second.z;
            }
            s.OnTable.Add(piece);
            Draw(c, piece, d);
            Note(s, $"{piece.OwnerName} casts the {d.Name} at ({at.x:0.0}, {at.z:0.0})");
            Save(c, s);
            c.Log($"Living Spells: {caster.Name} places the {d.Name} at ({at.x:0.0}, {at.z:0.0}).");
            await c.Announce($"{caster.Name} summons the {d.Name}.", Violet, EBannerTier.Toast);
        }

        private static string PlayerName(IGameContext c, PlayerID p) =>
            c.TableState.Players.Objects.FirstOrDefault(s => s.IsFilled && s.PlayerID == p)?.Name
            ?? State(c.GameDataStore)?.Names.GetValueOrDefault(Key(p)) ?? "Player";

        private static Position Centre(IUnit unit)
        {
            var alive = unit.Models.Where(m => m.GetIsAlive()).ToList();
            if (alive.Count == 0) return new Position(0f, 0f);
            return new Position(alive.Average(m => m.Position.x), alive.Average(m => m.Position.z));
        }

        private static float CasterRadius(IUnit unit)
        {
            Position mid = Centre(unit);
            return unit.Models.Where(m => m.GetIsAlive())
                .Select(m => Position.GetDistance2D(m.Position, mid) + m.BaseRadiusInches).DefaultIfEmpty(0f).Max();
        }

        /// <summary>Asks for a spot inside the table and within <paramref name="reach"/> (centres) of <paramref name="near"/>;
        /// a spot outside it is pulled back to the nearest legal one.</summary>
        private static async Task<Position> PlaceNear(IGameContext c, PlayerID player, string prompt, Position near, float reach, ESize size)
        {
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            float m = size == ESize.Long ? LongLength / 2f : Radius(size);
            var band = new RectangularZone(m, w - m, m, h - m);
            Position wanted = near;
            try
            {
                wanted = await c.PlayerRequester.RequestDecision<PlaceObjectiveRequest, Position>(new PlaceObjectiveRequest(player, prompt, 1, 1,
                    band, 0f, new CircularZone(new Float2(near.x, near.z), reach)));
            }
            catch (InvalidOperationException) { }
            return Clamp(near, wanted, reach, band);
        }

        private static Position Clamp(Position near, Position wanted, float reach, RectangularZone band)
        {
            float dx = wanted.x - near.x, dz = wanted.z - near.z, d = MathF.Sqrt(dx * dx + dz * dz);
            if (d > reach && d > 0.001f) wanted = new Position(near.x + dx / d * reach, near.z + dz / d * reach);
            return new Position(Math.Clamp(wanted.x, band.Left, band.Right), Math.Clamp(wanted.z, band.Bottom, band.Top));
        }

        public const string FaceEnemy = "At the nearest enemy unit", FaceAhead = "Towards the enemy army", FaceLeft = "To your left",
            FaceRight = "To your right", FaceBack = "Back towards your army";

        private static async Task<(float, float)> AskFacing(IGameContext c, IUnit caster, Position at, Def d)
        {
            var options = new List<string> { FaceEnemy, FaceAhead, FaceLeft, FaceRight, FaceBack };
            string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(caster.PlayerID,
                $"Living Spell: which way does the {d.Name}'s front face?" + (d.Move == EMove.Straight ? " It moves 12\" that way at the end of each round." : ""),
                options, new List<StringSelectionRequest.InvalidOption>(), displayName: "Living Spell - facing") { Recommended = FaceEnemy });
            Position own = ArmyCentre(c, caster.PlayerID, friendly: true) ?? at;
            Position foe = ArmyCentre(c, caster.PlayerID, friendly: false) ?? new Position(at.x, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES - at.z);
            (float ax, float az) = Unit(foe.x - own.x, foe.z - own.z);
            (float fx, float fz) = answer switch
            {
                FaceAhead => (ax, az),
                FaceLeft => (-az, ax),
                FaceRight => (az, -ax),
                FaceBack => (-ax, -az),
                _ => NearestEnemy(c, caster.PlayerID, at) is { } e ? Unit(e.x - at.x, e.z - at.z) : (ax, az),
            };
            return (fx, fz);
        }

        private static (float, float) Unit(float x, float z)
        {
            float d = MathF.Sqrt(x * x + z * z);
            return d < 0.001f ? (0f, 1f) : (x / d, z / d);
        }

        private static Position? ArmyCentre(IGameContext c, PlayerID p, bool friendly)
        {
            var models = c.TableState.Units.Objects.Where(u => u.GetIsAlive() && u.GetIsOnBattlefield()
                    && (ITeamExtensions.AreAllied(c.TableState.Teams.Objects, p, u.PlayerID) == friendly))
                .SelectMany(u => u.Models.Where(m => m.GetIsAlive())).ToList();
            if (models.Count == 0) return null;
            return new Position(models.Average(m => m.Position.x), models.Average(m => m.Position.z));
        }

        private static Position? NearestEnemy(IGameContext c, PlayerID p, Position at) =>
            c.TableState.Units.Objects.Where(u => u.GetIsAlive() && u.GetIsOnBattlefield()
                    && !ITeamExtensions.AreAllied(c.TableState.Teams.Objects, p, u.PlayerID))
                .Select(Centre).OrderBy(x => Position.GetDistance2D(x, at)).Cast<Position?>().FirstOrDefault();

        // ---------------------------------------------------------------- geometry

        /// <summary>Distance from a point to the spell's footprint edge (0 inside). Long spells lie across their facing (the
        /// front is a long side - house rules C).</summary>
        public static float DistanceTo(LivingSpellPiece p, Def d, float x, float z, bool second = false)
        {
            float cx = second ? p.X2 ?? p.X : p.X, cz = second ? p.Z2 ?? p.Z : p.Z;
            if (d.Size != ESize.Long) return MathF.Max(0f, MathF.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) - Radius(d.Size));
            // Long axis = perpendicular to the facing.
            float ux = -p.Fz, uz = p.Fx;
            float rx = x - cx, rz = z - cz;
            float along = rx * ux + rz * uz, across = rx * p.Fx + rz * p.Fz;
            float da = MathF.Max(0f, MathF.Abs(along) - LongLength / 2f), dc = MathF.Max(0f, MathF.Abs(across) - LongWidth / 2f);
            return MathF.Sqrt(da * da + dc * dc);
        }

        /// <summary>Closest gap between any living model's base and the spell (either copy).</summary>
        public static float Gap(IUnit unit, LivingSpellPiece p, Def d) =>
            unit.Models.Where(m => m.GetIsAlive()).Select(m =>
            {
                float a = DistanceTo(p, d, m.Position.x, m.Position.z) - m.BaseRadiusInches;
                if (p.X2 != null) a = MathF.Min(a, DistanceTo(p, d, m.Position.x, m.Position.z, second: true) - m.BaseRadiusInches);
                return a;
            }).DefaultIfEmpty(float.MaxValue).Min();

        public static bool Within(IUnit unit, LivingSpellPiece p, Def d, float inches) => Gap(unit, p, d) <= inches + 0.01f;

        // ---------------------------------------------------------------- drawing

        public const string MarkerPrefix = "Living spell: ";

        public static bool IsMarker(ITerrain t) => t.Name != null && t.Name.StartsWith(MarkerPrefix, StringComparison.Ordinal);

        private static string MarkerName(LivingSpellPiece p) => $"{MarkerPrefix}{p.Name} ({p.OwnerName})";

        /// <summary>The spell on the table: a piece with no rules of its own (so it can be seen and moved).</summary>
        private static void Draw(IGameContext c, LivingSpellPiece p, Def d)
        {
            Erase(c, p);
            foreach (bool second in p.X2 != null ? new[] { false, true } : new[] { false })
            {
                float x = second ? p.X2!.Value : p.X, z = second ? p.Z2!.Value : p.Z;
                IZone shape;
                if (d.Size == ESize.Long)
                {
                    float ux = -p.Fz * LongLength / 2f, uz = p.Fx * LongLength / 2f, vx = p.Fx * LongWidth / 2f, vz = p.Fz * LongWidth / 2f;
                    shape = new PolygonZone(new[] { new[]
                    {
                        x - ux - vx, z - uz - vz, x + ux - vx, z + uz - vz, x + ux + vx, z + uz + vz, x - ux + vx, z - uz + vz,
                    } });
                }
                else shape = new CircularZone(new Float2(x, z), Radius(d.Size));
                c.GameDataStore.Create(new TerrainData(ETerrainType.None, shape, heightInches: 0f, name: MarkerName(p), kind: "Hazard"));
            }
        }

        private static void Erase(IGameContext c, LivingSpellPiece p)
        {
            string name = MarkerName(p);
            var doomed = c.GameDataStore.GetAllDataReferences<TerrainData>()
                .Where(r => c.GameDataStore.IsValid(r, out _) && c.GameDataStore.GetValue<TerrainData>(r).Name == name).ToList();
            foreach (DataReference r in doomed) c.GameDataStore.Destroy(r);
        }

        private static void Remove(IGameContext c, LivingSpellsState s, LivingSpellPiece p, string why)
        {
            Erase(c, p);
            s.OnTable.Remove(p);
            Note(s, why);
            c.Log($"Living Spells: {why}.");
        }

        private static void Note(LivingSpellsState s, string line)
        {
            s.Recent.Add(line);
            while (s.Recent.Count > 4) s.Recent.RemoveAt(0);
        }

        // ---------------------------------------------------------------- during the game

        private static IEnumerable<(LivingSpellPiece Piece, Def Def)> Live(IGameContext c)
        {
            if (!On(c) || State(c.GameDataStore) is not { } s) yield break;
            foreach (LivingSpellPiece p in s.OnTable)
                if (Find(c.Settings.GameSystem, p.Name) is { } d) yield return (p, d);
        }

        /// <summary>Cogs of Time (+1) and Shard of Mist (-1) to hit when shooting, for a shooter within 6".</summary>
        public static IReadOnlyList<GrantedModifier> HitModifiers(IGameContext c, IUnit attacker, bool melee, bool spell)
        {
            if (spell || melee) return Array.Empty<GrantedModifier>();
            var list = new List<GrantedModifier>();
            foreach ((LivingSpellPiece p, Def d) in Live(c))
            {
                if (d.Name == CogsOfTime && Within(attacker, p, d, 6f)) list.Add(new GrantedModifier(CogsOfTime, 1));
                if (d.Name == ShardOfMist && Within(attacker, p, d, 6f)) list.Add(new GrantedModifier(ShardOfMist, -1));
            }
            return list;
        }

        /// <summary>Linked Jumpgates / Voidgates: a unit (either side) ending its activation within 3" of a copy may be placed
        /// within 3" of the other (asked; bots say no).</summary>
        public static async Task EndOfActivation(IGameContext c, IUnit unit)
        {
            if (!unit.GetIsAlive() || unit is not UnitData data) return;
            foreach ((LivingSpellPiece p, Def d) in Live(c).Where(x => x.Def.Name == Gates(c.Settings.GameSystem) && x.Piece.X2 != null).ToList())
            {
                bool nearA = unit.Models.Any(m => m.GetIsAlive() && DistanceTo(p, d, m.Position.x, m.Position.z) - m.BaseRadiusInches <= 3.01f);
                bool nearB = unit.Models.Any(m => m.GetIsAlive() && DistanceTo(p, d, m.Position.x, m.Position.z, second: true) - m.BaseRadiusInches <= 3.01f);
                if (!nearA && !nearB) continue;
                float tx = nearA ? p.X2!.Value : p.X, tz = nearA ? p.Z2!.Value : p.Z;
                bool go = await c.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(unit.PlayerID,
                    $"{d.Name}: place {unit.Name} within 3\" of the other copy?", defaultAnswer: false, displayName: "Living Spell - gate"));
                if (!go) return;
                var living = data.ModelBindings.Where(m => m.GetValue().GetIsAlive()).ToList();
                var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, $"{d.Name}: place {unit.Name} within 3\" of the other copy",
                    new CircularZone(new Float2(tx, tz), Radius(d.Size) + 3f), living, minDistanceFromEnemiesInches: 1f, allowCancel: true,
                    cancelHint: "Stay where it is.");
                var result = await c.PlayerRequester
                    .RequestDecision<PlaceObjectsRequest<ModelData>, CancellableResult<List<PlacedObjectEntry<ModelData>>>>(request);
                if (result is not Selected<List<PlacedObjectEntry<ModelData>>> selected) return;
                foreach (PlacedObjectEntry<ModelData> placement in selected.Value)
                {
                    placement.Binding.GetValue().SetPosition(placement.Position);
                    if (placement.Facing.HasValue) placement.Binding.GetValue().SetFacing(placement.Facing.Value);
                }
                c.Log($"{d.Name}: {unit.Name} steps through.");
                await c.Announce($"{unit.Name} steps through the {d.Name}", Violet, EBannerTier.Toast);
                return;
            }
        }

        /// <summary>End of the round, spell by spell in the order they were cast: Burning Pyre burns, movers move (a random
        /// corner, or straight ahead), Suffocating Cloud tests the units it passed through, Deathly Pendulum hits what is
        /// near where it stops, and a spell that reaches a table edge is removed.</summary>
        public static async Task RoundEnd(IGameContext c)
        {
            if (!On(c) || State(c.GameDataStore) is not { } s || s.OnTable.Count == 0) return;
            EGameSystem system = c.Settings.GameSystem;
            foreach (LivingSpellPiece p in s.OnTable.ToList())
            {
                if (!s.OnTable.Contains(p) || Find(system, p.Name) is not { } d) continue;
                if (d.Name == BurningPyre)
                {
                    foreach (IUnit u in UnitsWithin(c, p, d, 6f))
                    {
                        int d3 = D3(c);
                        c.Log($"Burning Pyre: {u.Name} takes {d3} wound{(d3 == 1 ? "" : "s")}.");
                        await CasualtyPresentation.ApplyUnitWounds(c, u, d3);
                    }
                    continue;
                }
                if (d.Move is not (EMove.Corner or EMove.Straight)) continue;

                float sx = p.X, sz = p.Z;
                (float dx, float dz) = d.Move == EMove.Straight ? (p.Fx, p.Fz) : CornerDirection(c, p);
                float r = d.Size == ESize.Long ? LongLength / 2f : Radius(d.Size);
                float travel = 12f;
                float toEdge = DistanceToEdge(sx, sz, dx, dz, r);
                bool offTable = toEdge <= travel + 0.001f;
                if (offTable) travel = MathF.Max(0f, toEdge);
                p.X = sx + dx * travel;
                p.Z = sz + dz * travel;
                if (d.Move == EMove.Corner) { p.Fx = dx; p.Fz = dz; }

                if (d.Name == SuffocatingCloud)
                    foreach (IUnit u in UnitsSwept(c, p, d, sx, sz, dx, dz, travel).ToList())
                    {
                        c.Log($"Suffocating Cloud: {u.Name} takes a Dangerous Terrain Test.");
                        await new GameOperationServices(c).ForceDangerousTerrainTest(u);
                    }

                if (offTable)
                {
                    Remove(c, s, p, $"The {p.Name} reaches the table edge and is removed");
                    await c.Announce($"The {p.Name} reaches the table edge and is gone.", Violet, EBannerTier.Toast);
                    continue;
                }
                Draw(c, p, d);
                c.Log($"Living Spells: the {p.Name} moves to ({p.X:0.0}, {p.Z:0.0}).");
                if (d.Name == DeathlyPendulum)
                    foreach (IUnit u in UnitsWithin(c, p, d, 6f))
                    {
                        int hits = D3(c) + D3(c);
                        c.Log($"Deathly Pendulum: {u.Name} takes {hits} hits.");
                        await CampaignEventRunner.DealHits(c, u, hits, DeathlyPendulum);
                    }
            }
            Save(c, s);
        }

        private static int D3(IGameContext c) => (c.DiceRoller.RollDecisiveFace() + 1) / 2;

        private static List<IUnit> UnitsWithin(IGameContext c, LivingSpellPiece p, Def d, float inches) =>
            c.TableState.Units.Objects.Where(u => u.GetIsAlive() && u.GetIsOnBattlefield() && Within(u, p, d, inches)).ToList();

        /// <summary>"Towards a random table corner": a die for one of the four corners (1-4; 5-6 re-rolled).</summary>
        private static (float, float) CornerDirection(IGameContext c, LivingSpellPiece p)
        {
            int corner = 0;
            for (int guard = 0; guard < 50 && corner == 0; guard++)
            {
                int f = c.DiceRoller.RollDecisiveFace();
                if (f <= 4) corner = f;
            }
            if (corner == 0) corner = 1;
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            (float cx, float cz) = corner switch { 1 => (0f, 0f), 2 => (w, 0f), 3 => (w, h), _ => (0f, h) };
            return Unit(cx - p.X, cz - p.Z);
        }

        /// <summary>How far the spell's centre can travel along (dx, dz) before its footprint touches a table edge.</summary>
        public static float DistanceToEdge(float x, float z, float dx, float dz, float r)
        {
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            float best = float.MaxValue;
            if (dx > 0.0001f) best = MathF.Min(best, (w - r - x) / dx);
            if (dx < -0.0001f) best = MathF.Min(best, (r - x) / dx);
            if (dz > 0.0001f) best = MathF.Min(best, (h - r - z) / dz);
            if (dz < -0.0001f) best = MathF.Min(best, (r - z) / dz);
            return MathF.Max(0f, best);
        }

        /// <summary>Units with a model whose base overlaps the area the spell swept, start to end.</summary>
        private static IEnumerable<IUnit> UnitsSwept(IGameContext c, LivingSpellPiece end, Def d, float sx, float sz, float dx, float dz, float travel)
        {
            float half = d.Size == ESize.Long ? LongLength / 2f : Radius(d.Size);
            float depth = d.Size == ESize.Long ? LongWidth / 2f : Radius(d.Size);
            foreach (IUnit u in c.TableState.Units.Objects.Where(u => u.GetIsAlive() && u.GetIsOnBattlefield()))
            {
                bool hit = u.Models.Any(m =>
                {
                    if (!m.GetIsAlive()) return false;
                    float rx = m.Position.x - sx, rz = m.Position.z - sz;
                    float along = rx * dx + rz * dz, side = MathF.Abs(rx * -dz + rz * dx);
                    float mr = m.BaseRadiusInches;
                    if (d.Size != ESize.Long)
                    {
                        // A round spell: the capsule from start to end.
                        float t = Math.Clamp(along, 0f, travel);
                        float px = sx + dx * t - m.Position.x, pz = sz + dz * t - m.Position.z;
                        return MathF.Sqrt(px * px + pz * pz) <= half + mr;
                    }
                    return along >= -depth - mr && along <= travel + depth + mr && side <= half + mr;
                });
                if (hit) yield return u;
            }
        }

        // ---------------------------------------------------------------- Mission panel

        public static List<(string Text, string Hover, bool Warn)> PanelLines(IReadableGameDataStore store, GameSettings settings)
        {
            var lines = new List<(string, string, bool)>();
            if (!AdvancedRules.IsOn(settings, EAdvancedRules.LivingSpells) || State(store) is not { } s) return lines;
            EGameSystem system = settings.GameSystem;
            lines.Add((Option, GameText, false));
            foreach ((string player, List<string> picks) in s.Picks)
                lines.Add(($"{s.Names.GetValueOrDefault(player, "Player")}: {string.Join(", ", picks)}",
                    string.Join("\n", picks.Select(n => Find(system, n) is { } d ? $"{d.Name} ({d.Value}): {d.Text}" : n)), false));
            foreach (LivingSpellPiece p in s.OnTable)
                lines.Add(($"On the table: {p.OwnerName}'s {p.Name} ({p.X:0}, {p.Z:0})" + (p.X2 != null ? $" and ({p.X2:0}, {p.Z2:0})" : ""),
                    Find(system, p.Name)?.Text ?? "", true));
            foreach (string recent in s.Recent) lines.Add((recent, "", false));
            return lines;
        }
    }
}
