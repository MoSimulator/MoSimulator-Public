using UnityEditor.ShortcutManagement;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Selects every object in the same model that draws the same mesh, for spotting how often a part
    /// is reused and for editing all of its copies at once.
    /// </summary>
    public sealed class SelectMeshInstances : AuthoringTool
    {
        public override ToolInfo Info => new ToolInfo("Select", "All Instances of Mesh", 40,
            "Select an object with a mesh first.");

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/All Instances of Mesh", KeyCode.I, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/All Instances of Mesh", KeyCode.I, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { if (CanRun<SelectMeshInstances>()) Menu(); }

        [MenuItem("GameObject/Select All Instances of Mesh", false, 37)]
        static void Menu() => Run<SelectMeshInstances>();

        [MenuItem("GameObject/Select All Instances of Mesh", true)]
        static bool Validate() => CanRun<SelectMeshInstances>();

        public override bool IsAvailable() => Has<MeshFilter>(TargetScope.Self, f => f.sharedMesh != null);

        /// <summary>Nothing here changes the scene; only what is selected in it.</summary>
        protected internal override bool UsesUndo => false;

        protected internal override void Execute(ToolRun run)
        {
            var meshes = new HashSet<Mesh>(SelectionSet.Targets<MeshFilter>(TargetScope.Self)
                .Select(f => f.sharedMesh)
                .Where(m => m != null));

            if (meshes.Count == 0)
            {
                run.Fail(Info.EmptyMessage);
                return;
            }

            // The search is scoped to the models the selection sits in, not the whole scene.
            var roots = new HashSet<Transform>(Selection.transforms.Select(t => t.root));

            GameObject[] instances = roots
                .SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true))
                .Where(f => f.sharedMesh != null && meshes.Contains(f.sharedMesh))
                .Select(f => f.gameObject)
                .Distinct()
                .ToArray();

            Selection.objects = instances;

            string names = string.Join(", ", meshes.Select(m => m.name).Take(3));
            if (meshes.Count > 3) names += $" (+{meshes.Count - 3} more)";
            run.Report($"{instances.Length} object(s) use {names}.");
        }
    }
}
