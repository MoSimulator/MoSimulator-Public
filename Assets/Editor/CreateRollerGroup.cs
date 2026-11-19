using UnityEditor.ShortcutManagement;
using System.Linq;
using RobotFramework.Components;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Groups the selection under a new parent carrying a <see cref="RollerGroup"/>, wired to every
    /// InteractionRoller inside it. Nothing is merged, stripped or removed: the rollers keep their own
    /// settings and only gain a parent that can drive them together.
    /// </summary>
    public sealed class CreateRollerGroup : AuthoringTool
    {
        public override ToolInfo Info => new ToolInfo("Hierarchy", "Create Roller Group", 31,
            "Nothing in the selection has an InteractionRoller on it.");

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/Create Roller Group", KeyCode.K, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/Create Roller Group", KeyCode.K, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { if (CanRun<CreateRollerGroup>()) Menu(); }

        [MenuItem("GameObject/Create Roller Group", false, 41)]
        static void Menu() => Run<CreateRollerGroup>();

        [MenuItem("GameObject/Create Roller Group", true)]
        static bool Validate() => CanRun<CreateRollerGroup>();

        public override bool IsAvailable() => Has<InteractionRoller>(TargetScope.Children);

        protected internal override void Execute(ToolRun run)
        {
            GameObject[] roots = SelectionSet.Roots();
            if (roots.Length == 0) return;

            // Registered in hierarchy order rather than selection order, so the group's list reads the
            // same way the Hierarchy does and two runs over the same objects produce the same array.
            InteractionRoller[] rollers = roots
                .OrderBy(r => r.transform.GetSiblingIndex())
                .SelectMany(r => r.GetComponentsInChildren<InteractionRoller>(true))
                .Distinct()
                .ToArray();

            if (rollers.Length == 0)
            {
                run.Fail(Info.EmptyMessage);
                return;
            }

            foreach (GameObject root in roots) HierarchyOps.Unpack(root);

            Transform container = SelectionSet.CommonParent(roots);

            var parent = new GameObject(
                HierarchyOps.UniqueChildName(container, ModelLayout.RollerGroupName));
            Undo.RegisterCreatedObjectUndo(parent, run.Name);
            Undo.SetTransformParent(parent.transform, container, run.Name);

            // Sat on the rollers it drives rather than at the container's origin, so the group is
            // findable in the Scene view. Where it sits changes nothing about the result: reparenting
            // through Undo.SetTransformParent preserves each child's world transform.
            parent.transform.position = MeshMath.Average(rollers.Select(r => r.transform.position));
            parent.transform.rotation = container != null ? container.rotation : Quaternion.identity;
            parent.transform.localScale = Vector3.one;
            parent.transform.SetSiblingIndex(roots.Min(r => r.transform.GetSiblingIndex()));

            foreach (GameObject root in roots.OrderBy(r => r.transform.GetSiblingIndex()))
                Undo.SetTransformParent(root.transform, parent.transform, run.Name);

            RollerGroups.Wire(Undo.AddComponent<RollerGroup>(parent), rollers);

            Selection.activeGameObject = parent;

            run.Context = parent;
            run.Report($"grouped {roots.Length} object(s) under '{parent.name}', " +
                       $"driving {rollers.Length} roller(s).");
        }
    }
}
