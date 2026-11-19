using UnityEditor.ShortcutManagement;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Editor
{
    /// <summary>Select a mesh destination, then confirm before moving the captured source objects.</summary>
    public sealed class SnapToMeshCenter : AuthoringTool
    {
        static GameObject[] sources;
        static GameObject candidate;
        static bool previousColliderMode;
        internal static bool Active => sources != null;

        public override ToolInfo Info => new ToolInfo("Hierarchy", "Snap to Mesh Center", 34,
            "Select the scene object(s) to move first.");

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/Snap to Mesh Center", KeyCode.S, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/Snap to Mesh Center", KeyCode.S, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { if (CanRun<SnapToMeshCenter>()) Menu(); }

        [MenuItem("GameObject/Snap to Mesh Center", false, 54)]
        static void Menu() => Run<SnapToMeshCenter>();

        [MenuItem("GameObject/Snap to Mesh Center", true)]
        static bool Validate() => CanRun<SnapToMeshCenter>();

        public override bool IsAvailable() => !EditorApplication.isPlayingOrWillChangePlaymode &&
            Selection.gameObjects.Any(IsEditable);
        protected internal override bool UsesUndo => false;

        static bool IsEditable(GameObject go) => go != null && !EditorUtility.IsPersistent(go) && go.scene.IsValid();

        protected internal override void Execute(ToolRun run)
        {
            GameObject[] objects = SelectionSet.Roots().Where(IsEditable).ToArray();
            if (objects.Length == 0) { run.Fail(Info.EmptyMessage); return; }
            if (objects.Any(go => go.scene != objects[0].scene))
            {
                run.Fail("Select source objects from one scene at a time.");
                return;
            }
            Begin(objects);
        }

        internal static void Begin(GameObject[] objects)
        {
            Cancel();
            ResizeInteractionRoller.Stop();
            AddObjectAsRollerModel.CancelPicking();
            RollerMeasureTool.Stop();
            sources = objects;
            candidate = null;
            previousColliderMode = ColliderSelectMode.Enabled;
            // The destination is a visible mesh, so use ordinary mesh picking for this step.
            ColliderSelectMode.SetEnabled(false);
            Selection.selectionChanged += RefreshCandidate;
            EditorApplication.update += CheckPicking;
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            PrefabStage.prefabStageOpened += OnStageChanged;
            PrefabStage.prefabStageClosing += OnStageChanged;
            SceneView.RepaintAll();
        }

        internal static bool TryCenter(GameObject meshObject, out Vector3 center)
        {
            center = default;
            if (!IsEditable(meshObject)) return false;
            MeshFilter filter = meshObject.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                center = filter.transform.TransformPoint(filter.sharedMesh.bounds.center);
                return true;
            }
            SkinnedMeshRenderer skinned = meshObject.GetComponent<SkinnedMeshRenderer>();
            if (skinned == null || skinned.sharedMesh == null) return false;
            center = skinned.transform.TransformPoint(skinned.localBounds.center);
            return true;
        }

        static bool CanTarget(GameObject go) => Active && TryCenter(go, out _) &&
            sources.All(source => IsEditable(source) && source.scene == go.scene &&
                !go.transform.IsChildOf(source.transform));

        internal static void RefreshCandidate()
        {
            if (!Active) return;
            GameObject next = CanTarget(Selection.activeGameObject) ? Selection.activeGameObject : null;
            if (next == candidate) return;
            candidate = next;
            // Enter should confirm even when the destination was selected through the Hierarchy.
            if (candidate != null) SceneView.lastActiveSceneView?.Focus();
            SceneView.RepaintAll();
        }

        static void CheckPicking()
        {
            if (!Active) return;
            if (ColliderSelectMode.Enabled) previousColliderMode = true;
            if (EditorApplication.isPlayingOrWillChangePlaymode || ColliderSelectMode.Enabled ||
                RollerMeasureTool.Active || sources.Any(source => !IsEditable(source))) Cancel();
            else RefreshCandidate();
        }

        internal static bool HandleKey(Event e)
        {
            if (!Active || e.type != EventType.KeyDown) return false;
            if (e.keyCode == KeyCode.Escape) Cancel();
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Confirm();
            else return false;
            e.Use();
            return true;
        }

        static void OnSceneGUI(SceneView view)
        {
            if (!Active || HandleKey(Event.current)) return;
            RefreshCandidate();
            if (candidate != null && TryCenter(candidate, out Vector3 center))
            {
                using (new Handles.DrawingScope(Color.cyan))
                {
                    Handles.DrawWireDisc(center, view.camera.transform.forward, HandleUtility.GetHandleSize(center) * 0.08f);
                    foreach (GameObject source in sources)
                        Handles.DrawDottedLine(source.transform.position, center, 4f);
                }
            }
            Handles.BeginGUI();
            GUI.Label(new Rect(12, 12, Mathf.Min(610, view.position.width - 24), 42),
                candidate != null ? $"Snap {sources.Length} object(s) to '{candidate.name}' mesh bounds center.\nEnter confirms. Select another mesh to change the target. Escape cancels."
                    : "Select a separate mesh to snap to.\nEnter confirms the selected mesh. Escape cancels.",
                EditorStyles.helpBox);
            Handles.EndGUI();
        }

        internal static bool Confirm()
        {
            RefreshCandidate();
            if (!CanTarget(candidate) || !TryCenter(candidate, out Vector3 center)) return false;
            GameObject[] objects = sources;
            Cancel();
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Snap to Mesh Center");
            foreach (GameObject go in objects)
            {
                Undo.RecordObject(go.transform, "Snap to Mesh Center");
                go.transform.position = center;
                if (PrefabUtility.IsPartOfPrefabInstance(go.transform))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            }
            Undo.CollapseUndoOperations(undo);
            Selection.objects = objects;
            return true;
        }

        static void OnPlayModeChanged(PlayModeStateChange state) => Cancel();
        static void OnStageChanged(PrefabStage stage) => Cancel();

        internal static void Cancel()
        {
            if (!Active) return;
            sources = null;
            candidate = null;
            Selection.selectionChanged -= RefreshCandidate;
            EditorApplication.update -= CheckPicking;
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
            PrefabStage.prefabStageOpened -= OnStageChanged;
            PrefabStage.prefabStageClosing -= OnStageChanged;
            ColliderSelectMode.SetEnabled(previousColliderMode);
            SceneView.RepaintAll();
        }
    }
}
