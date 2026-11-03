using System;
using System.Collections.Generic;

namespace FDG
{
    /// <summary>
    /// The single home for "arrange these models into rows" geometry (#277). Generates the candidate
    /// row partitions a unit can form up in (line, 5x2, 4-3-3, ...), lays any partition out from
    /// per-model footprint extents, and filters the catalog to shapes that satisfy the 9" all-pairs
    /// coherency span. CohesiveFormation.PackGrid and the GUI group placement/movement resolvers all
    /// lay rows out through <see cref="LayoutOffsets"/>, so row spacing / cohesion conventions live in
    /// exactly one place. All values are inches on the x/z plane; row 0 is the FRONT row (+z side),
    /// rows stack toward -z, and offsets are measured from the formation centroid.
    /// </summary>
    public static class FormationLibrary
    {
        /// <summary>A candidate formation shape: row sizes front-to-back plus a display name.</summary>
        public readonly struct Formation
        {
            /// <summary>Models per row, front row first. Sums to the unit's model count.</summary>
            public readonly int[] RowCounts;
            /// <summary>Display name: "line (10)", "5x2", "4-3-3". ASCII only (game-text rule).</summary>
            public readonly string Name;

            public Formation(int[] rowCounts, string name)
            {
                RowCounts = rowCounts;
                Name = name;
            }
        }

        /// <summary>
        /// The candidate row partitions for <paramref name="count"/> models, shallowest first: the
        /// single line, then balanced splits into 2, 3, ... rows down to rows of pairs. Rows within a
        /// partition differ by at most one model (longer rows first, i.e. at the front). Partitions
        /// that would leave a row with a single model are skipped — a lone model has no in-row
        /// neighbour for the 1" rule, which mixed base sizes can't always bridge cross-row (#159).
        /// </summary>
        public static List<int[]> RowPartitions(int count)
        {
            var partitions = new List<int[]>();
            if (count <= 0) return partitions;

            for (int rows = 1; rows <= Math.Max(1, count / 2); rows++)
            {
                int baseSize = count / rows, remainder = count % rows;
                if (rows > 1 && baseSize < 2) break; // rows would drop below pairs from here on
                var rowCounts = new int[rows];
                for (int r = 0; r < rows; r++) rowCounts[r] = baseSize + (r < remainder ? 1 : 0);
                partitions.Add(rowCounts);
            }
            return partitions;
        }

        /// <summary>Display name for a partition: "line (10)" for one row, "5x2" when all rows are
        /// equal, otherwise "4-3-3". ASCII only.</summary>
        public static string Describe(IReadOnlyList<int> rowCounts)
        {
            if (rowCounts.Count == 1) return $"line ({rowCounts[0]})";
            bool allEqual = true;
            for (int r = 1; r < rowCounts.Count; r++)
                if (rowCounts[r] != rowCounts[0]) { allEqual = false; break; }
            if (allEqual) return $"{rowCounts[0]}x{rowCounts.Count}";
            return string.Join("-", rowCounts);
        }

        /// <summary>
        /// Lays models out in the given rows and returns each model's offset from the formation
        /// centroid. Index k fills the rows in order (row 0 gets indices 0..RowCounts[0]-1 left to
        /// right). Within a row the bases sit edge-to-edge <paramref name="gap"/>" apart at their OWN
        /// X extent; rows are stacked by their tallest model's Z extent + <paramref name="gap"/> — the
        /// same per-model spacing PackGrid uses, so any base-size mix satisfies the 1" rule (#159).
        /// </summary>
        public static (float dx, float dz)[] LayoutOffsets(
            IReadOnlyList<float> halfXs, IReadOnlyList<float> halfZs, IReadOnlyList<int> rowCounts, float gap)
        {
            int n = halfXs.Count;
            var offsets = new (float dx, float dz)[n];
            if (n == 0) return offsets;

            int rows = rowCounts.Count;
            var rowHalfHeight = new float[rows];
            var rowWidth = new float[rows];
            int idx = 0;
            for (int row = 0; row < rows; row++)
            {
                float maxHalfZ = 0f, width = 0f;
                for (int c = 0; c < rowCounts[row]; c++, idx++)
                {
                    if (halfZs[idx] > maxHalfZ) maxHalfZ = halfZs[idx];
                    width += 2f * halfXs[idx];
                }
                rowHalfHeight[row] = maxHalfZ;
                rowWidth[row] = width + (rowCounts[row] - 1) * gap;
            }

            float totalHeight = (rows - 1) * gap;
            for (int row = 0; row < rows; row++) totalHeight += 2f * rowHalfHeight[row];

            idx = 0;
            float zCursor = totalHeight / 2f; // front row at the top (+z), stacking downward
            for (int row = 0; row < rows; row++)
            {
                float rowCenterZ = zCursor - rowHalfHeight[row];
                float xCursor = -rowWidth[row] / 2f;
                for (int c = 0; c < rowCounts[row]; c++, idx++)
                {
                    offsets[idx] = (xCursor + halfXs[idx], rowCenterZ);
                    xCursor += 2f * halfXs[idx] + gap;
                }
                zCursor -= 2f * rowHalfHeight[row] + gap;
            }

            Recenter(offsets);
            return offsets;
        }

