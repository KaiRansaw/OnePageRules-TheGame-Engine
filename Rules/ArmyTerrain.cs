using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;
using FDG.Missions;
using FDG.StageResolution;
using FDG.Stages;

namespace FDG
{
    /// <summary>One army's terrain piece (two for the twin pieces), saved with the game.</summary>
    [Serializable]
    public sealed class ArmyPiece
    {
        public string Owner { get; set; } = "";   // player id (Guid)
        public string Name { get; set; } = "";
        public float X { get; set; }
        public float Z { get; set; }
        /// <summary>The second copy (Arcane Pillars, Twin Jumpgates / Voidgates).</summary>
        public float? X2 { get; set; }
        public float? Z2 { get; set; }
    }

    [Serializable]
    public sealed class ArmyTerrainState
    {
        public List<ArmyPiece> Pieces { get; set; } = new();
        /// <summary>The unit activating now and the army terrain it activated near ("Battle Forge", "Icon of War",
        /// "Army Banner") - "when a friendly unit activates within X" of this army terrain".</summary>
        public string ActivatingUnit { get; set; } = "";
        public List<string> ActivationBuffs { get; set; } = new();
    }

    /// <summary>
    /// Army Terrain (Advanced Rulebooks 3.6.0, Advanced Terrain: GF p32, AoF p32, GFF p30, AoFS p30 - the same rules in all
    /// four; GF / GFF call the second table "Relic Terrain" with "Twin Jumpgates", AoF / AoFS "Magic Terrain" with "Twin
    /// Voidgates"). "Each army to place a special piece of terrain that affects the game in different interesting ways."
    /// Part 1 (build 3.31): the framework and the Battle Terrain. Judgement calls: house rules C, "Army Terrain readings".
    /// </summary>
    public static class ArmyTerrain
    {
        public const string Option = "Army Terrain";
        public const string ArmyBanner = "Army Banner", BattleForge = "Battle Forge", DefensiveBarrier = "Defensive Barrier",
            IconOfWar = "Icon of War", ProtectiveDome = "Protective Dome", WatchTower = "Watch Tower";

        public const string ArcanePillars = "Arcane Pillars", EnchantedShrine = "Enchanted Shrine", FactionRelic = "Faction Relic",
            HealingWell = "Healing Well", MagicArmory = "Magic Armory", TwinJumpgates = "Twin Jumpgates", TwinVoidgates = "Twin Voidgates";

        /// <summary>GF / GFF: "Relic Terrain" and "Twin Jumpgates"; AoF / AoFS: "Magic Terrain" and "Twin Voidgates".</summary>
        public static bool Fantasy(EGameSystem system) => system is EGameSystem.AgeOfFantasy or EGameSystem.AoFSkirmish;
        public static string SecondTable(EGameSystem system) => Fantasy(system) ? "Magic Terrain" : "Relic Terrain";
        public static string TwinName(EGameSystem system) => Fantasy(system) ? TwinVoidgates : TwinJumpgates;
        public static bool IsTwin(string name) => name is TwinJumpgates or TwinVoidgates;
        public static bool TwoCopies(string name) => name == ArcanePillars || IsTwin(name);

        public static IReadOnlyList<(string Name, string Text)> Relic(EGameSystem system) => new[]
        {
            (ArcanePillars, "When placing this army terrain, place two copies of it within 12\" of each other (instead of placing just one). Draw a straight line from one copy of this army terrain to the other. When an enemy unit ends its activation with at least one model touching the line, the unit takes D3 wounds."),
            (EnchantedShrine, "When a friendly Caster activates within 6\" of this army terrain, it gets 1 spell token."),
            (FactionRelic, "When a friendly unit within 12\" of this army terrain takes a Morale Test, it gets +1 to its Morale Test roll."),
            (HealingWell, "When a friendly unit within 6\" of this army terrain is activated, it may restore D3 models/wounds. Note that new models may only be restored if they can be placed in coherency with non-restored models."),
            (MagicArmory, "When a friendly unit within 6\" of this army terrain takes hits, it gets +1 to its Defense rolls."),
            (TwinName(system), "When placing this army terrain, place two copies of it within 24\" of each other (instead of placing just one). When a friendly unit ends its activation within 3\" of one of the copies of this army terrain, you may place it anywhere within 3\" of the other copy of this army terrain."),
        };

