using UnityEngine;
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
#endif

namespace RobotFramework.Components
{
    // Gives a concave part real collision under a non-kinematic Rigidbody, where Unity forces
    // every MeshCollider to be convex (and a convex hull fills in any inward curve or notch).
    // Instead of one hull for the whole mesh, PerTriangleColliderBuilder splits it into many
    // near-flat patches and this component spawns a thin convex MeshCollider for each.
    //
    // EDITOR ONLY. Generation is an explicit button press; the resulting children are saved into
    // the scene/prefab like hand-authored colliders and their hull meshes are written to a real
    // asset (a Mesh only referenced from a scene does NOT persist - if generated colliders ever
    // come back silently non-colliding, check that asset first). The class body is entirely
    // inside #if UNITY_EDITOR, so nothing here exists in a player build; the class declaration
    // itself stays so existing prefabs don't turn into "missing script".
    //
    // Preview before generating by selecting the object - the green gizmos come from the same
    // builder call, so they show exactly what the button would produce. The output is a
    // snapshot: re-press Generate after changing any setting or the source mesh.
    [RequireComponent(typeof(MeshFilter))]
    public class PerTriangleMeshCollider : MonoBehaviour
    {
#if UNITY_EDITOR
        // Sizes are in world-space mm/mm² so Inspector values stay human-readable instead of
        // tiny fractions; they're converted to the mesh's local space in CurrentSettings.
        [InspectorName("Dissolve Angle (degrees)")]
        [Tooltip("Cleans up the mesh's topology before anything else runs, the way Blender's Limited Dissolve does: removes vertices sitting in the middle of a flat span, and vertices splitting an otherwise straight edge or rim. Survivors do NOT move, so within this angle the surface is unchanged - this is cleanup, not simplification. Worth leaving on: stray vertices turn a rectangle into an n-gon and make a wall's two faces look like unrelated surfaces, which costs boxes and pairing. 0 leaves topology alone; raise it to also flatten gently faceted surfaces.")]
        [SerializeField, Range(0f, 30f)] private float dissolveAngle = 17.5f;

        [InspectorName("Decimate Ratio")]
        [Tooltip("Simplifies the mesh the colliders are built from - 1 uses it as-is, 0.25 aims for a quarter of its triangles. The RENDERED mesh is never touched. Collapsing inside a flat region costs nothing, so on a part made of flat panels this strips tessellation noise (which can fracture one face into several patches) without moving the surface. The ratio is a target, not a guarantee: no collapse may shift the surface by more than half the Thickness above, so it stops early rather than rounding off real features or collapsing a thin wall's two faces together.")]
        [SerializeField, Range(0.05f, 1f)] private float decimateRatio = 0.05f;

        [InspectorName("Thickness (mm)")]
        [Tooltip("Extrusion thickness along each patch's normal. Thicker = less chance of fast game pieces phasing through.")]
        [SerializeField, Min(0.1f)] private float thicknessMm = 3f;

        [InspectorName("Minimum Piece Size (mm²)")]
        [Tooltip("Patches with a smaller world-space area than this are skipped - keeps sliver triangles from costing colliders they don't earn.")]
        [SerializeField, Min(0f)] private float minPieceAreaMm2 = 1200f;

        [InspectorName("Merge Across Angles")]
        [Tooltip("Off (default): only merge triangles that are already coplanar, rebuilding the flat faces the exporter triangulated and nothing more. Colliders match the surface exactly, and flat faces still come out whole - which is what Merge Opposing Faces and Use Box Colliders need, since both work on a patch's outline. On: also merge across normals differing by up to Merge Angle, so a curved surface becomes one collider. Far fewer colliders, but a patch spanning real curvature flattens onto one plane and the resulting shape stops matching the part - turn this on per-object, only where it demonstrably helps.")]
        [SerializeField] private bool enableClustering = true;

        [InspectorName("Merge Angle (degrees)")]
        [Tooltip("Edge-connected triangles within this many degrees of each other merge into one collider. The main performance lever - bigger = far fewer colliders, at the cost of a coarser fit around curves.")]
        [SerializeField, Range(0f, 45f)] private float coplanarMergeAngle = 14.1f;