        /// <summary>
        /// The formations this unit may legally adopt: every <see cref="RowPartitions"/> candidate whose
        /// laid-out shape keeps all pairs within <paramref name="maxPairwiseInches"/> base-to-base
        /// (approximated by the circumscribing <paramref name="radii"/> — conservative for rectangles).
        /// A 10-model line that would span more than 9" simply isn't offered. The 1" nearest-neighbour
        /// rule is satisfied by construction (rows pack at <paramref name="gap"/>").
        /// </summary>
        public static List<Formation> LegalFormations(
            IReadOnlyList<float> halfXs, IReadOnlyList<float> halfZs, IReadOnlyList<float> radii,
            float gap, float maxPairwiseInches)
        {
            var legal = new List<Formation>();
            foreach (int[] rowCounts in RowPartitions(halfXs.Count))
            {
                var offsets = LayoutOffsets(halfXs, halfZs, rowCounts, gap);
                if (MaxPairwiseBaseToBase(offsets, radii) <= maxPairwiseInches)
                    legal.Add(new Formation(rowCounts, Describe(rowCounts)));
            }
            return legal;
        }

        /// <summary>
        /// Lays the partition out AND assigns models to slots so that the FURTHEST any single model
        /// has to walk is as short as possible - see <see cref="MinimiseTheWorstWalk"/> for why that is
        /// the quantity that decides whether a group move is legal at all. Returns each MODEL's offset from the formation
        /// centroid, in the input model order. Extents are permuted with the assignment before the
        /// final layout, so a mixed-base unit still gets per-model row spacing. For unplaced models
        /// (all at the same point) the assignment degenerates to input order.
        /// </summary>
        public static (float dx, float dz)[] PlanFormationOffsets(
            IReadOnlyList<Position> currentPositions,
            IReadOnlyList<float> halfXs, IReadOnlyList<float> halfZs,
            IReadOnlyList<int> rowCounts, float gap)
        {
            int n = halfXs.Count;
            if (n == 0) return Array.Empty<(float, float)>();

            float cx = 0f, cz = 0f;
            for (int i = 0; i < n; i++) { cx += currentPositions[i].x; cz += currentPositions[i].z; }
            cx /= n; cz /= n;

            // Assignment and spacing depend on each other when bases differ in size: a row's height is
            // its TALLEST model and each model sits at its OWN width, so moving a big model into a row
            // moves that row's slots. Assigning once against provisional (input-order) slots therefore
            // optimises against geometry that the assignment itself then changes - measurably: a mixed
            // base six-model unit came out walking 3.95" where 3.69" was achievable.
            //
            // So: assign, re-space with the assigned extents, and assign again on the slots that
            // actually resulted. It converges in a round or two (the slots stop moving once the
            // assignment stops changing); the cap is there so it cannot oscillate forever between two
            // equally good answers.
            var slotOffsets = LayoutOffsets(halfXs, halfZs, rowCounts, gap);
            int[] modelForSlot = Enumerable.Range(0, n).ToArray();
            var permutedHalfXs = new float[n];
            var permutedHalfZs = new float[n];

            const int maxRounds = 4;
            for (int round = 0; round < maxRounds; round++)
            {
                var cost = new float[n, n];       // [slot, model] - squared distance, monotone in distance
                for (int k = 0; k < n; k++)
                {
                    float slotX = cx + slotOffsets[k].dx, slotZ = cz + slotOffsets[k].dz;
                    for (int m = 0; m < n; m++)
                    {
                        float dx = currentPositions[m].x - slotX, dz = currentPositions[m].z - slotZ;
                        cost[k, m] = dx * dx + dz * dz;
                    }
                }

                int[] next = MinimiseTheWorstWalk(cost, n);

                for (int k = 0; k < n; k++)
                {
                    permutedHalfXs[k] = halfXs[next[k]];
                    permutedHalfZs[k] = halfZs[next[k]];
                }
                var nextOffsets = LayoutOffsets(permutedHalfXs, permutedHalfZs, rowCounts, gap);

                bool settled = round > 0 && next.AsSpan().SequenceEqual(modelForSlot);
                modelForSlot = next;
                slotOffsets = nextOffsets;
                if (settled) break;
            }

            var byModel = new (float dx, float dz)[n];
            for (int k = 0; k < n; k++) byModel[modelForSlot[k]] = slotOffsets[k];
            return byModel;
        }