        public static readonly IReadOnlyList<(string Name, string Text)> Battle = new[]
        {
            (ArmyBanner, "When a friendly unit activates within 6\" of this army terrain, it gets +2\" when using Advance actions, and +4\" when using Rush/Charge actions."),
            (BattleForge, "When a friendly unit activates within 6\" of this army terrain, it gets +1 to hit rolls when shooting."),
            (DefensiveBarrier, "The area within 6\" of this faction terrain counts as Difficult terrain for enemy units."),
            (IconOfWar, "When a friendly unit activates within 12\" of this army terrain, it gets +1 to hit rolls in melee when charging."),
            (ProtectiveDome, "The area within 6\" of this faction terrain counts as Cover terrain for friendly units."),
            (WatchTower, "The area within 6\" of this faction terrain counts as Dangerous terrain for enemy units."),
        };

        /// <summary>The pieces built so far (Defensive Barrier needs side-aware Difficult terrain in the move checks).</summary>
        public static readonly HashSet<string> Built = new()
        {
            ArmyBanner, BattleForge, DefensiveBarrier, IconOfWar, ProtectiveDome, WatchTower,
            ArcanePillars, EnchantedShrine, FactionRelic, HealingWell, MagicArmory, TwinJumpgates, TwinVoidgates,
        };

        public static string TextOf(string name) =>
            Battle.Concat(Relic(EGameSystem.GrimdarkFuture)).Concat(Relic(EGameSystem.AgeOfFantasy)).First(b => b.Name == name).Text;

        /// <summary>How a bot keeps one of its two rolled pieces: the first of these that it rolled.</summary>
        public static readonly string[] BotPreference =
        {
            BattleForge, MagicArmory, ProtectiveDome, HealingWell, ArmyBanner, FactionRelic, IconOfWar, WatchTower,
            ArcanePillars, EnchantedShrine, TwinJumpgates, TwinVoidgates, DefensiveBarrier,
        };

        public const float PieceRadius = 1.5f;   // a 3" round piece: "between 2"x2" and 4"x4""
        public const float PieceHeight = 3f;     // "between 2" and 4" tall"
        public const float AreaInches = 6f;

        public static readonly IReadOnlyList<(string Name, EAdvancedRules Flag)> All = new[] { (Option, EAdvancedRules.ArmyTerrain) };

        public static string BookText(string name) =>
            "Players can choose to play using army terrain, which allows each army to place a special piece of terrain that affects the game in different interesting ways. "
            + "Before the game begins, each army either picks one army terrain, or rolls to get one randomly. "
            + "Army terrain pieces should be somewhere between 2\"x2\" and 4\"x4\" in size, and somewhere between 2\" and 4\" tall. Each piece counts as Impassable and Sight Blocking terrain. "
            + "During deployment, army terrain pieces are placed as if they were a regular unit, but they can't be placed within 6\" of any objective marker, or the table edges.\n"
            + "To get army terrain randomly, roll one die for the Battle Terrain and one for the Relic (AoF: Magic) Terrain, then secretly pick one of the two.\n"
            + string.Join("\n", Battle.Concat(Relic(EGameSystem.GrimdarkFuture)).Select(b => $"{b.Name}: {b.Text}"));

        public static string GameText(string name) =>
            "Each player picks (or rolls) one piece once the deployment zones are chosen, and places it in their own zone before "
            + "any unit is deployed (two copies for Arcane Pillars and the Twin gates). Bots roll.";

        public static bool On(IGameContext c) => AdvancedRules.IsOn(c, EAdvancedRules.ArmyTerrain);

        // ---------------------------------------------------------------- state

        private static readonly ConditionalWeakTable<IReadableGameDataStore, ArmyTerrainState> Pending = new();

        public static ArmyTerrainState? State(IReadableGameDataStore store) =>
            GameProgressUtilities.TryGetProgress(store)?.ArmyTerrain
            ?? (Pending.TryGetValue(store, out ArmyTerrainState? s) ? s : null);

        internal static void Save(IGameContext c, ArmyTerrainState s)
        {
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(c.GameDataStore);
            if (progress == null)
            {
                Pending.Remove(c.GameDataStore);
                Pending.Add(c.GameDataStore, s);
                return;
            }
            progress.ArmyTerrain = s;
            GameProgressUtilities.WriteProgress(c.GameDataStore, progress);
        }