        [InspectorName("Merge Cylinders")]
        [Tooltip("On: before anything else runs, find surfaces swept around an axis - shafts, bosses, standoffs, round or hex spacers - and emit ONE solid collider spanning each, instead of the ring of little flat facets they otherwise become. The prism's corners are the part's own vertices, so on an exported cylinder it matches the geometry exactly. Only CLOSED rings qualify: a fillet or a rounded corner is left alone, because a convex shape spanning an open arc fills in space across its chord. The inside of a hole is never filled, and a bore running down the middle of a shaft blocks the merge unless the material around it is thinner than Max Wall Thickness - so a standoff fills solid and a tube you want game pieces to pass through does not.")]
        [SerializeField] private bool mergeCylinders = false;

        [InspectorName("Sharp Edge Angle (degrees)")]
        [Tooltip("What counts as a feature edge: adjacent faces meeting at this angle or more score as a full feature, shallower ones score proportionally. Open rims always score full. Patches grow through flat interior first and stop at these edges, so lower = more, smaller patches that follow the part's creases more tightly.")]
        [SerializeField, Range(5f, 90f)] private float sharpEdgeAngle = 90f;

        [InspectorName("Edge Vertex Priority")]
        [Tooltip("How much more a feature vertex counts than a flat-interior one when fitting each patch's plane - 0 weights every vertex equally, 3 means a crease or rim vertex pulls the plane 4x as hard. Aligns colliders with the edges a game piece actually hits instead of with whichever area is most densely tessellated. Affects the fit only; coverage is still checked against every vertex.")]
        [SerializeField, Range(0f, 10f)] private float edgeVertexPriority = 10f;

        // The next two are correctness checks, not a performance dial. A "fast" preset that
        // loosened them used to exist and was removed: on curved or looped parts it let
        // self-crossing patches survive, producing twisted ribbon colliders that matched nothing.
        [InspectorName("Max Shape Bulge Allowed")]
        [Tooltip("Rejects a patch whose flattened hull area exceeds this multiple of its real triangle area - catches both 3D curvature and concave outlines, which a convex hull always bridges straight across. Keep tight: a rejected patch leaves a small gap, a bad one juts into open space.")]
        [SerializeField, Min(1f)] private float maxHullAreaRatio = 1.27f;

        [InspectorName("Max Surface Curve Allowed")]
        [Tooltip("Rejects a patch whose vertices sit farther off its own fitted plane than this fraction of its footprint radius. Merge Angle only compares each triangle against the patch's seed, so a chain of tolerable steps can walk across a fold; this catches that directly.")]
        [SerializeField, Min(0f)] private float maxPlaneDeviationRatio = 0.1f;

        [InspectorName("Internal Gap Jump (mm)")]
        [Tooltip("Lets a face close over holes punched THROUGH it that are narrower than this. A bolt hole is missing area like any cutout, so Max Shape Bulge normally refuses the whole face over it and the face falls back to loose per-triangle colliders - losing its box and its wall pairing to guard a hole nothing fits through anyway. Only holes fully enclosed by the face count: a notch cut into its edge, or open space past its rim, is never jumped however small, so this can't quietly grow a collider outward. Size is the hole's widest diameter, so a long slot stays open even when it's narrow - a game piece can travel along one. 0 disables it; set it just above the biggest fastener hole on the part.")]
        [SerializeField, Min(0f)] private float internalGapJumpMm = 60f;

        [InspectorName("Merge Opposing Faces")]
        [Tooltip("On: where both surfaces of the same wall are found, emit ONE solid collider spanning between them instead of a thin shell on each face. Roughly halves the collider count on plate-like parts and removes the hollow core a game piece could otherwise end up inside. Off: every surface gets its own shell.")]
        [SerializeField] private bool pairOpposingFaces = true;

        [InspectorName("Max Wall Thickness (mm)")]
        [Tooltip("Widest gap between two opposing surfaces still treated as one wall. Set this above the thickest wall on the part but below its smallest hollow cavity - too high and the outside of a box pairs with its own far side, filling in a space that should be open.")]
        [SerializeField, Min(0.1f)] private float maxWallThicknessMm = 25f;

        [InspectorName("Use Box Colliders")]
        [Tooltip("On: any collider whose outline is close enough to rectangular is emitted as a BoxCollider instead of a convex MeshCollider - no hull to cook, and far cheaper for PhysX to resolve. Off: everything stays a mesh.")]
        [SerializeField] private bool useBoxColliders = true;

