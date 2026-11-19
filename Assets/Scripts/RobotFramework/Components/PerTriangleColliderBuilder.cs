#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace RobotFramework.Components
{
    // =====================================================================================
    // THE COLLIDER GENERATION ALGORITHM. This file is the one to edit when improving it -
    // everything it needs is either in Settings or in ColliderGeometry.cs (mesh access,
    // adjacency, feature weights, hulls, prisms). It touches no GameObjects, no assets, no
    // Editor API and no Transform, so it can be reasoned about and rewritten on its own.
    // =====================================================================================
    //
    // THE PROBLEM: Unity forces every MeshCollider under a non-kinematic Rigidbody to be
    // convex, and one convex hull fills in any inward curve or notch on a concave part. So
    // instead of one hull for the whole mesh, cover the surface with many small convex shells.
    //
    // THE CONTRACT - what Build must return for the caller to be able to use it:
    //   * Meshes in the SOURCE MESH'S LOCAL SPACE. No transforms are applied anywhere; the
    //     caller parents them under the source object and folds its scale into Settings first.
    //   * Each mesh must be genuinely CONVEX and CLOSED WITH REAL VOLUME. PhysX cooks these
    //     with mesh cleaning on but welding and inflation OFF (both visibly collapse shapes
    //     this thin), so a flat or self-intersecting mesh cooks into nothing and silently
    //     fails to collide - it does not throw.
    //   * Nothing may stick out past the source surface. This is the load-bearing invariant:
    //     REJECT, DON'T APPROXIMATE. A rejected patch leaves a small gap a game piece might
    //     clip; a bad one is a blade jutting into open space that swats pieces away. Gaps are
    //     recoverable, phantom collisions are not.
    //   * Fewer, bigger shells are better - collider count is what costs at runtime.
    //
    // FEATURE WEIGHTING reconciles those last two goals. Every vertex gets a 0..1 score
    // (FeatureWeights: sharp dihedrals and open rims score high, flat interior scores 0) and both
    // stages use it, because the vertices that define what a game piece actually hits are the
    // creases and rims - interior vertices in the middle of a flat span are nearly free to
    // approximate. So: grow patches through the cheap interior first and stop at the features,
    // and fit each patch's plane to the features rather than to whatever vertices are numerous.
    //
    // THE STAGES, which are also the places to intervene:
    //   0. ExtractSweptBands - OPTIONAL, off by default. Claims the triangles forming a closed
    //      convex band swept along an axis (a shaft, a boss, a round or hex standoff) and emits
    //      ONE inscribed prism for the whole solid, before anything planar is attempted. Nothing
    //      it claims reaches stage 1.
    //   1. Partition - triangles into patches, each of which SHOULD be coverable by one convex
    //      shell. Weighted region growing.
    //   2. TryAnalysePatch - a patch into a validated planar footprint, or a reason it can't be.
    //      Enforces the contract; everything downstream trusts its output.
    //   3. PairOpposingFaces + emit - a wall's two surface patches become ONE solid slab
    //      collider rather than two hollow shells, and any patch left over that falls inside a
    //      solid is dropped rather than shelled on top of it. See the notes on it below.
    //
    // SOLIDS AND PRIMITIVE FITTING. Stages 0 and 3 both produce a Solid: a closed convex volume
    // that can say whether a point is inside it, so leftover patches can be retired against it,
    // and that knows how to become a collider. Every solid - and every lone shell - goes through
    // the same TryFitPrimitive hook on the way out, which substitutes a real PhysX primitive
    // wherever the shape allows one and falls back to a cooked hull otherwise. That hook is the
    // single place to add a primitive; nothing else needs to know one was added.
    //
    // SCALE: local coordinates here are routinely sub-millimetre (parts sit under parents with
    // a large uniform scale), so every tolerance is relative to the geometry's own size and
    // every normalize goes through GeometryUtil.Normalized. See the header of
    // ColliderGeometry.cs before adding math.
    //
    // TESTING: the component's Log Diagnostics button runs Build with a Report attached and
    // prints patch/pair counts and every rejection, and the Scene view gizmos are this exact
    // code path - so a change here is visible without entering Play mode or generating anything.
    internal static class PerTriangleColliderBuilder
    {
        // Share of the LARGER face's footprint that the two must have in common to count as one
        // wall. Measured as area, so it is blind to how either side was tessellated or decimated -
        // the corner-counting version this replaced was really measuring tessellation, and failed
        // on the parts that most needed pairing. Loose enough to tolerate two faces that were
        // simplified independently, tight enough that a small patch cannot pair with a large one
        // it merely faces.
        private const float MinSharedFootprint = 0.6f;

        // The merge tolerance used when angled merging is switched off. Not zero: an exporter's
        // triangulation of one flat face still carries a hair of floating-point normal variation,
        // and this is wide enough to rejoin those and nothing else.
        private const float CoplanarOnlyAngle = 0.5f;

        // With angled merging off, a merged patch is only kept if its outline encloses the area
        // its triangles actually have - meaning the triangles' union was already convex and the
        // merge changed no geometry whatsoever. Anything else (a notch, a cutout, a bolt hole)
        // would have its hull bridge straight across the missing part, so it falls back to the
        // individual triangles instead. This is what keeps merging free: it may buy a box or a
        // slab, but it may never cost fidelity the per-triangle path would have kept.
        private const float LosslessMergeRatio = 1.02f;

        // How far from exactly antiparallel two faces may be and still be read as the two sides of
        // one wall. This used to borrow Merge Angle, which was wrong twice over: that setting is
        // about how far a patch may drift while growing - a different question - and it is inert
        // when angled merging is off, so pairing silently kept a 12 degree tolerance a coplanar
        // build never asked for. Real walls are parallel to well under a degree; the slack here is
        // for draft angles and export noise, not for genuinely tapered geometry.
        private const float OpposedAngleTolerance = 5f;

        // --- Swept band (stage 0) constants -----------------------------------------------
        // These describe what a band IS, so they live here rather than in Settings; the two things
        // a user would want to steer - how sharp a step still counts as a smooth facet, and how
        // round the ring has to be - are already exposed as Sharp Edge Angle and Max Surface Curve.

        // Below this the two seed faces are coplanar and their cross product is noise, not an axis.
        private const float MinBandDihedral = 0.5f;

        // How far a facet's normal may tilt off perpendicular to the axis. Tight on purpose: a
        // tapered surface tilts consistently, and a prism of constant cross-section built across a
        // taper stands proud of the narrow end - the one thing the contract forbids.
        private const float MaxBandAxisTilt = 3f;

        // The band must CLOSE around its axis, which this measures as the widest angular gap
        // between neighbouring points of the ring. An open arc is rejected outright, because the
        // prism for one bridges its own chord: for a 90 degree fillet that fills a wedge of space
        // the part may not occupy at all. A closed band has no chord to bridge - the prism is the
        // shaft. 125 admits a triangular prism (120 degree steps) and nothing less closed.
        private const float MaxBandAngularGap = 125f;

        // Fewer facets than this is a chamfer, not a swept surface, and the planar path already
        // handles it exactly.
        private const int MinBandFacets = 3;

        // How far two axes may diverge and still be read as coaxial, when deciding whether a bore
        // runs down the middle of a band.
        private const float CoaxialAngleTolerance = 2f;

        // One collider for the caller to instantiate: either a convex mesh, or a box, which
        // PhysX handles far more cheaply than any hull. Center and Rotation are in the source
        // mesh's local space; the caller puts them on the child's Transform.
        internal readonly struct ColliderShape
        {
            public readonly Mesh Mesh; // null when this is a box
            public readonly Vector3 Center;
            public readonly Vector3 Size;
            public readonly Quaternion Rotation;

            private ColliderShape(Mesh mesh, Vector3 center, Vector3 size, Quaternion rotation)
            {
                Mesh = mesh;
                Center = center;
                Size = size;
                Rotation = rotation;
            }

            public bool IsBox => Mesh == null;

            public static ColliderShape FromMesh(Mesh mesh) => new(mesh, Vector3.zero, Vector3.zero, Quaternion.identity);

            public static ColliderShape FromBox(Vector3 center, Vector3 size, Quaternion rotation) => new(null, center, size, rotation);
        }

        // Where a 2D outline sits in the mesh. Every shape this file emits is some convex outline
        // swept along an axis, and this is the frame that outline is written in: Origin plus U and
        // V span the plane it lives on, Normal is what it sweeps along. A patch's frame is its
        // fitted plane; a swept band's is the cross-section about its axis. Introducing it is what
        // lets the primitive fitting below be written once instead of once per solid kind.
        private readonly struct Frame
        {
            public readonly Vector3 Origin;
            public readonly Vector3 U;
            public readonly Vector3 V;
            public readonly Vector3 Normal;

            public Frame(Vector3 origin, Vector3 u, Vector3 v, Vector3 normal)
            {
                Origin = origin;
                U = u;
                V = v;
                Normal = normal;
            }

            public Vector3 At(Vector2 flat, float along) => Origin + U * flat.x + V * flat.y + Normal * along;
        }

        // A closed convex volume produced by merging several surfaces: stage 0's swept prism and
        // stage 3's slab. Both answer the same two questions, which is all the emit loop asks of
        // either - what collider are you, and does this leftover patch already sit inside you.
        private abstract class Solid
        {
            public abstract ColliderShape Build(Settings settings);

            // tolerance is a real distance in local units, applied in every axis.
            public abstract bool Contains(Vector3 point, float tolerance);
        }

        // All lengths/areas are in the source mesh's LOCAL space - the caller converts from the
        // world-space millimetres the Inspector exposes.
        internal struct Settings
        {
            public float DissolveAngle;     // 0 leaves the source topology alone
            public float DecimateRatio;     // 1 leaves the source mesh alone
            public float Thickness;
            public float MinPatchArea;
            public bool Merge;
            public float MergeAngle;
            public bool MergeSweptBands;    // stage 0: shafts and bosses become one prism
            public float MaxHullAreaRatio;
            public float MaxPlaneDeviation;
            public float InternalGapJump;   // widest interior hole a patch's outline may bridge
            public float FeatureAngle;      // dihedral at which an edge counts as a full feature
            public float EdgeVertexBias;    // how much more a feature vertex counts when fitting a plane
            public bool PairOpposingFaces;
            public float MaxWallThickness;  // widest gap between two faces still treated as one wall
            public bool UseBoxColliders;
            public float BoxFitTolerance;   // extra area, as a fraction, a box may enclose
        }

        // Optional feedback for tuning and debugging; costs nothing when not passed.
        internal sealed class Report
        {
            public int SourceTriangles;
            public int DissolvedTriangles;
            public int DecimatedTriangles;
            public int DuplicateTriangles;
            public int Patches;
            public int FallbackPatches;
            public int PairedPatches;
            public int EnclosedPatches;
            public int Boxes;
            public int SweptBands;
            public int SweptBandTriangles;
            public int BridgedPatches;
            public int BridgedHoles;
            public readonly List<string> Rejections = new();
        }

        internal static List<ColliderShape> Build(Mesh source, Settings settings, Report report = null)
        {
            List<ColliderShape> colliders = new List<ColliderShape>();
            if (!SourceGeometry.TryRead(source, out SourceGeometry geometry))
            {
                return colliders;
            }

            // Without angled merging the only patches are coplanar ones, and those are worth
            // keeping only where merging was free - see LosslessMergeRatio. Anything looser lets a
            // merged face bridge across its own cutouts, which is exactly what per-triangle
            // generation never does.
            //
            // This limit belongs to PATCHES alone. Stage 3 asks a different question of a slab -
            // whether two separate surfaces describe one wall - and answers it with the user's own
            // tolerance, because two faces are modelled and simplified independently and their
            // outlines are never going to agree to within a couple of percent.
            float patchHullLimit = settings.Merge
                ? settings.MaxHullAreaRatio
                : Mathf.Min(settings.MaxHullAreaRatio, LosslessMergeRatio);

            int sourceTriangles = geometry.TriangleCount;

            // Dissolve first: it only removes vertices that describe nothing, so it costs no
            // fidelity and leaves decimation a cleaner mesh to work on. Much of what decimation
            // used to be reached for is really this - stray vertices splitting straight edges and
            // subdividing flat faces - and this removes them without moving anything.
            geometry = MeshDecimation.Dissolve(geometry, settings.DissolveAngle);
            int dissolvedTriangles = geometry.TriangleCount;

            // The colliders already sit half a thickness proud of the surface, so letting
            // decimation move it by at most that keeps the simplification inside fuzz the output
            // already carries - and makes collapsing a wall's two faces together, which costs the
            // full wall thickness, unaffordable.
            geometry = MeshDecimation.Decimate(geometry, settings.DecimateRatio, settings.Thickness * 0.5f);

            Vector3[] normals = FaceNormals(geometry, out bool[] usable);
            TriangleAdjacency adjacency = TriangleAdjacency.Build(geometry, usable);
            FeatureWeights weights = FeatureWeights.Build(geometry, adjacency, normals, settings.FeatureAngle);

            // Stage 0 runs first and takes its triangles out of circulation (usable is cleared for
            // them), so the planar pipeline never sees a shaft it would only fracture into facets.
            List<Solid> solids = new List<Solid>();
            int bandTriangles = 0;
            if (settings.MergeSweptBands)
            {
                foreach (Prism prism in ExtractSweptBands(geometry, adjacency, normals, usable, settings))
                {
                    solids.Add(prism);
                    bandTriangles += prism.TriangleCount;
                }
            }

            List<List<int>> patches = Partition(geometry, adjacency, weights, normals, usable, settings);

            List<PatchShape> shapes = new List<PatchShape>(patches.Count);
            List<int> single = new List<int> { 0 };
            int fellBack = 0;
            int bridgedPatches = 0;
            int bridgedHoles = 0;

            foreach (List<int> patch in patches)
            {
                if (TryAnalysePatch(geometry, weights, patch, settings, patchHullLimit, out PatchShape shape, out string reason))
                {
                    shapes.Add(shape);
                    if (shape.BridgedHoles > 0)
                    {
                        bridgedPatches++;
                        bridgedHoles += shape.BridgedHoles;
                    }
                    continue;
                }

                if (patch.Count == 1)
                {
                    report?.Rejections.Add($"1 tri: {reason}");
                    continue;
                }

                // A refused patch used to leave a hole in the collision. It doesn't have to: the
                // triangles it was built from are each trivially flat and convex, so they always
                // describe their own surface exactly - that is the whole reason per-triangle
                // generation is the safe baseline. Merging can only ever be an improvement over
                // it, never a way to lose coverage.
                report?.Rejections.Add($"{patch.Count} tris: {reason} - fell back to individual triangles");
                fellBack++;

                foreach (int t in patch)
                {
                    single[0] = t;
                    if (TryAnalysePatch(geometry, weights, single, settings, patchHullLimit, out PatchShape triangle, out string triangleReason))
                    {
                        shapes.Add(triangle);
                    }
                    else
                    {
                        report?.Rejections.Add($"1 tri: {triangleReason}");
                    }
                }
            }

            int slabCount = 0;
            if (settings.PairOpposingFaces)
            {
                foreach (Slab slab in PairOpposingFaces(shapes, settings))
                {
                    solids.Add(slab);
                    slabCount++;
                }
            }

            foreach (Solid solid in solids)
            {
                colliders.Add(solid.Build(settings));
            }

            // Solids are emitted first so every patch can then be tested against them, INCLUDING
            // the two that formed each slab. A slab only covers the region its two faces share, so
            // whether a paired face is now redundant is a question about geometry, not about
            // bookkeeping: where the outlines matched, both faces fall inside the slab and drop
            // out; where one face reaches beyond the shared region, it keeps its own shell and
            // that surface stays covered. The same test retires the rim faces around a plate, and
            // the end caps of a shaft against the prism spanning it.
            int enclosed = 0;
            foreach (PatchShape shape in shapes)
            {
                if (EnclosedBySolid(shape, solids, settings.Thickness * 0.5f))
                {
                    enclosed++;
                    continue;
                }

                colliders.Add(BuildShell(shape, settings));
            }

            int boxes = 0;
            for (int i = 0; i < colliders.Count; i++)
            {
                if (colliders[i].IsBox) boxes++;
                else colliders[i].Mesh.name = $"ClusterHull{i}";
            }

            if (report != null)
            {
                report.SourceTriangles = sourceTriangles;
                report.DissolvedTriangles = dissolvedTriangles;
                report.DecimatedTriangles = geometry.TriangleCount;
                report.DuplicateTriangles = geometry.DuplicatesRemoved;
                report.Patches = patches.Count;
                report.FallbackPatches = fellBack;
                report.PairedPatches = slabCount * 2;
                report.EnclosedPatches = enclosed;
                report.Boxes = boxes;
                report.SweptBands = solids.Count - slabCount;
                report.SweptBandTriangles = bandTriangles;
                report.BridgedPatches = bridgedPatches;
                report.BridgedHoles = bridgedHoles;
            }

            return colliders;
        }

        // Unit face normals, plus which triangles are usable at all. Degenerate triangles are
        // dropped here and nowhere else; area is deliberately NOT judged per triangle, since many
        // tiny triangles can merge into one perfectly good patch (that test belongs in stage 2).
        private static Vector3[] FaceNormals(SourceGeometry geometry, out bool[] usable)
        {
            Vector3[] normals = new Vector3[geometry.TriangleCount];
            usable = new bool[geometry.TriangleCount];

            for (int t = 0; t < normals.Length; t++)
            {
                geometry.GetTriangle(t, out Vector3 a, out Vector3 b, out Vector3 c);
                if (GeometryUtil.IsDegenerate(a, b, c, out Vector3 cross)) continue;

                normals[t] = GeometryUtil.Normalized(cross);
                usable[t] = true;
            }

            return normals;
        }

        // --- Stage 1: triangles -> patches ------------------------------------------------

        // Weighted region growing. Two things make this produce fewer, larger patches than a
        // plain flood fill:
        //
        //   SEED ORDER. Seeds are taken in ascending feature weight, so patches start in the
        //   middle of flat spans and grow outward to the creases. Index order (what this used to
        //   use) routinely seeds on a crease, which splits one flat face into several patches
        //   that each stop where the last one already claimed the middle.
        //
        //   GROWTH ORDER. The frontier is a min-heap keyed by feature weight, so a patch absorbs
        //   every cheap interior triangle it can reach before it spends any of its flatness
        //   budget crossing a crease. With a FIFO queue the patch spends that budget on whatever
        //   the queue happened to reach first, and stops short with interior area left over.
        //
        // Growth is bounded incrementally, against the patch's RUNNING average plane rather than
        // its seed triangle: a candidate joins only if its normal is within MergeAngle of the
        // patch so far AND its vertices stay within the flatness budget. That's what stops the
        // old failure where a chain of individually-tolerable steps walked across a shallow fold
        // and got the whole patch rejected in stage 2 - now the patch just stops at the fold and
        // the far side becomes its own patch. The running plane is an approximation (it only
        // tests the candidate's own vertices, to stay O(1) per candidate); stage 2 still
        // re-checks the finished patch properly.
        private static List<List<int>> Partition(SourceGeometry geometry, TriangleAdjacency adjacency, FeatureWeights weights, Vector3[] normals, bool[] usable, Settings settings)
        {
            List<List<int>> patches = new List<List<int>>();

            // Merging always happens; the setting only controls how far a patch may drift from its
            // own plane while growing, and the DEFAULT is the coplanar-only floor.
            //
            // That floor is not optional, because stage 3 works on outlines: a face left split
            // into triangles has a triangular outline, which is never rectangular (so nothing
            // becomes a box) and never coincides with the independently-triangulated far side of a
            // wall (so nothing pairs). Rejoining coplanar triangles costs nothing in fidelity -
            // they already share a plane - and it is what makes boxes and slabs possible at all.
            //
            // Merging ACROSS an angle is the part that trades shape for count, and it is off by
            // default because it loses that trade often enough: a patch spanning real curvature
            // gets flattened onto a single plane, and the result stops resembling the surface it
            // came from. The checks in stage 2 catch the worst of it, but a collider that merely
            // passes them is not the same as one that fits.
            float mergeAngle = settings.Merge ? settings.MergeAngle : CoplanarOnlyAngle;

            List<int> seeds = new List<int>(geometry.TriangleCount);
            for (int t = 0; t < geometry.TriangleCount; t++)
            {
                if (usable[t]) seeds.Add(t);
            }
            seeds.Sort((a, b) => weights.Triangle(a).CompareTo(weights.Triangle(b)));

            float dotThreshold = Mathf.Cos(mergeAngle * Mathf.Deg2Rad);
            bool[] claimed = new bool[geometry.TriangleCount];
            int[] consideredBy = new int[geometry.TriangleCount];
            MinHeap frontier = new MinHeap();

            for (int s = 0; s < seeds.Count; s++)
            {
                int seed = seeds[s];
                if (claimed[seed]) continue;

                int patchId = s + 1; // 0 means "never considered", so ids start at 1
                List<int> patch = new List<int>();
                PatchPlane plane = new PatchPlane();
                frontier.Clear();

                Claim(seed);

                while (frontier.Count > 0)
                {
                    int candidate = frontier.Pop();
                    if (claimed[candidate]) continue;
                    if (Vector3.Dot(plane.Normal, normals[candidate]) < dotThreshold) continue;
                    if (!plane.Accepts(geometry, candidate, settings.MaxPlaneDeviation)) continue;

                    Claim(candidate);
                }

                patches.Add(patch);

                void Claim(int triangle)
                {
                    claimed[triangle] = true;
                    patch.Add(triangle);
                    plane.Add(geometry, triangle);

                    foreach (int neighbour in adjacency.Neighbours(triangle))
                    {
                        // usable is also how stage 0 hands back what it did NOT take: adjacency was
                        // built before it ran, so without this check a patch would grow straight
                        // through a shaft that already has its own collider.
                        if (!usable[neighbour] || claimed[neighbour] || consideredBy[neighbour] == patchId) continue;

                        consideredBy[neighbour] = patchId;
                        frontier.Push(neighbour, weights.Triangle(neighbour));
                    }
                }
            }

            return patches;
        }

        // The patch's plane as it grows: area-weighted normal, centroid, and how far out the
        // footprint currently reaches. Deliberately cheap - one candidate test is O(1), not
        // O(patch size), so growth stays linear in the mesh.
        private struct PatchPlane
        {
            private Vector3 crossSum;   // area-weighted normal, unnormalized
            private Vector3 vertexSum;
            private int vertexCount;
            private float maxRadiusSqr;

            public Vector3 Normal => GeometryUtil.Normalized(crossSum);
            private Vector3 Centroid => vertexSum / vertexCount;

            public void Add(SourceGeometry geometry, int triangle)
            {
                crossSum += geometry.TriangleCross(triangle);

                geometry.GetTriangle(triangle, out Vector3 a, out Vector3 b, out Vector3 c);
                vertexSum += a + b + c;
                vertexCount += 3;

                Vector3 normal = Normal;
                Vector3 centroid = Centroid;
                maxRadiusSqr = Mathf.Max(maxRadiusSqr, RadiusSqr(a, centroid, normal), RadiusSqr(b, centroid, normal), RadiusSqr(c, centroid, normal));
            }

            // Would this triangle stay flat enough to belong? Tolerance is relative to the
            // footprint the patch would have WITH it, so a big patch is allowed proportionally
            // more absolute deviation than a small one - the same ratio stage 2 enforces.
            public bool Accepts(SourceGeometry geometry, int triangle, float maxDeviationRatio)
            {
                geometry.GetTriangle(triangle, out Vector3 a, out Vector3 b, out Vector3 c);

                Vector3 normal = Normal;
                Vector3 centroid = Centroid;
                float radiusSqr = Mathf.Max(maxRadiusSqr, RadiusSqr(a, centroid, normal), RadiusSqr(b, centroid, normal), RadiusSqr(c, centroid, normal));
                float allowed = Mathf.Sqrt(radiusSqr) * maxDeviationRatio;

                return Deviation(a, centroid, normal) <= allowed
                    && Deviation(b, centroid, normal) <= allowed
                    && Deviation(c, centroid, normal) <= allowed;
            }

            private static float Deviation(Vector3 point, Vector3 centroid, Vector3 normal)
            {
                return Mathf.Abs(Vector3.Dot(point - centroid, normal));
            }

            private static float RadiusSqr(Vector3 point, Vector3 centroid, Vector3 normal)
            {
                Vector3 offset = point - centroid;
                return (offset - normal * Vector3.Dot(offset, normal)).sqrMagnitude;
            }
        }

        // --- Stage 2: patch -> validated planar footprint ---------------------------------

        // A patch that passed every check, reduced to the plane it sits on and the outline it
        // covers. Everything downstream works from this rather than from triangles.
        private sealed class PatchShape
        {
            public Vector3 Normal;
            public Vector3 Centroid;
            public Vector3 U;
            public Vector3 V;
            public List<Vector3> Points;   // the patch's vertices, in mesh local space
            public List<Vector2> Flat;     // those points in this patch's own plane basis
            public List<int> Hull;         // indices into Flat/Points, around the outline
            public List<Vector2> Outline;  // the hull as points, in this patch's own basis
            public float Area;             // real summed triangle area, not the hull's
            public float OutlineArea;      // the hull's area
            public float Radius;           // how far the outline reaches from the centroid
            public float MinAlong;         // how far the patch strays off its own plane, either way
            public float MaxAlong;
            public int BridgedHoles;       // interior holes the outline was allowed to close over
            public PatchShape Partner;     // the far side of the same wall, if one was found

            public Frame Frame => new(Centroid, U, V, Normal);

            public Vector2 Project(Vector3 point)
            {
                Vector3 offset = point - Centroid;
                return new Vector2(Vector3.Dot(offset, U), Vector3.Dot(offset, V));
            }

            public float Along(Vector3 point) => Vector3.Dot(point - Centroid, Normal);
        }

        // Fit a plane, project, hull, and check the result actually represents the patch.
        //
        // The plane is fitted with feature vertices weighted up (EdgeVertexBias), so it aligns
        // with the creases and rims that define the collision surface instead of being dragged
        // around by whichever flat region happens to be most densely tessellated.
        //
        // The rejections below are the enforcement of "nothing sticks out", and they are measured
        // on EVERY vertex at full strength - weighting decides where the plane goes, never what
        // the collider is allowed to miss. Whatever replaces this, something equivalent has to
        // survive; they are not tuning knobs.
        private static bool TryAnalysePatch(SourceGeometry geometry, FeatureWeights weights, List<int> patch, Settings settings, float hullAreaLimit, out PatchShape shape, out string reason)
        {
            shape = null;

            HashSet<int> vertexIndices = new HashSet<int>();
            Vector3 weightedNormal = Vector3.zero;
            float maxEdgeSqr = 0f;
            float totalArea = 0f;

            foreach (int t in patch)
            {
                geometry.GetTriangleIndices(t, out int i0, out int i1, out int i2);
                vertexIndices.Add(i0);
                vertexIndices.Add(i1);
                vertexIndices.Add(i2);

                geometry.GetTriangle(t, out Vector3 a, out Vector3 b, out Vector3 c);
                Vector3 cross = Vector3.Cross(b - a, c - a);

                // |cross| is twice the area, so this is area-weighted already; scaling by the
                // triangle's mean vertex bias layers the feature weighting on top.
                float bias = (FitWeight(weights, i0, settings) + FitWeight(weights, i1, settings) + FitWeight(weights, i2, settings)) / 3f;
                weightedNormal += cross * bias;

                maxEdgeSqr = Mathf.Max(maxEdgeSqr, (b - a).sqrMagnitude, (c - a).sqrMagnitude);
                totalArea += cross.magnitude * 0.5f;
            }

            if (vertexIndices.Count < 3 || maxEdgeSqr < 1e-14f || weightedNormal.sqrMagnitude < maxEdgeSqr * maxEdgeSqr * 1e-10f)
            {
                reason = "degenerate patch";
                return false;
            }

            // Judged on the patch's combined area, which is what lets tiny triangles earn a
            // collider by merging rather than being dropped one by one.
            if (totalArea < settings.MinPatchArea)
            {
                reason = $"below minimum size ({totalArea:G3} < {settings.MinPatchArea:G3})";
                return false;
            }

            PatchShape candidate = new PatchShape { Normal = GeometryUtil.Normalized(weightedNormal), Area = totalArea };
            GeometryUtil.PlaneBasis(candidate.Normal, out candidate.U, out candidate.V);

            Vector3 centroid = Vector3.zero;
            float weightSum = 0f;
            foreach (int vi in vertexIndices)
            {
                float weight = FitWeight(weights, vi, settings);
                centroid += geometry.Vertices[vi] * weight;
                weightSum += weight;
            }
            candidate.Centroid = centroid / weightSum;

            candidate.Points = new List<Vector3>(vertexIndices.Count);
            candidate.Flat = new List<Vector2>(vertexIndices.Count);
            candidate.MinAlong = float.MaxValue;
            candidate.MaxAlong = float.MinValue;
            float maxRadiusSqr = 0f;
            Dictionary<int, int> flatOf = new Dictionary<int, int>(vertexIndices.Count);
            foreach (int vi in vertexIndices)
            {
                flatOf[vi] = candidate.Flat.Count;
                Vector3 p = geometry.Vertices[vi];
                Vector2 flat = candidate.Project(p);
                float along = candidate.Along(p);

                candidate.Points.Add(p);
                candidate.Flat.Add(flat);
                candidate.MinAlong = Mathf.Min(candidate.MinAlong, along);
                candidate.MaxAlong = Mathf.Max(candidate.MaxAlong, along);
                maxRadiusSqr = Mathf.Max(maxRadiusSqr, flat.sqrMagnitude);
            }
            float maxDeviation = Mathf.Max(Mathf.Abs(candidate.MinAlong), Mathf.Abs(candidate.MaxAlong));

            candidate.Hull = GeometryUtil.ConvexHull2D(candidate.Flat);
            if (candidate.Hull.Count < 3)
            {
                reason = $"hull collapsed to {candidate.Hull.Count} points";
                return false;
            }

            // Is the patch actually flat? Growth bounds this incrementally against a moving
            // plane, which is an approximation; this is the real test, on the final plane, on
            // every vertex, relative to the patch's own footprint.
            candidate.Radius = Mathf.Sqrt(maxRadiusSqr);
            if (candidate.Radius > 1e-9f && maxDeviation > candidate.Radius * settings.MaxPlaneDeviation)
            {
                reason = $"not flat (deviation {maxDeviation:G3} vs radius {candidate.Radius:G3})";
                return false;
            }

            // Kept rather than recomputed: pairing compares every patch against every other, so
            // anything derived from the outline is asked for thousands of times per patch.
            candidate.Outline = new List<Vector2>(candidate.Hull.Count);
            foreach (int index in candidate.Hull)
            {
                candidate.Outline.Add(candidate.Flat[index]);
            }
            candidate.OutlineArea = GeometryUtil.PolygonArea(candidate.Outline);

            // A bolt hole in the middle of a face is missing area like any other, so the bulge test
            // below refuses the face and it falls back to loose triangles - over a hole nothing can
            // pass through. Interior holes small enough to be jumped are measured and credited back
            // here, so the test still sees the whole face while judging only the parts that matter.
            // Note this credits AREA, not licence: a hole too wide to jump is not credited, and the
            // patch is then rejected exactly as it is today.
            HoleCensus holes = BridgeableHoleArea(geometry, patch, candidate, flatOf, settings.InternalGapJump);
            candidate.BridgedHoles = holes.Bridged;

            // Does the hull stay on the surface? A patch spanning real curvature, or one with a
            // concave outline (a hull always bridges straight across a notch), covers more area
            // flattened than its triangles actually have. That excess is exactly the part that
            // would stick out into open space.
            float coverable = totalArea + holes.Area;
            if (candidate.OutlineArea > coverable * hullAreaLimit)
            {
                // The excess is what has to be explained away, and the census says how much of it
                // the holes accounted for - so "the bar is too low" and "this face is concave and
                // no hole setting will ever help it" stop looking the same.
                reason = $"hull bulges past the patch's real area (outline {candidate.OutlineArea:G3} vs {coverable:G3} covered; {holes})";
                return false;
            }

            shape = candidate;
            reason = null;
            return true;
        }

        // 1 for a flat-interior vertex, 1 + EdgeVertexBias for a full feature vertex. Never
        // zero: a low-weight vertex still has to be covered, it just doesn't get to steer.
        private static float FitWeight(FeatureWeights weights, int vertexIndex, Settings settings)
        {
            return 1f + settings.EdgeVertexBias * weights.Vertex(vertexIndex);
        }

        // What the hole census found, so a rejection can say which of the several quite different
        // things went wrong. Without this every outcome reads as the same "hull bulges" line: no
        // holes there at all, holes found but too wide, holes jumped and the patch refused for some
        // other reason, or a boundary that wouldn't chain into loops in the first place. Those want
        // opposite responses from whoever is tuning the part.
        private struct HoleCensus
        {
            public bool Chained;    // false means the patch's boundary isn't a set of simple loops
            public int Interior;    // loops that are holes rather than the patch's outer rim
            public int Bridged;
            public float Widest;    // widest interior hole, whether it was jumped or not
            public float Area;      // area credited back to the bulge test

            public override string ToString()
            {
                if (!Chained) return "boundary didn't chain into loops, so no hole could be identified";
                if (Interior == 0) return "no interior holes";
                return $"{Bridged}/{Interior} interior holes jumped, widest {Widest:G3}";
            }
        }

        // How much of the area "missing" from this patch is interior holes small enough to jump.
        //
        // A face with a bolt hole in it has a ring of triangles around the hole and none across it,
        // so its convex outline encloses more area than its triangles have and the bulge test
        // refuses it - correctly in general, since that is the same measurement that catches a
        // patch bridging a real notch or a fold. But over a small hole there is nothing to catch:
        // no game piece fits through it, and refusing the face costs the whole face, which falls
        // back to loose triangles and takes its box and its slab pairing down with it.
        //
        // INTERIOR is the load-bearing word, and it is decided structurally rather than by
        // guessing. The patch's boundary edges - those used by exactly one of its triangles - chain
        // into closed loops; the loop enclosing the most area is the patch's outer rim and every
        // other loop is a hole punched through it. A notch cut into the RIM leaves one loop, not
        // two, so it is never a hole here however small it is, and open space beyond a patch's edge
        // stays open space. That is the difference between jumping a hole and inflating the patch.
        //
        // Size is the hole's diameter about its own centre, which is what "would something fit
        // through this" actually asks. A slot longer than the bar therefore stays a slot even
        // though it is narrow - deliberately: a piece can travel along it.
        private static HoleCensus BridgeableHoleArea(SourceGeometry geometry, List<int> patch, PatchShape shape, Dictionary<int, int> flatOf, float maxHoleSize)
        {
            HoleCensus census = new HoleCensus { Chained = true };
            if (maxHoleSize <= 0f || patch.Count < 3) return census;

            // Counted undirected so an edge shared by two of the patch's triangles is interior, but
            // REMEMBERED directed, by the winding of the triangle that used it - which is what lets
            // the loops chain below without any geometric guesswork about which way they run.
            Dictionary<long, int> seen = new Dictionary<long, int>(patch.Count * 3);
            List<int> fromVertex = new List<int>(patch.Count);
            List<int> toVertex = new List<int>(patch.Count);
            List<int> uses = new List<int>(patch.Count);

            foreach (int t in patch)
            {
                geometry.GetTriangleIndices(t, out int i0, out int i1, out int i2);
                Count(i0, i1);
                Count(i1, i2);
                Count(i2, i0);
            }

            void Count(int a, int b)
            {
                long key = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                if (seen.TryGetValue(key, out int slot))
                {
                    uses[slot]++;
                    return;
                }

                seen[key] = fromVertex.Count;
                fromVertex.Add(a);
                toVertex.Add(b);
                uses.Add(1);
            }

            Dictionary<int, List<int>> leaving = new Dictionary<int, List<int>>();
            int boundaryEdges = 0;
            for (int e = 0; e < uses.Count; e++)
            {
                if (uses[e] != 1) continue;

                if (!leaving.TryGetValue(fromVertex[e], out List<int> list))
                {
                    list = new List<int>(2);
                    leaving[fromVertex[e]] = list;
                }
                list.Add(e);
                boundaryEdges++;
            }

            if (boundaryEdges == 0) return census;

            // Chain the boundary into loops. Anything that doesn't chain - inconsistent winding, a
            // vertex where three boundary edges meet - means the patch's boundary isn't a set of
            // simple loops, and then "which loop is the outside" has no answer. Give up rather than
            // credit area on a guess.
            bool[] walked = new bool[uses.Count];
            List<List<int>> loops = new List<List<int>>();
            for (int e = 0; e < uses.Count; e++)
            {
                if (uses[e] != 1 || walked[e]) continue;

                List<int> loop = new List<int>();
                int edge = e;
                int start = fromVertex[e];
                while (true)
                {
                    walked[edge] = true;
                    loop.Add(fromVertex[edge]);

                    int at = toVertex[edge];
                    if (at == start) break;

                    if (loop.Count > boundaryEdges || !TryStep(at, out edge))
                    {
                        census.Chained = false;
                        return census;
                    }
                }

                if (loop.Count >= 3) loops.Add(loop);
            }

            bool TryStep(int vertex, out int edge)
            {
                edge = -1;
                if (!leaving.TryGetValue(vertex, out List<int> list)) return false;

                foreach (int candidate in list)
                {
                    if (walked[candidate]) continue;

                    edge = candidate;
                    return true;
                }
                return false;
            }

            if (loops.Count < 2) return census; // no interior loops, so nothing to jump

            int outer = 0;
            float[] areas = new float[loops.Count];
            for (int i = 0; i < loops.Count; i++)
            {
                areas[i] = LoopArea(loops[i], shape, flatOf);
                if (areas[i] > areas[outer]) outer = i;
            }

            census.Interior = loops.Count - 1;
            for (int i = 0; i < loops.Count; i++)
            {
                if (i == outer) continue;

                float diameter = LoopDiameter(loops[i], shape, flatOf);
                census.Widest = Mathf.Max(census.Widest, diameter);
                if (diameter > maxHoleSize) continue;

                census.Area += areas[i];
                census.Bridged++;
            }

            return census;
        }

        private static float LoopArea(List<int> loop, PatchShape shape, Dictionary<int, int> flatOf)
        {
            float sum = 0f;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 a = shape.Flat[flatOf[loop[i]]];
                Vector2 b = shape.Flat[flatOf[loop[(i + 1) % loop.Count]]];
                sum += a.x * b.y - b.x * a.y;
            }
            return Mathf.Abs(sum) * 0.5f;
        }

        // Twice the furthest reach from the loop's own centre - exact for a round hole, and for an
        // irregular one it reads as the widest thing that could pass through, which is the
        // conservative direction.
        private static float LoopDiameter(List<int> loop, PatchShape shape, Dictionary<int, int> flatOf)
        {
            Vector2 centre = Vector2.zero;
            foreach (int vertex in loop)
            {
                centre += shape.Flat[flatOf[vertex]];
            }
            centre /= loop.Count;

            float furthest = 0f;
            foreach (int vertex in loop)
            {
                furthest = Mathf.Max(furthest, (shape.Flat[flatOf[vertex]] - centre).sqrMagnitude);
            }
            return 2f * Mathf.Sqrt(furthest);
        }

        // --- Stage 3: opposing faces -> solid slabs ---------------------------------------

        // A wall modelled as a shell has two surfaces, and shelling each one separately produces
        // two thin colliders with a HOLLOW CORE between them - twice the colliders, and a gap a
        // game piece can end up inside. Wherever both surfaces of the same wall survived stage 2,
        // pair them and emit ONE prism spanning from the front face to the back face. The span
        // comes from the mesh itself (how far apart the two fitted planes actually are), so the
        // collider is as thick as the real wall rather than an assumed thickness.
        //
        // Pairing requires, in ascending order of cost to test:
        //   * opposed normals, within OpposedAngleTolerance of antiparallel,
        //   * the far patch sitting BEHIND this one (a wall's other surface faces away),
        //   * a separation no wider than MaxWallThickness - what stops the top of a box pairing
        //     with its own floor,
        //   * enough shared footprint to be worth merging, and a span that stays within the wall
        //     limit across that shared region (Slab.TryBetween).
        //
        // Crucially it does NOT require the two outlines to resemble each other. They are separate
        // surfaces, tessellated independently and simplified independently, so demanding
        // correspondence made pairing fail on exactly the parts that most needed it - and a failed
        // pair costs three ways at once, since the wall becomes two shells AND its rim shells stop
        // being swallowed. Working from the shared region alone removes the requirement outright.
        //
        // O(patches²), so what matters is that a rejected pair costs nothing. Every test before
        // Slab.TryBetween is a handful of arithmetic on cached values; TryBetween itself allocates
        // several lists, sorts a hull and clips two polygons, and a part with thousands of patches
        // offers up millions of candidates. Letting even a small fraction of those through to the
        // polygon work is the difference between an instant build and an apparent hang.
        //
        // The area and reach tests below are exact, not heuristics - each states a condition the
        // shared region provably cannot meet - so nothing valid is lost by front-running them.
        private static List<Slab> PairOpposingFaces(List<PatchShape> shapes, Settings settings)
        {
            float opposedThreshold = -Mathf.Cos(OpposedAngleTolerance * Mathf.Deg2Rad);
            List<Slab> slabs = new List<Slab>();

            for (int i = 0; i < shapes.Count; i++)
            {
                PatchShape front = shapes[i];
                if (front.Partner != null) continue;

                PatchShape best = null;
                Slab bestSlab = null;
                float bestSeparation = float.MaxValue;

                for (int j = i + 1; j < shapes.Count; j++)
                {
                    PatchShape back = shapes[j];
                    if (back.Partner != null) continue;
                    if (Vector3.Dot(front.Normal, back.Normal) > opposedThreshold) continue;

                    // Negative means "behind me", which is where a wall's far surface lives.
                    float separation = -front.Along(back.Centroid);
                    if (separation <= 0f || separation > settings.MaxWallThickness) continue;
                    if (separation >= bestSeparation) continue;

                    // The shared region can be no larger than the smaller outline, and has to
                    // reach MinSharedFootprint of the larger one, so two faces of very different
                    // size can never qualify however they are arranged.
                    float smaller = Mathf.Min(front.OutlineArea, back.OutlineArea);
                    float larger = Mathf.Max(front.OutlineArea, back.OutlineArea);
                    if (smaller < larger * MinSharedFootprint) continue;

                    // Two outlines that don't come within reach of each other cannot overlap at
                    // all. Both radii are measured from their own centroids, so comparing them
                    // against the in-plane distance between those centroids is exact.
                    Vector2 offset = front.Project(back.Centroid);
                    float reach = front.Radius + back.Radius;
                    if (offset.sqrMagnitude > reach * reach) continue;

                    if (!Slab.TryBetween(front, back, settings, out Slab slab)) continue;

                    best = back;
                    bestSlab = slab;
                    bestSeparation = separation;
                }

                if (best == null) continue;

                front.Partner = best;
                best.Partner = front;
                slabs.Add(bestSlab);
            }

            return slabs;
        }

        // A patch's outline flattened into some other patch's plane basis, so two of them can be
        // compared in one shared frame.
        private static List<Vector2> OutlineIn(PatchShape shape, PatchShape basis)
        {
            List<Vector2> outline = new List<Vector2>(shape.Hull.Count);
            foreach (int index in shape.Hull)
            {
                outline.Add(basis.Project(shape.Points[index]));
            }
            return outline;
        }

        // --- Emitting -----------------------------------------------------------------------

        // One surface, extruded half the thickness either side of it. Used wherever a patch has
        // no opposing face - an open rim, a one-sided surface, or a wall thicker than
        // MaxWallThickness.
        //
        // The mesh keeps the patch's raw vertices, so it follows what little curvature the patch
        // has rather than flattening it; only the box substitution flattens, and only when the
        // outline is square enough that there is nothing to flatten.
        private static ColliderShape BuildShell(PatchShape shape, Settings settings)
        {
            List<Vector2> outline = new List<Vector2>(shape.Hull.Count);
            foreach (int index in shape.Hull)
            {
                outline.Add(shape.Flat[index]);
            }

            if (TryFitPrimitive(shape.Frame, outline, shape.MinAlong, shape.MaxAlong, settings, out ColliderShape primitive))
            {
                return primitive;
            }

            return ColliderShape.FromMesh(GeometryUtil.ExtrudePrism(shape.Points, shape.Hull, shape.Normal * (settings.Thickness * 0.5f)));
        }

        // THE PRIMITIVE HOOK. Everything this file emits arrives here first: a lone shell, a slab
        // spanning a wall, a prism spanning a shaft. All three are the same thing by the time they
        // reach it - a convex outline in a Frame, swept from minAlong to maxAlong - so a primitive
        // that fits one fits all three, and adding one means adding a case here and nothing else.
        //
        // Order is cheapest-first and the first fit wins, since a shape that fits two primitives
        // fits the cheaper one just as tightly. Anything that fits none falls back to a cooked
        // convex hull, which is always correct and always the most expensive option.
        //
        // What a primitive may NOT do is enclose space the hull wouldn't have: the fallback is the
        // reference shape, and every case here has to justify its own slack against it.
        private static bool TryFitPrimitive(Frame frame, List<Vector2> outline, float minAlong, float maxAlong, Settings settings, out ColliderShape shape)
        {
            shape = default;

            // A sphere would go here, and a capsule; neither has ever fitted anything a robot part
            // decomposes into without rounding off a corner that was square, so neither is written.
            return settings.UseBoxColliders && TryAsBox(frame, outline, minAlong, maxAlong, settings, out shape);
        }

        // The fallback the hook above falls back TO: the outline placed on the mid-plane of its
        // span and extruded both ways, which is the same construction for a slab and for a swept
        // band. Built from the mid-plane rather than from source vertices because those span the
        // whole solid, and extruding them raw would give a skewed prism.
        private static Mesh Sweep(Frame frame, List<Vector2> outline, float minAlong, float maxAlong, float margin)
        {
            float middle = (maxAlong + minAlong) * 0.5f;

            List<Vector3> points = new List<Vector3>(outline.Count);
            List<int> order = new List<int>(outline.Count);
            foreach (Vector2 flat in outline)
            {
                order.Add(points.Count);
                points.Add(frame.At(flat, middle));
            }

            return GeometryUtil.ExtrudePrism(points, order, frame.Normal * ((maxAlong - minAlong) * 0.5f + margin));
        }

        // An extruded rectangle is exactly a box, and PhysX resolves a box far more cheaply than a
        // convex hull of the same shape - no hull cooking, no support-mapping search. Flat panels
        // are most of what a robot part decomposes into, so this is worth checking on every
        // collider before falling back to a mesh.
        //
        // The test is how much EMPTY SPACE a box would add: fit the tightest rectangle around the
        // outline and compare its area to the outline's own. That measures the actual consequence
        // of substituting, and it is blind to how the outline happens to be subdivided - which
        // matters, because a rectangle that kept a vertex partway along one edge is a pentagon,
        // and an earlier version of this test, which demanded four corners all square, rejected
        // every one of them. Tessellation and decimation leave those vertices everywhere.
        //
        // Depth is untouched by the tolerance: the box always spans the patch's full along-normal
        // range plus the usual thickness, so it encloses every vertex the mesh version would have.
        // Only the outline is ever loosened, and only by BoxFitTolerance.
        private static bool TryAsBox(Frame frame, List<Vector2> outline, float minAlong, float maxAlong, Settings settings, out ColliderShape box)
        {
            box = default;

            float outlineArea = GeometryUtil.PolygonArea(outline);
            if (outlineArea <= 0f) return false;

            float rectangleArea = GeometryUtil.MinimumAreaRectangle(outline, out Vector2 centre, out Vector2 axis, out Vector2 size);

            // The epsilon is there so a true rectangle, whose two areas differ only in the last
            // few bits, still substitutes at a tolerance of zero.
            if (rectangleArea - outlineArea > outlineArea * (settings.BoxFitTolerance + 1e-5f)) return false;

            float middle = (maxAlong + minAlong) * 0.5f;

            // The box's axes come from the fitted rectangle, not from the frame's arbitrary in-plane
            // basis - an outline is almost never aligned to that.
            Vector3 width = GeometryUtil.Normalized(frame.U * axis.x + frame.V * axis.y);
            Vector3 height = Vector3.Cross(frame.Normal, width);

            // Z to the sweep axis and Y across the rectangle puts X along its width, matching
            // the size components below.
            box = ColliderShape.FromBox(
                frame.At(centre, middle),
                new Vector3(size.x, size.y, maxAlong - minAlong + settings.Thickness),
                Quaternion.LookRotation(frame.Normal, height));
            return true;
        }

        // The solid volume between a wall's two surfaces. This is the shape of the collider AND
        // the region other patches get tested against - the two must be the same thing, or a slab
        // fails to retire the shells it was meant to replace and the pairing costs colliders
        // instead of saving them.
        //
        // The two questions pulling in opposite directions here are worth keeping straight.
        // Whether to pair is decided by the SHARED area, which no amount of mismatched
        // tessellation can confuse. How far the slab reaches is decided by the UNION, because
        // anything smaller leaves each face sticking out of the volume meant to replace it. The
        // union's licence to over-reach is then bounded by the same bulge check a single patch
        // gets, which is what stops it bridging across a step between two faces that only roughly
        // agree.
        private sealed class Slab : Solid
        {
            public readonly PatchShape Front;
            public readonly List<Vector2> Outline;  // the wall's footprint, in Front's plane basis
            public readonly float MinAlong;
            public readonly float MaxAlong;

            // A bounding circle on the outline, so the containment test can reject in a few
            // arithmetic operations. Every patch is tested against every slab, which is the other
            // place a part with thousands of patches can quietly become quadratic.
            private readonly Vector2 centre;
            private readonly float radius;

            private Slab(PatchShape front, List<Vector2> outline, float minAlong, float maxAlong)
            {
                Front = front;
                Outline = outline;
                MinAlong = minAlong;
                MaxAlong = maxAlong;

                centre = Vector2.zero;
                foreach (Vector2 corner in outline)
                {
                    centre += corner;
                }
                centre /= outline.Count;

                float furthest = 0f;
                foreach (Vector2 corner in outline)
                {
                    furthest = Mathf.Max(furthest, (corner - centre).sqrMagnitude);
                }
                radius = Mathf.Sqrt(furthest);
            }

            public static bool TryBetween(PatchShape front, PatchShape back, Settings settings, out Slab slab)
            {
                slab = null;

                List<Vector2> frontOutline = front.Outline; // already in front's own basis
                List<Vector2> backOutline = OutlineIn(back, front);

                // DETECTION - are these two the same wall? Answered by the area they share, which
                // is independent of how either side was tessellated or decimated. Measured against
                // the LARGER face, because the slab is going to span both of them: a small patch
                // opposite a big one is a boss on a plate, not a wall, and spanning that pair
                // would extrude the whole plate to the boss's depth.
                float larger = Mathf.Max(front.OutlineArea, GeometryUtil.PolygonArea(backOutline));

                List<Vector2> shared = GeometryUtil.IntersectConvex(frontOutline, backOutline);
                if (shared.Count < 3 || larger <= 0f) return false;
                if (GeometryUtil.PolygonArea(shared) < larger * MinSharedFootprint) return false;

                // EXTENT - a different question, and the one I got wrong by answering it with the
                // shared region too. A slab only pays for itself by RETIRING the two shells it
                // replaces, and a patch is only retired once the slab covers it; an intersection
                // is smaller than both faces, so with any mismatch at all it covered neither, and
                // the pair added a collider instead of saving two. The extent has to be the union.
                // What keeps that honest is the bulge check below, exactly as for a single patch.
                List<Vector2> combined = new List<Vector2>(frontOutline.Count + backOutline.Count);
                combined.AddRange(frontOutline);
                combined.AddRange(backOutline);

                List<int> hull = GeometryUtil.ConvexHull2D(combined);
                if (hull.Count < 3) return false;

                List<Vector2> outline = new List<Vector2>(hull.Count);
                foreach (int index in hull)
                {
                    outline.Add(combined[index]);
                }

                if (GeometryUtil.PolygonArea(outline) > larger * settings.MaxHullAreaRatio) return false;

                // Depth is read off the two fitted PLANES rather than off every vertex either
                // patch owns. Both are planar (they passed the flatness test), so each plane's
                // depth varies linearly and its extremes land on the outline's corners - no
                // stray vertex can dictate the thickness, and a taper shows up as a wide span
                // and is rejected below rather than being built as a thick straight wall.
                float opposition = Vector3.Dot(front.Normal, back.Normal);
                if (opposition > -1e-3f) return false;

                float minAlong = 0f; // the front patch's own plane, by definition
                float maxAlong = 0f;
                foreach (Vector2 corner in outline)
                {
                    Vector3 onFront = front.Centroid + front.U * corner.x + front.V * corner.y;
                    float depth = -Vector3.Dot(onFront - back.Centroid, back.Normal) / opposition;

                    minAlong = Mathf.Min(minAlong, depth);
                    maxAlong = Mathf.Max(maxAlong, depth);
                }

                if (maxAlong - minAlong > settings.MaxWallThickness) return false;

                slab = new Slab(front, outline, minAlong, maxAlong);
                return true;
            }

            // Convex, so this is just "between the two faces and inside the outline".
            public override bool Contains(Vector3 point, float tolerance)
            {
                float along = Front.Along(point);
                if (along < MinAlong - tolerance || along > MaxAlong + tolerance) return false;

                Vector2 flat = Front.Project(point);
                float reach = radius + tolerance;
                if ((flat - centre).sqrMagnitude > reach * reach) return false;

                return GeometryUtil.PolygonContains(Outline, flat, tolerance);
            }

            // One solid prism spanning the wall, half a thickness proud on EVERY side - across the
            // faces, and outward around the rim.
            //
            // The rim margin is the point. A shell is extruded along its own normal, so the little
            // shells on a wall's edge faces stick out sideways by half a thickness; a slab
            // swallows those shells, and without inflating its outline to match, the wall would
            // quietly become narrower than it was and lose exactly the tunnelling margin the
            // thickness exists to provide. A lone shell must never present a different surface
            // than a paired one.
            //
            // Only slabs do this. Inflating a lone shell's outline would push it sideways over
            // whatever surface sits next to it, which has its own collider - it's the fact that
            // the edge faces are gone that makes it right here.
            public override ColliderShape Build(Settings settings)
            {
                float margin = settings.Thickness * 0.5f;
                List<Vector2> outline = GeometryUtil.InflateConvex(Outline, margin);

                if (TryFitPrimitive(Front.Frame, outline, MinAlong, MaxAlong, settings, out ColliderShape primitive))
                {
                    return primitive;
                }

                // Built on the span's mid-plane rather than from the source vertices: those span
                // the full wall thickness, and extruding them raw would give a skewed prism.
                return ColliderShape.FromMesh(Sweep(Front.Frame, outline, MinAlong, MaxAlong, margin));
            }
        }

        // --- Stage 0: swept bands -> solid prisms -----------------------------------------

        // A shaft, a boss, a standoff, a round or hex spacer: a surface swept along an axis, which
        // the planar pipeline can only ever cover as a ring of little flat facets. Every one of
        // those facets is a separate collider, and together they leave the shaft HOLLOW, which is
        // the same failure the slab pairing exists to fix on a wall - so this fixes it the same
        // way, by emitting the solid instead of its surface.
        //
        // The prism is INSCRIBED: its corners are the surface's own vertices, so on an exported
        // cylinder (which is already an n-gon prism) it reproduces the geometry exactly, and on
        // anything rounder it sits inside the true surface rather than proud of it. That is what
        // makes this safe under "nothing may stick out" without any tolerance being spent.
        //
        // THE BAND MUST CLOSE. An open arc - a fillet, a rounded corner - is refused outright,
        // because the convex prism for one bridges its own chord and fills a wedge of space the
        // part need not occupy. A closed band has no chord: the hull of a full ring IS the solid.
        // This is the single rule keeping a merge that spans real curvature honest, and it is why
        // there is no "cylinder tolerance" to loosen.
        //
        // A BORE IS NOT A CYLINDER. The inside of a hole is a ring of vertices too, and fitting a
        // prism to it would plug the hole with collision. Bands are classified by which way their
        // faces point; inward ones are never emitted, and they also veto any band they run down the
        // middle of, unless the material between the two is thin enough to count as a wall by the
        // same MaxWallThickness the slab pairing uses. So a standoff fills and a chute does not.
        private static List<Prism> ExtractSweptBands(SourceGeometry geometry, TriangleAdjacency adjacency, Vector3[] normals, bool[] usable, Settings settings)
        {
            List<Band> outward = new List<Band>();
            List<Band> bores = new List<Band>();

            // attempted is what keeps this linear. A band that grows large and then fails
            // validation - a sphere, a swept surface that isn't closed - would otherwise be regrown
            // from every triangle in it, and a part with thousands of them would appear to hang.
            // Every triangle a failed attempt touched is barred from seeding another.
            bool[] taken = new bool[geometry.TriangleCount];
            bool[] attempted = new bool[geometry.TriangleCount];

            for (int seed = 0; seed < geometry.TriangleCount; seed++)
            {
                if (!usable[seed] || taken[seed] || attempted[seed]) continue;
                if (!TryGrowBand(geometry, adjacency, normals, usable, taken, attempted, seed, settings, out Band band)) continue;

                foreach (int t in band.Triangles)
                {
                    taken[t] = true;
                }
                (band.Outward ? outward : bores).Add(band);
            }

            List<Prism> prisms = new List<Prism>(outward.Count);
            foreach (Band band in outward)
            {
                if (HasOpenBore(band, bores, settings)) continue;

                // Only now do the triangles leave the planar pipeline. A band that was grown and
                // then vetoed hands every one of them back, and gets its ring of facet shells.
                foreach (int t in band.Triangles)
                {
                    usable[t] = false;
                }
                prisms.Add(band.ToPrism());
            }

            return prisms;
        }

        // Region growing again, but constrained by a fitted circle rather than by a fitted plane.
        // The axis comes from the first smooth step found off the seed - two faces of a swept
        // surface meet along an edge parallel to the axis, so their normals' cross product IS the
        // axis - and the circle through those first two faces then bounds everything after.
        //
        // That circle is what stops the band running off the shaft: a plate whose normal happens to
        // be perpendicular to the axis passes the perpendicularity test and would be swallowed, but
        // its vertices are nowhere near a constant radius. Growth is a filter, though, not a proof;
        // the checks after it are what decide.
        private static bool TryGrowBand(SourceGeometry geometry, TriangleAdjacency adjacency, Vector3[] normals, bool[] usable, bool[] taken, bool[] attempted, int seed, Settings settings, out Band band)
        {
            band = null;
            attempted[seed] = true;

            // The widest step still counted as smooth gives the best-conditioned cross product.
            // Steps beyond Sharp Edge Angle are creases - the boundary of the swept surface, not a
            // facet of it - which is the same line stage 1 draws.
            float widest = 0f;
            int partner = -1;
            Vector3 axis = Vector3.zero;
            foreach (int neighbour in adjacency.Neighbours(seed))
            {
                if (!usable[neighbour] || taken[neighbour]) continue;

                float step = Vector3.Angle(normals[seed], normals[neighbour]);
                if (step < MinBandDihedral || step > settings.FeatureAngle || step <= widest) continue;

                Vector3 cross = Vector3.Cross(normals[seed], normals[neighbour]);
                if (cross.sqrMagnitude < 1e-20f) continue;

                widest = step;
                partner = neighbour;
                axis = GeometryUtil.Normalized(cross);
            }

            if (partner < 0) return false;

            GeometryUtil.PlaneBasis(axis, out Vector3 u, out Vector3 v);

            Vector2 Project(Vector3 point) => new(Vector3.Dot(point, u), Vector3.Dot(point, v));

            List<Vector2> sample = new List<Vector2>(6);
            AddCorners(seed, sample);
            AddCorners(partner, sample);
            if (!GeometryUtil.TryFitCircle(sample, out Vector2 centre, out float radius)) return false;

            void AddCorners(int triangle, List<Vector2> into)
            {
                geometry.GetTriangle(triangle, out Vector3 a, out Vector3 b, out Vector3 c);
                into.Add(Project(a));
                into.Add(Project(b));
                into.Add(Project(c));
            }

            float maxTilt = Mathf.Sin(MaxBandAxisTilt * Mathf.Deg2Rad);
            float roundness = Mathf.Max(settings.MaxPlaneDeviation, 1e-4f);

            List<int> members = new List<int>();
            HashSet<int> claimed = new HashSet<int>();
            Queue<int> frontier = new Queue<int>();
            Offer(seed);
            Offer(partner);

            void Offer(int triangle)
            {
                if (!claimed.Add(triangle)) return;

                members.Add(triangle);
                attempted[triangle] = true;
                frontier.Enqueue(triangle);
            }

            while (frontier.Count > 0)
            {
                foreach (int neighbour in adjacency.Neighbours(frontier.Dequeue()))
                {
                    if (!usable[neighbour] || taken[neighbour] || claimed.Contains(neighbour)) continue;
                    if (Mathf.Abs(Vector3.Dot(normals[neighbour], axis)) > maxTilt) continue;

                    geometry.GetTriangle(neighbour, out Vector3 a, out Vector3 b, out Vector3 c);
                    if (!OnCircle(a) || !OnCircle(b) || !OnCircle(c)) continue;

                    Offer(neighbour);
                }
            }

            bool OnCircle(Vector3 point) => Mathf.Abs((Project(point) - centre).magnitude - radius) <= radius * roundness;

            return TryCloseBand(geometry, normals, members, axis, u, v, settings, out band);
        }

        // Everything that decides whether the grown band may become a solid. The circle is refitted
        // over the whole band first - the seed pair's fit was only ever a growth bound, and a fit
        // to two facets of a many-faceted ring is the least of the fits available.
        private static bool TryCloseBand(SourceGeometry geometry, Vector3[] normals, List<int> members, Vector3 axis, Vector3 u, Vector3 v, Settings settings, out Band band)
        {
            band = null;
            if (members.Count < MinBandFacets) return false;

            Vector2 Project(Vector3 point) => new(Vector3.Dot(point, u), Vector3.Dot(point, v));

            HashSet<int> vertexIndices = new HashSet<int>();
            float area = 0f;
            foreach (int t in members)
            {
                geometry.GetTriangleIndices(t, out int i0, out int i1, out int i2);
                vertexIndices.Add(i0);
                vertexIndices.Add(i1);
                vertexIndices.Add(i2);
                area += geometry.TriangleArea(t);
            }

            if (area < settings.MinPatchArea) return false;

            List<Vector2> flat = new List<Vector2>(vertexIndices.Count);
            float minAlong = float.MaxValue;
            float maxAlong = float.MinValue;
            foreach (int vi in vertexIndices)
            {
                Vector3 p = geometry.Vertices[vi];
                flat.Add(Project(p));

                float along = Vector3.Dot(p, axis);
                minAlong = Mathf.Min(minAlong, along);
                maxAlong = Mathf.Max(maxAlong, along);
            }

            if (!GeometryUtil.TryFitCircle(flat, out Vector2 centre, out float radius)) return false;

            List<int> hull = GeometryUtil.ConvexHull2D(flat);
            if (hull.Count < MinBandFacets) return false;

            List<Vector2> outline = new List<Vector2>(hull.Count);
            foreach (int index in hull)
            {
                outline.Add(flat[index] - centre);
            }

            // Does the ring CLOSE? Measured as the widest angular gap between neighbouring corners
            // of the cross-section, which is the one question that separates a shaft from a fillet.
            // A UV seam splits the vertices there but leaves them in the same place, so a seam
            // reads as a zero gap and a genuine arc's chord reads as its whole missing sweep.
            if (WidestAngularGap(outline) > MaxBandAngularGap) return false;

            // Does the surface DIP INSIDE its own cross-section anywhere? A vertex well inside the
            // hull is a flat or a notch the prism would stand proud of. The allowance is half a
            // thickness - the same margin every collider here already carries - so this refuses
            // anything that would stick out further than a plain shell already does.
            float allowance = settings.Thickness * 0.5f;
            foreach (Vector2 point in flat)
            {
                if (GeometryUtil.DepthInsideConvex(outline, point - centre) > allowance) return false;
            }

            // Which way does it FACE? Unanimously outward and it is a shaft; unanimously inward and
            // it is a bore, which is recorded and never emitted. Mixed means the growth wandered
            // across something that isn't one swept surface, so it is neither.
            bool? outward = null;
            foreach (int t in members)
            {
                geometry.GetTriangle(t, out Vector3 a, out Vector3 b, out Vector3 c);

                Vector2 radial = Project((a + b + c) / 3f) - centre;
                if (radial.sqrMagnitude < 1e-20f) return false;

                Vector3 normal = normals[t];
                float facing = Vector2.Dot(new Vector2(Vector3.Dot(normal, u), Vector3.Dot(normal, v)), radial);
                if (facing == 0f) return false;

                bool faces = facing > 0f;
                if (outward.HasValue && outward.Value != faces) return false;
                outward = faces;
            }

            float half = (maxAlong - minAlong) * 0.5f;
            if (half <= 0f) return false;

            Vector3 origin = u * centre.x + v * centre.y + axis * ((maxAlong + minAlong) * 0.5f);
            band = new Band(members, new Frame(origin, u, v, axis), outline, radius, half, outward.Value);
            return true;
        }

        // The widest turn between neighbouring corners of a cross-section already centred on its
        // own axis. A closed ring's corners come back round to the first, so the wrap is one of the
        // gaps measured; an arc's is the sweep it never made.
        private static float WidestAngularGap(List<Vector2> outline)
        {
            List<float> angles = new List<float>(outline.Count);
            foreach (Vector2 corner in outline)
            {
                angles.Add(Mathf.Atan2(corner.y, corner.x) * Mathf.Rad2Deg);
            }
            angles.Sort();

            float widest = angles[0] + 360f - angles[angles.Count - 1];
            for (int i = 1; i < angles.Count; i++)
            {
                widest = Mathf.Max(widest, angles[i] - angles[i - 1]);
            }
            return widest;
        }

        // Is there a bore running down the middle of this band, too wide to be called a wall? Then
        // the band is a tube and filling it would plug whatever the tube is for. The threshold is
        // MaxWallThickness, which already means exactly this elsewhere: above a wall, below a
        // cavity. So a standoff's screw hole fills over and a chute's bore does not.
        private static bool HasOpenBore(Band band, List<Band> bores, Settings settings)
        {
            float coaxial = Mathf.Cos(CoaxialAngleTolerance * Mathf.Deg2Rad);

            foreach (Band bore in bores)
            {
                if (Mathf.Abs(Vector3.Dot(band.Axis, bore.Axis)) < coaxial) continue;
                if (bore.Radius >= band.Radius) continue;

                // Coaxial in position as well as direction: how far the bore's axis line sits off
                // this one, measured across it.
                Vector3 offset = bore.Origin - band.Origin;
                float alongAxis = Vector3.Dot(offset, band.Axis);
                if ((offset - band.Axis * alongAxis).magnitude > band.Radius * settings.MaxPlaneDeviation) continue;

                // And overlapping along it - a hole in the far end of a part is not this shaft's.
                if (Mathf.Abs(alongAxis) > band.HalfLength + bore.HalfLength) continue;

                if (band.Radius - bore.Radius > settings.MaxWallThickness) return true;
            }

            return false;
        }

        // A grown and validated band, before it is decided whether it becomes a collider. Carries
        // what the bore test needs on top of what the prism does.
        private sealed class Band
        {
            public readonly List<int> Triangles;
            public readonly bool Outward;
            public readonly float Radius;
            public readonly float HalfLength;

            private readonly Frame frame;
            private readonly List<Vector2> outline;

            public Band(List<int> triangles, Frame frame, List<Vector2> outline, float radius, float halfLength, bool outward)
            {
                Triangles = triangles;
                Outward = outward;
                Radius = radius;
                HalfLength = halfLength;
                this.frame = frame;
                this.outline = outline;
            }

            public Vector3 Axis => frame.Normal;
            public Vector3 Origin => frame.Origin;

            public Prism ToPrism() => new(frame, outline, Radius, HalfLength, Triangles.Count);
        }

        // The solid a swept band becomes: its cross-section, swept the length of the band. Half a
        // thickness proud on every side, exactly as a slab is - inflated radially and extended past
        // both ends - so a shaft presents the same tunnelling margin as the plate it is welded to.
        private sealed class Prism : Solid
        {
            public readonly int TriangleCount;

            private readonly Frame frame;
            private readonly List<Vector2> outline;  // the cross-section, about the axis
            private readonly float radius;
            private readonly float halfLength;

            public Prism(Frame frame, List<Vector2> outline, float radius, float halfLength, int triangleCount)
            {
                TriangleCount = triangleCount;
                this.frame = frame;
                this.outline = outline;
                this.radius = radius;
                this.halfLength = halfLength;
            }

            public override ColliderShape Build(Settings settings)
            {
                float margin = settings.Thickness * 0.5f;
                List<Vector2> inflated = GeometryUtil.InflateConvex(outline, margin);

                // A four-sided band is a square tube, and the hook turns that into a BoxCollider -
                // the case that makes running every solid through one primitive fit worth doing.
                if (TryFitPrimitive(frame, inflated, -halfLength, halfLength, settings, out ColliderShape primitive))
                {
                    return primitive;
                }

                return ColliderShape.FromMesh(Sweep(frame, inflated, -halfLength, halfLength, margin));
            }

            public override bool Contains(Vector3 point, float tolerance)
            {
                Vector3 offset = point - frame.Origin;

                float along = Vector3.Dot(offset, frame.Normal);
                if (along < -halfLength - tolerance || along > halfLength + tolerance) return false;

                Vector2 flat = new(Vector3.Dot(offset, frame.U), Vector3.Dot(offset, frame.V));
                float reach = radius + tolerance;
                if (flat.sqrMagnitude > reach * reach) return false;

                return GeometryUtil.PolygonContains(outline, flat, tolerance);
            }
        }

        // Is every one of this patch's vertices already inside a solid? Then a shell for it would
        // sit entirely within collision that already exists - the cases this exists for are the
        // thin rim around a plate, whose edge faces lie inside the slab spanning the plate the
        // moment its front and back merge, and the end cap of a shaft, which lies flush against the
        // end of the prism spanning it.
        //
        // Requires ALL vertices, not most: a partly-covered patch still has real surface outside
        // the solid, and dropping it would leave a genuine gap. The centroid pre-test is exact
        // rather than a heuristic - every solid here is convex, so if every vertex is inside it,
        // the centroid is too, and a centroid outside proves at least one vertex is.
        private static bool EnclosedBySolid(PatchShape shape, List<Solid> solids, float tolerance)
        {
            foreach (Solid solid in solids)
            {
                if (!solid.Contains(shape.Centroid, tolerance)) continue;

                bool allInside = true;
                foreach (Vector3 point in shape.Points)
                {
                    if (solid.Contains(point, tolerance)) continue;

                    allInside = false;
                    break;
                }

                if (allInside) return true;
            }

            return false;
        }
    }
}
#endif
