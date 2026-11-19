#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace RobotFramework.Components
{
    // Simplifies the mesh the collider builder works from. The RENDERED mesh is never touched -
    // this operates on the in-memory copy SourceGeometry read out, which is the entire reason
    // PerTriangleMeshCollider exists as a separate collision representation in the first place.
    //
    // Quadric error metric edge collapse - the same method as Blender's Decimate (Collapse) - run
    // to a target triangle ratio. Each vertex accumulates the squared-distance-to-plane error of
    // the faces around it; the cheapest edge is collapsed repeatedly until the target is hit. The
    // useful property for us: collapsing an edge INSIDE a flat region has exactly zero error, so
    // on a part made of flat panels this strips tessellation noise without moving the surface at
    // all, and only starts rounding off real features once the flat runs are exhausted.
    //
    // WHAT THIS DOES AND DOESN'T BUY, so the ratio gets set for the right reason: the collider
    // outlines come from convex hulls, which already discard every interior vertex, so decimating
    // does NOT directly reduce collider count. What it buys is build time (partitioning is linear
    // in triangles) and cleaner patches - a "flat" face that CAD exported as slightly faceted can
    // fracture into several patches, and collapsing that noise away merges them.
    //
    // SAFETY: collapses that would flip a face are refused, so the surface can't turn itself
    // inside out. But a collapse still MOVES the surface, and at low ratios it can move outward -
    // the builder's own rejection tests then measure the moved surface, not the original. Treat
    // the ratio as a real fidelity trade, not free savings, and check the Scene view preview.
    //
    // TERMINATION is structural, and deliberately so - this used to hang outright on any mesh
    // past a few thousand triangles. See the note on vertexVersion in State.
    internal static class MeshDecimation
    {
        // How strongly an open edge resists being moved off its own line, relative to the ordinary
        // face weighting. Large because a rim only ever gives way once nothing cheaper is left.
        private const float BoundaryPenalty = 1000f;

        // Most faces a single vertex may end up carrying. Every candidate costs time proportional
        // to the degree of both its endpoints - the flip test walks each one - so without a ceiling
        // one vertex swallowing a large region makes every later candidate expensive and the whole
        // run quadratic. Well above any sane fan, so this only ever catches the runaway case; the
        // cost is that a ratio may stop slightly short, which it is already allowed to do.
        private const int MaxVertexDegree = 64;

        // Limited dissolve: remove vertices that describe nothing, leaving every survivor exactly
        // where it was. A vertex goes if the faces around it agree to within angleDegrees (it sits
        // in the middle of a flat span) or if it sits partway along an otherwise straight crease
        // or boundary. Everything else - real corners, real creases, the ends of edges - stays.
        //
        // This is topology cleanup, not simplification, and the difference matters: no surviving
        // vertex moves, so within the angle the surface is unchanged rather than approximated.
        // Worth running before anything else because the pipeline is sensitive to tessellation in
        // ways it shouldn't be - stray vertices along a straight edge turn a rectangle into an
        // n-gon, and the two faces of a wall subdivided differently make one wall look like two
        // unrelated surfaces.
        //
        // Dissolve decides everything from face normals and topology and never reads a quadric, so
        // it builds none - that also skips the boundary constraint pass and a full edge costing.
        public static SourceGeometry Dissolve(SourceGeometry geometry, float angleDegrees)
        {
            if (angleDegrees <= 0f || geometry.TriangleCount == 0) return geometry;

            State state = State.From(geometry, withQuadrics: false);
            state.DissolveFlat(angleDegrees);
            return state.ToGeometry(geometry);
        }

        // maxSurfaceShift caps how far a collapse may move the surface. It is a hard limit, not a
        // preference: the ratio is only reached if it can be reached within it, and stopping short
        // is the correct outcome. Without it, aggressive ratios collapse a thin wall's two faces
        // into each other - the wall stops having two sides, and the collider builder can no
        // longer pair them into a solid slab, which is both a worse result and a confusing one.
        public static SourceGeometry Decimate(SourceGeometry geometry, float ratio, float maxSurfaceShift)
        {
            int target = Mathf.CeilToInt(geometry.TriangleCount * Mathf.Clamp01(ratio));
            if (ratio >= 1f || geometry.TriangleCount == 0 || target >= geometry.TriangleCount)
            {
                return geometry;
            }

            State state = State.From(geometry, withQuadrics: true);
            state.CollapseTo(target, maxSurfaceShift);
            return state.ToGeometry(geometry);
        }

        // Everything the collapse loop mutates. Vertices are welded by position first: a mesh with
        // split vertices has every seam duplicated, and without welding no collapse could ever
        // cross one, which on a CAD export is most of the mesh.
        private sealed class State
        {
            private Vector3[] positions;
            private Quadric[] quadrics;
            private double[] areaAround;       // face area feeding each vertex's quadric
            private bool[] vertexAlive;
            private List<int>[] vertexTriangles;

            // Bumped whenever a vertex's position, quadric or triangle list changes. A queued
            // collapse records both endpoints' stamps, so on pop "did anything about this edge
            // change since I costed it" is one integer compare - and a stale entry is DROPPED
            // rather than re-costed and pushed back.
            //
            // That last part is what makes this terminate. The re-costing scheme it replaces
            // compared the fresh cost against the stored one and requeued when it had risen, which
            // spins forever the moment a cost lands slightly NEGATIVE - a sum of squares that
            // cancels to -1e-18 is routine here, since a33 accumulates w*d^2 over every incident
            // face and boundary constraints weight that by BoundaryPenalty. Negative c satisfies
            // c > c * 1.0001 for all time, so that entry stayed the heap minimum and popped
            // forever while the triangle count never moved. Quadric.Error now clamps at zero as
            // well, but the loop no longer depends on the cost at all.
            //
            // Dropping stale entries is exact, not an approximation: a collapse changes only the
            // survivor, so every edge that does not touch it keeps a cost that is still correct,
            // and every edge that does is re-offered by Collapse. It also settles the one question
            // the old union-find could not - a valid stamp pair PROVES the two are still joined by
            // a live triangle, because a triangle containing both a and b can only die when two of
            // its corners merge, and each such merge bumps a or b. No non-edge collapse can slip
            // through, so nothing merges two vertices that never shared a face.
            private int[] vertexVersion;

            private int[] corners;             // 3 vertex ids per triangle
            private bool[] triangleAlive;
            private int aliveTriangles;

            private readonly CandidateHeap queue = new();

            // Reused across collapses and sweeps - both run once per collapsed vertex, so a fresh
            // collection per call is pure garbage on a mesh large enough to be worth simplifying.
            private readonly HashSet<long> offered = new();
            private readonly List<int> neighbours = new();
            private readonly List<int> reoffer = new();

            // Dissolve drives collapses itself, so re-costing edges after each one is pure waste.
            private bool maintainQueue = true;

            // Set by CollapseTo before anything is queued; see Offer.
            private double maxMeanSquare;

            private struct Candidate
            {
                public int A;
                public int B;
                public int VersionA;
                public int VersionB;
                public double Cost;
                public Vector3 Position;
            }

            public static State From(SourceGeometry geometry, bool withQuadrics)
            {
                State state = new State();
                state.positions = GeometryUtil.WeldPositions(geometry.Vertices, out int[] remap);

                // Triangles with no usable area are dropped outright rather than carried: welding
                // can leave two corners on the same point, and a collinear triangle has no plane
                // to build a quadric from - normalizing its cross product would yield NaN and
                // poison every vertex it touches. The builder discards these downstream anyway.
                List<int> kept = new List<int>(geometry.Indices.Length);
                for (int i = 0; i < geometry.Indices.Length; i += 3)
                {
                    int a = remap[geometry.Indices[i]];
                    int b = remap[geometry.Indices[i + 1]];
                    int c = remap[geometry.Indices[i + 2]];
                    if (a == b || b == c || c == a) continue;
                    if (GeometryUtil.IsDegenerate(state.positions[a], state.positions[b], state.positions[c], out _)) continue;

                    kept.Add(a);
                    kept.Add(b);
                    kept.Add(c);
                }

                state.corners = kept.ToArray();
                state.aliveTriangles = state.corners.Length / 3;
                state.triangleAlive = new bool[state.aliveTriangles];
                state.vertexAlive = new bool[state.positions.Length];
                state.vertexVersion = new int[state.positions.Length];
                state.vertexTriangles = new List<int>[state.positions.Length];

                if (withQuadrics)
                {
                    state.quadrics = new Quadric[state.positions.Length];
                    state.areaAround = new double[state.positions.Length];
                }

                // Sized from the real degree rather than List's doubling from 4: on a mesh worth
                // simplifying this is tens of thousands of lists, and every regrow is a copy.
                int[] degree = new int[state.positions.Length];
                foreach (int corner in state.corners)
                {
                    degree[corner]++;
                }
                for (int v = 0; v < state.positions.Length; v++)
                {
                    state.vertexTriangles[v] = new List<int>(degree[v]);
                }

                for (int t = 0; t < state.aliveTriangles; t++)
                {
                    state.triangleAlive[t] = true;
                    state.Corners(t, out int a, out int b, out int c);

                    if (withQuadrics)
                    {
                        // Area-weighted, so a large face resists being moved more than a sliver.
                        Vector3 cross = Vector3.Cross(state.positions[b] - state.positions[a], state.positions[c] - state.positions[a]);
                        Vector3 normal = GeometryUtil.Normalized(cross);
                        float area = cross.magnitude * 0.5f;
                        Quadric plane = Quadric.FromPlane(normal, -Vector3.Dot(normal, state.positions[a]), area);

                        state.quadrics[a].Add(plane);
                        state.quadrics[b].Add(plane);
                        state.quadrics[c].Add(plane);
                        state.areaAround[a] += area;
                        state.areaAround[b] += area;
                        state.areaAround[c] += area;
                    }

                    state.Attach(a, t);
                    state.Attach(b, t);
                    state.Attach(c, t);
                }

                if (withQuadrics) state.ConstrainBoundaries();
                return state;
            }

            // Pins the open edges of the mesh. Without this a flat region costs nothing to
            // collapse ANYWHERE - including inward from its own rim, since every face around it
            // shares one plane and the quadric only measures distance to those faces. A plate
            // would quietly lose its outline, which for collider generation is the one thing that
            // matters, and the collapses would all pile into a few survivors of enormous degree,
            // which is what made this quadratic on any part with large flat faces.
            //
            // The constraint is a plane standing perpendicular to the face along the open edge, so
            // moving a rim vertex ALONG its rim stays free while moving it off costs immediately.
            // That is exactly the freedom a rim wants: collinear vertices dissolve, corners stay.
            private void ConstrainBoundaries()
            {
                Dictionary<long, int> firstTriangle = new Dictionary<long, int>(corners.Length);
                HashSet<long> shared = new HashSet<long>();

                for (int t = 0; t < aliveTriangles; t++)
                {
                    Corners(t, out int a, out int b, out int c);
                    Record(a, b, t);
                    Record(b, c, t);
                    Record(c, a, t);
                }

                foreach (KeyValuePair<long, int> entry in firstTriangle)
                {
                    if (shared.Contains(entry.Key)) continue;

                    int va = (int)(entry.Key >> 32);
                    int vb = (int)(entry.Key & 0xFFFFFFFF);

                    Vector3 edge = positions[vb] - positions[va];
                    if (edge.sqrMagnitude < 1e-20f) continue;

                    Vector3 normal = Vector3.Cross(edge, FaceNormal(entry.Value));
                    if (normal.sqrMagnitude < 1e-20f) continue;
                    normal = GeometryUtil.Normalized(normal);

                    // Weighted against the edge's own length so the penalty scales with the mesh
                    // rather than against an absolute figure, and heavily enough that a rim only
                    // gives way once there is nothing cheaper left anywhere.
                    Quadric wall = Quadric.FromPlane(normal, -Vector3.Dot(normal, positions[va]), BoundaryPenalty * edge.magnitude);
                    quadrics[va].Add(wall);
                    quadrics[vb].Add(wall);
                }

                void Record(int a, int b, int triangle)
                {
                    long key = PackEdge(a, b);
                    if (!firstTriangle.TryAdd(key, triangle)) shared.Add(key);
                }
            }

            // The triangle lists accumulate entries that later died - from this vertex's own
            // collapses, and from collapses elsewhere that killed a shared triangle. Left alone, a
            // vertex absorbing a large flat region ends up with a list thousands long that every
            // subsequent collapse walks three times over, which is quadratic in the size of the
            // region and is what turned decimating a big part into a hang.
            //
            // Compacting on access keeps every list proportional to the live degree, and each dead
            // entry is paid for exactly once no matter how often the list is read afterwards.
            private List<int> LiveTriangles(int vertex)
            {
                List<int> triangles = vertexTriangles[vertex];
                if (triangles == null) return null;

                int write = 0;
                for (int read = 0; read < triangles.Count; read++)
                {
                    int triangle = triangles[read];
                    if (!triangleAlive[triangle]) continue;

                    triangles[write++] = triangle;
                }

                if (write < triangles.Count) triangles.RemoveRange(write, triangles.Count - write);
                return triangles;
            }

            private void Attach(int vertex, int triangle)
            {
                vertexAlive[vertex] = true;
                vertexTriangles[vertex].Add(triangle);
            }

            private void SeedQueue()
            {
                offered.Clear();
                for (int t = 0; t < aliveTriangles; t++)
                {
                    Corners(t, out int a, out int b, out int c);
                    TryOffer(a, b, offered);
                    TryOffer(b, c, offered);
                    TryOffer(c, a, offered);
                }
            }

            private void TryOffer(int a, int b, HashSet<long> seen)
            {
                if (!seen.Add(PackEdge(a, b))) return;
                Offer(a, b);
            }

            // Both rejections here are applied at OFFER time rather than on pop, which keeps the
            // heap to the edges that could actually be collapsed - on a part with large flat
            // panels the boundary-constrained edges alone are most of what used to be queued, and
            // every one of them cost a push, a pop and a re-cost to learn nothing.
            //
            // Filtering early loses nothing, because an offer is not a permanent verdict: the edge
            // is offered again by Collapse whenever either endpoint changes, which is the only
            // thing that can change either answer. If nothing around it ever changes, the pop-time
            // test would have reached the same verdict on the same numbers.
            private void Offer(int a, int b)
            {
                // Before the flip test, which is the expensive part and scales with exactly this.
                if (LiveTriangles(a).Count + LiveTriangles(b).Count > MaxVertexDegree) return;

                double cost = Evaluate(a, b, out Vector3 position);

                // The quadric is a sum of squared distances weighted by area, so dividing by the
                // area behind it recovers a mean squared distance - a real length that can be held
                // to a real limit, rather than an error number whose scale depends on how big the
                // surrounding faces happen to be. Written as a multiply to keep it exact at zero.
                double area = areaAround[a] + areaAround[b];
                if (area > 0.0 && cost > maxMeanSquare * area) return;

                queue.Push(new Candidate
                {
                    A = a,
                    B = b,
                    VersionA = vertexVersion[a],
                    VersionB = vertexVersion[b],
                    Cost = cost,
                    Position = position
                });
            }

            // Pops the cheapest collapse until the target is met. Every entry that survives the
            // stamp check is exactly as valid as when it was costed, so there is nothing to
            // re-derive here - the loop is a straight drain of the heap, and each entry is popped
            // at most once. Total work is bounded by the seed edges plus one re-offer per edge
            // around each survivor, and every push needs a collapse to have happened first.
            public void CollapseTo(int target, float maxSurfaceShift)
            {
                maxMeanSquare = (double)maxSurfaceShift * maxSurfaceShift;
                SeedQueue();

                while (aliveTriangles > target && queue.Count > 0)
                {
                    Candidate candidate = queue.Pop();

                    int a = candidate.A;
                    int b = candidate.B;
                    if (!vertexAlive[a] || !vertexAlive[b]) continue;
                    if (vertexVersion[a] != candidate.VersionA || vertexVersion[b] != candidate.VersionB) continue;

                    if (WouldFlip(a, b, candidate.Position)) continue;

                    Collapse(a, b, candidate.Position);
                }
            }

            // Sweeps until nothing more can go. Dissolving one vertex can flatten its neighbours'
            // surroundings and make them dissolvable in turn, so a single pass leaves work behind;
            // the cap is there because each pass is a full sweep and the tail is never worth much.
            public void DissolveFlat(float angleDegrees)
            {
                const int maxPasses = 8;

                maintainQueue = false;
                float cosLimit = Mathf.Cos(angleDegrees * Mathf.Deg2Rad);

                for (int pass = 0; pass < maxPasses; pass++)
                {
                    bool removedAny = false;

                    for (int v = 0; v < positions.Length; v++)
                    {
                        if (!vertexAlive[v]) continue;
                        if (!TryFindDissolveTarget(v, cosLimit, out int target)) continue;
                        if (WouldFlip(target, v, positions[target])) continue;

                        // The target keeps its own position - that is what makes this a dissolve
                        // rather than a simplification.
                        Collapse(target, v, positions[target]);
                        removedAny = true;
                    }

                    if (!removedAny) break;
                }
            }

            // Can this vertex go without changing what the surface describes? Two cases qualify.
            //
            // No creases at all: every edge around the vertex has faces agreeing to within the
            // limit, so it sits inside a flat span and contributes nothing.
            //
            // Exactly two creases, running straight through: the vertex is partway along an edge
            // or a boundary, splitting one straight line into two. The line survives its removal
            // unchanged, and collapsing ALONG the crease is what keeps it straight.
            //
            // A mesh boundary counts as a crease. Nothing beyond the far side of it constrains the
            // shape, so an edge of the mesh has to be preserved as carefully as a real fold.
            private bool TryFindDissolveTarget(int vertex, float cosLimit, out int target)
            {
                target = -1;

                List<int> incident = LiveTriangles(vertex);
                if (incident == null || incident.Count == 0) return false;

                int creases = 0;
                int firstCrease = -1;
                int secondCrease = -1;
                int anyNeighbour = -1;

                CollectNeighbours(vertex, neighbours);
                foreach (int neighbour in neighbours)
                {
                    if (!IsSmoothEdge(vertex, neighbour, cosLimit, out bool exists))
                    {
                        if (!exists) continue;

                        creases++;
                        if (firstCrease < 0) firstCrease = neighbour;
                        else if (secondCrease < 0) secondCrease = neighbour;
                        else return false; // three or more creases meet here: a real corner
                    }

                    anyNeighbour = neighbour;
                }

                if (creases == 0)
                {
                    if (anyNeighbour < 0) return false;

                    target = anyNeighbour;
                    return true;
                }

                if (creases != 2) return false;

                // Straight only if the two creases leave in opposite directions.
                Vector3 toFirst = GeometryUtil.Normalized(positions[firstCrease] - positions[vertex]);
                Vector3 toSecond = GeometryUtil.Normalized(positions[secondCrease] - positions[vertex]);
                if (Vector3.Dot(toFirst, toSecond) > -cosLimit) return false;

                target = firstCrease;
                return true;
            }

            // Every distinct vertex sharing a triangle with this one, into a caller-owned buffer.
            // Dissolve sweeps every vertex up to eight times, so returning a fresh collection here
            // would allocate two objects per vertex per pass for no reason.
            private void CollectNeighbours(int vertex, List<int> into)
            {
                into.Clear();

                foreach (int t in LiveTriangles(vertex))
                {
                    Corners(t, out int a, out int b, out int c);
                    if (a != vertex && !into.Contains(a)) into.Add(a);
                    if (b != vertex && !into.Contains(b)) into.Add(b);
                    if (c != vertex && !into.Contains(c)) into.Add(c);
                }
            }

            // Do the faces meeting along this edge agree closely enough to treat it as no edge at
            // all? A boundary edge (one face) never does, and anything non-manifold is left alone.
            private bool IsSmoothEdge(int from, int to, float cosLimit, out bool exists)
            {
                int first = -1;
                int second = -1;

                foreach (int t in LiveTriangles(from))
                {
                    Corners(t, out int a, out int b, out int c);
                    if (a != to && b != to && c != to) continue;

                    if (first < 0) first = t;
                    else if (second < 0) second = t;
                    else
                    {
                        exists = true;
                        return false; // non-manifold, treat as a crease and leave it be
                    }
                }

                exists = first >= 0;
                if (!exists || second < 0) return false;

                return Vector3.Dot(FaceNormal(first), FaceNormal(second)) >= cosLimit;
            }

            private Vector3 FaceNormal(int triangle)
            {
                Corners(triangle, out int a, out int b, out int c);
                return GeometryUtil.Normalized(Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]));
            }

            private void Collapse(int a, int b, Vector3 position)
            {
                positions[a] = position;
                if (quadrics != null)
                {
                    quadrics[a].Add(quadrics[b]);
                    areaAround[a] += areaAround[b];
                }
                vertexAlive[b] = false;

                // Both stamps move: a changed, and b is gone. Anything queued against either is
                // now dead, and the survivor's edges are re-offered below.
                vertexVersion[a]++;
                vertexVersion[b]++;

                foreach (int t in LiveTriangles(b))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        if (corners[t * 3 + i] == b) corners[t * 3 + i] = a;
                    }

                    Corners(t, out int c0, out int c1, out int c2);
                    if (c0 == c1 || c1 == c2 || c2 == c0)
                    {
                        // The triangle folded onto the collapsed edge - this is where the
                        // triangle count actually comes down.
                        triangleAlive[t] = false;
                        aliveTriangles--;
                        continue;
                    }

                    vertexTriangles[a].Add(t);
                }

                vertexTriangles[b] = null;

                if (!maintainQueue) return;

                // The survivor's neighbourhood has moved, so its edges need re-costing. Every
                // other vertex's own stamp is untouched, so every edge not touching a keeps a
                // queue entry that is still exactly right.
                //
                // Gathered in full before any of it is offered, and not fused into one loop:
                // Offer reads a's own triangle list, and reading it compacts it, so offering from
                // inside a walk of that same list invalidates the enumerator mid-collapse.
                offered.Clear();
                reoffer.Clear();
                foreach (int t in LiveTriangles(a))
                {
                    Corners(t, out int c0, out int c1, out int c2);
                    if (c0 != a && offered.Add(PackEdge(a, c0))) reoffer.Add(c0);
                    if (c1 != a && offered.Add(PackEdge(a, c1))) reoffer.Add(c1);
                    if (c2 != a && offered.Add(PackEdge(a, c2))) reoffer.Add(c2);
                }

                foreach (int neighbour in reoffer)
                {
                    Offer(a, neighbour);
                }
            }

            // Refuses a collapse that would turn any surviving face inside out, or squash one flat
            // enough to be meaningless. The area comparison is relative to the face's own size,
            // never an absolute epsilon - local coordinates here are routinely sub-millimetre.
            private bool WouldFlip(int a, int b, Vector3 position)
            {
                return FlipsAround(a, a, b, position) || FlipsAround(b, a, b, position);
            }

            private bool FlipsAround(int vertex, int a, int b, Vector3 position)
            {
                foreach (int t in LiveTriangles(vertex))
                {
                    Corners(t, out int c0, out int c1, out int c2);

                    // Faces containing both ends of the edge disappear in the collapse.
                    bool hasA = c0 == a || c1 == a || c2 == a;
                    bool hasB = c0 == b || c1 == b || c2 == b;
                    if (hasA && hasB) continue;

                    Vector3 p0 = Moved(c0, a, b, position);
                    Vector3 p1 = Moved(c1, a, b, position);
                    Vector3 p2 = Moved(c2, a, b, position);

                    Vector3 before = Vector3.Cross(positions[c1] - positions[c0], positions[c2] - positions[c0]);
                    Vector3 after = Vector3.Cross(p1 - p0, p2 - p0);

                    if (Vector3.Dot(before, after) <= 0f) return true;
                    if (after.sqrMagnitude < before.sqrMagnitude * 1e-6f) return true;
                }

                return false;
            }

            private Vector3 Moved(int vertex, int a, int b, Vector3 position)
            {
                return vertex == a || vertex == b ? position : positions[vertex];
            }

            // Subset placement: try both endpoints and the midpoint and keep the cheapest, rather
            // than solving for the optimal position. No matrix inversion to go singular on a flat
            // region, and on flat regions - which is most of what gets collapsed here - an endpoint
            // is already the exact answer.
            private double Evaluate(int a, int b, out Vector3 position)
            {
                Quadric sum = quadrics[a];
                sum.Add(quadrics[b]);

                Vector3 midpoint = (positions[a] + positions[b]) * 0.5f;
                double costA = sum.Error(positions[a]);
                double costB = sum.Error(positions[b]);
                double costMid = sum.Error(midpoint);

                if (costA <= costB && costA <= costMid)
                {
                    position = positions[a];
                    return costA;
                }
                if (costB <= costMid)
                {
                    position = positions[b];
                    return costB;
                }

                position = midpoint;
                return costMid;
            }

            public SourceGeometry ToGeometry(SourceGeometry original)
            {
                int[] compacted = new int[positions.Length];
                List<Vector3> vertices = new List<Vector3>(positions.Length);
                for (int v = 0; v < positions.Length; v++)
                {
                    if (!vertexAlive[v]) continue;

                    compacted[v] = vertices.Count;
                    vertices.Add(positions[v]);
                }

                List<int> indices = new List<int>(aliveTriangles * 3);
                for (int t = 0; t < triangleAlive.Length; t++)
                {
                    if (!triangleAlive[t]) continue;

                    Corners(t, out int a, out int b, out int c);
                    indices.Add(compacted[a]);
                    indices.Add(compacted[b]);
                    indices.Add(compacted[c]);
                }

                return original.Replacing(vertices.ToArray(), indices.ToArray());
            }

            private void Corners(int triangle, out int a, out int b, out int c)
            {
                a = corners[triangle * 3];
                b = corners[triangle * 3 + 1];
                c = corners[triangle * 3 + 2];
            }

            private static long PackEdge(int a, int b)
            {
                return ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
            }

            // Binary min-heap holding the candidates inline. The shared MinHeap keys int payloads
            // by float, which forced the caller to keep an ever-growing side list of candidates to
            // index into - it was never trimmed, so a long run held every entry it had ever costed.
            // Storing the struct itself keeps the heap the only allocation and lets the key stay a
            // double, which matters: the costs that decide the ordering here are routinely many
            // orders of magnitude apart and float collapses the small ones together.
            private sealed class CandidateHeap
            {
                private Candidate[] items = new Candidate[64];
                private int count;

                public int Count => count;

                public void Push(in Candidate item)
                {
                    if (count == items.Length) System.Array.Resize(ref items, count * 2);

                    int child = count++;
                    while (child > 0)
                    {
                        int parent = (child - 1) / 2;
                        if (items[parent].Cost <= item.Cost) break;

                        items[child] = items[parent];
                        child = parent;
                    }
                    items[child] = item;
                }

                public Candidate Pop()
                {
                    Candidate result = items[0];
                    Candidate last = items[--count];

                    int parent = 0;
                    while (true)
                    {
                        int left = parent * 2 + 1;
                        if (left >= count) break;

                        int smallest = left + 1 < count && items[left + 1].Cost < items[left].Cost ? left + 1 : left;
                        if (items[smallest].Cost >= last.Cost) break;

                        items[parent] = items[smallest];
                        parent = smallest;
                    }

                    if (count > 0) items[parent] = last;
                    return result;
                }
            }
        }

        // The symmetric 4x4 that gives a point's summed squared distance to a set of planes.
        // Doubles throughout: these are squared quantities on coordinates that may already be
        // sub-millimetre, and float would lose the small differences the ordering depends on.
        private struct Quadric
        {
            private double a00, a01, a02, a03, a11, a12, a13, a22, a23, a33;

            public static Quadric FromPlane(Vector3 normal, float offset, float weight)
            {
                double x = normal.x, y = normal.y, z = normal.z, w = offset;
                return new Quadric
                {
                    a00 = x * x * weight, a01 = x * y * weight, a02 = x * z * weight, a03 = x * w * weight,
                    a11 = y * y * weight, a12 = y * z * weight, a13 = y * w * weight,
                    a22 = z * z * weight, a23 = z * w * weight,
                    a33 = w * w * weight
                };
            }

            public void Add(in Quadric other)
            {
                a00 += other.a00; a01 += other.a01; a02 += other.a02; a03 += other.a03;
                a11 += other.a11; a12 += other.a12; a13 += other.a13;
                a22 += other.a22; a23 += other.a23;
                a33 += other.a33;
            }

            // Mathematically a sum of squared distances and so never negative, but it is evaluated
            // as a difference of accumulated terms - a33 alone sums weight * (plane offset)^2 over
            // every incident face, scaled by BoundaryPenalty on a rim - and a point sitting ON all
            // of those planes cancels to somewhere around -1e-18 rather than to zero. Clamping is
            // what makes the returned figure mean what every caller assumes it means.
            public double Error(Vector3 point)
            {
                double x = point.x, y = point.y, z = point.z;
                double error = a00 * x * x + 2 * a01 * x * y + 2 * a02 * x * z + 2 * a03 * x
                             + a11 * y * y + 2 * a12 * y * z + 2 * a13 * y
                             + a22 * z * z + 2 * a23 * z
                             + a33;
                return error > 0.0 ? error : 0.0;
            }
        }
    }
}
#endif