        [InspectorName("Box Fit Tolerance")]
        [Tooltip("How much empty space a box may add to be used in place of a mesh, as a fraction of the shape's real area. 0 accepts only true rectangles (extra vertices along an edge are fine either way - the test measures area, not corners). 0.05 lets a box swallow a small chamfer or a slightly-off angle. High values will box off shapes that are nowhere near rectangular, filling space that should be open - raise it while watching the Scene view preview, not blind. 1 is the top of the range because every convex outline fits a rectangle of at most twice its area (a triangle is the worst case, at exactly twice), so 1 boxes everything unconditionally.")]
        [SerializeField, Range(0f, 1f)] private float boxFitTolerance = 0.8f;

        [Tooltip("Optional physic material applied to every generated collider.")]
        [SerializeField] private PhysicMaterial physicMaterial;

        [Header("Layer Overrides")]
        [Tooltip("Forces collisions with these layers on regardless of the physics layer matrix.")]
        [SerializeField] private LayerMask includeLayers;
        [Tooltip("Forces collisions with these layers off regardless of the physics layer matrix.")]
        [SerializeField] private LayerMask excludeLayers;
        [Tooltip("Same as a regular Collider's Layer Override Priority.")]
        [SerializeField] private int layerOverridePriority = 0;

        [Header("Editor Preview")]
        [Tooltip("While this object is selected, draw the colliders the current settings WOULD produce as Scene view gizmos.")]
        [SerializeField] private bool showGizmos = true;
        [SerializeField] private Color gizmoColor = new(0f, 1f, 0.4f, 0.5f);

        private const string CollidersRootName = "GeneratedClusterColliders";
        private const string MeshAssetFolder = "Assets/GeneratedColliderMeshes";

        [HideInInspector, SerializeField] private GameObject generatedCollidersRoot;

        private Mesh SourceMesh => TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;

        // Falls back to the name if the serialized reference was lost to a hand-edit, so a
        // regenerate can't leave an orphaned second collider tree behind.
        private GameObject CollidersRoot => generatedCollidersRoot != null
            ? generatedCollidersRoot
            : transform.Find(CollidersRootName)?.gameObject;

        // A generated box is placed by rotating its child Transform, and a rotation followed by a
        // non-uniform scale is a shear - which a BoxCollider cannot represent, so it would collide
        // as some other shape entirely. Mesh colliders have their vertices transformed and are
        // fine either way, so a non-uniformly scaled part silently falls back to meshes rather
        // than generating boxes that don't match the part.
        private bool HasUniformScale
        {
            get
            {
                Vector3 scale = transform.lossyScale;
                float tolerance = 1e-4f * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                return Mathf.Abs(scale.x - scale.y) <= tolerance && Mathf.Abs(scale.y - scale.z) <= tolerance;
            }
        }

