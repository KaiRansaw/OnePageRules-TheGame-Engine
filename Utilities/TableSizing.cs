using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG
{
    /// <summary>
    /// How big the table is for a game, and the bigger tables a big game grows onto.
    ///
    /// <para>
    /// <b>Book part.</b> Every core book prints exactly one table: 6'x4' for Grimdark Future and Age of
    /// Fantasy, 4'x4' for Firefight, Skirmish and both Quest games (<see cref="GameWideConstants.TableSizeFor"/>).
    /// The Advanced Rulebooks name a "full match" size for each: 2000pts GF, 1500pts AoF, 800pts Firefight,
    /// 600pts Skirmish (each Advanced Rulebook v3.6.0: "you can start playing full matches with 2000pts armies each"). No book prints a table
    /// larger than 6'x4'.
    /// </para>
    ///
    /// <para>
    /// <b>House rule (Kai, 2026-11-08):</b> "larger maps for personal choice ... even up to 10k points ...
    /// want the maps to reflect their size." Above the full-match size the table grows so the space per
    /// point stays what the book's table gives a full match: area x (points / full-match points), picked
    /// from <see cref="Sizes"/>. Depth is capped at 6' on purpose - the deployment zones sit 12" from the
    /// long edges whatever the table, so every extra foot of depth is a foot of no-man's-land the armies
    /// must cross inside a 4-round game; width carries the rest. Horde and the Quest games keep their book
    /// table: their geometry is fixed round the centre and corners and their points are fixed by the book.
    /// </para>
    ///
    /// <para>
    /// A map always plays on the table it was drawn for (the B56 gap, closed): with Load From File the
    /// map's own size wins. Otherwise a size picked in the lobby wins, and otherwise the automatic size.
    /// </para>
    /// </summary>
    public static class TableSizing
    {
        /// <summary>Every table size offered, in feet (width x depth), smallest first. The two book sizes
        /// (4'x4', 6'x4') are members; everything else is the house rule above.</summary>
        public static readonly IReadOnlyList<(float W, float H)> SizesFeet = new (float, float)[]
        {
            (4, 4), (5, 4), (6, 4), (7, 4.5f), (8, 4.5f), (9, 5), (10, 5), (12, 5), (12, 6), (14, 6),
            (16, 6), (18, 6), (20, 6), (24, 6), (28, 6), (32, 6), (36, 6),
        };

        /// <summary><see cref="SizesFeet"/> in inches, which is what the table is measured in.</summary>
        public static IEnumerable<(float W, float H)> Sizes => SizesFeet.Select(s => (s.W * 12f, s.H * 12f));

        /// <summary>The book's "full match" army size for a system, or null where the table never grows
        /// (both Quest games).</summary>
        public static int? FullMatchPoints(EGameSystem system) => system switch
        {
            EGameSystem.GrimdarkFuture => 2000,
            EGameSystem.AgeOfFantasy => 1500,
            EGameSystem.GFFirefight => 800,
            EGameSystem.AoFSkirmish => 600,
            _ => null,
        };

        /// <summary>Whether a game of this system and mode can be played on anything but the book table.</summary>
        public static bool CanGrow(EGameSystem system, EGameMode mode) =>
            FullMatchPoints(system) is not null && !GameSystemInfo.IsHordeLike(mode);

        /// <summary>The sizes a game of this system can be played on: its book table and everything bigger.</summary>
        public static IReadOnlyList<(float W, float H)> SizesFor(EGameSystem system)
        {
            (float bw, float bh) = GameWideConstants.TableSizeFor(system);
            return Sizes.Where(s => s.W >= bw - 0.01f && s.H >= bh - 0.01f).ToList();
        }

        /// <summary>
        /// The table an army of <paramref name="points"/> per player plays on, by the rule above. At or
        /// below the full-match size it is the book table.
        /// </summary>
        public static (float W, float H) AutoSize(EGameSystem system, EGameMode mode, int points)
        {
            (float bw, float bh) = GameWideConstants.TableSizeFor(system);
            if (!CanGrow(system, mode) || FullMatchPoints(system) is not int full || points <= full)
                return (bw, bh);

            float wantedArea = bw * bh * points / (float)full;
            IReadOnlyList<(float W, float H)> sizes = SizesFor(system);
            foreach ((float w, float h) in sizes)
                if (w * h >= wantedArea - 0.01f)
                    return (w, h);
            return sizes[^1];
        }

        /// <summary>
        /// The table a game is actually played on: the chosen map's own size (Load From File), else a size
        /// picked in the lobby, else <see cref="AutoSize"/>.
        /// </summary>
        public static (float W, float H) Resolve(GameSettings settings)
        {
            if (settings.TerrainPlacementMode == ETerrainPlacementMode.LoadFromFile
                && settings.MapTableWidthInches is > 0f && settings.MapTableHeightInches is > 0f)
                return (settings.MapTableWidthInches.Value, settings.MapTableHeightInches.Value);
            if (settings.TableWidthInches is > 0f && settings.TableHeightInches is > 0f)
                return (settings.TableWidthInches.Value, settings.TableHeightInches.Value);
            return AutoSize(settings.GameSystem, settings.GameMode, settings.ArmyPoints);
        }

        /// <summary>
        /// The army size (per player, rounded to 250 pts) a table suits under the automatic rule, or null when
        /// the system's table never grows or the table is smaller than its book table.
        /// </summary>
        public static int? SuggestedPoints(EGameSystem system, float widthInches, float heightInches)
        {
            if (FullMatchPoints(system) is not int full) return null;
            (float bw, float bh) = GameWideConstants.TableSizeFor(system);
            if (widthInches < bw - 0.01f || heightInches < bh - 0.01f) return null;
            float points = full * (widthInches * heightInches) / (bw * bh);
            return (int)(MathF.Round(points / 250f) * 250f);
        }

        /// <summary>"9' x 5'" - game text, ASCII only.</summary>
        public static string Label(float widthInches, float heightInches) =>
            $"{widthInches / 12f:0.#}' x {heightInches / 12f:0.#}'";

        /// <summary>Whether two sizes are the same table.</summary>
        public static bool Same((float W, float H) a, (float W, float H) b) =>
            MathF.Abs(a.W - b.W) < 0.01f && MathF.Abs(a.H - b.H) < 0.01f;
    }
}
