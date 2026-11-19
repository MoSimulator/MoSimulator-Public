using UnityEditor.ShortcutManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using MoSimLib;
using RobotFramework;
using RobotFramework.Components;
using RobotFramework.GamePieceSystem;
using RobotFramework.Interfaces;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Sorts a subsystem's contents into the shape every model is supposed to have: its geometry under
    /// "Model", its colliders under the collider holder, and its rollers under DynamicPhysics in a group
    /// named after the subsystem. Nothing changes shape — objects are filed, and colliders sharing an
    /// object with a renderer are lifted off it the way Split Colliders does. The only thing dropped is
    /// an unreferenced object carrying nothing but a Transform, the way Remove Empty Parents drops it.
    /// </summary>
    /// <remarks>
    /// A Rigidbody, GenericJoint, or GenericAnimationJoint marks a subsystem boundary, so a descendant
    /// carrying one is a subsystem in its own right: its contents are never pulled up into the parent's
    /// folders, and it stays a direct child rather than becoming part of the parent's model. Each one is
    /// then organised on its own terms, so a single run over a robot's root tidies the whole thing.
    /// </remarks>
    public sealed class AutoOrganize : AuthoringTool
    {
        int _subsystems;
        int _model;
        int _colliders;
        int _rollers;
        int _intakes;
        int _animationTargets;
        int _emptied;
        int _rescaled;
        readonly Dictionary<RobotBase, Transform[]> _stateTargets = new();
        readonly HashSet<Collider> _crossSubsystemIntakeColliders = new();
        readonly HashSet<Transform> _animationNodes = new();
        readonly HashSet<Transform> _referencedNodes = new();
        readonly HashSet<Transform> _genericJointParents = new();
        readonly Dictionary<Collider, List<(MonoBehaviour Script, string Path)>> _colliderReferences = new();

        public override ToolInfo Info => new ToolInfo("Hierarchy", "Auto Organize", 30,
            "Select the subsystem(s) to organize.");

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/Auto Organize", KeyCode.H, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/Auto Organize", KeyCode.H, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { if (CanRun<AutoOrganize>()) Menu(); }

        [MenuItem("GameObject/Auto Organize", false, 43)]
        static void Menu() => Run<AutoOrganize>();

        [MenuItem("GameObject/Auto Organize", true)]
        static bool Validate() => CanRun<AutoOrganize>();

        public override bool IsAvailable() => Selection.gameObjects.Length > 0;

        protected internal override void Execute(ToolRun run)
        {
            GameObject[] roots = SelectionSet.Roots();
            if (roots.Length == 0) return;

            _subsystems = 0;
            _model = 0;
            _colliders = 0;
            _rollers = 0;
            _intakes = 0;
            _animationTargets = 0;
            _emptied = 0;
            _rescaled = 0;
            _stateTargets.Clear();
            _crossSubsystemIntakeColliders.Clear();
            _animationNodes.Clear();
            _referencedNodes.Clear();
            _genericJointParents.Clear();
            _colliderReferences.Clear();

            foreach (GameObject root in roots) HierarchyOps.Unpack(root);
            CaptureScriptReferences();
            foreach (GameObject root in roots) FlagCrossSubsystemIntakeColliders(root.transform);
            foreach (GameObject root in roots) FileXpbdNetNodes(root.transform, run);
            foreach (GameObject root in roots) Organize(root.transform, run);

            run.Context = roots[0];
            run.Report($"organized {_subsystems} subsystem(s): {_model} model object(s), " +
                       $"{_colliders} collider(s) and {_rollers} roller(s) filed, " +
                       $"{_intakes} intake(s) and {_animationTargets} animation target(s) filed, " +
                       $"{_emptied} empty object(s) removed, {_rescaled} object(s) put back on a scale of one.");
        }

        /// <summary>
        /// Files one subsystem, then each subsystem nested inside it. The order is the one thing here
        /// that has to hold: rollers leave first so they are not read as geometry, colliders leave next
        /// so they are not filed under Model, the objects those moves emptied are dropped, everything
        /// holding no geometry of its own is put back on a scale of one — which is what lets some of
        /// those be dropped at all — and whatever is left over is the model.
        /// </summary>
        void Organize(Transform subsystem, ToolRun run)
        {
            _subsystems++;

            // Read before anything moves. Nothing this pass does crosses a subsystem boundary, so the
            // list stays true, and reading it first keeps the folders this pass creates out of it.
            Transform[] nested = Nested(subsystem).ToArray();

            FileAnimationTargets(subsystem, run);
            FileIntakes(subsystem, run);
            FileFlaggedIntakeColliders(subsystem, run);
            FileRollers(subsystem, run);
            FileColliders(subsystem, run);
            PruneEmptyHolders(subsystem);
            NormalizeScales(subsystem, run);
            PruneEmptyNodes(subsystem, run);
            FileModel(subsystem, nested, run);
            OrderFolders(subsystem);

            foreach (Transform child in nested)
                if (child != null) Organize(child, run);
        }

        // ---- scale --------------------------------------------------------------------------

        /// <summary>
        /// Puts everything holding no geometry of its own back on a scale of one, from the subsystem
        /// root down, handing the scale it was carrying to its children. An import's hundredfold root,
        /// a folder some earlier pass left scaled: whatever the source, a scale on an object that is
        /// neither drawn nor collided with is a scale every later tool has to work around, and the
        /// model looks exactly the same with it pushed down to the geometry that is drawn.
        /// </summary>
        /// <remarks>
        /// Shallowest first, so a chain of scaled nodes hands its scale all the way down in one pass
        /// rather than one level per run: by the time a node is reached, whatever its parent handed it
        /// is already on it, and the last thing to receive it is the geometry, where a scale is the
        /// size of the thing itself rather than something imposed on it. This runs once the colliders
        /// have been lifted off, so an object that only carried them counts as grouping by then.
        /// </remarks>
        void NormalizeScales(Transform subsystem, ToolRun run)
        {
            Transform[] grouping = new[] { subsystem }
                .Concat(Content(subsystem))
                .Where(t => !IsGeometry(t))
                .OrderBy(HierarchyOps.Depth)
                .ToArray();

            foreach (Transform node in grouping)
                if (HierarchyOps.NormalizeScale(node, run.Name)) _rescaled++;
        }

        /// <summary>
        /// True when the object is something the scale is the size of, rather than something the scale
        /// is merely imposed on: what is drawn, what is collided with, what is emitted.
        /// </summary>
        static bool IsGeometry(Transform transform)
        {
            return transform.GetComponent<Renderer>() != null
                || transform.GetComponent<MeshFilter>() != null
                || transform.GetComponent<Collider>() != null
                || transform.GetComponent<ParticleSystem>() != null;
        }

        // ---- rollers ------------------------------------------------------------------------

        // ---- game-piece authoring ------------------------------------------------------------

        /// <summary>
        /// Files the state targets declared on the nearest robot script in the subsystem that owns
        /// them. Subsystem boundaries are respected, so a target on a moving mechanism stays with
        /// that mechanism rather than being pulled up to the robot root.
        /// </summary>
        void FileAnimationTargets(Transform subsystem, ToolRun run)
        {
            RobotBase robot = FindRobot(subsystem);
            if (robot == null) return;

            Transform[] targets = StateTargets(robot)
                .Where(target => target != null && (target == subsystem || InScope(target, subsystem)))
                .ToArray();
            if (targets.Length == 0) return;

            Transform animation = ModelLayout.AnimationParent(subsystem, run.Name);
            Transform[] nodes = targets.Select(target => TargetNode(target, subsystem))
                .Where(node => node != animation)
                .Distinct()
                .Where(node => !targets.Select(target => TargetNode(target, subsystem))
                    .Any(other => other != node && node.IsChildOf(other)))
                .ToArray();

            // Both the referenced target and the optional one-child wrapper are meaningful authoring
            // nodes. Keep them out of the later generic empty-node cleanup.
            foreach (Transform target in targets) _animationNodes.Add(target);
            foreach (Transform node in nodes) _animationNodes.Add(node);

            foreach (Transform node in nodes)
            {
                if (node.parent == animation) continue;
                Undo.SetTransformParent(node, animation, run.Name);
                _animationTargets++;
            }
        }

        /// <summary>
        /// Uses the target's otherwise-empty one-child parent as the animation node. This keeps an
        /// author-created pivot/group intact instead of leaving a meaningless wrapper behind.
        /// </summary>
        static Transform TargetNode(Transform target, Transform subsystem)
        {
            Transform parent = target.parent;
            return parent != null && parent.childCount == 1 && IsAnimationWrapper(parent) &&
                   parent != subsystem && InScope(parent, subsystem) && !IsFolder(parent) ? parent : target;
        }

        /// <summary>
        /// Audio sources do not make a target's offset parent structural. Keeping them with the target
        /// preserves both the authored local offset and any sound emitted from that offset.
        /// </summary>
        static bool IsAnimationWrapper(Transform transform)
        {
            return transform.GetComponents<Component>()
                .All(component => component is Transform || component is AudioSource);
        }

        /// <summary>
        /// Puts intake mechanisms in DynamicPhysics with their collider children. A collider beyond
        /// a subsystem boundary is deliberately not moved: that boundary's own pass files it with
        /// the subsystem that actually owns it.
        /// </summary>
        void FileIntakes(Transform subsystem, ToolRun run)
        {
            Transform[] intakes = Content(subsystem)
                .Concat(new[] { subsystem })
                .Where(transform => transform.GetComponents<MonoBehaviour>().Any(IsGamePieceIntake))
                .Where(transform => !Content(subsystem).Any(ancestor => ancestor != transform &&
                    ancestor.GetComponents<MonoBehaviour>().Any(IsGamePieceIntake) && transform.IsChildOf(ancestor)))
                .ToArray();
            if (intakes.Length == 0) return;

            Transform physics = ModelLayout.DynamicPhysicsParent(subsystem, run.Name);
            foreach (Transform intake in intakes)
            {
                if (intake == physics || intake.parent == physics) continue;
                Undo.SetTransformParent(intake, physics, run.Name);
                _intakes++;
            }
        }

        /// <summary>
        /// Finds intake colliders that cross a subsystem boundary before any hierarchy is
        /// changed. Their marker prevents the normal collider pass from filing them under Physics.
        /// </summary>
        void FlagCrossSubsystemIntakeColliders(Transform root)
        {
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true)
                         .Where(IsGamePieceIntake))
            {
                Transform intakeSubsystem = OwningSubsystem(component.transform);
                foreach (Collider collider in component.GetComponentsInChildren<Collider>(true))
                    if (OwningSubsystem(collider.transform) != intakeSubsystem)
                        _crossSubsystemIntakeColliders.Add(collider);
            }
        }

        /// <summary>
        /// Files a marked collider under the DynamicPhysics folder of the subsystem containing it.
        /// Extract is deliberately used here so a renderer sharing the collider remains in Model.
        /// </summary>
        void FileFlaggedIntakeColliders(Transform subsystem, ToolRun run)
        {
            Transform[] owners = Content(subsystem)
                .Concat(new[] { subsystem })
                .Where(transform => transform.GetComponents<Collider>()
                    .Any(collider => _crossSubsystemIntakeColliders.Contains(collider)))
                .ToArray();
            if (owners.Length == 0) return;

            Transform physics = ModelLayout.DynamicPhysicsParent(subsystem, run.Name);
            foreach (Transform owner in owners)
            {
                _colliders += ColliderSplit.Extract(owner.gameObject, physics, run.Name,
                    (original, replacement) => RewireColliderReferences(original, replacement, run.Name));
            }
        }

        /// <summary>
        /// Read before anything is replaced or deleted. Include inactive scripts and other scene roots:
        /// a script outside the selection can still reference an object being organized.
        /// </summary>
        void CaptureScriptReferences()
        {
            foreach (MonoBehaviour script in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (script == null || EditorUtility.IsPersistent(script) || !script.gameObject.scene.IsValid()) continue;

                // Both joint types read local position/rotation relative to this authoring parent.
                // Capture it before filing moves, even when no serialized field references it.
                if ((script is GenericJoint || script is GenericAnimationJoint) && script.transform.parent != null)
                    _genericJointParents.Add(script.transform.parent);

                using var serialized = new SerializedObject(script);
                SerializedProperty property = serialized.GetIterator();
                var visitedManagedReferences = new HashSet<long>();
                bool enterChildren = true;
                while (property.Next(enterChildren))
                {
                    // Next includes hidden fields; descend into arrays and nested serializable data.
                    // Strings have no references, and managed-reference cycles must only be entered once.
                    enterChildren = property.propertyType != SerializedPropertyType.String;
                    if (property.propertyType == SerializedPropertyType.ManagedReference)
                        enterChildren = visitedManagedReferences.Add(property.managedReferenceId);
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;

                    UnityEngine.Object target = property.objectReferenceValue;
                    if (target is GameObject gameObject) _referencedNodes.Add(gameObject.transform);
                    else if (target is Transform transform) _referencedNodes.Add(transform);
                    else if (target is Collider collider)
                    {
                        if (!_colliderReferences.TryGetValue(collider, out var references))
                            _colliderReferences.Add(collider, references = new());
                        references.Add((script, property.propertyPath));
                    }
                }
            }
        }

        /// <summary>Keeps all serialized script references attached to a copied collider, before deletion.</summary>
        void RewireColliderReferences(Collider original,
            Collider replacement, string undoName)
        {
            if (original == replacement || !_colliderReferences.TryGetValue(original, out var references)) return;

            foreach (var group in references.GroupBy(reference => reference.Script))
            {
                MonoBehaviour script = group.Key;
                if (script == null) continue;
                using var serialized = new SerializedObject(script);
                bool changed = false;
                foreach (var reference in group)
                {
                    SerializedProperty property = serialized.FindProperty(reference.Path);
                    if (property == null || property.propertyType != SerializedPropertyType.ObjectReference ||
                        property.objectReferenceValue != original) continue;

                    if (!changed) Undo.RecordObject(script, undoName);
                    property.objectReferenceValue = replacement;
                    changed = true;
                }

                if (changed) serialized.ApplyModifiedProperties();
            }
        }

        /// <summary>The closest Rigidbody or generic joint owns the transform; otherwise the scene root owns it.</summary>
        static Transform OwningSubsystem(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
                if (IsSubsystem(current)) return current;

            return transform.root;
        }

        static bool IsGamePieceIntake(MonoBehaviour component)
        {
            for (Type type = component.GetType(); type != null; type = type.BaseType)
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(GamePieceIntake<,>)) return true;

            return false;
        }

        /// <summary>
        /// XPBD net anchor targets are runtime physics nodes. Search every solver in the selected
        /// prefab before restructuring its hierarchy, then file each target with the
        /// subsystem it currently belongs to so generic empty-node cleanup cannot remove it.
        /// </summary>
        void FileXpbdNetNodes(Transform prefabRoot, ToolRun run)
        {
            Transform[] nodes = prefabRoot.GetComponentsInChildren<NetSolver>(true)
                .SelectMany(solver => solver.customAnchors ?? new List<NetSolver.NodeAnchor>())
                .Select(anchor => anchor.targetTransform)
                .Where(node => node != null && (node == prefabRoot || node.IsChildOf(prefabRoot)))
                .Distinct()
                .ToArray();
            var assignments = nodes.Select(node => new
            {
                Node = node,
                Subsystem = OwningSubsystem(node)
            }).ToArray();

            foreach (var assignment in assignments)
            {
                Transform physics = ModelLayout.DynamicPhysicsParent(assignment.Subsystem, run.Name);
                if (assignment.Node == physics || assignment.Node.parent == physics) continue;
                Undo.SetTransformParent(assignment.Node, physics, run.Name);
            }
        }

        RobotBase FindRobot(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                RobotBase robot = current.GetComponent<RobotBase>();
                if (robot != null) return robot;
            }

            return null;
        }

        Transform[] StateTargets(RobotBase robot)
        {
            if (_stateTargets.TryGetValue(robot, out Transform[] cached)) return cached;

            // Read Unity's serialized representation rather than the component's CLR fields. Besides
            // covering direct GamePieceState fields, this catches states nested in arrays/lists and
            // in serializable wrapper types, which reflection can easily miss.
            var targets = new List<Transform>();
            var serialized = new SerializedObject(robot);
            SerializedProperty property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.name != nameof(GamePieceState.stateTarget) ||
                    property.propertyType != SerializedPropertyType.ObjectReference) continue;

                if (property.objectReferenceValue is Transform target) targets.Add(target);
            }

            cached = targets.Distinct().ToArray();
            _stateTargets.Add(robot, cached);
            return cached;
        }

        /// <summary>
        /// Moves the subsystem's rollers under its DynamicPhysics object, one group per subsystem. An
        /// existing RollerGroup keeps its rollers and its wiring and is only moved and renamed; rollers
        /// belonging to no group get a new one, which is named after the subsystem either way.
        /// </summary>
        void FileRollers(Transform subsystem, ToolRun run)
        {
            Transform[] content = Content(subsystem).ToArray();

            Transform[] groups = content
                .Where(t => t.GetComponent<RollerGroup>() != null)
                .ToArray();

            // Group membership is decided before anything moves, and moving a group does not change
            // what is inside it, so this stays correct for the rest of the pass.
            Transform[] loose = content
                .Where(t => !groups.Any(g => t.IsChildOf(g)))
                .Where(IsRoller)
                .ToArray();

            if (groups.Length == 0 && loose.Length == 0) return;

            Transform physics = ModelLayout.DynamicPhysicsParent(subsystem, run.Name);

            foreach (Transform group in groups)
            {
                if (group == physics) continue;
                if (group.parent != physics) Undo.SetTransformParent(group, physics, run.Name);
                Rename(group, subsystem.name, run.Name);
            }

            _rollers += loose.Length;
            if (loose.Length == 0) return;

            var node = new GameObject(HierarchyOps.UniqueChildName(physics, subsystem.name));
            Undo.RegisterCreatedObjectUndo(node, run.Name);
            Undo.SetTransformParent(node.transform, physics, run.Name);
            node.transform.localPosition = Vector3.zero;
            node.transform.localRotation = Quaternion.identity;
            node.transform.localScale = Vector3.one;

            foreach (Transform roller in loose)
                Undo.SetTransformParent(roller, node.transform, run.Name);

            // The component goes on even when the group drives nothing yet, so a second run reads this
            // node as an existing group and moves it rather than building another one beside it. Only
            // InteractionRollers are wired: they are the only kind RollerGroup's list can name.
            RollerGroups.Wire(Undo.AddComponent<RollerGroup>(node), loose
                .SelectMany(t => t.GetComponents<InteractionRoller>())
                .ToArray());
        }

        /// <summary>True when the object drives something, rather than merely being drawn or collided with.</summary>
        static bool IsRoller(Transform transform)
        {
            return transform.GetComponents<MonoBehaviour>()
                .Any(component => component is IRoller && !(component is RollerGroup));
        }

        // ---- colliders ----------------------------------------------------------------------

        /// <summary>
        /// Files every collider in the subsystem under its collider holder, lifting the ones sharing an
        /// object with a renderer off it on the way. Rollers are left alone: what they collide with is
        /// built at runtime, not authored here.
        /// </summary>
        void FileColliders(Transform subsystem, ToolRun run)
        {
            Transform physics = subsystem.Find(ModelLayout.DynamicPhysicsName);

            // The subsystem's own colliders count: they belong in the holder like any other, and a
            // Rigidbody does not care which of its descendants carries them.
            Transform[] owners = Content(subsystem)
                .Concat(new[] { subsystem })
                .Where(t => physics == null || !t.IsChildOf(physics))
                .Where(t => !t.GetComponents<Collider>()
                    .Any(collider => _crossSubsystemIntakeColliders.Contains(collider)))
                .Where(t => t.GetComponent<Collider>() != null)
                .ToArray();

            if (owners.Length == 0) return;

            Transform holder = ModelLayout.ColliderParent(subsystem, run.Name);

            foreach (Transform owner in owners)
            {
                if (owner == null || owner == holder) continue;
                _colliders += ColliderSplit.Extract(owner.gameObject, holder, run.Name,
                    (original, replacement) => RewireColliderReferences(original, replacement, run.Name));
            }
        }

        /// <summary>
        /// Drops the collider holders left empty by the move — the per-object ones Split Colliders makes
        /// and the differently spelled ones older models carry. The subsystem's own holder is kept
        /// however empty it is, since the tools look it up by name.
        /// </summary>
        void PruneEmptyHolders(Transform subsystem)
        {
            Transform holder = subsystem.Find(ModelLayout.ColliderParentName);

            foreach (Transform candidate in Content(subsystem).ToArray())
            {
                if (candidate == null || candidate == holder) continue;
                if (_referencedNodes.Contains(candidate) || _genericJointParents.Contains(candidate)) continue;
                if (candidate.childCount > 0) continue;
                if (!IsColliderHolderName(candidate.name)) continue;
                if (candidate.GetComponents<Component>().Length > 1) continue;

                Undo.DestroyObjectImmediate(candidate.gameObject);
            }
        }

        /// <summary>
        /// Drops the grouping an import leaves behind and the leftovers of the moves above: objects
        /// carrying nothing but a Transform and no serialized script references. Generic joint parents
        /// are kept because they define the joint's local frame. Other nodes with children
        /// hand them up in their place, ones without simply go. This runs before the model is filed,
        /// so the geometry a dissolved node held is filed under Model directly rather than through
        /// a wrapper that does nothing.
        /// </summary>
        /// <remarks>
        /// The three folders the pattern is built from are kept whether or not they hold anything: the
        /// tools look them up by name, and a run that deleted them would rebuild them on the next one.
        /// </remarks>
        void PruneEmptyNodes(Transform subsystem, ToolRun run)
        {
            _emptied += HierarchyOps.Dissolve(Content(subsystem)
                .Where(t => !IsFolder(t) && !_animationNodes.Contains(t) && !_referencedNodes.Contains(t)
                            && !_genericJointParents.Contains(t)),
                run.Name, out int _);
        }

        static bool IsColliderHolderName(string name)
        {
            return string.Equals(name, ModelLayout.ColliderParentName, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "colliders", System.StringComparison.OrdinalIgnoreCase);
        }

        // ---- model --------------------------------------------------------------------------

        /// <summary>
        /// Everything the subsystem still owns that is neither a folder nor a subsystem of its own is
        /// geometry, and goes under Model with its own hierarchy intact. Generic joint parents stay
        /// alongside the nested subsystems so the joint's authoring frame remains separate from Model.
        /// </summary>
        void FileModel(Transform subsystem, IReadOnlyList<Transform> nested, ToolRun run)
        {
            Transform[] loose = subsystem.Cast<Transform>()
                .Where(t => !IsFolder(t))
                .Where(t => !nested.Contains(t))
                .Where(t => !_genericJointParents.Contains(t))
                .ToArray();

            if (loose.Length == 0) return;

            Transform model = ModelLayout.ModelParent(subsystem, run.Name);

            // In sibling order, so the model reads inside its folder the way it read outside it.
            foreach (Transform child in loose.OrderBy(t => t.GetSiblingIndex()))
            {
                Undo.SetTransformParent(child, model, run.Name);
                child.SetAsLastSibling();
                _model++;
            }
        }

        static bool IsFolder(Transform transform)
        {
            return transform.name == ModelLayout.ModelParentName
                || transform.name == ModelLayout.ColliderParentName
                || transform.name == ModelLayout.DynamicPhysicsName
                || transform.name == ModelLayout.AnimationParentName;
        }

        /// <summary>Puts the three folders in the order the pattern lists them, above any nested subsystem.</summary>
        static void OrderFolders(Transform subsystem)
        {
            int index = 0;
            foreach (string name in new[]
                     {
                         ModelLayout.ModelParentName,
                         ModelLayout.ColliderParentName,
                         ModelLayout.DynamicPhysicsName,
                         ModelLayout.AnimationParentName
                     })
            {
                Transform folder = subsystem.Find(name);
                if (folder != null) folder.SetSiblingIndex(index++);
            }
        }

        // ---- subsystem boundaries -----------------------------------------------------------

        /// <summary>The subsystems directly inside this one: the outermost Rigidbody or generic joint per branch.</summary>
        static IEnumerable<Transform> Nested(Transform subsystem)
        {
            return subsystem.GetComponentsInChildren<Transform>(true)
                .Where(t => t != subsystem && IsSubsystem(t) && InScope(t.parent, subsystem));
        }

        static bool IsSubsystem(Transform transform)
        {
            return transform.GetComponent<Rigidbody>() != null
                || transform.GetComponent<GenericJoint>() != null
                || transform.GetComponent<GenericAnimationJoint>() != null;
        }

        /// <summary>
        /// Everything the subsystem owns: its descendants, stopping at whatever belongs to someone else.
        /// </summary>
        static IEnumerable<Transform> Content(Transform subsystem)
        {
            return subsystem.GetComponentsInChildren<Transform>(true)
                .Where(t => t != subsystem && InScope(t, subsystem));
        }

        /// <summary>
        /// Whether the transform is the subsystem's to move: no Rigidbody or generic joint between it
        /// and the subsystem root, and nothing generated on the way down.
        /// </summary>
        /// <remarks>
        /// A PerTriangleMeshCollider owns the colliders under it — they are a snapshot of its mesh that
        /// it regenerates in place — so the whole branch is left where its component can find it, and
        /// travels as one piece with the object those colliders were built from.
        /// </remarks>
        static bool InScope(Transform transform, Transform subsystem)
        {
            for (Transform t = transform; t != null && t != subsystem; t = t.parent)
            {
                if (IsSubsystem(t)) return false;
                if (t.parent != null && t.parent.GetComponent<PerTriangleMeshCollider>() != null) return false;
            }

            return true;
        }

        static void Rename(Transform node, string wanted, string undoName)
        {
            if (node.name == wanted) return;

            Undo.RecordObject(node.gameObject, undoName);
            node.gameObject.name = HierarchyOps.UniqueChildName(node.parent, wanted);
        }
    }
}