        // Local space is what the builder works in, so fold the parent scale out here. Robot
        // parts are typically under a large uniform scale, making local units far smaller than
        // the millimeters above suggest.
        private PerTriangleColliderBuilder.Settings CurrentSettings
        {
            get
            {
                Vector3 scale = transform.lossyScale;
                float average = (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;
                if (average < 1e-6f) average = 1f;

                return new PerTriangleColliderBuilder.Settings
                {
                    DissolveAngle = dissolveAngle,
                    DecimateRatio = decimateRatio,
                    Thickness = thicknessMm / 1000f / average,
                    MinPatchArea = minPieceAreaMm2 / 1_000_000f / (average * average),
                    Merge = enableClustering,
                    MergeAngle = coplanarMergeAngle,
                    MergeSweptBands = mergeCylinders,
                    MaxHullAreaRatio = maxHullAreaRatio,
                    MaxPlaneDeviation = maxPlaneDeviationRatio,
                    InternalGapJump = internalGapJumpMm / 1000f / average,
                    FeatureAngle = sharpEdgeAngle,
                    EdgeVertexBias = edgeVertexPriority,
                    PairOpposingFaces = pairOpposingFaces,
                    MaxWallThickness = maxWallThicknessMm / 1000f / average,
                    UseBoxColliders = useBoxColliders && HasUniformScale,
                    BoxFitTolerance = boxFitTolerance
                };
            }
        }

        // --- Readout --------------------------------------------------------------------------
        // What the Inspector reports: how much collision geometry this part is actually paying
        // for, against the mesh it came from. Counts come off the generated hierarchy, so they
        // describe what's saved right now - not what the current settings would produce.

        public int SourceTriangleCount => TriangleCount(SourceMesh);

        // Boxes contribute nothing here on purpose: they cost PhysX a fraction of what a cooked
        // hull does, so counting their corners against the same budget would be misleading.
        // They're reported separately instead.
        public int GeneratedTriangleCount
        {
            get
            {
                int total = 0;
                foreach (Collider collider in GeneratedColliders)
                {
                    if (collider is MeshCollider mesh) total += TriangleCount(mesh.sharedMesh);
                }
                return total;
            }
        }

        public int GeneratedColliderCount => GeneratedColliders.Length;

        public int GeneratedBoxCount
        {
            get
            {
                int boxes = 0;
                foreach (Collider collider in GeneratedColliders)
                {
                    if (collider is BoxCollider) boxes++;
                }
                return boxes;
            }
        }

        private Collider[] GeneratedColliders
        {
            get
            {
                GameObject root = CollidersRoot;
                return root != null ? root.GetComponentsInChildren<Collider>() : new Collider[0];
            }
        }

        // What the CURRENT settings would produce, as opposed to what was last generated - the
        // same shapes the Scene view gizmos draw, counted rather than drawn. Reading these next to
        // the generated counts is what makes a settings change legible before committing to it,
        // and makes an out-of-date generation obvious.
        public int PreviewTriangleCount
        {
            get
            {
                int total = 0;
                foreach (PerTriangleColliderBuilder.ColliderShape shape in Preview)
                {
                    if (!shape.IsBox) total += TriangleCount(shape.Mesh);
                }
                return total;
            }
        }

        public int PreviewColliderCount => Preview.Count;

        public int PreviewBoxCount
        {
            get
            {
                int boxes = 0;
                foreach (PerTriangleColliderBuilder.ColliderShape shape in Preview)
                {
                    if (shape.IsBox) boxes++;
                }
                return boxes;
            }
        }

        // GetIndexCount rather than mesh.triangles: no managed copy, and it works on meshes
        // imported without "Read/Write Enabled". Cheap enough to call every Inspector repaint.
        private static int TriangleCount(Mesh mesh)
        {
            if (mesh == null) return 0;

            uint indices = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                indices += mesh.GetIndexCount(i);
            }
            return (int)(indices / 3);
        }

        // --- Actions --------------------------------------------------------------------------

        // Full rebuild every time, on purpose: it's a manual action, and it's the only way to
        // guarantee the output matches the current settings.
        [ContextMenu("Generate Colliders")]
        public void GenerateColliders()
        {
            ClearColliders();

            List<PerTriangleColliderBuilder.ColliderShape> shapes = PerTriangleColliderBuilder.Build(SourceMesh, CurrentSettings);
            if (shapes.Count == 0)
            {
                Debug.LogWarning($"[PerTriangleMeshCollider] {name}: no colliders generated (missing mesh, or every patch was rejected - see Log Diagnostics).", this);
                return;
            }

            // Must happen before the scene is saved: until each hull is an asset, the colliders
            // below point at throwaway in-memory meshes. Boxes need no asset at all.
            List<Mesh> hulls = new List<Mesh>(shapes.Count);
            foreach (PerTriangleColliderBuilder.ColliderShape shape in shapes)
            {
                if (!shape.IsBox) hulls.Add(shape.Mesh);
            }
            SaveHullMeshes(hulls);

            generatedCollidersRoot = new GameObject(CollidersRootName) { layer = gameObject.layer };
            generatedCollidersRoot.transform.SetParent(transform, false);

            for (int i = 0; i < shapes.Count; i++)
            {
                PerTriangleColliderBuilder.ColliderShape shape = shapes[i];

                GameObject child = new GameObject(shape.IsBox ? $"Box{i}" : $"Cluster{i}") { layer = gameObject.layer };
                child.transform.SetParent(generatedCollidersRoot.transform, false);

                Collider collider;
                if (shape.IsBox)
                {
                    // The box's placement rides on the child's Transform - a BoxCollider is
                    // axis-aligned in its own local space and can't be rotated any other way.
                    child.transform.localPosition = shape.Center;
                    child.transform.localRotation = shape.Rotation;

                    BoxCollider box = child.AddComponent<BoxCollider>();
                    box.size = shape.Size;
                    collider = box;
                }
                else
                {
                    MeshCollider mesh = child.AddComponent<MeshCollider>();
                    mesh.sharedMesh = shape.Mesh;
                    mesh.convex = true;
                    // Unity's defaults include WeldColocatedVertices and InflateConvexMesh, which
                    // visibly collapse prisms this thin - PhysX ends up cooking a hull noticeably
                    // smaller than the mesh we authored. Keep the two harmless options only.
                    mesh.cookingOptions = MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.UseFastMidphase;
                    collider = mesh;
                }

                collider.includeLayers = includeLayers;
                collider.excludeLayers = excludeLayers;
                collider.layerOverridePriority = layerOverridePriority;
                collider.sharedMaterial = physicMaterial;
            }

            Undo.RegisterCreatedObjectUndo(generatedCollidersRoot, "Generate Per-Triangle Colliders");

            if (useBoxColliders && !HasUniformScale)
            {
                Debug.LogWarning($"[PerTriangleMeshCollider] {name}: lossyScale {transform.lossyScale} isn't uniform, so box colliders were skipped - a rotated box can't represent a sheared shape. Everything was generated as mesh colliders instead.", this);
            }

            MarkDirty();
            Debug.Log($"[PerTriangleMeshCollider] {name}: {SourceTriangleCount} part triangles -> {shapes.Count} colliders " +
                $"({GeneratedBoxCount} boxes), {GeneratedTriangleCount} collider triangles.", this);
        }

