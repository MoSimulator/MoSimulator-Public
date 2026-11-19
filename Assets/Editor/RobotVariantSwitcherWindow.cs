using System.Collections.Generic;
using GameSystems.Management;
using RobotFramework;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    public sealed class RobotVariantSwitcherWindow : EditorWindow
    {
        [SerializeField] private RobotMetadataSO robot;
        [SerializeField] private int variantIndex;
        [SerializeField] private bool applyOnPlay = true;

        private bool _pendingInitialSelection;
        private double _initialSelectionDeadline;
        private string _message;
        private MessageType _messageType;

        [MenuItem("Tools/Modding/Robot Variant Switcher")]
        private static void Open()
        {
            GetWindow<RobotVariantSwitcherWindow>("Robot Switcher").Show();
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && applyOnPlay && robot)
            {
                _pendingInitialSelection = true;
                _initialSelectionDeadline = EditorApplication.timeSinceStartup + 15;
                _message = "Waiting for the scene's initial spawn.";
                _messageType = MessageType.Info;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _pendingInitialSelection = false;
                _message = null;
            }

            Repaint();
        }

        private void OnEditorUpdate()
        {
            if (!_pendingInitialSelection) return;

            var controller = FindController();
            if (controller && (controller.EditorWaitingForInitialSpawn || controller.EditorInitialSpawnComplete))
            {
                _pendingInitialSelection = false;
                Apply(controller);
            }
            else if (EditorApplication.timeSinceStartup >= _initialSelectionDeadline)
            {
                _pendingInitialSelection = false;
                SetMessage("No ready robot spawn controller was found. Open a supported game scene and try again.",
                    MessageType.Error);
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Choose a metadata asset and variant. Switching during Play mode resets the match and field. " +
                "This tool is singleplayer only and does not change player settings or scene assets.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            robot = (RobotMetadataSO)EditorGUILayout.ObjectField("Robot metadata", robot, typeof(RobotMetadataSO), false);
            if (EditorGUI.EndChangeCheck()) variantIndex = 0;

            if (robot)
            {
                var options = new List<string>
                {
                    string.IsNullOrWhiteSpace(robot.RobotName) ? robot.TeamNumber.ToString() : robot.RobotName
                };
                var variants = robot.AlternateRobots;
                if (variants != null)
                {
                    for (var i = 0; i < variants.Count; i++)
                    {
                        var name = variants[i]?.DisplayName;
                        options.Add(string.IsNullOrWhiteSpace(name) ? $"{robot.TeamNumber}v{i + 1}" : name);
                    }
                }

                variantIndex = Mathf.Clamp(variantIndex, 0, options.Count - 1);
                variantIndex = EditorGUILayout.Popup("Variant", variantIndex, options.ToArray());
            }

            applyOnPlay = EditorGUILayout.Toggle(
                new GUIContent("Apply on entering Play", "Queue this selection for the scene's initial spawn."),
                applyOnPlay);

            var controller = Application.isPlaying ? FindController() : null;
            if (controller && controller.EditorRobotOverride)
            {
                EditorGUILayout.LabelField("Active override",
                    $"{controller.EditorRobotOverride.TeamName} (variant {controller.EditorVariantIndex})");
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying || EditorApplication.isPaused || !robot ||
                                               !controller || _pendingInitialSelection))
            {
                if (GUILayout.Button("Apply Robot"))
                {
                    if (!controller.EditorInitialSpawnComplete ||
                        EditorUtility.DisplayDialog("Reset match?",
                            "Switching robots resets the match and field.", "Reset and switch", "Cancel"))
                    {
                        Apply(controller);
                    }
                }
            }

            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, _messageType);
        }

        private void Apply(RobotSpawnController controller)
        {
            if (EditorApplication.isPaused)
            {
                SetMessage("Resume the Unity Editor before switching robots.", MessageType.Error);
                return;
            }

            var initialSpawn = !controller.EditorInitialSpawnComplete;
            if (!controller.EditorTrySwitchRobot(robot, variantIndex, out var error))
            {
                SetMessage(error, MessageType.Error);
                return;
            }

            SetMessage(initialSpawn
                ? "Selection queued for the initial spawn. Waiting for robot loading to finish."
                : "Match reset started with the selected robot.",
                MessageType.Info);
        }

        private void SetMessage(string message, MessageType type)
        {
            _message = message;
            _messageType = type;
            Repaint();
        }

        private static RobotSpawnController FindController()
        {
            return Object.FindFirstObjectByType<RobotSpawnController>();
        }
    }
}
