using UnityEditor.ShortcutManagement;
using System.Collections.Generic;
using System.Linq;
using RobotFramework.Components;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Fits an <see cref="InteractionRoller"/> to each selected mesh, using the same shape classifier
    /// the collider guesser does: a mesh that reads as a cylinder or capsule becomes a spinning roller,
    /// one that reads as a box becomes a belt.
    /// </summary>
    /// <remarks>
    /// This only places and sizes the roller. Everything the roller actually does — rpm, motor, squish,
    /// friction — is left at the component's defaults, because none of it is inferable from geometry.
    /// </remarks>
    public abstract class GuessInteractionRollerBase : TargetTool<MeshFilter>
    {
        /// <summary>InteractionRoller reads its sizes in inches; the fit is in world metres.</summary>
        const float MetersPerInch = 0.0254f;

        /// <summary>
        /// One group per model, keyed by that model's DynamicPhysics object: a selection spanning
        /// several models must not end up with one group reaching across all of them. Per run, and
        /// cleared at the start of one — a tool instance outlives the pass, its working state does not.
        /// </summary>
        readonly Dictionary<Transform, Group> _groups = new Dictionary<Transform, Group>();

        int _rollers;
        int _belts;
        int _merged;

        /// <summary>Whether a row of like-sized wheels sharing an axis becomes one roller spanning them.</summary>
        protected abstract bool MergeInline { get; }

        protected override bool Filter(MeshFilter target) => target.sharedMesh != null;

        protected override void Begin(ToolRun run)
        {
            _groups.Clear();
            _rollers = 0;
            _belts = 0;
            _merged = 0;
        }

        /// <summary>
        /// Reads one mesh and files what it would become, without building anything yet: whether a row
        /// of them becomes a single roller is a question about the whole selection, and cannot be
        /// answered while looking at one mesh. The rollers are built in <see cref="Finish"/>.
        /// </summary>
        protected override bool Apply(MeshFilter target, ToolRun run)
        {
            GameObject source = target.gameObject;
            if (!ShapeFit.TryFit(target.sharedMesh, out ShapeFit.Fit fit)) return false;
            if (!TryPlace(fit, source.transform.lossyScale, out Placement placement)) return false;

            // Only once the mesh is known to be getting a roller: unpacking is destructive to the
            // prefab connection and irrelevant to a part this pass is going to skip.
            HierarchyOps.Unpack(source);

            // Filed under the model's DynamicPhysics object rather than in its "collider" one. A
            // roller is a functional component that gets tuned afterwards, and it builds its own
            // trigger and core colliders at runtime rather than being one of the authored colliders
            // filed there.
            Group group = GroupFor(ModelLayout.DynamicPhysicsParent(source.transform.parent, run.Name), run.Name);

            group.Candidates.Add(new Candidate(
                source,
                placement,
                source.transform.TransformPoint(fit.Rotation * fit.Center),
                source.transform.rotation * fit.Rotation * placement.LocalRotation));

            return true;
        }

        protected override void Finish(IReadOnlyList<MeshFilter> targets, ToolRun run)
        {
            foreach (Group group in _groups.Values)
            {
                foreach (List<Candidate> row in Rows(group.Candidates))
                {
                    group.Rollers.Add(Emit(group.Node, row, run));
                    _merged += row.Count - 1;
                }

                // Left until every roller in the group exists, so it is wired once with a full list.
                RollerGroups.Wire(Undo.AddComponent<RollerGroup>(group.Node.gameObject), group.Rollers);
            }

            int skipped = targets.Count - _rollers - _belts - _merged;
            run.Report($"{_rollers} roller(s) and {_belts} belt(s) from {targets.Count} mesh(es) " +
                       $"in {_groups.Count} group(s)" +
                       (_merged > 0 ? $", {_merged} mesh(es) merged into a neighbour" : string.Empty) +
                       (skipped > 0 ? $", {skipped} skipped (neither cylindrical nor box-shaped)" : string.Empty) + ".");
        }

        /// <summary>
        /// Builds the roller one row of candidates asks for. A row of one is the ordinary case and is
        /// placed exactly where it was fitted; a longer one is placed on the shape the row spans.
        /// </summary>
        InteractionRoller Emit(Transform parent, IReadOnlyList<Candidate> row, ToolRun run)
        {
            Placement placement = row[0].Placement;
            Vector3 position = row[0].Position;
            Quaternion rotation = row[0].Rotation;

            if (row.Count > 1) Span(row, ref placement, ref position, ref rotation);

            var holder = new GameObject(
                HierarchyOps.UniqueChildName(parent, placement.Belt ? "Belt" : "Roller"));
            Undo.RegisterCreatedObjectUndo(holder, run.Name);
            Undo.SetTransformParent(holder.transform, parent, run.Name);
            holder.layer = row[0].Source.layer;

            Transform placed = holder.transform;
            placed.position = position;
            placed.rotation = rotation;

            // World scale 1, with every size below written in world inches. The roller's overlap
            // queries are built from those inches directly and are never scaled by the transform,
            // so a holder inheriting the model's scale would collide over a different volume than
            // the one it sweeps.
            HierarchyOps.SetWorldScale(placed, Vector3.one);

            InteractionRoller roller = Undo.AddComponent<InteractionRoller>(holder);
            Configure(roller, placement, row.Select(c => c.Source).ToArray());

            if (placement.Belt) _belts++;
            else _rollers++;

            return roller;
        }

        // ---- rows -----------------------------------------------------------------------

        /// <summary>How far two axes may point apart and still be called the same shaft.</summary>
        const float AxisTolerance = 0.996f; // cos(5 degrees)

        /// <summary>How far apart two radii may be, as a fraction of the larger, to count as alike.</summary>
        const float RadiusTolerance = 0.15f;

        /// <summary>How far off a shared centre line a wheel may sit, as a fraction of its radius.</summary>
        const float OffsetTolerance = 0.25f;

        /// <summary>
        /// The candidates grouped into the rollers they will become: one row each without merging, and
        /// with it, one row per run of wheels that share a shaft. Rows keep the order they were read in,
        /// so a run over the same selection twice builds the same thing twice.
        /// </summary>
        IEnumerable<List<Candidate>> Rows(IReadOnlyList<Candidate> candidates)
        {
            var rows = new List<List<Candidate>>();

            foreach (Candidate candidate in candidates)
            {
                List<Candidate> row = MergeInline ? rows.FirstOrDefault(r => Joins(r[0], candidate)) : null;

                if (row != null) row.Add(candidate);
                else rows.Add(new List<Candidate> { candidate });
            }

            return rows;
        }

        /// <summary>
        /// Whether two candidates are the same shaft's wheels: turning about the same line, at much the
        /// same radius. Being far apart along that line is not disqualifying — wheels on a shaft are
        /// spaced, and closing those gaps into one long roller is the whole point of the merge.
        /// </summary>
        /// <remarks>
        /// A belt never joins anything. It is a slab with a travel direction rather than a wheel on a
        /// shaft, and two of them in a row are two conveyors, not one longer one.
        /// </remarks>
        static bool Joins(Candidate row, Candidate candidate)
        {
            if (row.Placement.Belt || candidate.Placement.Belt) return false;

            if (Mathf.Abs(Vector3.Dot(row.Axis, candidate.Axis)) < AxisTolerance) return false;

            float larger = Mathf.Max(row.Placement.RadiusInches, candidate.Placement.RadiusInches);
            if (larger <= 0f) return false;
            if (Mathf.Abs(row.Placement.RadiusInches - candidate.Placement.RadiusInches) > larger * RadiusTolerance)
                return false;

            // Distance from the row's centre line, which is what tells a second wheel on this shaft
            // from a wheel on the parallel shaft beside it.
            Vector3 offset = candidate.Position - row.Position;
            float lateral = Vector3.ProjectOnPlane(offset, row.Axis).magnitude;

            return lateral <= larger * MetersPerInch * OffsetTolerance;
        }

        /// <summary>
        /// The single roller a row of wheels becomes: one cylinder on their shared axis, long enough to
        /// reach from the outer face of the first to the outer face of the last, at the radius they
        /// average. The gaps between them are swept too — a shaft of wheels grips as one thing.
        /// </summary>
        /// <remarks>
        /// The radius is weighted by each wheel's own length so a wide wheel counts for more than a
        /// narrow one, and the axis is taken from the first wheel with the rest flipped onto it: the
        /// fitter names an axis without a sign, so half a row can come back pointing the other way.
        /// </remarks>
        static void Span(IReadOnlyList<Candidate> row,
                         ref Placement placement, ref Vector3 position, ref Quaternion rotation)
        {
            Vector3 axis = row[0].Axis;

            float min = float.MaxValue;
            float max = float.MinValue;
            float radius = 0f;
            float weight = 0f;

            foreach (Candidate candidate in row)
            {
                float along = Vector3.Dot(candidate.Position - row[0].Position, axis);
                float half = candidate.Placement.LengthInches * 0.5f * MetersPerInch;

                min = Mathf.Min(min, along - half);
                max = Mathf.Max(max, along + half);

                radius += candidate.Placement.RadiusInches * candidate.Placement.LengthInches;
                weight += candidate.Placement.LengthInches;
            }

            position = row[0].Position + axis * ((min + max) * 0.5f);
            rotation = row[0].Rotation;

            placement = new Placement(
                belt: false,
                localRotation: Quaternion.identity, // Already folded into the rotation carried above.
                radius: weight > 0f ? radius / weight : row[0].Placement.RadiusInches,
                length: (max - min) / MetersPerInch,
                beltLength: 0f);
        }

        /// <summary>This run's rollers for one model, and the node that will drive them.</summary>
        sealed class Group
        {
            public readonly Transform Node;
            public readonly List<Candidate> Candidates = new List<Candidate>();
            public readonly List<InteractionRoller> Rollers = new List<InteractionRoller>();

            public Group(Transform node) { Node = node; }
        }

        /// <summary>A mesh that has been read as a roller, before anything has been built for it.</summary>
        readonly struct Candidate
        {
            public readonly GameObject Source;
            public readonly Placement Placement;

            /// <summary>Where the fitted shape sits in world space, and which way it was fitted.</summary>
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;

            public Candidate(GameObject source, Placement placement, Vector3 position, Quaternion rotation)
            {
                Source = source;
                Placement = placement;
                Position = position;
                Rotation = rotation;
            }

            /// <summary>The axis it turns about, which is the roller's own local X.</summary>
            public Vector3 Axis => Rotation * Vector3.right;
        }

        /// <summary>
        /// The RollerGroup node this run is filling for a model, created inside its DynamicPhysics
        /// object the first time that model produces a roller.
        /// </summary>
        /// <remarks>
        /// A fresh node every run, not a reused one - the opposite of how DynamicPhysics itself is
        /// resolved, and deliberately. DynamicPhysics is a filing cabinet that should stay singular;
        /// a group is a statement about which rollers are driven together, and folding a later run's
        /// rollers into an earlier run's group would rewrite that statement behind your back.
        /// UniqueChildName keeps the second one alongside the first rather than over it.
        /// </remarks>
        Group GroupFor(Transform physics, string undoName)
        {
            if (_groups.TryGetValue(physics, out Group existing)) return existing;

            var node = new GameObject(HierarchyOps.UniqueChildName(physics, ModelLayout.RollerGroupName));
            Undo.RegisterCreatedObjectUndo(node, undoName);
            Undo.SetTransformParent(node.transform, physics, undoName);
            node.transform.localPosition = Vector3.zero;
            node.transform.localRotation = Quaternion.identity;
            node.transform.localScale = Vector3.one;

            // Keyed by the model's DynamicPhysics object, which is what "same model" means here -
            // the group node itself is the value, and every later mesh under that model finds it.
            var group = new Group(node.transform);
            _groups[physics] = group;
            return group;
        }

        /// <summary>
        /// The fields are private and serialized, which is where they belong - a tool reaching in is
        /// not a reason to widen the component's surface, so they are written through SerializedObject.
        /// </summary>
        static void Configure(InteractionRoller roller, Placement placement, GameObject[] sources)
        {
            var serialized = new SerializedObject(roller);
            serialized.FindProperty("useBeltMode").boolValue = placement.Belt;
            serialized.FindProperty("radius").floatValue = placement.RadiusInches;
            serialized.FindProperty("length").floatValue = placement.LengthInches;
            if (placement.Belt) serialized.FindProperty("beltLength").floatValue = placement.BeltLengthInches;

            // The meshes a ROLLER was guessed from are also the things that should be seen turning,
            // so they go into the models list - one entry each, multiplier 1, axis resolved below.
            //
            // A belt gets none. The models list is spun with Transform.Rotate whatever the mode, and
            // a belt's local X is its width rather than an axis it turns about, so an entry here
            // would tumble the slab end over end. Belt travel is not an animation this drives.
            if (!placement.Belt)
            {
                SerializedProperty models = serialized.FindProperty("models");
                models.arraySize = sources.Length;

                // Every mesh a merged roller was built from, not just the first: they were separate
                // wheels before the merge and all of them still have to be seen turning.
                for (int i = 0; i < sources.Length; i++)
                {
                    SerializedProperty entry = models.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("model").objectReferenceValue = sources[i];
                    entry.FindPropertyRelative("multiplier").floatValue = 1f;

                    ResolveSpinAxis(roller.transform, sources[i].transform, out int axis, out bool reversed);
                    entry.FindPropertyRelative("jointAxis").enumValueIndex = axis;
                    entry.FindPropertyRelative("reversed").boolValue = reversed;
                }
            }

            serialized.ApplyModifiedProperties();
        }

        /// <summary>
        /// Which of the mesh object's own axes the roller turns about, and whether it points the
        /// opposite way.
        /// </summary>
        /// <remarks>
        /// InteractionRoller spins a model with Transform.Rotate, which is self-space, so the axis has
        /// to be named in the MESH's frame rather than the roller's - the two are almost never aligned,
        /// since the roller was rotated onto the fitted shape and the mesh object was not. The roller
        /// always turns about its own local X (its trigger capsule is direction 0), so that direction
        /// carried back into the mesh's space is the answer, and the largest component names it.
        ///
        /// JointAxis cannot carry a sign, so a mesh whose axis runs against the spin would otherwise
        /// be drawn turning backwards; that is exactly what <c>reversed</c> is for.
        /// </remarks>
        static void ResolveSpinAxis(Transform roller, Transform source, out int axis, out bool reversed)
        {
            Vector3 spin = source.InverseTransformDirection(roller.right);

            axis = 0;
            if (Mathf.Abs(spin.y) > Mathf.Abs(spin[axis])) axis = 1;
            if (Mathf.Abs(spin.z) > Mathf.Abs(spin[axis])) axis = 2;

            reversed = spin[axis] < 0f;
        }

        /// <summary>How a fitted shape maps onto the roller's own frame and dimensions.</summary>
        readonly struct Placement
        {
            public readonly bool Belt;

            /// <summary>Turns the fitted box's frame into the roller's, which is not the same frame.</summary>
            public readonly Quaternion LocalRotation;

            public readonly float RadiusInches;
            public readonly float LengthInches;
            public readonly float BeltLengthInches;

            public Placement(bool belt, Quaternion localRotation, float radius, float length, float beltLength)
            {
                Belt = belt;
                LocalRotation = localRotation;
                RadiusInches = radius;
                LengthInches = length;
                BeltLengthInches = beltLength;
            }
        }

        /// <summary>
        /// Reads the fitted shape as a roller. False for anything that is neither - a sphere is not a
        /// roller in either mode, and guessing one anyway would put a spinning volume on a part that
        /// plainly is not one.
        /// </summary>
        static bool TryPlace(ShapeFit.Fit fit, Vector3 lossyScale, out Placement placement)
        {
            placement = default;

            // Full size along each of the fitted box's own axes, in world metres.
            Vector3 size = ShapeFit.WorldSize(fit, lossyScale);

            switch (fit.Primitive)
            {
                case PrimitiveType.Cylinder:
                case PrimitiveType.Capsule:
                {
                    int axis = fit.Axis;
                    int a = (axis + 1) % 3;
                    int b = (axis + 2) % 3;

                    // The roller's trigger is a CapsuleCollider with direction 0, so it spins about
                    // its own local X and that is where the fitted axis has to land. Whatever twist
                    // FromToRotation leaves around that axis is free - the shape is symmetric about
                    // the one axis it turns on.
                    //
                    // Radius comes from the mean of the two cross-section extents, matching how
                    // the fitter scored the cylinder in the first place, so an oval cross section
                    // is not judged against its widest span alone.
                    placement = new Placement(
                        belt: false,
                        localRotation: Quaternion.FromToRotation(Vector3.right, ShapeFit.Unit(axis)),
                        radius: (size[a] + size[b]) * 0.25f / MetersPerInch,
                        length: size[axis] / MetersPerInch,
                        beltLength: 0f);
                    return true;
                }

                case PrimitiveType.Cube:
                {
                    // A belt is a slab, so the fitted extents name themselves: thinnest across the
                    // belt's surface, longest along its travel, the remaining one its width. The
                    // roller's belt frame is X = width (its "length" field), Y = half thickness (its
                    // "radius" field, the reach either side of the surface), Z = travel.
                    Order(size, out int thin, out int middle, out int along);

                    placement = new Placement(
                        belt: true,
                        localRotation: Quaternion.LookRotation(ShapeFit.Unit(along), ShapeFit.Unit(thin)),
                        radius: size[thin] * 0.5f / MetersPerInch,
                        length: size[middle] / MetersPerInch,
                        beltLength: size[along] / MetersPerInch);
                    return true;
                }

                default:
                    return false;
            }
        }

        /// <summary>The three axis indices ordered by extent, smallest first.</summary>
        static void Order(Vector3 size, out int smallest, out int middle, out int largest)
        {
            int[] ranked = { 0, 1, 2 };
            System.Array.Sort(ranked, (x, y) => size[x].CompareTo(size[y]));
            smallest = ranked[0];
            middle = ranked[1];
            largest = ranked[2];
        }
    }

    /// <summary>One roller per mesh, each fitted to the shape that mesh reads as.</summary>
    public sealed class GuessInteractionRoller : GuessInteractionRollerBase
    {
        public override ToolInfo Info => new ToolInfo("Collider", "Guess Interaction Roller", 22,
            "Select the object(s) holding the meshes to fit.");

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/Guess Interaction Roller", KeyCode.R, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/Guess Interaction Roller", KeyCode.R, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { if (CanRun<GuessInteractionRoller>()) Menu(); }

        [MenuItem("GameObject/Guess Interaction Roller", false, 35)]
        static void Menu() => Run<GuessInteractionRoller>();

        [MenuItem("GameObject/Guess Interaction Roller", true)]
        static bool Validate() => CanRun<GuessInteractionRoller>();

        protected override bool MergeInline => false;
    }

    /// <summary>
    /// The same fit, but a row of like-sized wheels turning about one shaft becomes a single roller
    /// spanning the whole row instead of one roller each.
    /// </summary>
    /// <remarks>
    /// A shaft of wheels is one roller as far as the game piece is concerned: it meets the row across
    /// its width, not one wheel at a time, and the gaps between the wheels are exactly where a piece
    /// sags through and is dropped by a set of separate short rollers. One long roller also costs one
    /// overlap query a frame instead of one each, and leaves the group's list readable.
    ///
    /// The wheels still turn: every mesh the row was built from goes into the merged roller's models
    /// list, so each one is seen spinning on its own axis as before.
    /// </remarks>
    public sealed class GuessInteractionRollerMerged : GuessInteractionRollerBase
    {
        public override ToolInfo Info => new ToolInfo("Collider", "Guess Interaction Roller (Merge Inline)", 23,
            "Select the object(s) holding the meshes to fit.");

        [MenuItem("GameObject/Guess Interaction Roller (Merge Inline)", false, 36)]
        static void Menu() => Run<GuessInteractionRollerMerged>();

        [MenuItem("GameObject/Guess Interaction Roller (Merge Inline)", true)]
        static bool Validate() => CanRun<GuessInteractionRollerMerged>();

        protected override bool MergeInline => true;
    }
}
