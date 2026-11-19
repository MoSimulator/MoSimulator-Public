using UnityEditor.ShortcutManagement;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Groups the selection under a new parent placed on its combined centre of mass. Nothing is merged,
    /// stripped or moved out of the hierarchy: the objects keep their geometry and colliders and only
    /// gain a pivot that sits where their mass does.
    /// </summary>
    public sealed class CreateParentCenterOfMass : AuthoringTool
    {
        const string ParentSuffix = "_COM";

        public override ToolInfo Info =>
            new ToolInfo("Hierarchy", "Create Parent at Center of Mass", 30);

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/Create Parent at Center of Mass", KeyCode.P, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/Create Parent at Center of Mass", KeyCode.P, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { if (CanRun<CreateParentCenterOfMass>()) Menu(); }

        [MenuItem("GameObject/Create Parent at Center of Mass", false, 40)]
        static void Menu() => Run<CreateParentCenterOfMass>();

        [MenuItem("GameObject/Create Parent at Center of Mass", true)]
        static bool Validate() => CanRun<CreateParentCenterOfMass>();

        public override bool IsAvailable() => Selection.gameObjects.Length > 0;

        protected internal override void Execute(ToolRun run)
        {
            GameObject[] roots = SelectionSet.Roots();
            if (roots.Length == 0) return;

            foreach (GameObject root in roots) HierarchyOps.Unpack(root);

            Transform container = SelectionSet.CommonParent(roots);

            string name = (container != null && roots.Length > 1 ? container.name : roots[0].name) + ParentSuffix;
            var parent = new GameObject(HierarchyOps.UniqueChildName(container, name));
            Undo.RegisterCreatedObjectUndo(parent, run.Name);
            Undo.SetTransformParent(parent.transform, container, run.Name);

            parent.transform.position = CenterOfMass(roots);
            parent.transform.rotation = container != null ? container.rotation : Quaternion.identity;
            parent.transform.localScale = Vector3.one;
            parent.transform.SetSiblingIndex(roots.Min(r => r.transform.GetSiblingIndex()));

            foreach (GameObject root in roots.OrderBy(r => r.transform.GetSiblingIndex()))
                Undo.SetTransformParent(root.transform, parent.transform, run.Name);

            Selection.activeGameObject = parent;

            run.Context = parent;
            run.Report($"grouped {roots.Length} object(s) under '{parent.name}'.");
        }

        /// <summary>
        /// Combined centre of mass of every mesh surface in the selection, in world space. Objects
        /// without geometry fall back to their transform positions so the pivot still lands somewhere
        /// sensible.
        /// </summary>
        static Vector3 CenterOfMass(GameObject[] roots)
        {
            var filters = roots.SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true));
            if (MeshMath.TrySurfaceCenter(filters, out Vector3 center)) return center;

            return MeshMath.Average(roots.Select(r => r.transform.position));
        }
    }
}
