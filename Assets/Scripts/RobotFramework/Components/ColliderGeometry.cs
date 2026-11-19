#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace RobotFramework.Components
{
    // Geometry toolkit for PerTriangleColliderBuilder. Nothing here decides anything - it's the
    // mesh plumbing and the textbook primitives, kept out of the algorithm file so that file
    // stays readable top-to-bottom. Add to this when a new algorithm needs a new primitive;
    // don't put policy (thresholds, accept/reject rules, tuning) in here.
    //
    // THE ONE RULE THAT BITES EVERYONE: robot parts sit under parents with a large uniform
    // scale, so local vertex coordinates are routinely sub-millimetre. Unity's Vector3.normalized
    // has a fixed ~1e-5 ABSOLUTE epsilon and silently returns zero below it - hence
    // GeometryUtil.Normalized, and hence every tolerance here being relative to the geometry's
    // own size rather than a constant. Any new math you add has to hold to that.

    // A source mesh flattened into plain arrays, with the index arithmetic done for you.
    // Triangle t occupies Indices[t*3 .. t*3+2]; everything downstream refers to triangles by
    // that index and vertices by their index into Vertices.
    internal readonly struct SourceGeometry
    {
        public readonly Vector3[] Vertices;
        public readonly int[] Indices;

        // How many triangles TryRead dropped as duplicates. Worth surfacing rather than hiding:
        // a non-zero count means the source mesh has coincident geometry, which is usually a
        // modelling accident worth knowing about.
        public readonly int DuplicatesRemoved;

        private SourceGeometry(Vector3[] vertices, int[] indices, int duplicatesRemoved)
        {
            Vertices = vertices;
            Indices = indices;
            DuplicatesRemoved = duplicatesRemoved;
        }

        public int TriangleCount => Indices.Length / 3;

        public void GetTriangleIndices(int triangle, out int i0, out int i1, out int i2)
        {
            int i = triangle * 3;
            i0 = Indices[i];
            i1 = Indices[i + 1];
            i2 = Indices[i + 2];
        }

        public void GetTriangle(int triangle, out Vector3 a, out Vector3 b, out Vector3 c)
        {
            GetTriangleIndices(triangle, out int i0, out int i1, out int i2);
            a = Vertices[i0];
            b = Vertices[i1];
            c = Vertices[i2];
        }

        // Cross product of two edges: direction is the face normal, magnitude is twice the
        // triangle's area. Summing these over a group gives an area-weighted normal for free.
        public Vector3 TriangleCross(int triangle)
        {
            GetTriangle(triangle, out Vector3 a, out Vector3 b, out Vector3 c);
            return Vector3.Cross(b - a, c - a);
        }

        public float TriangleArea(int triangle) => TriangleCross(triangle).magnitude * 0.5f;

        // Same mesh, different geometry - used by MeshDecimation to hand back a simplified copy
        // without losing what was already learned about the original.
        public SourceGeometry Replacing(Vector3[] vertices, int[] indices)
        {
            return new SourceGeometry(vertices, indices, DuplicatesRemoved);
        }

        // AcquireReadOnlyMeshData rather than mesh.vertices/mesh.triangles: no managed copy, and
        // it works on meshes imported without "Read/Write Enabled", so this needs no importer
        // changes on any part it's used with. Submeshes are concatenated - only geometry matters.
        public static bool TryRead(Mesh mesh, out SourceGeometry geometry)
        {
            geometry = default;
            if (mesh == null) return false;

            Mesh.MeshDataArray dataArray = Mesh.AcquireReadOnlyMeshData(mesh);
            try
            {
                if (dataArray.Length == 0) return false;
                Mesh.MeshData data = dataArray[0];

                Vector3[] vertices;
                using (NativeArray<Vector3> nativeVerts = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                {
                    data.GetVertices(nativeVerts);
                    vertices = nativeVerts.ToArray();
                }

                List<int> indices = new List<int>();
                for (int sm = 0; sm < data.subMeshCount; sm++)
                {
                    using NativeArray<int> subIndices = new NativeArray<int>((int)data.GetSubMesh(sm).indexCount, Allocator.Temp);
                    data.GetIndices(subIndices, sm);
                    indices.AddRange(subIndices.ToArray());
                }

                int[] triangles = Deduplicate(vertices, indices, out int duplicatesRemoved);
                geometry = new SourceGeometry(vertices, triangles, duplicatesRemoved);
                return true;
            }
            finally
            {
                dataArray.Dispose();
            }
        }

        // Drops any triangle covering ground an earlier one already covered. Duplicates reach us
        // from double-sided modelling, overlapping submeshes (all submeshes are concatenated
        // above) and plain copy-paste accidents, and they do more harm than one wasted collider:
        // a duplicated triangle makes each of its edges look non-manifold, so FeatureWeights
        // reads a 180 degree crease there and the partition fractures around geometry that is
        // actually flat.
        //
        // Triangles are compared by the POSITIONS of their corners (see WeldPositions), not by
        // vertex index - a mesh exported with split vertices gives a duplicated face entirely
        // different indices, so index comparison would miss duplicates in the common case.
        //
        // The corner triple is sorted before comparing, so a duplicate wound the other way round
        // (the usual way a double-sided surface is built) counts as the same triangle. For
        // collision that's correct: one prism has thickness and covers both facings at once.
        private static int[] Deduplicate(Vector3[] vertices, List<int> indices, out int duplicatesRemoved)
        {
            GeometryUtil.WeldPositions(vertices, out int[] atPosition);

            HashSet<(int, int, int)> seen = new HashSet<(int, int, int)>();
            List<int> kept = new List<int>(indices.Count);
            duplicatesRemoved = 0;

            for (int i = 0; i < indices.Count; i += 3)
            {
                int a = atPosition[indices[i]];
                int b = atPosition[indices[i + 1]];
                int c = atPosition[indices[i + 2]];

                if (a > b) (a, b) = (b, a);
                if (b > c) (b, c) = (c, b);
                if (a > b) (a, b) = (b, a);

                if (!seen.Add((a, b, c)))
                {
                    duplicatesRemoved++;
                    continue;
                }

                kept.Add(indices[i]);
                kept.Add(indices[i + 1]);
                kept.Add(indices[i + 2]);
            }

            return kept.ToArray();
        }
    }

    // "Which triangles share an edge with this one", precomputed. Built over a caller-supplied
    // subset so excluded (e.g. degenerate) triangles can't be walked into.
    //
    // Note this is EDGE adjacency by vertex index - two triangles that touch in space but were
    // authored with separate vertices (a split UV seam, a hard normal edge) are NOT neighbours
    // here. If a future algorithm needs to bridge those, weld positions first rather than
    // loosening this.
    internal sealed class TriangleAdjacency
    {
        // One mesh edge and the triangles using it: 2 on a normal interior edge, 1 on an open
        // boundary, more on non-manifold geometry.
        public readonly struct Edge
        {
            public readonly int VertexA;
            public readonly int VertexB;
            public readonly List<int> Triangles;

            public Edge(int vertexA, int vertexB, List<int> triangles)
            {
                VertexA = vertexA;
                VertexB = vertexB;
                Triangles = triangles;
            }
        }

        private readonly Dictionary<long, List<int>> edgeToTriangles = new();
        private readonly SourceGeometry geometry;

        private TriangleAdjacency(SourceGeometry geometry)
        {
            this.geometry = geometry;
        }

        public static TriangleAdjacency Build(SourceGeometry geometry, bool[] include)
        {
            TriangleAdjacency adjacency = new TriangleAdjacency(geometry);

            for (int t = 0; t < geometry.TriangleCount; t++)
            {
                if (!include[t]) continue;

                geometry.GetTriangleIndices(t, out int i0, out int i1, out int i2);
                adjacency.AddEdge(i0, i1, t);
                adjacency.AddEdge(i1, i2, t);
                adjacency.AddEdge(i2, i0, t);
            }

            return adjacency;
        }

        public IEnumerable<Edge> Edges
        {
            get
            {
                foreach (KeyValuePair<long, List<int>> entry in edgeToTriangles)
                {
                    yield return new Edge((int)(entry.Key >> 32), (int)(entry.Key & 0xFFFFFFFF), entry.Value);
                }
            }
        }

        public IEnumerable<int> Neighbours(int triangle)
        {
            geometry.GetTriangleIndices(triangle, out int i0, out int i1, out int i2);

            foreach (int t in Across(i0, i1, triangle)) yield return t;
            foreach (int t in Across(i1, i2, triangle)) yield return t;
            foreach (int t in Across(i2, i0, triangle)) yield return t;
        }

        private IEnumerable<int> Across(int va, int vb, int exclude)
        {
            if (!edgeToTriangles.TryGetValue(PackEdge(va, vb), out List<int> triangles)) yield break;

            foreach (int t in triangles)
            {
                if (t != exclude) yield return t;
            }
        }

        private void AddEdge(int va, int vb, int triangle)
        {
            long key = PackEdge(va, vb);
            if (!edgeToTriangles.TryGetValue(key, out List<int> triangles))
            {
                triangles = new List<int>();
                edgeToTriangles[key] = triangles;
            }
            triangles.Add(triangle);
        }

        // Two vertex indices as one order-independent 64-bit key (A->B is the same edge as B->A).
        private static long PackEdge(int va, int vb)
        {
            return ((long)Mathf.Min(va, vb) << 32) | (uint)Mathf.Max(va, vb);
        }
    }

    // How much each vertex matters to the part's collision shape, as a 0..1 score. 1 means the
    // vertex sits on a feature the collision surface is defined by; 0 means it's in the middle of
    // a flat span where its exact position barely matters.
    //
    // Two things score: a SHARP DIHEDRAL (adjacent faces meeting at an angle - the creases that
    // give a part its silhouette) and an OPEN BOUNDARY (an edge used by exactly one triangle -
    // the rim where a shell mesh ends, which robot parts have constantly). Boundaries score a
    // flat 1; dihedrals ramp linearly to 1 at FeatureAngle, so gentle bends carry partial weight
    // instead of being invisible until they cross a threshold.
    //
    // The score is a PRIORITY, not a constraint - it decides what patches grow toward and which
    // vertices dominate a fitted plane. It never authorises covering less of the mesh.
    internal sealed class FeatureWeights
    {
        private readonly float[] vertexWeights;
        private readonly float[] triangleWeights;

        private FeatureWeights(float[] vertexWeights, float[] triangleWeights)
        {
            this.vertexWeights = vertexWeights;
            this.triangleWeights = triangleWeights;
        }

        public float Vertex(int vertexIndex) => vertexWeights[vertexIndex];

        // A triangle is as much of a feature as its most significant corner: touching one crease
        // is enough to make it a boundary a patch should stop at rather than grow through.
        public float Triangle(int triangle) => triangleWeights[triangle];

        public static FeatureWeights Build(SourceGeometry geometry, TriangleAdjacency adjacency, Vector3[] faceNormals, float featureAngle)
        {
            float[] vertexWeights = new float[geometry.Vertices.Length];
            float safeAngle = Mathf.Max(featureAngle, 1e-3f);

            foreach (TriangleAdjacency.Edge edge in adjacency.Edges)
            {
                float weight;
                if (edge.Triangles.Count == 1)
                {
                    weight = 1f; // open rim: nothing on the far side to blend with
                }
                else
                {
                    // Widest disagreement across the edge. Non-manifold edges (3+ faces) are
                    // covered by the same pairwise scan and land at or near 1.
                    float widest = 0f;
                    for (int i = 0; i < edge.Triangles.Count; i++)
                    {
                        for (int j = i + 1; j < edge.Triangles.Count; j++)
                        {
                            widest = Mathf.Max(widest, Vector3.Angle(faceNormals[edge.Triangles[i]], faceNormals[edge.Triangles[j]]));
                        }
                    }
                    weight = Mathf.Clamp01(widest / safeAngle);
                }

                vertexWeights[edge.VertexA] = Mathf.Max(vertexWeights[edge.VertexA], weight);
                vertexWeights[edge.VertexB] = Mathf.Max(vertexWeights[edge.VertexB], weight);
            }

            float[] triangleWeights = new float[geometry.TriangleCount];
            for (int t = 0; t < triangleWeights.Length; t++)
            {
                geometry.GetTriangleIndices(t, out int i0, out int i1, out int i2);
                triangleWeights[t] = Mathf.Max(vertexWeights[i0], Mathf.Max(vertexWeights[i1], vertexWeights[i2]));
            }

            return new FeatureWeights(vertexWeights, triangleWeights);
        }
    }

    // Binary min-heap over int payloads keyed by float. Unity's runtime predates
    // System.Collections.Generic.PriorityQueue, hence the hand-rolled one.
    internal sealed class MinHeap
    {
        private readonly List<int> items = new();
        private readonly List<float> keys = new();

        public int Count => items.Count;

        public void Clear()
        {
            items.Clear();
            keys.Clear();
        }

        public void Push(int item, float key)
        {
            items.Add(item);
            keys.Add(key);

            int child = items.Count - 1;
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (keys[parent] <= keys[child]) break;
                Swap(parent, child);
                child = parent;
            }
        }

        public int Pop()
        {
            int result = items[0];
            int last = items.Count - 1;
            Swap(0, last);
            items.RemoveAt(last);
            keys.RemoveAt(last);

            int parent = 0;
            while (true)
            {
                int left = parent * 2 + 1;
                int right = left + 1;
                int smallest = parent;

                if (left < items.Count && keys[left] < keys[smallest]) smallest = left;
                if (right < items.Count && keys[right] < keys[smallest]) smallest = right;
                if (smallest == parent) break;

                Swap(parent, smallest);
                parent = smallest;
            }

            return result;
        }

        private void Swap(int a, int b)
        {
            (items[a], items[b]) = (items[b], items[a]);
            (keys[a], keys[b]) = (keys[b], keys[a]);
        }
    }

    // Stateless math the algorithm builds on.
    internal static class GeometryUtil
    {
        // Manual normalize: Vector3.normalized zeroes anything under its own ~1e-5 absolute
        // epsilon, which our local-space magnitudes routinely fall below. Always use this.
        public static Vector3 Normalized(Vector3 v) => v / Mathf.Sqrt(v.sqrMagnitude);

        // Collapses vertices that share a position into one id, and reports where each original
        // vertex went. Meshes routinely carry several vertices per position - any hard edge or UV
        // seam splits one - and anything reasoning about the SURFACE rather than about shading
        // needs them treated as the single point they are.
        //
        // Matching is EXACT, deliberately: split vertices are copies and carry bit-identical
        // coordinates, while genuinely distinct nearby points are not ours to merge, and any
        // tolerance would need to be scale-relative guesswork. Note Dictionary uses
        // Vector3.Equals, which is exact - unlike operator==, which is not.
        public static Vector3[] WeldPositions(Vector3[] vertices, out int[] remap)
        {
            Dictionary<Vector3, int> ids = new Dictionary<Vector3, int>(vertices.Length);
            List<Vector3> unique = new List<Vector3>(vertices.Length);
            remap = new int[vertices.Length];

            for (int v = 0; v < vertices.Length; v++)
            {
                if (!ids.TryGetValue(vertices[v], out int id))
                {
                    id = unique.Count;
                    ids[vertices[v]] = id;
                    unique.Add(vertices[v]);
                }
                remap[v] = id;
            }

            return unique.ToArray();
        }

        // Relative, not absolute: |cross|² scales with (edge length)⁴, so a fixed epsilon would
        // condemn nearly every triangle on a mesh authored at a tiny local scale.
        public static bool IsDegenerate(Vector3 a, Vector3 b, Vector3 c, out Vector3 cross)
        {
            Vector3 ab = b - a, ac = c - a;
            cross = Vector3.Cross(ab, ac);
            float maxEdgeSqr = Mathf.Max(ab.sqrMagnitude, ac.sqrMagnitude);
            return maxEdgeSqr < 1e-14f || cross.sqrMagnitude < maxEdgeSqr * maxEdgeSqr * 1e-10f;
        }

        // Any two perpendicular axes spanning the plane with this normal - which pair in
        // particular doesn't matter, only that they're orthonormal.
        public static void PlaneBasis(Vector3 normal, out Vector3 u, out Vector3 v)
        {
            u = Normalized(Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.99f ? Vector3.up : Vector3.right));
            v = Normalized(Vector3.Cross(normal, u));
        }

        // Monotone chain (Andrew's algorithm), returning indices into points around the hull
        // boundary. Winding doesn't matter - convex cooking and wireframe gizmos ignore it.
        public static List<int> ConvexHull2D(List<Vector2> points)
        {
            List<int> sorted = new List<int>(points.Count);
            for (int i = 0; i < points.Count; i++) sorted.Add(i);
            sorted.Sort((a, b) =>
            {
                int byX = points[a].x.CompareTo(points[b].x);
                return byX != 0 ? byX : points[a].y.CompareTo(points[b].y);
            });

            List<int> hull = new List<int>();

            // Lower boundary left-to-right, then upper boundary right-to-left onto the same
            // list; a non-left turn means the middle point is inside the hull, so pop it.
            foreach (int i in sorted)
            {
                PopUntilConvex(hull, points, i, 2);
                hull.Add(i);
            }

            int upperFloor = hull.Count + 1;
            for (int k = sorted.Count - 2; k >= 0; k--)
            {
                PopUntilConvex(hull, points, sorted[k], upperFloor);
                hull.Add(sorted[k]);
            }

            if (hull.Count > 0) hull.RemoveAt(hull.Count - 1); // last point repeats the first
            return hull;
        }

        // Signed area of the polygon described by order, as absolute area. Only valid for a
        // simple (non-self-intersecting) polygon, which a convex hull always is.
        public static float PolygonArea(List<Vector2> points, List<int> order)
        {
            float sum = 0f;
            for (int i = 0; i < order.Count; i++)
            {
                Vector2 a = points[order[i]];
                Vector2 b = points[order[(i + 1) % order.Count]];
                sum += a.x * b.y - b.x * a.y;
            }
            return Mathf.Abs(sum) * 0.5f;
        }

        public static float PolygonArea(List<Vector2> polygon)
        {
            float sum = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                sum += a.x * b.y - b.x * a.y;
            }
            return Mathf.Abs(sum) * 0.5f;
        }

        // The overlapping region of two CONVEX polygons, by Sutherland-Hodgman: clip one against
        // each of the other's edges in turn. Returns an empty polygon when they don't meet.
        //
        // This is how to compare two outlines that describe the same surface but were built from
        // different vertices - counting how many of one's corners fall inside the other is brittle
        // the moment the two are tessellated differently, since corner counts and positions stop
        // corresponding at all. Area doesn't care how either outline was subdivided.
        public static List<Vector2> IntersectConvex(List<Vector2> subject, List<Vector2> clip)
        {
            List<Vector2> output = new List<Vector2>(subject);
            MakeCounterClockwise(output);

            List<Vector2> window = new List<Vector2>(clip);
            MakeCounterClockwise(window);

            for (int edge = 0; edge < window.Count && output.Count > 0; edge++)
            {
                Vector2 a = window[edge];
                Vector2 b = window[(edge + 1) % window.Count];

                List<Vector2> input = output;
                output = new List<Vector2>(input.Count + 1);

                Vector2 previous = input[input.Count - 1];
                bool previousInside = Cross2D(a, b, previous) >= 0f;

                foreach (Vector2 point in input)
                {
                    bool inside = Cross2D(a, b, point) >= 0f;
                    if (inside != previousInside)
                    {
                        output.Add(LineIntersection(previous, point, a, b));
                    }
                    if (inside)
                    {
                        output.Add(point);
                    }

                    previous = point;
                    previousInside = inside;
                }
            }

            return output;
        }

        // Furthest a corner may be pushed from where it started, as a multiple of the margin.
        // Beyond this the corner is cut off square instead. Without a limit, sharpening the corner
        // sharpens the spike: a mitered corner sits margin/sin(half-angle) from the original
        // vertex, which is a mild 1.4x at a right angle but grows without bound as the corner
        // closes up, so one sliver in an outline throws a long spike far past any real geometry.
        private const float MiterLimit = 2f;

        // Pushes every edge of a convex polygon outward by margin. Used to give a shape the same
        // proud margin an extruded one gets along its normal.
        //
        // Corners stay sharp where that is cheap and are bevelled where it is not - both cover the
        // original corner, so the inflated outline always contains the original one either way.
        public static List<Vector2> InflateConvex(List<Vector2> polygon, float margin)
        {
            List<Vector2> ordered = new List<Vector2>(polygon);
            if (margin <= 0f || ordered.Count < 3) return ordered;

            MakeCounterClockwise(ordered);

            // Outward normal per edge. For a counter-clockwise polygon the interior is to the left
            // of each directed edge, so (dy, -dx) points away from it.
            int count = ordered.Count;
            Vector2[] outward = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 edge = ordered[(i + 1) % count] - ordered[i];
                Vector2 normal = new Vector2(edge.y, -edge.x);
                float length = normal.magnitude;
                outward[i] = length > 1e-20f ? normal / length : Vector2.zero;
            }

            // Both offset edges sit margin along their own normal, so their meeting point lies on
            // the bisector at margin/cos(half-angle), and |n1 + n2| is 2cos(half-angle) - which
            // makes the miter point, and the limit test on it, fall out without solving anything.
            float minimumBisector = 4f / (MiterLimit * MiterLimit);

            List<Vector2> inflated = new List<Vector2>(count + 4);
            for (int i = 0; i < count; i++)
            {
                Vector2 vertex = ordered[i];
                Vector2 before = outward[(i - 1 + count) % count];
                Vector2 after = outward[i];

                Vector2 bisector = before + after;
                float bisectorSqr = bisector.sqrMagnitude;

                if (bisectorSqr < minimumBisector)
                {
                    // Too sharp to miter: cut the corner off, keeping both edges at full margin.
                    inflated.Add(vertex + before * margin);
                    inflated.Add(vertex + after * margin);
                    continue;
                }

                inflated.Add(vertex + bisector * (2f * margin / bisectorSqr));
            }

            return inflated;
        }

        // Clipping needs a known orientation to tell inside from outside; the polygon's own signed
        // area is the only reliable source, since hulls here carry no guaranteed winding.
        private static void MakeCounterClockwise(List<Vector2> polygon)
        {
            float winding = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                winding += a.x * b.y - b.x * a.y;
            }

            if (winding < 0f) polygon.Reverse();
        }

        // Where the segment from->to crosses the infinite line a->b. Only called when the two
        // endpoints straddle that line, so they cannot be parallel; the guard is for the
        // degenerate case where the segment has collapsed to a point.
        private static Vector2 LineIntersection(Vector2 from, Vector2 to, Vector2 a, Vector2 b)
        {
            Vector2 segment = to - from;
            Vector2 line = b - a;

            float denominator = line.x * segment.y - line.y * segment.x;
            if (Mathf.Abs(denominator) < 1e-20f) return to;

            Vector2 offset = a - from;
            float t = (line.x * offset.y - line.y * offset.x) / denominator;
            return from + segment * t;
        }

        // The circle best fitting a set of coplanar points, by Kasa's algebraic least squares -
        // minimising the error in x^2+y^2-2ax-2by-c rather than in the radius itself, which is what
        // makes it a plain 2x2 solve instead of an iteration. On points that genuinely lie on a
        // circle (an exported cylinder's ring of vertices) the two minimisations agree exactly, so
        // the usual criticism of Kasa - that it biases toward small radii on short, noisy arcs -
        // doesn't apply to what this is used for.
        //
        // Points are mean-centred first. Without that the moments below are dominated by the
        // distance from the mesh origin rather than by the spread of the points, and at our local
        // scale that alone loses the fit.
        public static bool TryFitCircle(List<Vector2> points, out Vector2 centre, out float radius)
        {
            centre = Vector2.zero;
            radius = 0f;
            if (points.Count < 3) return false;

            Vector2 mean = Vector2.zero;
            foreach (Vector2 point in points)
            {
                mean += point;
            }
            mean /= points.Count;

            // Doubles throughout: these are third moments of sub-millimetre coordinates, so the
            // sums run around 1e-12 and float would keep barely a digit of them.
            double suu = 0d, svv = 0d, suv = 0d, suuu = 0d, svvv = 0d, suvv = 0d, svuu = 0d;
            foreach (Vector2 point in points)
            {
                double u = point.x - mean.x;
                double v = point.y - mean.y;

                suu += u * u;
                svv += v * v;
                suv += u * v;
                suuu += u * u * u;
                svvv += v * v * v;
                suvv += u * v * v;
                svuu += v * u * u;
            }

            // Relative, like every other tolerance here: collinear points leave the system singular
            // and the moments themselves are scale-dependent, so an absolute epsilon would call
            // every small ring collinear.
            double determinant = suu * svv - suv * suv;
            if (determinant <= 1e-12d * suu * svv) return false;

            double rhsU = (suuu + suvv) * 0.5d;
            double rhsV = (svvv + svuu) * 0.5d;
            double a = (rhsU * svv - rhsV * suv) / determinant;
            double b = (suu * rhsV - suv * rhsU) / determinant;

            double radiusSqr = a * a + b * b + (suu + svv) / points.Count;
            if (radiusSqr <= 0d) return false;

            centre = mean + new Vector2((float)a, (float)b);
            radius = (float)System.Math.Sqrt(radiusSqr);
            return radius > 0f;
        }

        // The smallest rectangle, at any rotation, enclosing a convex polygon - returning its area
        // and, through the out parameters, where it sits. Rests on the standard result that such a
        // rectangle always has one side flush with an edge of the polygon, so trying each edge in
        // turn finds it exactly rather than approximately.
        //
        // Comparing its area against the polygon's own is a far better test of "is this shape a
        // box" than inspecting corners: it is blind to how the outline was subdivided, so a
        // rectangle carrying extra vertices along its edges still measures as a perfect fit, and
        // what it reports for anything else is precisely the quantity that matters - how much
        // empty space a box would add.
        public static float MinimumAreaRectangle(List<Vector2> polygon, out Vector2 centre, out Vector2 axis, out Vector2 size)
        {
            centre = Vector2.zero;
            axis = Vector2.right;
            size = Vector2.zero;

            if (polygon.Count < 3) return 0f;

            float bestArea = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 edge = polygon[(i + 1) % polygon.Count] - polygon[i];
                float length = edge.magnitude;
                if (length < 1e-20f) continue;

                Vector2 u = edge / length;
                Vector2 v = new Vector2(-u.y, u.x);

                float minU = float.MaxValue, maxU = float.MinValue;
                float minV = float.MaxValue, maxV = float.MinValue;
                foreach (Vector2 point in polygon)
                {
                    float alongU = Vector2.Dot(point, u);
                    float alongV = Vector2.Dot(point, v);
                    minU = Mathf.Min(minU, alongU);
                    maxU = Mathf.Max(maxU, alongU);
                    minV = Mathf.Min(minV, alongV);
                    maxV = Mathf.Max(maxV, alongV);
                }

                float area = (maxU - minU) * (maxV - minV);
                if (area >= bestArea) continue;

                bestArea = area;
                axis = u;
                size = new Vector2(maxU - minU, maxV - minV);
                centre = u * ((minU + maxU) * 0.5f) + v * ((minV + maxV) * 0.5f);
            }

            return bestArea == float.MaxValue ? 0f : bestArea;
        }

        // Is the point inside the convex polygon described by order? Winding-agnostic: the
        // polygon's own signed area decides which way "inside" turns, so this doesn't care
        // whether the hull came out clockwise or not.
        //
        // tolerance is a DISTANCE the point may sit outside an edge and still count as inside -
        // needed because a point lying exactly on the boundary (a shared vertex, which is the
        // common case when testing one patch against another) lands either side of zero on
        // rounding alone. Pass a real length, never a bare epsilon.
        public static bool PolygonContains(List<Vector2> polygon, Vector2 point, float tolerance = 0f)
        {
            float winding = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                winding += a.x * b.y - b.x * a.y;
            }
            float sign = winding >= 0f ? 1f : -1f;

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];

                float side = Cross2D(a, b, point) * sign;
                if (side >= 0f) continue;

                // |cross| is twice the triangle's area, so dividing by the edge length turns it
                // into how far outside the point actually is.
                float edgeLength = (b - a).magnitude;
                if (edgeLength > 1e-12f && -side / edgeLength > tolerance) return false;
            }
            return true;
        }

        // How far inside the convex polygon the point sits: zero on the boundary, positive within,
        // negative outside. Winding-agnostic, on the polygon's own signed area, like
        // PolygonContains - which answers the same question with a threshold; this returns the
        // distance itself, for callers that need to know how deep rather than whether.
        public static float DepthInsideConvex(List<Vector2> polygon, Vector2 point)
        {
            float winding = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                winding += a.x * b.y - b.x * a.y;
            }
            float sign = winding >= 0f ? 1f : -1f;

            float depth = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];

                float edgeLength = (b - a).magnitude;
                if (edgeLength < 1e-12f) continue;

                // |cross| is twice the triangle's area, so over the edge length it is the distance.
                depth = Mathf.Min(depth, Cross2D(a, b, point) * sign / edgeLength);
            }

            return depth == float.MaxValue ? 0f : depth;
        }

        // Convex polygon -> triangulated prism: two fanned end caps plus two triangles per edge.
        // The result is closed and has real volume, which is what PhysX needs to cook a convex
        // collider at all.
        public static Mesh ExtrudePrism(List<Vector3> points, List<int> order, Vector3 halfOffset)
        {
            int n = order.Count;
            Vector3[] vertices = new Vector3[n * 2];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = points[order[i]];
                vertices[i] = p - halfOffset;
                vertices[n + i] = p + halfOffset;
            }

            List<int> triangles = new List<int>((n - 2) * 6 + n * 6);
            for (int i = 1; i < n - 1; i++)
            {
                triangles.Add(0); triangles.Add(i + 1); triangles.Add(i);
                triangles.Add(n); triangles.Add(n + i); triangles.Add(n + i + 1);
            }
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                triangles.Add(i); triangles.Add(n + i); triangles.Add(next);
                triangles.Add(next); triangles.Add(n + i); triangles.Add(n + next);
            }

            Mesh mesh = new Mesh { vertices = vertices, triangles = triangles.ToArray() };
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        private static void PopUntilConvex(List<int> hull, List<Vector2> points, int candidate, int floor)
        {
            while (hull.Count >= floor && Cross2D(points[hull[hull.Count - 2]], points[hull[hull.Count - 1]], points[candidate]) <= 0f)
            {
                hull.RemoveAt(hull.Count - 1);
            }
        }

        private static float Cross2D(Vector2 o, Vector2 a, Vector2 b)
        {
            return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        }
    }
}
#endif
