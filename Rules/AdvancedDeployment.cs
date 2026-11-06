using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG
{
    /// <summary>A deployment style from the Advanced Rulebooks, or the core rules' own layout.</summary>
    public enum EDeploymentStyle
    {
        /// <summary>The core rules: each side within 12" of its long table edge. The default.</summary>
        Core = 0,

        // Standard - "a balanced experience, with fairly standard gameplay"
        FrontLine, GroundWar, SideBattle, Disordered, Spearhead, OpposingForces,
        // Strategic - "a balanced experience, with interesting twists that require strategic thinking"
        NoMansLand, LongHaul, FlankAssault, FrontalClash, TacticalPush, MeetingEngagement,
        // Asymmetric - "an unbalanced experience, with one player being surrounded or disadvantaged"
        OpenWarzone, Pushback, Cornered, Encircled, BehindEnemyLines, LightningStrike,

        /// <summary>"To select a random deployment style, simply roll one die" - one per family.</summary>
        RandomStandard, RandomStrategic, RandomAsymmetric,
    }

    public enum EDeploymentFamily { Standard, Strategic, Asymmetric }

    /// <summary>
    /// The Advanced Rulebooks' 18 deployment styles (Kai, 2026-11-05, from screenshots of the pages: the
    /// zones are diagrams only, the PDF text names them and nothing more).
    ///
    /// <para>
    /// <b>Source:</b> GF: Firefight and AoF: Skirmish Advanced Rules 3.6.0 pp19-21 - Kai checked that the two
    /// books print the same diagrams. The diagrams are a square table on a 6x6 grid; both games are played
    /// on 4'x4' (core rules), so one grid square is 8". The shapes below are read off the diagrams at that
    /// scale; every edge lands on a 6" step, and Front Line - "the basic deployment from the core rules" -
    /// comes out at exactly the core rules' 12", which is the check that the scale is right.
    /// </para>
    ///
    /// <para>
    /// Grimdark Future and Age of Fantasy print their own pages (6'x4' table); those are not in yet, so
    /// <see cref="Available"/> says no for them rather than stretching these square diagrams to a long table.
    /// </para>
    ///
    /// <para>
    /// Coordinates: inches on a 48" x 48" table, x to the right, z up, origin bottom-left - the engine's own.
    /// Zone A is the top of the diagram. They are scaled to the real table size, so a resized table keeps
    /// the shapes.
    /// </para>
    /// </summary>
    public static class AdvancedDeployment
    {
        /// <summary>Whether this game's books have these styles in the engine yet.</summary>
        public static bool Available(EGameSystem system) =>
            system is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;

        /// <summary>The style that is actually played: <paramref name="style"/> itself, or the rolled one.</summary>
        public static EDeploymentStyle Resolve(EDeploymentStyle style, Func<int> rollD6)
        {
            EDeploymentFamily? family = style switch
            {
                EDeploymentStyle.RandomStandard => EDeploymentFamily.Standard,
                EDeploymentStyle.RandomStrategic => EDeploymentFamily.Strategic,
                EDeploymentStyle.RandomAsymmetric => EDeploymentFamily.Asymmetric,
                _ => null,
            };
            if (family is not { } f) return style;
            int roll = Math.Clamp(rollD6(), 1, 6);
            return InFamily(f)[roll - 1];
        }

        /// <summary>The six styles of a family, in the book's roll order (1-6).</summary>
        public static IReadOnlyList<EDeploymentStyle> InFamily(EDeploymentFamily family) => family switch
        {
            EDeploymentFamily.Standard => new[]
            {
                EDeploymentStyle.FrontLine, EDeploymentStyle.GroundWar, EDeploymentStyle.SideBattle,
                EDeploymentStyle.Disordered, EDeploymentStyle.Spearhead, EDeploymentStyle.OpposingForces,
            },
            EDeploymentFamily.Strategic => new[]
            {
                EDeploymentStyle.NoMansLand, EDeploymentStyle.LongHaul, EDeploymentStyle.FlankAssault,
                EDeploymentStyle.FrontalClash, EDeploymentStyle.TacticalPush, EDeploymentStyle.MeetingEngagement,
            },
            _ => new[]
            {
                EDeploymentStyle.OpenWarzone, EDeploymentStyle.Pushback, EDeploymentStyle.Cornered,
                EDeploymentStyle.Encircled, EDeploymentStyle.BehindEnemyLines, EDeploymentStyle.LightningStrike,
            },
        };

        public static string Name(EDeploymentStyle style) => style switch
        {
            EDeploymentStyle.Core => "Core rules (12\" from your edge)",
            EDeploymentStyle.FrontLine => "Front Line",
            EDeploymentStyle.GroundWar => "Ground War",
            EDeploymentStyle.SideBattle => "Side Battle",
            EDeploymentStyle.Disordered => "Disordered",
            EDeploymentStyle.Spearhead => "Spearhead",
            EDeploymentStyle.OpposingForces => "Opposing Forces",
            EDeploymentStyle.NoMansLand => "No Man's Land",
            EDeploymentStyle.LongHaul => "Long Haul",
            EDeploymentStyle.FlankAssault => "Flank Assault",
            EDeploymentStyle.FrontalClash => "Frontal Clash",
            EDeploymentStyle.TacticalPush => "Tactical Push",
            EDeploymentStyle.MeetingEngagement => "Meeting Engagement",
            EDeploymentStyle.OpenWarzone => "Open Warzone",
            EDeploymentStyle.Pushback => "Pushback",
            EDeploymentStyle.Cornered => "Cornered",
            EDeploymentStyle.Encircled => "Encircled",
            EDeploymentStyle.BehindEnemyLines => "Behind Enemy Lines",
            EDeploymentStyle.LightningStrike => "Lightning Strike",
            EDeploymentStyle.RandomStandard => "Random Standard (roll)",
            EDeploymentStyle.RandomStrategic => "Random Strategic (roll)",
            EDeploymentStyle.RandomAsymmetric => "Random Asymmetric (roll)",
            _ => style.ToString(),
        };

        /// <summary>The style's two zones, A (top of the diagram) then B, on a table of the given size.</summary>
        public static (RectangularZone A, RectangularZone B) Zones(EDeploymentStyle style, float width, float height)
        {
            float sx = width / 48f, sz = height / 48f;
            RectangularZone R(float l, float r, float b, float t) => new(l * sx, r * sx, b * sz, t * sz);
            RectangularZone Rects(params (float l, float r, float b, float t)[] parts) =>
                MultiRectZone.Of(parts.Select(p => R(p.l, p.r, p.b, p.t)).ToList());
            RectangularZone Poly(params (float x, float z)[][] polys) =>
                PolygonZone.Of(polys.Select(p => p.Select(v => (v.x * sx, v.z * sz)).ToArray()).ToArray());

            switch (style)
            {
                // ── Standard ──
                case EDeploymentStyle.Core:
                case EDeploymentStyle.FrontLine:
                    return (R(0, 48, 36, 48), R(0, 48, 0, 12));
                case EDeploymentStyle.GroundWar:
                    return (Rects((0, 24, 36, 48), (0, 12, 24, 36)), Rects((24, 48, 0, 12), (36, 48, 12, 24)));
                case EDeploymentStyle.SideBattle:
                    return (Poly(new[] { (0f, 24f), (0f, 0f), (42f, 0f) }),
                            Poly(new[] { (6f, 48f), (48f, 48f), (48f, 24f) }));
                case EDeploymentStyle.Disordered:
                    return (Poly(new[] { (0f, 48f), (24f, 48f), (0f, 24f) }, new[] { (24f, 48f), (48f, 48f), (48f, 24f) }),
                            Poly(new[] { (0f, 24f), (0f, 0f), (24f, 0f) }, new[] { (48f, 24f), (24f, 0f), (48f, 0f) }));
                case EDeploymentStyle.Spearhead:
                    return (Poly(new[] { (0f, 48f), (48f, 48f), (24f, 36f) }),
                            Poly(new[] { (0f, 0f), (24f, 12f), (48f, 0f) }));
                case EDeploymentStyle.OpposingForces:
                    return (R(0, 24, 36, 48), R(24, 48, 0, 12));

                // ── Strategic ──
                case EDeploymentStyle.NoMansLand:
                    return (R(0, 48, 42, 48), R(0, 48, 0, 6));
                case EDeploymentStyle.LongHaul:
                    return (Poly(new[] { (0f, 48f), (24f, 48f), (0f, 24f) }, new[] { (24f, 48f), (48f, 48f), (48f, 24f) }),
                            Poly(new[] { (0f, 0f), (24f, 24f), (48f, 0f) }));
                case EDeploymentStyle.FlankAssault:
                    return (Rects((12, 36, 42, 48), (12, 36, 0, 6)), Rects((0, 6, 12, 36), (42, 48, 12, 36)));
                case EDeploymentStyle.FrontalClash:
                    return (Rects((0, 48, 42, 48), (12, 36, 36, 42)), Rects((0, 48, 0, 6), (12, 36, 6, 12)));
                case EDeploymentStyle.TacticalPush:
                    return (Rects((0, 48, 42, 48), (0, 12, 36, 42), (36, 48, 36, 42)),
                            Rects((0, 48, 0, 6), (0, 12, 6, 12), (36, 48, 6, 12)));
                case EDeploymentStyle.MeetingEngagement:
                    return (Rects((0, 12, 36, 48), (36, 48, 36, 48)), Rects((0, 12, 0, 12), (36, 48, 0, 12)));

                // ── Asymmetric ──
                case EDeploymentStyle.OpenWarzone:
                    return (R(0, 48, 30, 48), R(0, 48, 0, 6));
                case EDeploymentStyle.Pushback:
                    return (R(0, 48, 18, 30), Rects((0, 48, 42, 48), (0, 48, 0, 6)));
                case EDeploymentStyle.Cornered:
                    return (Rects((0, 48, 36, 48), (36, 48, 0, 36)), R(0, 12, 0, 12));
                case EDeploymentStyle.Encircled:
                    return (Rects((0, 48, 42, 48), (0, 48, 0, 6), (0, 6, 6, 42), (42, 48, 6, 42)), R(18, 30, 18, 30));
                case EDeploymentStyle.BehindEnemyLines:
                    return (Rects((0, 48, 36, 48), (0, 6, 0, 36), (42, 48, 0, 36)), R(18, 30, 0, 24));
                case EDeploymentStyle.LightningStrike:
                    return (Poly(new[] { (0f, 48f), (48f, 48f), (48f, 24f), (24f, 12f), (0f, 24f) }),
                            Poly(new[] { (0f, 12f), (0f, 0f), (24f, 0f) }, new[] { (48f, 12f), (24f, 0f), (48f, 0f) }));
            }
            throw new ArgumentOutOfRangeException(nameof(style), style, "A random style must be resolved first.");
        }
    }
}