        /// <summary>
        /// Who stands where: the assignment of models to formation slots that minimises <b>the furthest
        /// any single model has to walk</b>.
        ///
        /// <para>
        /// That is the right thing to minimise, and it is not obvious. OPR measures movement <b>per
        /// model</b>, so a group move is legal only if EVERY model reaches its slot inside the unit's
        /// allowance - the move is decided by its worst-off model, not by the total walked or by the
        /// unit's centre. Minimise the total and you will happily walk one model 8" to save four models
        /// an inch each, and the whole move is then rejected.
        /// </para>
        ///
        /// <para>
        /// This replaces a greedy nearest-model pass (slot 0 took the closest unused model, then slot 1,
        /// and so on). Greedy is optimal for nothing: an early slot takes a model a later slot needed,
        /// and the last slot is left with whoever is furthest away. Kai reported it from play on
        /// 2026-11-02 - "the unit CAN make the move, but certain models can't because their spot in the
        /// formation is weird" - and the tests measure it: a six-model unit re-forming had its worst
        /// model walking 6.13" where 3.8" was achievable.
        /// </para>
        ///
        /// <para>
        /// Method: binary search the threshold over the distinct costs, and at each one ask whether a
        /// perfect matching exists using only pairs within it (Kuhn's augmenting paths). The smallest
        /// threshold that still matches everybody IS the best achievable worst walk. Then a bounded
        /// swap pass shortens the total among the assignments that tie at that threshold, so the
        /// unit does not wander while respecting the limit.
        /// </para>
        ///
        /// <para>
        /// Costs are SQUARED distances. Squaring is monotone, so the ordering - and therefore the
        /// chosen threshold - is identical to using real distances, with no square roots in the inner
        /// loop. Units are small (n is a squad), so the O(n^3 log n) worst case is microseconds.
        /// </para>
        /// </summary>
        /// <param name="cost">[slot, model] cost of sending that model to that slot.</param>
        /// <returns>For each slot, the index of the model that should stand in it.</returns>
        internal static int[] MinimiseTheWorstWalk(float[,] cost, int n)
        {
            var thresholds = new List<float>(n * n);
            for (int k = 0; k < n; k++)
                for (int m = 0; m < n; m++)
                    thresholds.Add(cost[k, m]);
            thresholds.Sort();

            // Smallest threshold that still lets everybody be matched.
            int lo = 0, hi = thresholds.Count - 1, bestIndex = thresholds.Count - 1;
            int[]? bestMatch = null;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                int[]? match = TryMatchWithin(cost, n, thresholds[mid]);
                if (match != null)
                {
                    bestIndex = mid;
                    bestMatch = match;
                    hi = mid - 1;
                }
                else lo = mid + 1;
            }

            // The search always succeeds at the largest threshold (every pair is allowed), so a null
            // here would mean the cost matrix was not square. Fall back to the identity rather than
            // throwing: a formation that is merely untidy beats a crash mid-move.
            int[] result = bestMatch ?? Enumerable.Range(0, n).ToArray();

