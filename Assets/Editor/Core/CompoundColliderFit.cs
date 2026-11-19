using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Conservative compound boxes fitted to a solid voxelization of a mesh. Greedy spatial splits
    /// spend the collider budget where they remove the most empty volume. All boxes share the mesh's
    /// fitted frame; this is an approximation, not a globally optimal convex decomposition.
    /// </summary>
    public static class CompoundColliderFit
    {
        public const int MaxColliders = 64;
        const int Resolution = 64;

        public struct Box
        {
            public Vector3 Center;
            public Quaternion Rotation;
            public Vector3 Size;
        }

        struct CellBounds
        {
            public Vector3Int Min, Max;
            public bool Valid;
            public float Volume => Valid ? (float)(Max.x - Min.x + 1) * (Max.y - Min.y + 1) * (Max.z - Min.z + 1) : 0f;

            public void Include(Vector3Int point)
            {
                if (!Valid) { Min = Max = point; Valid = true; }
                else { Min = Vector3Int.Min(Min, point); Max = Vector3Int.Max(Max, point); }
            }

            public void Include(CellBounds other)
            {
                if (!other.Valid) return;
                Include(other.Min);
                Include(other.Max);
            }
        }

        struct Split
        {
            public bool Valid;
            public int Axis, Cut, Length;
            public float Gain, Concentration, Balance;
        }

        sealed class Region
        {
            public readonly List<Vector3Int> Cells;
            public CellBounds Bounds;
            public Split Split;

            public Region(List<Vector3Int> cells)
            {
                Cells = cells;
                foreach (Vector3Int cell in cells) Bounds.Include(cell);
                Split = FindSplit(this);
            }
        }

        /// <summary>Returns exactly count boxes, in world space. Cancellation happens before any scene edits.</summary>
        public static Box[] Fit(Mesh mesh, Matrix4x4 localToWorld, int count, Func<float, bool> cancel = null)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (count < 1 || count > MaxColliders) throw new ArgumentOutOfRangeException(nameof(count));
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices.Length == 0 || triangles.Length < 3)
                throw new ArgumentException("The mesh has no triangles to fit.");
            Poll(cancel, 0f);

            // Fit the actual transformed geometry, including non-uniform and reflected parent scales.
            // Keeping translation separate avoids losing precision in the orientation search.
            Vector3 origin = localToWorld.MultiplyPoint3x4(Vector3.zero);
            for (int i = 0; i < vertices.Length; i++) vertices[i] = localToWorld.MultiplyVector(vertices[i]);
            var transformed = new Mesh { indexFormat = mesh.indexFormat, vertices = vertices, triangles = triangles };
            ShapeFit.Fit frame;
            try
            {
                if (!ShapeFit.TryFit(transformed, out frame)) throw new ArgumentException("The mesh could not be fitted.");
            }
            finally { UnityEngine.Object.DestroyImmediate(transformed); }

            Quaternion inverse = Quaternion.Inverse(frame.Rotation);
            var bounds = new Bounds(inverse * vertices[0], Vector3.zero);
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = inverse * vertices[i];
                bounds.Encapsulate(vertices[i]);
            }
            float span = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (span <= 1e-12f || float.IsNaN(span) || float.IsInfinity(span))
                throw new ArgumentException("The mesh has no finite size to fit.");
            if (count == 1)
                return new[] { new Box { Center = origin + frame.Rotation * bounds.center,
                    Rotation = frame.Rotation, Size = Vector3.Max(bounds.size, Vector3.one * span * 1e-5f) } };

            // Work in cell units. Two empty cells around the mesh let the outside flood reach every side.
            float cellSize = span / Resolution;
            Vector3 gridOrigin = bounds.min - Vector3.one * (2f * cellSize);
            Vector3Int dimensions = new Vector3Int(
                Mathf.CeilToInt(bounds.size.x / cellSize) + 4,
                Mathf.CeilToInt(bounds.size.y / cellSize) + 4,
                Mathf.CeilToInt(bounds.size.z / cellSize) + 4);
            for (int i = 0; i < vertices.Length; i++) vertices[i] = (vertices[i] - gridOrigin) / cellSize;
            byte[] grid = new byte[dimensions.x * dimensions.y * dimensions.z];
            Rasterize(vertices, triangles, dimensions, grid, cancel);
            Poll(cancel, .6f);
            FillSolid(dimensions, grid);
            var cells = new List<Vector3Int>();
            for (int z = 0; z < dimensions.z; z++)
                for (int y = 0; y < dimensions.y; y++)
                    for (int x = 0; x < dimensions.x; x++)
                        if (grid[Index(x, y, z, dimensions)] != 2) cells.Add(new Vector3Int(x, y, z));
            if (cells.Count < count) throw new ArgumentException("The mesh has too little geometry for that many colliders.");

            var regions = new List<Region> { new Region(cells) };
            while (regions.Count < count)
            {
                Poll(cancel, .65f + .35f * regions.Count / count);
                Region best = null;
                foreach (Region region in regions)
                    if (region.Split.Valid && (best == null || region.Split.Gain > best.Split.Gain ||
                        (region.Split.Gain == best.Split.Gain && region.Bounds.Volume > best.Bounds.Volume))) best = region;
                if (best == null) throw new ArgumentException("The shape cannot be divided into that many colliders.");
                var left = new List<Vector3Int>();
                var right = new List<Vector3Int>();
                foreach (Vector3Int cell in best.Cells)
                    (cell[best.Split.Axis] <= best.Split.Cut ? left : right).Add(cell);
                int at = regions.IndexOf(best);
                regions[at] = new Region(left);
                regions.Add(new Region(right));
            }
            Poll(cancel, 1f);
            return regions.Select(region =>
            {
                // Rasterization conservatively thickens the surface. Trim its outer margin to the
                // exact mesh bounds, especially important for thin plates and frame members.
                Vector3 min = Vector3.Max(bounds.min, gridOrigin + (Vector3)region.Bounds.Min * cellSize);
                Vector3 max = Vector3.Min(bounds.max, gridOrigin + (Vector3)(region.Bounds.Max + Vector3Int.one) * cellSize);
                return new Box
                {
                    Center = origin + frame.Rotation * ((min + max) * .5f),
                    Rotation = frame.Rotation,
                    Size = Vector3.Max(max - min, Vector3.one * span * 1e-5f)
                };
            }).ToArray();
        }

        static Split FindSplit(Region region)
        {
            var best = new Split();
            for (int axis = 0; axis < 3; axis++)
            {
                int start = region.Bounds.Min[axis], length = region.Bounds.Max[axis] - start + 1;
                if (length < 2) continue;
                var slices = new CellBounds[length];
                var counts = new int[length];
                foreach (Vector3Int cell in region.Cells)
                {
                    int slice = cell[axis] - start;
                    slices[slice].Include(cell);
                    counts[slice]++;
                }
                var suffix = new CellBounds[length];
                suffix[length - 1] = slices[length - 1];
                for (int i = length - 2; i >= 0; i--)
                {
                    suffix[i] = suffix[i + 1];
                    suffix[i].Include(slices[i]);
                }
                var prefix = new CellBounds();
                int leftCount = 0;
                for (int i = 0; i < length - 1; i++)
                {
                    prefix.Include(slices[i]);
                    leftCount += counts[i];
                    if (!prefix.Valid || !suffix[i + 1].Valid) continue;
                    float gain = region.Bounds.Volume - prefix.Volume - suffix[i + 1].Volume;
                    float leftEmpty = prefix.Volume - leftCount;
                    float rightEmpty = suffix[i + 1].Volume - (region.Cells.Count - leftCount);
                    // When several cuts remove the same volume, concentrate the remaining empty space
                    // in one child. This isolates a solid bar on a U/frame instead of bisecting its gap
                    // into two equally concave pieces that both need another collider to improve.
                    float concentration = leftEmpty * leftEmpty / prefix.Volume +
                        rightEmpty * rightEmpty / suffix[i + 1].Volume;
                    float balance = (float)Math.Min(leftCount, region.Cells.Count - leftCount) / region.Cells.Count;
                    if (!best.Valid || gain > best.Gain || (gain == best.Gain &&
                        (concentration > best.Concentration || (concentration == best.Concentration &&
                        (balance > best.Balance || (balance == best.Balance && length > best.Length))))))
                        best = new Split { Valid = true, Axis = axis, Cut = start + i,
                            Gain = gain, Concentration = concentration, Balance = balance, Length = length };
                }
            }
            return best;
        }

        static void Rasterize(Vector3[] vertices, int[] triangles, Vector3Int dimensions, byte[] grid, Func<float, bool> cancel)
        {
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                if (t % 768 == 0) Poll(cancel, .1f + .5f * t / triangles.Length);
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                Vector3Int min = Vector3Int.Max(Vector3Int.zero, Vector3Int.FloorToInt(Vector3.Min(a, Vector3.Min(b, c)) - Vector3.one * 1e-4f));
                Vector3Int max = Vector3Int.Min(dimensions - Vector3Int.one, Vector3Int.FloorToInt(Vector3.Max(a, Vector3.Max(b, c)) + Vector3.one * 1e-4f));
                for (int z = min.z; z <= max.z; z++)
                    for (int y = min.y; y <= max.y; y++)
                        for (int x = min.x; x <= max.x; x++)
                        {
                            int index = Index(x, y, z, dimensions);
                            if (grid[index] == 1) continue;
                            Vector3 center = new Vector3(x + .5f, y + .5f, z + .5f);
                            if (TriangleTouchesCell(a - center, b - center, c - center)) grid[index] = 1;
                        }
            }
        }

        // Separating-axis triangle/box intersection. The raster bounds already test the three box axes.
        static bool TriangleTouchesCell(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, bc = c - b, ca = a - c;
            if (Separated(Vector3.Cross(ab, bc), a, b, c)) return false;
            return !EdgeSeparates(ab, a, b, c) && !EdgeSeparates(bc, a, b, c) && !EdgeSeparates(ca, a, b, c);
        }

        static bool EdgeSeparates(Vector3 edge, Vector3 a, Vector3 b, Vector3 c)
        {
            return Separated(new Vector3(0, edge.z, -edge.y), a, b, c) ||
                Separated(new Vector3(-edge.z, 0, edge.x), a, b, c) ||
                Separated(new Vector3(edge.y, -edge.x, 0), a, b, c);
        }

        static bool Separated(Vector3 axis, Vector3 a, Vector3 b, Vector3 c)
        {
            float p = Vector3.Dot(a, axis), q = Vector3.Dot(b, axis), r = Vector3.Dot(c, axis);
            float radius = .5001f * (Mathf.Abs(axis.x) + Mathf.Abs(axis.y) + Mathf.Abs(axis.z));
            return Mathf.Min(p, Mathf.Min(q, r)) > radius || Mathf.Max(p, Mathf.Max(q, r)) < -radius;
        }

        static void FillSolid(Vector3Int dimensions, byte[] grid)
        {
            var queue = new int[grid.Length];
            int read = 0, write = 1;
            grid[0] = 2;
            while (read < write)
            {
                int index = queue[read++];
                int x = index % dimensions.x, y = index / dimensions.x % dimensions.y;
                int z = index / (dimensions.x * dimensions.y);
                if (x > 0) Visit(index - 1);
                if (x + 1 < dimensions.x) Visit(index + 1);
                if (y > 0) Visit(index - dimensions.x);
                if (y + 1 < dimensions.y) Visit(index + dimensions.x);
                if (z > 0) Visit(index - dimensions.x * dimensions.y);
                if (z + 1 < dimensions.z) Visit(index + dimensions.x * dimensions.y);
            }

            void Visit(int index)
            {
                if (grid[index] != 0) return;
                grid[index] = 2;
                queue[write++] = index;
            }
        }

        static int Index(int x, int y, int z, Vector3Int dimensions) => x + dimensions.x * (y + dimensions.y * z);

        static void Poll(Func<float, bool> cancel, float progress)
        {
            if (cancel != null && cancel(progress)) throw new OperationCanceledException();
        }
    }
}