        private static ITeam? TeamOf(IGameContext c, PlayerID p) => c.TableState.Teams.Objects.FirstOrDefault(t => t.IsPlayerOnTeam(p));

        private static bool Friendly(IGameContext c, PlayerID a, string ownerId) =>
            Guid.TryParse(ownerId, out Guid g) && (a.ID == g || (TeamOf(c, a) is { } team && team.IsPlayerOnTeam(new PlayerID(g))));

        private static IEnumerable<ArmyPiece> Pieces(IGameContext c) => State(c.GameDataStore)?.Pieces ?? Enumerable.Empty<ArmyPiece>();

        /// <summary>The closest gap from any living model's base to the piece's edge.</summary>
        public static float Gap(IUnit unit, ArmyPiece piece) =>
            unit.Models.Where(m => m.GetIsAlive())
                .Select(m => Position.GetDistance2D(m.Position, new Position(piece.X, piece.Z)) - m.BaseShape.BoundingRadiusInches - PieceRadius)
                .DefaultIfEmpty(float.MaxValue).Min();

        // ---------------------------------------------------------------- Defensive Barrier: whose models are whose

        public const string BarrierAreaPrefix = "Defensive Barrier area (";

        /// <summary>Each model's team, so the one move check (MovementUtilities) can tell the barrier's own side from the
        /// enemy without a game in hand. Filled when a barrier is placed and refreshed at every activation; a model it
        /// doesn't know (a fresh copy in a simulation, a joined player's machine) treats every barrier as Difficult, which
        /// only ever makes a planner stricter than the engine - never looser, so no plan the engine rejects.</summary>
        private static readonly ConditionalWeakTable<IModel, StrongBox<int>> ModelTeam = new();

        public static void RegisterTeams(IGameContext c)
        {
            foreach (IUnit unit in c.TableState.Units.Objects)
            {
                if (TeamOf(c, unit.PlayerID) is not { } team) continue;
                foreach (IModel m in unit.Models)
                {
                    ModelTeam.Remove(m);
                    ModelTeam.Add(m, new StrongBox<int>(team.TeamNumber));
                }
            }
        }

        /// <summary>The team a Defensive Barrier area belongs to (from its name), or null for any other terrain.</summary>
        public static int? BarrierTeam(ITerrain terrain)
        {
            string? name = terrain.Name;
            if (name == null || !name.StartsWith(BarrierAreaPrefix, StringComparison.Ordinal)) return null;
            int at = name.LastIndexOf("[team ", StringComparison.Ordinal);
            return at >= 0 && int.TryParse(name.AsSpan(at + 6).TrimEnd(']'), out int team) ? team : null;
        }

        /// <summary>The team registered for a model by <see cref="RegisterTeams"/>, or null.</summary>
        public static int? TeamOfModel(IModel m) => ModelTeam.TryGetValue(m, out StrongBox<int>? box) ? box.Value : null;

        /// <summary>True when this terrain is a Defensive Barrier area of the moving model's own side (not Difficult for it).</summary>
        public static bool FriendlyBarrier(ITerrain terrain, IModel mover) =>
            BarrierTeam(terrain) is int team && ModelTeam.TryGetValue(mover, out StrongBox<int>? box) && box.Value == team;

        // ---------------------------------------------------------------- before the game

        public const string Roll = "Roll for it";