        [ContextMenu("Clear Colliders")]
        public void ClearColliders()
        {
            GameObject root = CollidersRoot;
            if (root != null)
            {
                Undo.DestroyObjectImmediate(root);
            }
            generatedCollidersRoot = null;

            string assetPath = HullMeshAssetPath();
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            MarkDirty();
        }

        // Replaces the rendered mesh with the generated collision geometry, welded into one mesh.
        // The point is to SEE what physics sees: a collider that doesn't match the part is obvious
        // when it's the thing being drawn, and impossible to miss the way a wireframe overlay is.
        //
        // This overwrites the MeshFilter's mesh reference (undoable, and the original asset is
        // untouched on disk). Note that generating again afterwards would build colliders from the
        // remeshed geometry rather than the real part, so treat this as a one-way inspection step:
        // undo it, or reassign the original mesh, before touching Generate again.
        [ContextMenu("Remesh From Colliders")]
        public void RemeshFromColliders()
        {
            Collider[] colliders = GeneratedColliders;
            if (colliders.Length == 0)
            {
                Debug.LogWarning($"[PerTriangleMeshCollider] {name}: nothing to remesh from - generate colliders first.", this);
                return;
            }

            if (!TryGetComponent(out MeshFilter meshFilter))
            {
                return;
            }

            List<Volume> volumes = new List<Volume>(colliders.Length);
            foreach (Collider collider in colliders)
            {
                if (Volume.TryFrom(collider, transform, out Volume volume)) volumes.Add(volume);
            }

            Mesh remeshed = BuildOuterSurface(volumes);
            remeshed.name = "ColliderRemesh";

            SaveMeshAsset(remeshed, RemeshAssetPath());

            Undo.RecordObject(meshFilter, "Remesh From Colliders");
            meshFilter.sharedMesh = remeshed;

            MarkDirty();
            Debug.Log($"[PerTriangleMeshCollider] {name}: remeshed from {volumes.Count} colliders into {TriangleCount(remeshed)} triangles.", this);
        }

