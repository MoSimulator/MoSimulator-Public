using UnityEditor.ShortcutManagement;
using System.Linq;
using RobotFramework.Components;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>Adds selected interaction rollers to an existing group's drive list.</summary>
    public sealed class AddToRollerGroup : AuthoringTool
    {
        public override ToolInfo Info => new ToolInfo("Hierarchy", "Add to Roller Group", 32,
            "Nothing in the selection has an InteractionRoller on it.");

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/Add to Roller Group", KeyCode.J, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/Add to Roller Group", KeyCode.J, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { if (CanRun<AddToRollerGroup>()) Menu(); }

        [MenuItem("GameObject/Add to Roller Group", false, 49)]
        static void Menu() => Run<AddToRollerGroup>();

        [MenuItem("GameObject/Add to Roller Group", true)]
        static bool Validate() => CanRun<AddToRollerGroup>();

        public override bool IsAvailable() => !EditorApplication.isPlayingOrWillChangePlaymode &&
            SelectionSet.Targets<InteractionRoller>(TargetScope.Children).Any(IsEditable);

        // The picker makes no changes. Its callback owns the undo group when a target is chosen.
        protected internal override bool UsesUndo => false;

        protected internal override void Execute(ToolRun run)
        {
            InteractionRoller[] rollers = SelectionSet.Targets<InteractionRoller>(TargetScope.Roots)
                .Where(IsEditable).ToArray();
            if (rollers.Length == 0)
            {
                run.Fail(Info.EmptyMessage);
                return;
            }

            RollerGroup[] groups = SelectionSet.HierarchyRoots()
                .SelectMany(root => root.GetComponentsInChildren<RollerGroup>(true))
                .Where(group => CanAdd(group, rollers))
                .OrderBy(GroupLabel).ToArray();

            var menu = new GenericMenu();
            if (groups.Length == 0)
                menu.AddDisabledItem(new GUIContent("No Roller Groups in the selected rollers' scene"));

            foreach (RollerGroup group in groups)
            {
                RollerGroup target = group;
                string label = GroupLabel(group);
                if (groups.Count(g => GroupLabel(g) == label) > 1)
                    label += $" ({group.GetInstanceID()})";
                menu.AddItem(new GUIContent(label), false, () => Add(target, rollers, run.Name));
            }
            menu.ShowAsContext();
        }

        static bool IsEditable(InteractionRoller roller) => roller != null &&
            !EditorUtility.IsPersistent(roller) && roller.gameObject.scene.IsValid();

        static bool CanAdd(RollerGroup group, InteractionRoller[] rollers) => group != null &&
            !EditorUtility.IsPersistent(group) && rollers.Length > 0 &&
            rollers.All(r => IsEditable(r) && r.gameObject.scene == group.gameObject.scene);

        static string GroupLabel(RollerGroup group)
        {
            string path = group.name;
            for (Transform parent = group.transform.parent; parent != null; parent = parent.parent)
                path = parent.name + " > " + path;
            // Slashes in object names should not create extra popup submenus.
            return (group.gameObject.scene.name + ": " + path).Replace('/', '\u2215');
        }

        static void Add(RollerGroup group, InteractionRoller[] rollers, string name)
        {
            // Selection is captured before opening the picker; validate again in case objects changed.
            if (EditorApplication.isPlayingOrWillChangePlaymode || !CanAdd(group, rollers)) return;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            try
            {
                int added = RollerGroups.Append(group, rollers);
                Debug.Log($"{name}: added {added} roller(s) to '{group.name}'" +
                          $", skipped {rollers.Length - added} already present.", group);
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }
    }
}