        /// <summary>
        /// After the deployment zones are picked: each player picks (or rolls) its army terrain and places it inside its own
        /// zone, over 6" from objective markers and the table edges, not on other terrain.
        /// </summary>
        public static async Task Place(IGameContext c, IReadOnlyDictionary<int, ZoneRect> zonesByTeam)
        {
            if (!On(c)) return;
            ArmyTerrainState s = State(c.GameDataStore) ?? new ArmyTerrainState();
            if (s.Pieces.Count > 0) return;
            foreach (IPlayerSlotInfo seat in c.TableState.Players.Objects.Where(p => p.IsFilled).OrderBy(p => p.SlotID))
            {
                if (!zonesByTeam.TryGetValue(seat.TeamNumber, out ZoneRect? zone)) continue;
                string name = await PickPiece(c, seat);
                Position at = await PlacePiece(c, seat, name, zone, null, 0f);
                var piece = new ArmyPiece { Owner = seat.PlayerID.ID.ToString(), Name = name, X = at.x, Z = at.z };
                CreateTerrain(c, name, seat.Name, at);
                if (name == DefensiveBarrier)
                {
                    // "The area within 6\" of this faction terrain counts as Difficult terrain for enemy units" - real
                    // Difficult terrain on the table, which the move check skips for the owner's own models.
                    c.GameDataStore.Create(new TerrainData(ETerrainType.Difficult,
                        new CircularZone(new Float2(at.x, at.z), PieceRadius + AreaInches), heightInches: 0f,
                        name: $"{BarrierAreaPrefix}{seat.Name}) [team {seat.TeamNumber}]"));
                    RegisterTeams(c);
                }
                c.Log($"Army Terrain: {seat.Name} places the {name} at ({at.x:0.0}, {at.z:0.0}).");
                if (TwoCopies(name))
                {
                    // "Place two copies of it within 12\" (24\") of each other" - edge to edge.
                    float within = name == ArcanePillars ? 12f : 24f;
                    Position second = await PlacePiece(c, seat, name, zone, at, within + 2f * PieceRadius);
                    piece.X2 = second.x;
                    piece.Z2 = second.z;
                    CreateTerrain(c, name, seat.Name, second);
                    c.Log($"Army Terrain: {seat.Name} places the second {name} at ({second.x:0.0}, {second.z:0.0}).");
                }
                s.Pieces.Add(piece);
                await c.Announce($"{seat.Name}: {name}", BattlefieldEffects.Amber, EBannerTier.Toast);
            }
            Save(c, s);
        }

        private static async Task<string> PickPiece(IGameContext c, IPlayerSlotInfo seat)
        {
            EGameSystem system = c.Settings.GameSystem;
            var all = Battle.Concat(Relic(system)).Where(b => Built.Contains(b.Name)).ToList();
            var options = new List<string> { Roll };
            options.AddRange(all.Select(b => $"{b.Name}: {b.Text}"));
            string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(seat.PlayerID,
                "Army Terrain: pick your army's piece, or roll for it (one die on each table, then keep one).", options,
                new List<StringSelectionRequest.InvalidOption>(), displayName: "Army Terrain - pick") { Recommended = Roll });
            int idx = options.IndexOf(answer);
            if (idx > 0) return all[idx - 1].Name;