        // Welds the colliders into a single skin instead of a heap of overlapping solids.
        //
        // Each collider is a closed shape, so simply combining them leaves every face that is
        // buried inside a neighbour still there and still drawn - which is what shows up as lines
        // criss-crossing the part, since those interior faces and the surfaces around them fight
        // over the same pixels. Dropping any triangle whose middle lies inside another collider
        // leaves only the outer skin.
        //
        // This is not a boolean union: triangles that straddle an intersection are kept whole, so
        // shapes still cross rather than being cut against each other. It removes the buried
        // faces, which is what the lines actually were.
        private static Mesh BuildOuterSurface(List<Volume> volumes)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            foreach (Volume volume in volumes)
            {
                for (int t = 0; t < volume.Triangles.Length; t += 3)
                {
                    Vector3 a = volume.Vertices[volume.Triangles[t]];
                    Vector3 b = volume.Vertices[volume.Triangles[t + 1]];
                    Vector3 c = volume.Vertices[volume.Triangles[t + 2]];

                    if (IsBuried(volume, (a + b + c) / 3f, volumes)) continue;

                    triangles.Add(vertices.Count);
                    triangles.Add(vertices.Count + 1);
                    triangles.Add(vertices.Count + 2);
                    vertices.Add(a);
                    vertices.Add(b);
                    vertices.Add(c);
                }
            }

            // Welding lets neighbouring shapes that meet exactly share vertices, so the normals
            // blend across the join rather than every shape being shaded as its own island.
            Vector3[] welded = GeometryUtil.WeldPositions(vertices.ToArray(), out int[] remap);
            for (int i = 0; i < triangles.Count; i++)
            {
                triangles[i] = remap[triangles[i]];
            }

