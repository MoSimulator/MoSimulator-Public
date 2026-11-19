using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Flattens the leftover grouping objects an import leaves behind: nodes carrying nothing but a
    /// Transform. Their children are promoted in place, keeping their world transforms and order.
    /// </summary>
    public sealed class RemoveEmptyParents : AuthoringTool
    {
        public override ToolInfo Info => new ToolInfo("Hierarchy", "Remove Empty Parents", 31);

        [MenuItem("GameObject/Remove Empty Parents", false, 42)]
        static void Menu() => Run<RemoveEmptyParents>();

        [MenuItem("GameObject/Remove Empty Parents", true)]
        static bool Validate() => CanRun<RemoveEmptyParents>();

        public override bool IsAvailable() => Selection.gameObjects.Length > 0;

        protected internal override void Execute(ToolRun run)
        {
            GameObject[] selected = Selection.gameObjects;
            if (selected.Length == 0) return;

            foreach (GameObject go in selected) HierarchyOps.Unpack(go);

            // The selected objects themselves are left alone: removing them would drop their contents
            // somewhere else.
            Transform[] candidates = selected
                .SelectMany(go => go.GetComponentsInChildren<Transform>(true))
                .Distinct()
                .Where(t => !selected.Contains(t.gameObject))
                .ToArray();

            int removed = HierarchyOps.Dissolve(candidates, run.Name, out int kept);

            string note = kept > 0 ? $" Kept {kept} that would have moved their children." : string.Empty;
            run.Report($"removed {removed} empty parent(s).{note}");
        }
    }
}
