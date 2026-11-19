using UnityEditor.ShortcutManagement;
using System.Collections.Generic;
using System.Linq;
using RobotFramework.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Editor
{
    /// <summary>Registers selected objects as animated models on an existing interaction roller.</summary>
    public sealed class AddObjectAsRollerModel : AuthoringTool
    {
        static GameObject[] pendingModels;
        static bool previousColliderMode;
        static string pendingName;

        public override ToolInfo Info => new ToolInfo("Hierarchy", "Add Object as Roller Model", 33,
            "Select a scene object to use as a roller model.");

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/Add Object as Roller Model", KeyCode.V, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/Add Object as Roller Model", KeyCode.V, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { if (CanRun<AddObjectAsRollerModel>()) Menu(); }

        [MenuItem("GameObject/Add Object as Roller Model", false, 51)]
        static void Menu() => Run<AddObjectAsRollerModel>();

        [MenuItem("GameObject/Add Object as Roller Model", true)]
        static bool Validate() => CanRun<AddObjectAsRollerModel>();

        public override bool IsAvailable() => !EditorApplication.isPlayingOrWillChangePlaymode &&
            Selection.gameObjects.Any(IsEditable);

        protected internal override bool UsesUndo => false;

        protected internal override void Execute(ToolRun run)
        {
            GameObject[] models = SelectionSet.Roots().Where(IsEditable).ToArray();
            if (models.Length == 0)
            {
                run.Fail(Info.EmptyMessage);
                return;
            }
            if (models.Any(model => model.scene != models[0].scene))
            {
                run.Fail("Select model objects from one scene at a time.");
                return;
            }

            BeginPicking(models, run.Name);
        }

        internal static void BeginPicking(GameObject[] models, string name)
        {
            CancelPicking();
            SnapToMeshCenter.Cancel();
            previousColliderMode = ColliderSelectMode.Enabled;
            pendingModels = models;
            pendingName = name;
            RollerMeasureTool.Stop();
            ColliderSelectMode.SetEnabled(true);
            Selection.selectionChanged += OnSelectionChanged;
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update += CheckPicking;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += CancelPicking;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
        }

        static void OnSelectionChanged()
        {
            if (pendingModels == null || !ColliderSelectMode.Enabled ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;

            GameObject selected = Selection.activeGameObject;
            InteractionRoller roller = selected != null ? selected.GetComponent<InteractionRoller>() : null;
            if (!CanAdd(roller, pendingModels)) return;

            GameObject[] models = pendingModels;
            string name = pendingName;
            CancelPicking();
            Add(roller, models, name);
        }

        static void OnSceneGUI(SceneView view)
        {
            if (pendingModels == null) return;
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                CancelPicking();
                e.Use();
                return;
            }

            Handles.BeginGUI();
            GUI.Label(new Rect(12, 12, Mathf.Min(580, view.position.width - 24), 42),
                $"Add {pendingModels.Length} model object(s): select an Interaction Roller.\nEscape cancels.",
                EditorStyles.helpBox);
            Handles.EndGUI();
        }

        static void CheckPicking()
        {
            if (pendingModels == null) return;
            // Switching the mode off explicitly should cancel without switching it back on.
            if (!ColliderSelectMode.Enabled) previousColliderMode = false;
            if (!ColliderSelectMode.Enabled || RollerMeasureTool.Active ||
                pendingModels.Any(model => !IsEditable(model))) CancelPicking();
            // Some editor selections (including preview scenes) do not emit selectionChanged.
            else OnSelectionChanged();
        }

        static void OnPlayModeChanged(PlayModeStateChange state) => CancelPicking();
        static void OnPrefabStageChanged(PrefabStage stage) => CancelPicking();

        internal static void CancelPicking()
        {
            if (pendingModels == null) return;
            pendingModels = null;
            pendingName = null;
            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= CheckPicking;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= CancelPicking;
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            ColliderSelectMode.SetEnabled(previousColliderMode);
        }

        static bool IsEditable(GameObject model) => model != null &&
            !EditorUtility.IsPersistent(model) && model.scene.IsValid();

        static bool CanAdd(InteractionRoller roller, GameObject[] models) => roller != null &&
            !EditorUtility.IsPersistent(roller) && models.Length > 0 &&
            models.All(model => IsEditable(model) && model.scene == roller.gameObject.scene &&
                // Animating the roller itself or an ancestor would rotate its physics geometry too.
                !roller.transform.IsChildOf(model.transform));

        static void Add(InteractionRoller roller, GameObject[] models, string name)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !CanAdd(roller, models)) return;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            try
            {
                int added = Append(roller, models);
                Debug.Log($"{name}: added {added} model(s) to '{roller.name}', " +
                          $"skipped {models.Length - added} already present.", roller);
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        internal static int Append(InteractionRoller roller, IReadOnlyList<GameObject> models)
        {
            var serialized = new SerializedObject(roller);
            SerializedProperty list = serialized.FindProperty("models");
            var existing = new HashSet<GameObject>();
            for (int i = 0; i < list.arraySize; i++)
                existing.Add(list.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("model").objectReferenceValue as GameObject);

            int added = 0;
            foreach (GameObject model in models)
            {
                if (model == null || !existing.Add(model)) continue;
                int index = list.arraySize++;
                SerializedProperty entry = list.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("model").objectReferenceValue = model;
                entry.FindPropertyRelative("multiplier").floatValue = 1f;

                // Transform.Rotate uses model-local axes; the roller spins about its own local X.
                Vector3 spin = model.transform.InverseTransformDirection(roller.transform.right);
                int axis = 0;
                if (Mathf.Abs(spin.y) > Mathf.Abs(spin[axis])) axis = 1;
                if (Mathf.Abs(spin.z) > Mathf.Abs(spin[axis])) axis = 2;
                entry.FindPropertyRelative("jointAxis").enumValueIndex = axis;
                entry.FindPropertyRelative("reversed").boolValue = spin[axis] < 0f;
                added++;
            }

            if (added > 0) serialized.ApplyModifiedProperties();
            return added;
        }
    }
}