            Mesh mesh = new Mesh
            {
                // Overlapping per-shape geometry runs past the 16-bit index limit on any real part.
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = welded,
                triangles = triangles.ToArray()
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool IsBuried(Volume owner, Vector3 point, List<Volume> volumes)
        {
            foreach (Volume other in volumes)
            {
                if (other.Planes == owner.Planes) continue; // the shape the face belongs to
                if (!other.Bounds.Contains(point)) continue;
                if (other.Contains(point)) return true;
            }

            return false;
        }

        // One collider as geometry in the part's local space, plus the planes bounding it. Every
        // generated collider is convex, so a point is inside exactly when it is behind all of them.
        private readonly struct Volume
        {
            public readonly Vector3[] Vertices;
            public readonly int[] Triangles;
            public readonly Plane[] Planes;
            public readonly Bounds Bounds;

            // A face sitting exactly ON this shape's surface is not inside it. Two shapes that abut
            // share that surface, and each would otherwise read as buried in the other - erasing
            // both and leaving a hole where they meet. Scaled to the shape so it holds at any size.
            private readonly float depth;

            private Volume(Vector3[] vertices, int[] triangles, Plane[] planes, Bounds bounds)
            {
                Vertices = vertices;
                Triangles = triangles;
                Planes = planes;
                Bounds = bounds;
                depth = bounds.size.magnitude * 1e-4f;
            }

            public bool Contains(Vector3 point)
            {
                foreach (Plane plane in Planes)
                {
                    if (plane.GetDistanceToPoint(point) > -depth) return false;
                }
                return true;
            }

            public static bool TryFrom(Collider collider, Transform part, out Volume volume)
            {
                volume = default;

                Matrix4x4 toPart = part.worldToLocalMatrix * collider.transform.localToWorldMatrix;

                Mesh source;
                if (collider is BoxCollider box)
                {
                    source = BoxMesh(box.size);
                    toPart *= Matrix4x4.Translate(box.center);
                }
                else
                {
                    source = (collider as MeshCollider)?.sharedMesh;
                }

                if (source == null || !SourceGeometry.TryRead(source, out SourceGeometry geometry)) return false;

                Vector3[] vertices = new Vector3[geometry.Vertices.Length];
                Bounds bounds = new Bounds(toPart.MultiplyPoint3x4(geometry.Vertices[0]), Vector3.zero);
                Vector3 centre = Vector3.zero;
                for (int v = 0; v < vertices.Length; v++)
                {
                    vertices[v] = toPart.MultiplyPoint3x4(geometry.Vertices[v]);
                    bounds.Encapsulate(vertices[v]);
                    centre += vertices[v];
                }
                centre /= vertices.Length;

                // Each face's plane, turned to face outwards using the shape's own centre rather
                // than its winding - the prisms are built winding-agnostic, since nothing that
                // consumes them until now has cared.
                List<Plane> planes = new List<Plane>(geometry.TriangleCount);
                for (int t = 0; t < geometry.TriangleCount; t++)
                {
                    geometry.GetTriangleIndices(t, out int i0, out int i1, out int i2);
                    Vector3 a = vertices[i0], b = vertices[i1], c = vertices[i2];

                    Vector3 normal = Vector3.Cross(b - a, c - a);
                    if (normal.sqrMagnitude < 1e-20f) continue;

                    normal = GeometryUtil.Normalized(normal);
                    if (Vector3.Dot(normal, a - centre) < 0f) normal = -normal;

                    planes.Add(new Plane(normal, a));
                }

                if (planes.Count == 0) return false;

                volume = new Volume(vertices, geometry.Indices, planes.ToArray(), bounds);
                return true;
            }
        }

        // A closed box centred on the origin, reusing the same prism builder the colliders
        // themselves come from rather than hand-rolling another one.
        private static Mesh BoxMesh(Vector3 size)
        {
            Vector3 half = size * 0.5f;
            List<Vector3> corners = new List<Vector3>
            {
                new(-half.x, -half.y, 0f),
                new(half.x, -half.y, 0f),
                new(half.x, half.y, 0f),
                new(-half.x, half.y, 0f)
            };

            return GeometryUtil.ExtrudePrism(corners, new List<int> { 0, 1, 2, 3 }, new Vector3(0f, 0f, half.z));
        }

        // Runs the builder against the CURRENT settings and reports what it threw away and why -
        // the fast way to tell "this part won't merge" from "these settings are too tight", and
        // the main feedback loop while working on the algorithm itself.
        [ContextMenu("Log Diagnostics")]
        public void LogDiagnostics()
        {
            PerTriangleColliderBuilder.Report report = new PerTriangleColliderBuilder.Report();
            List<PerTriangleColliderBuilder.ColliderShape> colliders = PerTriangleColliderBuilder.Build(SourceMesh, CurrentSettings, report);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"[PerTriangleMeshCollider] {name}: {report.SourceTriangles} part triangles, lossyScale {transform.lossyScale}");
            if (report.DissolvedTriangles != report.SourceTriangles)
            {
                sb.AppendLine($"Dissolved to {report.DissolvedTriangles} triangles (topology cleanup, surface unchanged).");
            }
            if (report.DecimatedTriangles != report.DissolvedTriangles)
            {
                sb.AppendLine($"Decimated to {report.DecimatedTriangles} triangles before building.");
            }
            if (report.DuplicateTriangles > 0)
            {
                sb.AppendLine($"{report.DuplicateTriangles} duplicate triangles ignored - the source mesh has coincident geometry.");
            }
            if (report.FallbackPatches > 0)
            {
                sb.AppendLine($"{report.FallbackPatches} patches fell back to individual triangles rather than leaving a gap.");
            }
            if (report.SweptBands > 0)
            {
                sb.AppendLine($"{report.SweptBands} swept bands ({report.SweptBandTriangles} triangles) became solid prisms before partitioning.");
            }
            if (report.BridgedHoles > 0)
            {
                sb.AppendLine($"{report.BridgedHoles} interior holes jumped across {report.BridgedPatches} patches.");
            }
            sb.AppendLine($"{report.Patches} patches -> {colliders.Count} colliders " +
                $"({report.Boxes} boxes, {report.PairedPatches} patches merged into {report.PairedPatches / 2} solid slabs, " +
                $"{report.EnclosedPatches} already covered by one), {report.Rejections.Count} rejected:");
            foreach (string rejection in report.Rejections)
            {
                sb.AppendLine($"  {rejection}");
            }

            Debug.Log(sb.ToString(), this);

            foreach (PerTriangleColliderBuilder.ColliderShape collider in colliders)
            {
                if (!collider.IsBox) DestroyImmediate(collider.Mesh);
            }
        }

        // --- Persistence ----------------------------------------------------------------------

        // One asset per component, keyed by hierarchy path - short names repeat constantly on a
        // robot ("Plate" x30), so the path is what keeps two parts from overwriting each other.
        private string HullMeshAssetPath() => AssetPath(string.Empty);

        private string RemeshAssetPath() => AssetPath("_Remesh");

        private string AssetPath(string suffix)
        {
            StringBuilder identity = new StringBuilder(name);
            for (Transform t = transform.parent; t != null; t = t.parent)
            {
                identity.Insert(0, t.name + "_");
            }
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                identity.Replace(invalid, '_');
            }
            return $"{MeshAssetFolder}/{identity}{suffix}.asset";
        }

