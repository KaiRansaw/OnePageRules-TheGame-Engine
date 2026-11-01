using FDG.Stages;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #419 - where the official AI puts an objective marker, from <b>Solo &amp; Co-Op Rules v3.6.0</b>:
    ///
    /// <para>
    /// "When setting up objectives first divide the area of the table where you are allowed to place
    /// objectives into <b>6 equal squares</b> [<b>4</b> in AoF: Skirmish]. When it's the AI's turn to place
    /// an objective first <b>roll for a random square</b>, and then place the objective <b>in the center of
    /// the square</b>. If that's not possible roll for another random square and <b>move the objective
    /// toward it just enough to be in a valid position</b>, whilst following the mission's objective set up
    /// rules."
    /// </para>
    /// <para>
    /// What it replaces: a random point mirrored across the table centre. That spread markers evenly, which
    /// is arguably better board design - and is not what the book says. The difference a player feels is
    /// clustering: squares can land two markers in the same half, which the mirrored version could never do,
    /// and the AI's own deployment then has to react to it.
    /// </para>
    /// <para>
    /// <b>Grid shape.</b> Six squares means 3 x 2 across the legal band, laid out along its longer axis;
    /// four means 2 x 2. "Squares" is the book's word for the division, not a promise that the cells are
    /// square - the legal band between two deployment zones is a wide, shallow strip in every mode the
    /// engine has, so its sixths are wide and shallow too.
    /// </para>
    /// </summary>
    public static class SoloCoOpObjectivePlacer
    {
        /// <summary>The centre of each cell, in roll order (1..<paramref name="squares"/>).</summary>
        public static List<Position> CellCentres(RectangularZone band, int squares)
        {
            (int cols, int rows) = Grid(band, squares);
            float left = band.Left;
            float bottom = band.Bottom;
            float cellW = (band.Right - band.Left) / cols;
            float cellH = (band.Top - band.Bottom) / rows;

            var centres = new List<Position>(cols * rows);
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < cols; col++)
                    centres.Add(new Position(
                        left + cellW * (col + 0.5f),
                        bottom + cellH * (row + 0.5f)));
            return centres;
        }

        /// <summary>
        /// How the cells are arranged: the longer axis gets the extra column, so a wide band is cut into
        /// 3 x 2 rather than 2 x 3 and the cells stay as close to square as the band allows.
        /// </summary>
        public static (int Cols, int Rows) Grid(RectangularZone band, int squares)
        {
            if (squares <= 1) return (1, 1);
            if (squares == 4) return (2, 2);

            // 6 (and anything else even) across the longer axis.
            int half = Math.Max(1, squares / 2);
            float width = band.Right - band.Left;
            float height = band.Top - band.Bottom;
            return width >= height ? (half, 2) : (2, half);
        }

        /// <summary>
        /// Picks a placement: a random cell's centre, and if that is not legal, another random cell with
        /// the marker moved toward it only as far as it must go to become legal.
        /// </summary>
        /// <param name="isLegal">The mission's own validator - separation from other markers, terrain, band.</param>
        /// <param name="roll">Rolls 1..n inclusive, the AI's own seeded stream.</param>
        public static Position Choose(RectangularZone band, int squares, Func<Position, bool> isLegal,
            Func<int, int> roll)
        {
            List<Position> centres = CellCentres(band, squares);
            if (centres.Count == 0)
                return new Position((band.Left + band.Right) / 2f, (band.Bottom + band.Top) / 2f);

            int first = Math.Clamp(roll(centres.Count), 1, centres.Count) - 1;
            if (isLegal(centres[first])) return centres[first];

            // "roll for another random square and move the objective toward it just enough to be in a
            // valid position". Walking out from where it already stands, toward the new cell, taking the
            // first legal point - which is what "just enough" means. Every cell is tried before giving up,
            // because a mission with tight separation rules can rule out most of them.
            var start = centres[first];
            for (int attempt = 0; attempt < centres.Count; attempt++)
            {
                int next = (Math.Clamp(roll(centres.Count), 1, centres.Count) - 1 + attempt) % centres.Count;
                if (TryWalkToward(start, centres[next], isLegal, out Position found)) return found;
            }

            // Nothing in the band worked. The caller's validator is the authority on what is legal, so
            // hand back the first cell's centre and let it fault with a message about the layout rather
            // than silently placing a marker somewhere the mission forbids.
            return start;
        }

        /// <summary>Steps from <paramref name="from"/> toward <paramref name="to"/>, returning the first legal point.</summary>
        private static bool TryWalkToward(Position from, Position to, Func<Position, bool> isLegal,
            out Position found)
        {
            const float StepInches = 1f;
            float dx = to.x - from.x, dz = to.z - from.z;
            float distance = MathF.Sqrt(dx * dx + dz * dz);
            found = to;
            if (distance < 0.01f) return isLegal(to);

            int steps = Math.Max(1, (int)(distance / StepInches));
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                var candidate = new Position(from.x + dx * t, from.z + dz * t);
                if (!isLegal(candidate)) continue;
                found = candidate;
                return true;
            }

            return false;
        }
    }
}
