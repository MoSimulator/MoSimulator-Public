using System;
using System.IO;
using GameSystems.Management;
using UnityEditor;
using UnityEngine;

namespace GameSystems.Modding.Authoring.GameSystems.Modding.Editor
{
    public class SteamWorkshopUploaderWindow : EditorWindow
    {
        private const string ItemUrlFormat = "https://steamcommunity.com/sharedfiles/filedetails/?id={0}";

        private sealed class ProgressSink : IProgress<float>
        {
            public volatile float Value;
            public void Report(float value) => Value = value;
        }

        private static bool s_isUploading;

        [SerializeField] private BaseModpackSO _modpack;
        [SerializeField] private string _changeNote;
        [SerializeField] private Vector2 _scroll;

        private ModpackWorkshopMetadata _metadata;
        private SerializedObject _serializedMetadata;
        private readonly ProgressSink _progress = new();
        private string _status;
        private MessageType _statusType;

        [MenuItem("Tools/Modding/Steam Workshop Uploader", priority = 20)]
        public static void ShowWindow()
        {
            var window = GetWindow<SteamWorkshopUploaderWindow>("Workshop Uploader");
            if (window._modpack == null && Selection.activeObject is BaseModpackSO selected)
            {
                window.SetModpack(selected);
            }
        }

        private void OnEnable()
        {
            SetModpack(_modpack);
        }

        private void OnInspectorUpdate()
        {
            if (s_isUploading)
            {
                Repaint();
            }
        }

        private void SetModpack(BaseModpackSO modpack)
        {
            _modpack = modpack;
            _metadata = ModpackWorkshopMetadataEditorUtility.FindMetadata(modpack);
            _serializedMetadata = _metadata == null ? null : new SerializedObject(_metadata);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            using (new EditorGUI.DisabledScope(s_isUploading))
            {
                DrawModpackSection();
                if (_metadata != null)
                {
                    EditorGUILayout.Space();
                    DrawDetailsSection();
                    EditorGUILayout.Space();
                    DrawBuildSection();
                }
            }

            if (_metadata != null)
            {
                EditorGUILayout.Space();
                DrawUploadSection();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawModpackSection()
        {
            var selected = (BaseModpackSO)EditorGUILayout.ObjectField("Modpack", _modpack, typeof(BaseModpackSO), false);
            if (selected != _modpack)
            {
                SetModpack(selected);
                _status = null;
            }

            if (_modpack == null)
            {
                EditorGUILayout.HelpBox("Select the modpack to publish.", MessageType.Info);
            }

            if (_modpack != null && _metadata == null && GUILayout.Button("Create Workshop Metadata"))
            {
                ModpackWorkshopMetadataEditorUtility.CreateMetadata(_modpack);
                SetModpack(_modpack);
            }
        }

        private void DrawDetailsSection()
        {
            EditorGUILayout.LabelField("Workshop Details", EditorStyles.boldLabel);
            _serializedMetadata.Update();
            EditorGUILayout.PropertyField(_serializedMetadata.FindProperty("title"));
            EditorGUILayout.PropertyField(_serializedMetadata.FindProperty("description"));
            EditorGUILayout.PropertyField(_serializedMetadata.FindProperty("previewImage"));
            EditorGUILayout.PropertyField(_serializedMetadata.FindProperty("visibility"));
            EditorGUILayout.PropertyField(_serializedMetadata.FindProperty("tags"), true);
            EditorGUILayout.PropertyField(_serializedMetadata.FindProperty("publishedFileId"));
            EditorGUILayout.PropertyField(_serializedMetadata.FindProperty("autoBumpPatchOnBuild"));
            _serializedMetadata.ApplyModifiedProperties();

            DrawVersionRow();

            EditorGUILayout.LabelField("Tags uploaded", string.Join(", ", WorkshopTags.Build(_metadata)));
        }

        private void DrawVersionRow()
        {
            var version = _modpack.ModpackVersion;
            var valid = WorkshopVersion.IsValid(version);
            EditorGUILayout.LabelField("Displayed Version", string.IsNullOrEmpty(version) ? "not set" : version);
            if (!valid)
            {
                EditorGUILayout.HelpBox("Set a version like 1.0.0 below, or type it on the modpack asset.", MessageType.Warning);
            }

            EditorGUI.BeginChangeCheck();
            var edited = EditorGUILayout.DelayedTextField("Set Version", version);
            if (EditorGUI.EndChangeCheck())
            {
                WorkshopVersion.SetVersion(_modpack, edited?.Trim());
            }

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!valid))
            {
                foreach (var bump in new[] { VersionBump.Major, VersionBump.Minor, VersionBump.Patch })
                {
                    if (GUILayout.Button($"Bump {bump}"))
                    {
                        WorkshopVersion.BumpModpack(_modpack, bump);
                    }
                }
            }
        }