        // A mesh referenced only from a scene or prefab does not persist, so anything that outlives
        // this call has to become a file first.
        private void SaveMeshAsset(Mesh mesh, string assetPath)
        {
            EnsureAssetFolder();
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(mesh, assetPath);
            AssetDatabase.SaveAssets();
        }

        private void EnsureAssetFolder()
        {
            if (AssetDatabase.IsValidFolder(MeshAssetFolder)) return;

            AssetDatabase.CreateFolder("Assets", Path.GetFileName(MeshAssetFolder));
        }

        // The first hull becomes the main asset and the rest are sub-objects of it, the same way
        // an imported FBX holds its meshes.
        private void SaveHullMeshes(List<Mesh> hulls)
        {
            if (hulls.Count == 0) return; // an all-box part needs no mesh asset

            EnsureAssetFolder();

            string assetPath = HullMeshAssetPath();
            AssetDatabase.CreateAsset(hulls[0], assetPath);
            for (int i = 1; i < hulls.Count; i++)
            {
                AssetDatabase.AddObjectToAsset(hulls[i], assetPath);
            }
            AssetDatabase.SaveAssets();
        }

        private void MarkDirty()
        {
            EditorUtility.SetDirty(this);
            if (gameObject.scene.IsValid())
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
        }

        // --- Preview --------------------------------------------------------------------------

        // Unity calls this regardless of whether the component is enabled, hence the check.
        private void OnDrawGizmosSelected()
        {
            if (enabled && showGizmos) DrawPreview();
        }

        // Cached because gizmos redraw every Scene view repaint, the readout is rebuilt on every
        // Inspector repaint, and a rebuild is a full partitioning pass plus a Mesh allocation per
        // patch. OnValidate drops the cache whenever a setting changes, which is the only thing
        // that can invalidate it mid-session.
        // How long a preview build may take before it stops refreshing on its own.
        private const long AutoRefreshBudgetMs = 100;

        private List<PerTriangleColliderBuilder.ColliderShape> previewShapes;
        private bool previewStale;
        private long previewBuildMs;

        public bool PreviewOutOfDate => previewStale;

        // Once a build costs real time, dragging a slider becomes unusable: every mouse-move fires
        // OnValidate, and rebuilding on each one runs the whole pipeline - decimation included -
        // dozens of times a second, which reads as the editor hanging. Past the budget the preview
        // stops chasing changes and waits to be asked. Cheap parts never notice.
        public bool PreviewRefreshIsManual => previewBuildMs > AutoRefreshBudgetMs;

        private List<PerTriangleColliderBuilder.ColliderShape> Preview
        {
            get
            {
                if (previewShapes == null || (previewStale && !PreviewRefreshIsManual)) RefreshPreview();
                return previewShapes;
            }
        }

        public void RefreshPreview()
        {
            DiscardPreview();

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            previewShapes = PerTriangleColliderBuilder.Build(SourceMesh, CurrentSettings);
            previewBuildMs = stopwatch.ElapsedMilliseconds;
            previewStale = false;
        }

        // Only flags the cache rather than dropping it, so an expensive preview stays on screen
        // until a fresh one replaces it - and so nothing is destroyed inside OnValidate.
        private void OnValidate()
        {
            previewStale = true;
        }

        private void OnDisable()
        {
            DiscardPreview();
        }

        private void DiscardPreview()
        {
            if (previewShapes == null) return;

            foreach (PerTriangleColliderBuilder.ColliderShape shape in previewShapes)
            {
                if (!shape.IsBox) DestroyImmediate(shape.Mesh);
            }
            previewShapes = null;
        }

        private void DrawPreview()
        {
            Gizmos.color = gizmoColor;
            Matrix4x4 toLocal = transform.localToWorldMatrix;

            foreach (PerTriangleColliderBuilder.ColliderShape shape in Preview)
            {
                if (shape.IsBox)
                {
                    // Boxes carry their own orientation, so each needs its own gizmo matrix.
                    Gizmos.matrix = toLocal * Matrix4x4.TRS(shape.Center, shape.Rotation, Vector3.one);
                    Gizmos.DrawWireCube(Vector3.zero, shape.Size);
                }
                else
                {
                    Gizmos.matrix = toLocal;
                    Gizmos.DrawWireMesh(shape.Mesh);
                }
            }
        }
#endif
    }
}
