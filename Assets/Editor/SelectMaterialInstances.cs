using UnityEditor.ShortcutManagement;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Selects every visible renderer in the open hierarchy that uses the same material, for retouching
    /// or reassigning a material everywhere it actually shows up.
    /// </summary>
    public sealed class SelectMaterialInstances : AuthoringTool
    {
        public override ToolInfo Info => new ToolInfo("Select", "All of Material", 41,
            "Select an object with a renderer first.");

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/All of Material", KeyCode.A, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/All of Material", KeyCode.A, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { if (CanRun<SelectMaterialInstances>()) Menu(); }

        [MenuItem("GameObject/Select All of Material", false, 38)]
        static void Menu() => Run<SelectMaterialInstances>();

        [MenuItem("GameObject/Select All of Material", true)]
        static bool Validate() => CanRun<SelectMaterialInstances>();

        public override bool IsAvailable() => Has<Renderer>(TargetScope.Self);

        /// <summary>Nothing here changes the scene; only what is selected in it.</summary>
        protected internal override bool UsesUndo => false;

        protected internal override void Execute(ToolRun run)
        {
            var materials = new HashSet<Material>(SelectionSet.Targets<Renderer>(TargetScope.Self)
                .SelectMany(r => r.sharedMaterials)
                .Where(m => m != null));

            if (materials.Count == 0)
            {
                run.Fail(Info.EmptyMessage);
                return;
            }

            GameObject[] instances = SelectionSet.HierarchyRoots()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(false))
                .Where(r => r.enabled && r.gameObject.activeInHierarchy)
                .Where(r => r.sharedMaterials.Any(m => m != null && materials.Contains(m)))
                .Select(r => r.gameObject)
                .Distinct()
                .ToArray();

            Selection.objects = instances;

            string names = string.Join(", ", materials.Select(m => m.name).Take(3));
            if (materials.Count > 3) names += $" (+{materials.Count - 3} more)";
            run.Report($"{instances.Length} visible object(s) use {names}.");
        }
    }
}
