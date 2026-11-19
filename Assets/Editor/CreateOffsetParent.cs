using UnityEditor.ShortcutManagement;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Gives each selected object a parent that takes over its offset, leaving the object itself sat at
    /// its parent's origin: a local position of zero and no local rotation. Nothing moves — what the
    /// object was offset by is now carried one level up, where it can be edited on its own.
    /// </summary>
    /// <remarks>
    /// One parent per selected object rather than one for the selection: the offset a parent takes over
    /// is a single object's, and two objects offset differently cannot both be zeroed by the same one.
    /// The parent is created on a scale of one, so the scale a model carries stays on the model.
    /// </remarks>
    public sealed class CreateOffsetParent : AuthoringTool
    {
        const string ParentSuffix = "_Offset";

        public override ToolInfo Info => new ToolInfo("Hierarchy", "Create Offset Parent", 32);

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/Create Offset Parent", KeyCode.O, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/Create Offset Parent", KeyCode.O, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { if (CanRun<CreateOffsetParent>()) Menu(); }

        [MenuItem("GameObject/Create Offset Parent", false, 48)]
        static void Menu() => Run<CreateOffsetParent>();

        [MenuItem("GameObject/Create Offset Parent", true)]
        static bool Validate() => CanRun<CreateOffsetParent>();

        public override bool IsAvailable() => Selection.gameObjects.Length > 0;

        protected internal override void Execute(ToolRun run)
        {
            GameObject[] roots = SelectionSet.Roots();
            if (roots.Length == 0) return;

            foreach (GameObject root in roots) HierarchyOps.Unpack(root);

            var parents = new GameObject[roots.Length];

            for (int i = 0; i < roots.Length; i++)
                parents[i] = Insert(roots[i].transform, run);

            Selection.objects = parents;

            run.Context = parents[0];
            run.Report(roots.Length == 1
                ? $"offset '{roots[0].name}' onto a new parent '{parents[0].name}'."
                : $"gave {roots.Length} object(s) an offset parent each.");
        }

        /// <summary>
        /// Puts a new parent between the object and its container, on the object's current position and
        /// rotation. Reparenting through <see cref="Undo.SetTransformParent"/> keeps the object's world
        /// transform, so landing it on a parent already sat there is what zeroes its local one.
        /// </summary>
        static GameObject Insert(Transform target, ToolRun run)
        {
            Transform container = target.parent;

            var parent = new GameObject(HierarchyOps.UniqueChildName(container, target.name + ParentSuffix));
            Undo.RegisterCreatedObjectUndo(parent, run.Name);
            Undo.SetTransformParent(parent.transform, container, run.Name);

            parent.transform.position = target.position;
            parent.transform.rotation = target.rotation;
            parent.transform.localScale = Vector3.one;
            parent.transform.SetSiblingIndex(target.GetSiblingIndex());

            Undo.SetTransformParent(target, parent.transform, run.Name);
            return parent;
        }
    }
}