            // "Roll one die to get one type of Battle Terrain. Then, roll one die to get one type of Relic Terrain. Finally,
            // secretly pick one of the two results."
            string battle = Battle[c.DiceRoller.RollDecisiveFace() - 1].Name;
            string relic = Relic(system)[c.DiceRoller.RollDecisiveFace() - 1].Name;
            var rolled = new[] { battle, relic }.Where(n => Built.Contains(n)).ToList();
            if (!Built.Contains(battle)) c.Log($"Army Terrain: {seat.Name} rolled the {battle}, which isn't in the game yet.");
            if (rolled.Count == 1) return rolled[0];
            var keep = rolled.Select(n => $"{n}: {TextOf(n)}").ToList();
            string bot = BotPreference.First(rolled.Contains);
            string kept = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(seat.PlayerID,
                $"Army Terrain: you rolled the {battle} and the {relic} - keep one.", keep, new List<StringSelectionRequest.InvalidOption>(),
                displayName: "Army Terrain - keep") { Recommended = keep[rolled.IndexOf(bot)] });
            int k = keep.IndexOf(kept);
            return rolled[k >= 0 ? k : 0];
        }

        /// <summary>Where the piece may go: inside the zone, over 6" from the table edges (its edge), as a band.</summary>
        public static RectangularZone LegalBand(ZoneRect zone)
        {
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            float m = AreaInches + PieceRadius;
            return new RectangularZone(MathF.Max(zone.Left + PieceRadius, m), MathF.Min(zone.Right - PieceRadius, w - m),
                MathF.Max(zone.Bottom + PieceRadius, m), MathF.Min(zone.Top - PieceRadius, h - m));
        }

        /// <summary>Clear of every terrain piece (the whole round footprint).</summary>
        public static bool ClearOfTerrain(IGameContext c, Position p) =>
            !c.TableState.Terrain.Objects.Any(t => Enumerable.Range(0, 9).Any(i =>
            {
                float a = i * MathF.PI / 4f, r = i == 8 ? 0f : PieceRadius;
                return t.IsPointWithinZone(new Float2(p.x + MathF.Cos(a) * r, p.z + MathF.Sin(a) * r));
            }));

        public static bool Legal(IGameContext c, Position p, RectangularZone band) =>
            ObjectivePlacementValidator.Check(p, band, AreaInches + PieceRadius, c.TableState.Objectives.Objects.ToList(),
                Array.Empty<ITerrain>()) == ObjectivePlacementValidity.Valid && ClearOfTerrain(c, p);

        private static void CreateTerrain(IGameContext c, string name, string owner, Position at) =>
            c.GameDataStore.Create(new TerrainData(ETerrainType.Impassible | ETerrainType.Blocking,
                new CircularZone(new Float2(at.x, at.z), PieceRadius), heightInches: PieceHeight, name: $"{name} ({owner})"));

        private static async Task<Position> PlacePiece(IGameContext c, IPlayerSlotInfo seat, string name, ZoneRect zone,
            Position? near, float maxCentres)
        {
            RectangularZone band = LegalBand(zone);
            bool Ok(Position p) => Legal(c, p, band) && (near is not { } n || Position.GetDistance2D(p, n) <= maxCentres + 0.01f);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Position wanted;
                try
                {
                    wanted = await c.PlayerRequester.RequestDecision<PlaceObjectiveRequest, Position>(new PlaceObjectiveRequest(seat.PlayerID,
                        near == null
                            ? $"Army Terrain: place your {name} in your deployment zone (over 6\" from objectives and the table edges)"
                            : $"Army Terrain: place the second {name}, within {maxCentres - 2f * PieceRadius:0}\" of the first",
                        near == null ? 1 : 2, TwoCopies(name) ? 2 : 1, band, AreaInches + PieceRadius));
                }
                catch (InvalidOperationException) { break; }
                if (Ok(wanted)) return wanted;
            }
            // No legal answer: the first legal spot on a 1" grid, from the band's middle outwards.
            var mid = near ?? new Position((band.Left + band.Right) / 2f, (band.Bottom + band.Top) / 2f);
            var spots = new List<Position>();
            for (float x = band.Left; x <= band.Right; x += 1f)
            for (float z = band.Bottom; z <= band.Top; z += 1f)
                spots.Add(new Position(x, z));
            Position? found = spots.OrderBy(p => near == null ? Position.GetDistance2D(p, mid) : -Position.GetDistance2D(p, mid))
                .Cast<Position?>().FirstOrDefault(p => Ok(p!.Value));
            return found ?? spots.OrderBy(p => Position.GetDistance2D(p, mid)).Cast<Position?>().FirstOrDefault(p => Legal(c, p!.Value, band)) ?? mid;
        }

        // ---------------------------------------------------------------- during the game

        /// <summary>A unit activates: which of its army's pieces it activated near (Army Banner / Battle Forge 6", Icon of War 12").</summary>
        public static async Task OnActivation(IGameContext c, IUnit unit)
        {
            if (!On(c) || State(c.GameDataStore) is not { } s) return;
            if (s.Pieces.Any(p => p.Name == DefensiveBarrier)) RegisterTeams(c);
            s.ActivatingUnit = unit.ID.ID.ToString();
            s.ActivationBuffs.Clear();
            foreach (ArmyPiece p in s.Pieces.Where(p => Friendly(c, unit.PlayerID, p.Owner)))
            {
                float reach = p.Name == IconOfWar ? 12f : AreaInches;
                if ((p.Name is ArmyBanner or BattleForge or IconOfWar) && Gap(unit, p) <= reach + 0.01f && !s.ActivationBuffs.Contains(p.Name))
                {
                    s.ActivationBuffs.Add(p.Name);
                    c.Log($"Army Terrain: {unit.Name} activates near the {p.Name}.");
                }
                if (Gap(unit, p) > AreaInches + 0.01f) continue;
                // Healing Well: "may restore D3 models/wounds" - taken whenever something is missing.
                if (p.Name == HealingWell && unit.Models.Any(m => !m.GetIsAlive() || m.WoundsDealt > 0.999f))
                    await new GameOperationServices(c).RestoreD3(unit, HealingWell);
                // Enchanted Shrine: "When a friendly Caster activates within 6\" ... it gets 1 spell token."
                if (p.Name == EnchantedShrine && unit.Models.Any(m => m.GetIsAlive() && MissionCardGame.IsCasterModel(unit, m))
                    && unit.Tokens.GetTokenCount(Rules.Foundation.TokenType.SpellTokens) < GameWideConstants.MAX_SPELL_TOKENS)
                {
                    unit.Tokens.AddToken(new Rules.Tokens.Token(Rules.Foundation.TokenType.SpellTokens, 1, new Rules.Foundation.TokenClearTrigger.ManualOnly()));
                    c.Log($"Enchanted Shrine: {unit.Name} gets 1 spell token.");
                }
            }
            Save(c, s);
        }

        /// <summary>The closest gap from any living model's base to either copy of a two-copy piece.</summary>
        private static float GapAny(IUnit unit, ArmyPiece p) =>
            p.X2 is float x2 && p.Z2 is float z2
                ? MathF.Min(Gap(unit, p), Gap(unit, new ArmyPiece { X = x2, Z = z2 }))
                : Gap(unit, p);

        /// <summary>Faction Relic: +1 to Morale Test rolls for a friendly unit within 12".</summary>
        public static int MoraleModifier(IGameContext c, IUnit unit) =>
            On(c) && Pieces(c).Any(p => p.Name == FactionRelic && Friendly(c, unit.PlayerID, p.Owner) && Gap(unit, p) <= 12f + 0.01f) ? 1 : 0;

        /// <summary>Magic Armory: +1 to Defense rolls for a friendly unit within 6" when it takes hits.</summary>
        public static IReadOnlyList<GrantedModifier> SaveModifiers(IGameContext c, IUnit defender) =>
            On(c) && Pieces(c).Any(p => p.Name == MagicArmory && Friendly(c, defender.PlayerID, p.Owner) && Gap(defender, p) <= AreaInches + 0.01f)
                ? new[] { new GrantedModifier(MagicArmory, 1) }
                : Array.Empty<GrantedModifier>();

        /// <summary>Whether a model's base touches the line between a piece's two copies (Arcane Pillars).</summary>
        public static bool TouchesLine(IModel m, ArmyPiece p)
        {
            if (p.X2 is not float x2 || p.Z2 is not float z2) return false;
            float ax = p.X, az = p.Z, dx = x2 - ax, dz = z2 - az;
            float len2 = dx * dx + dz * dz;
            float t = len2 < 1e-6f ? 0f : Math.Clamp(((m.Position.x - ax) * dx + (m.Position.z - az) * dz) / len2, 0f, 1f);
            float px = ax + t * dx - m.Position.x, pz = az + t * dz - m.Position.z;
            return MathF.Sqrt(px * px + pz * pz) <= m.BaseShape.BoundingRadiusInches + 0.01f;
        }

        public const string UseGate = "Use the gate";

        /// <summary>
        /// End of a unit's activation: an enemy Arcane Pillars line it touches deals D3 wounds; a friendly gate within 3"
        /// may move it to within 3" of the other copy (the player places it; bots don't).
        /// </summary>
        public static async Task EndOfActivation(IGameContext c, IUnit unit)
        {
            if (!On(c) || !unit.GetIsAlive() || State(c.GameDataStore) is not { } s) return;
            foreach (ArmyPiece p in s.Pieces.Where(p => p.Name == ArcanePillars && !Friendly(c, unit.PlayerID, p.Owner)).ToList())
            {
                if (!unit.Models.Any(m => m.GetIsAlive() && TouchesLine(m, p))) continue;
                int d3 = (c.DiceRoller.RollDecisiveFace() + 1) / 2;
                c.Log($"Arcane Pillars: {unit.Name} ends its activation on the line - {d3} wound{(d3 == 1 ? "" : "s")}.");
                await CasualtyPresentation.ApplyUnitWounds(c, unit, d3);
                if (!unit.GetIsAlive()) return;
            }
            if (unit is not UnitData data) return;
            foreach (ArmyPiece p in s.Pieces.Where(p => IsTwin(p.Name) && Friendly(c, unit.PlayerID, p.Owner) && p.X2 != null).ToList())
            {
                var a = new Position(p.X, p.Z);
                var b = new Position(p.X2!.Value, p.Z2!.Value);
                bool nearA = Gap(unit, p) <= 3f + 0.01f, nearB = Gap(unit, new ArmyPiece { X = b.x, Z = b.z }) <= 3f + 0.01f;
                if (!nearA && !nearB) continue;
                Position to = nearA ? b : a;
                bool go = await c.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(unit.PlayerID,
                    $"{p.Name}: place {unit.Name} within 3\" of the other gate?", defaultAnswer: false, displayName: "Army Terrain - gate"));
                if (!go) return;
                var living = data.ModelBindings.Where(m => m.GetValue().GetIsAlive()).ToList();
                var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, $"{p.Name}: place {unit.Name} within 3\" of the other gate",
                    new CircularZone(new Float2(to.x, to.z), PieceRadius + 3f), living, minDistanceFromEnemiesInches: 1f, allowCancel: true,
                    cancelHint: "Stay where it is.");
                var result = await c.PlayerRequester
                    .RequestDecision<PlaceObjectsRequest<ModelData>, CancellableResult<List<PlacedObjectEntry<ModelData>>>>(request);
                if (result is not Selected<List<PlacedObjectEntry<ModelData>>> selected) return;
                foreach (PlacedObjectEntry<ModelData> placement in selected.Value)
                {
                    placement.Binding.GetValue().SetPosition(placement.Position);
                    if (placement.Facing.HasValue) placement.Binding.GetValue().SetFacing(placement.Facing.Value);
                }
                c.Log($"{p.Name}: {unit.Name} steps through the gate.");
                await c.Announce($"{unit.Name} steps through the {p.Name}", BattlefieldEffects.Amber, EBannerTier.Toast);
                return;
            }
        }

        private static bool Buffed(IGameContext c, IUnit unit, string piece) =>
            On(c) && State(c.GameDataStore) is { } s && s.ActivatingUnit == unit.ID.ID.ToString() && s.ActivationBuffs.Contains(piece);

        /// <summary>Army Banner: (+Advance, +Rush/Charge) for the unit activating near it.</summary>
        public static (float Advance, float Rush) MoveChange(IGameContext c, IUnit unit) =>
            Buffed(c, unit, ArmyBanner) ? (2f, 4f) : (0f, 0f);

        /// <summary>Battle Forge (+1 shooting) and Icon of War (+1 in melee when charging), for the activating unit.</summary>
        public static IReadOnlyList<GrantedModifier> HitModifiers(IGameContext c, IUnit attacker, bool melee, bool spell, bool charging)
        {
            if (spell || !On(c)) return Array.Empty<GrantedModifier>();
            if (!melee && Buffed(c, attacker, BattleForge)) return new[] { new GrantedModifier(BattleForge, 1) };
            if (melee && charging && Buffed(c, attacker, IconOfWar)) return new[] { new GrantedModifier(IconOfWar, 1) };
            return Array.Empty<GrantedModifier>();
        }

        /// <summary>Protective Dome: most of the unit's living models are within 6" of a friendly dome - it counts as in Cover.</summary>
        public static bool InFriendlyDome(IGameContext c, IUnit defender)
        {
            if (!On(c)) return false;
            var living = defender.Models.Where(m => m.GetIsAlive()).ToList();
            if (living.Count == 0) return false;
            foreach (ArmyPiece p in Pieces(c).Where(p => p.Name == ProtectiveDome && Friendly(c, defender.PlayerID, p.Owner)))
            {
                int inside = living.Count(m => Position.GetDistance2D(m.Position, new Position(p.X, p.Z))
                    - m.BaseShape.BoundingRadiusInches - PieceRadius <= AreaInches + 0.01f);
                if (inside * 2 > living.Count) return true;
            }
            return false;
        }

        /// <summary>Watch Tower: the enemy towers' 6" areas, as Dangerous terrain for this unit's Dangerous tests.</summary>
        public static IReadOnlyList<ITerrain> EnemyDangerous(IGameContext c, IUnit unit)
        {
            if (!On(c)) return Array.Empty<ITerrain>();
            return Pieces(c).Where(p => p.Name == WatchTower && !Friendly(c, unit.PlayerID, p.Owner))
                .Select(p => (ITerrain)new TerrainData(ETerrainType.Dangerous,
                    new CircularZone(new Float2(p.X, p.Z), PieceRadius + AreaInches), heightInches: 0f, name: $"{WatchTower} area"))
                .ToList();
        }
    }
}
