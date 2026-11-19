using UnityEditor.ShortcutManagement;
using RobotFramework.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Editor
{
    /// <summary>Modal mouse sizing for the unscaled dimensions used by interaction roller physics.</summary>
    public abstract class ResizeInteractionRoller : AuthoringTool
    {
        const float MetresPerInch = 0.0254f;
        const float MinimumInches = 0.01f;
        static InteractionRoller target;
        static bool previousColliderMode, previousToolsHidden;
        static int dragUndo = -1;
        static float dragLength, dragRadius;
        static bool diameter, pointerReady, swallowLeftRelease, navigating;
        static float metresPerPoint;
        static SceneView ownerView;
        internal static bool Active => target != null;

        protected abstract bool ResizeDiameter { get; }

        public override bool IsAvailable() => CanResize(SelectedRoller());
        protected internal override bool UsesUndo => false;

        static InteractionRoller SelectedRoller() => Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<InteractionRoller>() : null;

        static bool CanResize(InteractionRoller roller) => roller != null &&
            !EditorApplication.isPlayingOrWillChangePlaymode && !EditorUtility.IsPersistent(roller) &&
            roller.gameObject.scene.IsValid() && !roller.EditorUsesBeltMode;

        protected internal override void Execute(ToolRun run)
        {
            InteractionRoller roller = SelectedRoller();
            if (!CanResize(roller)) { run.Fail(Info.EmptyMessage); return; }
            Begin(roller, ResizeDiameter);
        }

        internal static void Begin(InteractionRoller roller, bool resizeDiameter)
        {
            Stop();
            SnapToMeshCenter.Cancel();
            AddObjectAsRollerModel.CancelPicking();
            RollerMeasureTool.Stop();
            target = roller;
            diameter = resizeDiameter;
            pointerReady = false;
            navigating = false;
            ownerView = SceneView.lastActiveSceneView;
            previousColliderMode = ColliderSelectMode.Enabled;
            previousToolsHidden = Tools.hidden;
            ColliderSelectMode.SetEnabled(false);
            Tools.hidden = true;
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update += CheckTarget;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            PrefabStage.prefabStageClosing += OnStageClosing;
            SceneView.RepaintAll();
        }

        static void CheckTarget()
        {
            if (ColliderSelectMode.Enabled) previousColliderMode = true;
            if (!CanResize(target) || SelectedRoller() != target ||
                RollerMeasureTool.Active || ColliderSelectMode.Enabled) Stop();
        }

        static void OnPlayModeChanged(PlayModeStateChange state) => Stop();
        static void OnStageClosing(PrefabStage stage) => Stop();

        internal static void Stop()
        {
            if (ReferenceEquals(target, null)) return;
            FinishDrag(false);
            target = null;
            ownerView = null;
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= CheckTarget;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            PrefabStage.prefabStageClosing -= OnStageClosing;
            ColliderSelectMode.SetEnabled(previousColliderMode);
            Tools.hidden = previousToolsHidden;
            SceneView.RepaintAll();
        }

        static void OnSceneGUI(SceneView view)
        {
            if (!CanResize(target)) { Stop(); return; }
            if (ownerView == null) ownerView = view;
            if (view != ownerView) return;
            Event e = Event.current;
            if (HandleFinishEvent(e)) return;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Escape ||
                e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                FinishDrag(e.keyCode == KeyCode.Escape);
                Stop();
                e.Use();
                return;
            }

            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout && !e.alt && !navigating) HandleUtility.AddDefaultControl(control);
            if (e.alt || e.type == EventType.ScrollWheel) pointerReady = false;
            if (e.type == EventType.MouseMove && !e.alt && !navigating && GUIUtility.hotControl == 0)
            {
                // The first move after dismissing the menu establishes the pointer, avoiding a jump.
                if (!pointerReady)
                {
                    metresPerPoint = HandleUtility.GetHandleSize(target.transform.position) * 0.01f;
                    pointerReady = true;
                }
                else ApplyMouseDelta(e.delta.x * metresPerPoint, e.shift);
                e.Use();
                SceneView.RepaintAll();
            }
            DrawVolume(target.transform, target.EditorLength, target.EditorRadius);

            Handles.BeginGUI();
            GUI.Label(new Rect(12, 12, Mathf.Min(610, view.position.width - 24), 58),
                $"Resize {target.name}: length {target.EditorLength / MetresPerInch:F3} in, " +
                $"diameter {target.EditorRadius * 2f / MetresPerInch:F3} in\n" +
                $"Move mouse right/left to adjust {(diameter ? "diameter" : "length")}. Shift: fine adjustment.\n" +
                "Left-click or Enter finishes. Escape cancels. Right/middle-drag navigates.",
                EditorStyles.helpBox);
            Handles.EndGUI();
        }

        internal static void ApplyMouseDelta(float deltaMetres, bool precision)
        {
            if (target == null || deltaMetres == 0f) return;
            BeginDrag();
            float delta = precision ? deltaMetres * 0.1f : deltaMetres;
            SetSizeMetres(target, target.EditorLength + (diameter ? 0f : delta),
                target.EditorRadius + (diameter ? delta * 0.5f : 0f));
        }

        // Runs before ordinary scene picking. Camera navigation events remain available to Unity.
        internal static bool HandleFinishEvent(Event e)
        {
            if (Active && (e.button == 1 || e.button == 2))
            {
                if (e.rawType == EventType.MouseDown) navigating = true;
                if (e.rawType == EventType.MouseUp)
                {
                    navigating = false;
                    pointerReady = false;
                }
            }
            if (Active && e.type == EventType.MouseDown && e.button == 0 && !e.alt && !navigating)
            {
                Stop();
                swallowLeftRelease = true;
                e.Use();
                return true;
            }
            if (!Active && e.type == EventType.MouseDown && e.button == 0) swallowLeftRelease = false;
            if (!Active && swallowLeftRelease && e.type == EventType.MouseUp && e.button == 0)
            {
                swallowLeftRelease = false;
                e.Use();
                return true;
            }
            return false;
        }

        static void DrawVolume(Transform t, float length, float radius)
        {
            using (new Handles.DrawingScope(Color.cyan))
            {
                Vector3 a = t.position - t.right * (length * 0.5f);
                Vector3 b = t.position + t.right * (length * 0.5f);
                Handles.DrawWireDisc(a, t.right, radius);
                Handles.DrawWireDisc(b, t.right, radius);
                foreach (Vector3 direction in new[] { t.up, -t.up, t.forward, -t.forward })
                    Handles.DrawLine(a + direction * radius, b + direction * radius);
            }
        }

        internal static void BeginDrag()
        {
            if (dragUndo >= 0) return;
            dragLength = target.EditorLength;
            dragRadius = target.EditorRadius;
            Undo.IncrementCurrentGroup();
            dragUndo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(diameter ? "Set Roller Diameter" : "Set Roller Length");
        }

        internal static void FinishDrag(bool cancel)
        {
            if (dragUndo < 0) return;
            if (cancel && target != null) SetSizeMetres(target, dragLength, dragRadius);
            Undo.CollapseUndoOperations(dragUndo);
            dragUndo = -1;
        }

        internal static void SetSizeMetres(InteractionRoller roller, float length, float radius)
        {
            var serialized = new SerializedObject(roller);
            serialized.FindProperty("length").floatValue = Mathf.Max(MinimumInches, length / MetresPerInch);
            serialized.FindProperty("radius").floatValue = Mathf.Max(MinimumInches * 0.5f, radius / MetresPerInch);
            serialized.ApplyModifiedProperties();
        }
    }

    public sealed class SetInteractionRollerLength : ResizeInteractionRoller
    {
        public override ToolInfo Info => new ToolInfo("Collider", "Set Roller Length", 24,
            "Select an InteractionRoller in roller mode to resize.");
        protected override bool ResizeDiameter => false;

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/Set Roller Length", KeyCode.L, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/Set Roller Length", KeyCode.L, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { if (CanRun<SetInteractionRollerLength>()) Menu(); }

        [MenuItem("GameObject/Set Roller Length", false, 52)]
        static void Menu() => Run<SetInteractionRollerLength>();

        [MenuItem("GameObject/Set Roller Length", true)]
        static bool Validate() => CanRun<SetInteractionRollerLength>();
    }

    public sealed class SetInteractionRollerDiameter : ResizeInteractionRoller
    {
        public override ToolInfo Info => new ToolInfo("Collider", "Set Roller Diameter", 25,
            "Select an InteractionRoller in roller mode to resize.");
        protected override bool ResizeDiameter => true;

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/Set Roller Diameter", KeyCode.M, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/Set Roller Diameter", KeyCode.M, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { if (CanRun<SetInteractionRollerDiameter>()) Menu(); }

        [MenuItem("GameObject/Set Roller Diameter", false, 53)]
        static void Menu() => Run<SetInteractionRollerDiameter>();

        [MenuItem("GameObject/Set Roller Diameter", true)]
        static bool Validate() => CanRun<SetInteractionRollerDiameter>();
    }
}
