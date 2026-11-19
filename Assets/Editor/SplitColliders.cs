using UnityEditor.ShortcutManagement;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Lifts existing colliders off the renderer objects they share and files them under the model's
    /// "collider" object, the same nesting GuessGenericCollider produces.
    /// </summary>
    public sealed class SplitColliders : TargetTool<Collider>
    {
        /// <summary>Objects already filed this pass; an object carrying several colliders is filed once.</summary>
        readonly HashSet<GameObject> _handled = new HashSet<GameObject>();

        int _moved;

        public override ToolInfo Info => new ToolInfo("Collider", "Split Colliders", 21,
            "The selection holds no colliders.");

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/Split Colliders", KeyCode.X, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/Split Colliders", KeyCode.X, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { if (CanRun<SplitColliders>()) Menu(); }

        [MenuItem("GameObject/Split Colliders", false, 39)]
        static void Menu() => Run<SplitColliders>();

        [MenuItem("GameObject/Split Colliders", true)]
        static bool Validate() => CanRun<SplitColliders>();

        protected override void Begin(ToolRun run)
        {
            _handled.Clear();
            _moved = 0;
        }

        protected override bool Apply(Collider target, ToolRun run)
        {
            GameObject owner = target.gameObject;
            if (!_handled.Add(owner)) return false;

            HierarchyOps.Unpack(owner);

            int moved = SplitOne(owner, run.Name);
            _moved += moved;
            return moved > 0;
        }

        protected override void Finish(IReadOnlyList<Collider> targets, ToolRun run)
        {
            run.Report($"filed {_moved} collider(s) under their model's collider object.");
        }

        static int SplitOne(GameObject owner, string undoName)
        {
            if (owner.GetComponent<Collider>() == null) return 0;

            // Anything already sitting in a collider holder is filed; asking for the holder first would
            // nest a second "collider" object inside the first before anything got checked.
            if (ModelLayout.IsColliderHolder(owner.transform.parent)) return 0;

            return ColliderSplit.Extract(owner, ModelLayout.ColliderParent(owner.transform.parent, undoName), undoName);
        }
    }
}