            ShortenTotalWithoutWalkingAnyoneFurther(cost, n, result, thresholds[bestIndex]);
            return result;
        }

        /// <summary>
        /// Kuhn's algorithm: can every slot be given a model using only pairs costing at most
        /// <paramref name="limit"/>? Returns the assignment, or null if somebody would be left out.
        /// </summary>
        private static int[]? TryMatchWithin(float[,] cost, int n, float limit)
        {
            var modelForSlot = new int[n];
            var slotForModel = new int[n];
            Array.Fill(modelForSlot, -1);
            Array.Fill(slotForModel, -1);

            var seen = new bool[n];
            for (int slot = 0; slot < n; slot++)
            {
                Array.Clear(seen);
                if (!Augment(cost, n, limit, slot, seen, modelForSlot, slotForModel)) return null;
            }
            return modelForSlot;
        }

        private static bool Augment(float[,] cost, int n, float limit, int slot, bool[] seen,
            int[] modelForSlot, int[] slotForModel)
        {
            for (int model = 0; model < n; model++)
            {
                if (seen[model] || cost[slot, model] > limit) continue;
                seen[model] = true;

                // Free, or its current slot can be re-housed somewhere else within the limit.
                if (slotForModel[model] == -1
                    || Augment(cost, n, limit, slotForModel[model], seen, modelForSlot, slotForModel))
                {
                    modelForSlot[slot] = model;
                    slotForModel[model] = slot;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Many assignments tie at the best worst-walk. Among those, prefer the one where the unit
        /// walks less in total - otherwise models that could have stayed put take a stroll for no
        /// reason, which looks like a bug even when the move is legal.
        ///
        /// <para>
        /// A bounded pass of pairwise swaps, taking any swap that shortens the total without pushing
        /// either model past <paramref name="limit"/>. Not guaranteed optimal, and deliberately so: the
        /// number that governs legality is already optimal, and this is tidiness. The pass count is
        /// capped so a pathological case cannot spin.
        /// </para>
        /// </summary>
        private static void ShortenTotalWithoutWalkingAnyoneFurther(float[,] cost, int n,
            int[] modelForSlot, float limit)
        {
            const int maxPasses = 4;
            for (int pass = 0; pass < maxPasses; pass++)
            {
                bool improved = false;
                for (int a = 0; a < n; a++)
                {
                    for (int b = a + 1; b < n; b++)
                    {
                        int ma = modelForSlot[a], mb = modelForSlot[b];
                        float now = cost[a, ma] + cost[b, mb];
                        float swapped = cost[a, mb] + cost[b, ma];
                        if (swapped >= now) continue;
                        if (cost[a, mb] > limit || cost[b, ma] > limit) continue;

                        modelForSlot[a] = mb;
                        modelForSlot[b] = ma;
                        improved = true;
                    }
                }
                if (!improved) break;
            }
        }

        /// <summary>Largest base-to-base distance between any two laid-out models, by circumscribing radii.</summary>
        private static float MaxPairwiseBaseToBase((float dx, float dz)[] offsets, IReadOnlyList<float> radii)
        {
            float max = 0f;
            for (int i = 0; i < offsets.Length; i++)
                for (int j = i + 1; j < offsets.Length; j++)
                {
                    float dx = offsets[i].dx - offsets[j].dx, dz = offsets[i].dz - offsets[j].dz;
                    float b2b = MathF.Sqrt(dx * dx + dz * dz) - radii[i] - radii[j];
                    if (b2b > max) max = b2b;
                }
            return max;
        }

        /// <summary>Shifts all offsets so the formation centroid sits at (0,0).</summary>
        private static void Recenter((float dx, float dz)[] offsets)
        {
            if (offsets.Length == 0) return;
            float sx = 0f, sz = 0f;
            for (int i = 0; i < offsets.Length; i++) { sx += offsets[i].dx; sz += offsets[i].dz; }
            float cx = sx / offsets.Length, cz = sz / offsets.Length;
            for (int i = 0; i < offsets.Length; i++) offsets[i] = (offsets[i].dx - cx, offsets[i].dz - cz);
        }
    }
}