        private void DrawBuildSection()
        {
            var buildFolder = ModBuildUtility.GetBuildPath(_modpack);
            var stagingFolder = ModBuildUtility.GetStagingPath(_modpack);
            var hasBuild = !string.IsNullOrEmpty(buildFolder) && Directory.Exists(buildFolder);
            var hasStaged = !string.IsNullOrEmpty(stagingFolder) && Directory.Exists(stagingFolder);

            EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Last build", hasBuild ? Directory.GetLastWriteTime(buildFolder).ToString("g") : "none");
            EditorGUILayout.LabelField("Staged", hasStaged ? Directory.GetLastWriteTime(stagingFolder).ToString("g") : "none");

            if (GUILayout.Button("Build All Platforms + Stage"))
            {
                BuildAndStage();
            }

            using (new EditorGUI.DisabledScope(!hasBuild))
            {
                if (GUILayout.Button("Stage Last Build"))
                {
                    Stage();
                }
            }
        }

        private void DrawUploadSection()
        {
            EditorGUILayout.LabelField("Upload", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(s_isUploading))
            {
                _changeNote = EditorGUILayout.TextField("Change Note", _changeNote);
            }

            var blocked = s_isUploading || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUI.DisabledScope(blocked))
            {
                if (GUILayout.Button("Upload Staged Build to Steam"))
                {
                    Upload();
                }
            }

            if (s_isUploading)
            {
                var rect = EditorGUILayout.GetControlRect();
                EditorGUI.ProgressBar(rect, _progress.Value, "Uploading to Steam...");
            }

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, _statusType);
            }

            if (_metadata.PublishedFileId != 0 && GUILayout.Button("Open on Steam"))
            {
                Application.OpenURL(string.Format(ItemUrlFormat, _metadata.PublishedFileId));
            }
        }

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
        }

        private bool Build()
        {
            if (_metadata.AutoBumpPatchOnBuild && !WorkshopVersion.BumpModpack(_modpack, VersionBump.Patch))
            {
                SetStatus("Cannot auto-bump: the modpack version must look like 1.0.0.", MessageType.Error);
                return false;
            }

            if (!ModBuildUtility.TryBuildModpack(_modpack, true, out _, buildAllPlatforms: true))
            {
                SetStatus("Build failed. See the Console for details.", MessageType.Error);
                return false;
            }

            return true;
        }

        private bool Stage()
        {
            if (!ModpackWorkshopStaging.PrepareStagingFolder(_modpack, ModBuildUtility.GetBuildPath(_modpack), out _))
            {
                SetStatus("Staging failed. See the Console for details.", MessageType.Error);
                return false;
            }

            SetStatus("Staged and ready to upload.", MessageType.Info);
            return true;
        }

        private void BuildAndStage()
        {
            if (Build())
            {
                Stage();
            }
        }

        private async void Upload()
        {
            var stagingFolder = ModBuildUtility.GetStagingPath(_modpack);
            var problems = WorkshopPublisher.Validate(_metadata, stagingFolder, _changeNote);
            if (problems.Count > 0)
            {
                SetStatus(string.Join("\n", problems), MessageType.Warning);
                return;
            }

            if (!EditorUtility.DisplayDialog("Upload to Steam Workshop",
                    WorkshopPublisher.Describe(_metadata, stagingFolder), "Upload", "Cancel"))
            {
                return;
            }

            s_isUploading = true;
            _progress.Value = 0;
            SetStatus(null, MessageType.None);

            // An assembly reload would drop the upload's callbacks halfway through.
            EditorApplication.LockReloadAssemblies();
            try
            {
                var outcome = await WorkshopPublisher.PublishAsync(_metadata, stagingFolder, _changeNote, _progress);
                if (outcome.Success)
                {
                    _changeNote = string.Empty;
                }
                else
                {
                    Debug.LogError($"Workshop upload failed: {outcome.Message}");
                }

                if (this)
                {
                    SetStatus(outcome.Message, outcome.Success ? MessageType.Info : MessageType.Error);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (this)
                {
                    SetStatus($"Upload failed: {exception.Message}", MessageType.Error);
                }
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
                s_isUploading = false;
                if (this)
                {
                    Repaint();
                }
            }
        }
    }
}
