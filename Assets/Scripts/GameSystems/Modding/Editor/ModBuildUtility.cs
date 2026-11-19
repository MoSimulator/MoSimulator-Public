using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameSystems.Management;
using UnityEditor;
using UnityEngine;

namespace GameSystems.Modding.Authoring.GameSystems.Modding.Editor
{
    public static class ModBuildUtility
    {
        public const string BuiltModsPath = "BuiltMods";
        public const string StagingFolderName = "_Staging";

        private static string BuiltModsRoot => Path.Combine(Path.GetDirectoryName(Application.dataPath), BuiltModsPath);

        private static readonly BuildTarget[] SupportedBuildTargets =
        {
            BuildTarget.StandaloneWindows64,
            BuildTarget.StandaloneOSX,
            BuildTarget.StandaloneLinux64
        };

        /// <summary>Per-platform folder names a full build produces, matching what the runtime loaders look for.</summary>
        public static IEnumerable<string> PlatformFolderNames =>
            SupportedBuildTargets.Select(target => target.ToString().ToLowerInvariant());

        [MenuItem("Tools/Modding/Full Build Selected Modpack (All Platforms)", priority = 1)]
        public static void FullBuildSelectedModpack()
        {
            var selected = Selection.activeObject as BaseModpackSO;

            if (selected == null)
            {
                EditorUtility.DisplayDialog("No Modpack Selected", "Please select a Modpack ScriptableObject to build.",
                    "OK");
                return;
            }

            BuildModpack(selected, fullBuild: true, buildAllPlatforms: true);
        }

        [MenuItem("Tools/Modding/Build Selected Modpack (Assets + Code)", priority = 2)]
        public static void BuildSelectedModpack()
        {
            var selected = Selection.activeObject as BaseModpackSO;

            if (selected == null)
            {
                EditorUtility.DisplayDialog("No Modpack Selected", "Please select a Modpack ScriptableObject to build.",
                    "OK");
                return;
            }

            BuildModpack(selected, fullBuild: true, buildAllPlatforms: false);
        }

        [MenuItem("Tools/Modding/Quick Build Selected Modpack (Code Only)", priority = 3)]
        public static void QuickBuildSelectedModpack()
        {
            var selected = Selection.activeObject as BaseModpackSO;

            if (selected == null)
            {
                EditorUtility.DisplayDialog("No Modpack Selected", "Please select a Modpack ScriptableObject to build.",
                    "OK");
                return;
            }

            BuildModpack(selected, fullBuild: false, buildAllPlatforms: false);
        }

        public static bool TryBuildModpack(BaseModpackSO modpack, bool fullBuild, out string buildPath,
            bool buildAllPlatforms = false)
        {
            if (modpack == null)
            {
                buildPath = null;
                return false;
            }

            buildPath = BuildModpack(modpack, fullBuild, buildAllPlatforms);
            return !string.IsNullOrEmpty(buildPath);
        }

        public static string GetModId(BaseModpackSO modpack)
        {
            if (modpack == null || string.IsNullOrWhiteSpace(modpack.ModpackName))
            {
                return null;
            }

            var name = Sanitize(modpack.ModpackName);
            var gameId = modpack.GameOption != null ? Sanitize(modpack.GameOption.name) : null;
            return string.IsNullOrEmpty(gameId) ? name : $"{gameId}_{name}";
        }

        /// <summary>Absolute folder the platform build folders for this modpack are written to.</summary>
        public static string GetBuildPath(BaseModpackSO modpack)
        {
            var modId = GetModId(modpack);
            return string.IsNullOrEmpty(modId) ? null : Path.Combine(BuiltModsRoot, modId);
        }

        /// <summary>Absolute folder uploaded to the Workshop: a copy of the build folder.</summary>
        public static string GetStagingPath(BaseModpackSO modpack)
        {
            var modId = GetModId(modpack);
            return string.IsNullOrEmpty(modId) ? null : Path.Combine(BuiltModsRoot, StagingFolderName, modId);
        }

        private static string BuildModpack(BaseModpackSO modpack, bool fullBuild = true, bool buildAllPlatforms = false)
        {
            var modId = GetModId(modpack);
            if (string.IsNullOrEmpty(modId))
            {
                Debug.LogError("Cannot build a modpack with no name.", modpack);
                return null;
            }

            var buildPath = GetBuildPath(modpack);

            Directory.CreateDirectory(buildPath);

            if (fullBuild)
            {
                AssignBundleNames(modpack, modId);
            }

            var buildTargets = buildAllPlatforms
                ? SupportedBuildTargets
                : new[] { EditorUserBuildSettings.activeBuildTarget };

            foreach (var buildTarget in buildTargets)
            {
                var platformFolder = buildTarget.ToString().ToLowerInvariant();
                var platformBuildPath = Path.Combine(buildPath, platformFolder);
                Directory.CreateDirectory(platformBuildPath);

                if (fullBuild)
                {
                    if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(buildTarget), buildTarget))
                    {
                        Debug.LogError($"Cannot build for {buildTarget}: install its build support module in Unity Hub.");
                        return null;
                    }

                    var build = new AssetBundleBuild
                    {
                        assetBundleName = $"{modId}_assets",
                        assetNames = AssetDatabase.GetAssetPathsFromAssetBundle($"{modId}_assets")
                    };

                    var manifest = BuildPipeline.BuildAssetBundles(platformBuildPath, new[] { build },
                        BuildAssetBundleOptions.None,
                        buildTarget);
                    if (manifest == null)
                    {
                        Debug.LogError($"AssetBundle build failed for {buildTarget}. See the errors above.");
                        return null;
                    }
                }

                var modDllName = CopyModAssemblyToBuild(modpack, platformBuildPath);
                GenerateModpackManifest(modpack, platformBuildPath, modDllName);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var scope = buildAllPlatforms ? "all platforms" : $"active platform ({EditorUserBuildSettings.activeBuildTarget})";
            Debug.Log($"Modpack '{modpack.ModpackName}' built successfully for {scope} at: {buildPath}");
            return buildPath;
        }

        private static void AssignBundleNames(BaseModpackSO modpack, string modId)
        {
            var bundleName = $"{modId}_assets";

            var modpackPath = AssetDatabase.GetAssetPath(modpack);
            if (!string.IsNullOrEmpty(modpackPath))
            {
                var importer = AssetImporter.GetAtPath(modpackPath);
                importer.assetBundleName = bundleName;
                Debug.Log($"Assigned bundle name '{importer.assetBundleName}' to asset at path: {modpackPath}");
            }

            foreach (var robotMetadata in modpack.Robots)
            {
                if (robotMetadata == null)
                {
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(robotMetadata);

                if (!string.IsNullOrEmpty(path))
                {
                    var importer = AssetImporter.GetAtPath(path);
                    importer.assetBundleName = bundleName;
                    Debug.Log($"Assigned bundle name '{importer.assetBundleName}' to asset at path: {path}");
                }
                
                var renderers = robotMetadata.RobotPrefab.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        if (mat != null)
                        {
                            var matPath = AssetDatabase.GetAssetPath(mat);
                            var matImporter = AssetImporter.GetAtPath(matPath);
                            if (matImporter != null)
                            {
                                matImporter.assetBundleName = bundleName;
                                Debug.Log(
                                    $"Assigned bundle name '{matImporter.assetBundleName}' to material at path: {matPath}");
                            }
                        }
                    }
                }

                // Metadata also references preload and alternate-variant prefabs outside RobotPrefab.
                // Make their dependencies explicit so runtime material enumeration includes them.
                if (!string.IsNullOrEmpty(path))
                {
                    var dependencies = AssetDatabase.GetDependencies(path, true);
                    foreach (var dependency in dependencies)
                    {
                        var depImporter = AssetImporter.GetAtPath(dependency);
                        if (depImporter != null)
                        {
                            depImporter.assetBundleName = bundleName;
                            Debug.Log(
                                $"Assigned bundle name '{depImporter.assetBundleName}' to dependency at path: {dependency}");
                        }
                    }
                }
            }
        }

        private static void GenerateModpackManifest(BaseModpackSO modpack, string buildPath, string modDllName)
        {
            var manifest = new ModpackManifest
            {
                id = GetModId(modpack) + "_assets",
                name = modpack.ModpackName,
                version = modpack.ModpackVersion,
                author = modpack.AuthorName,
                description = modpack.Description,
                gameYear = modpack.GameOption != null ? modpack.GameOption.GameYear : 0,
                gameId = modpack.GameOption != null ? modpack.GameOption.name : null,
                gameName = modpack.GameOption != null ? modpack.GameOption.name : null,
                gameType = modpack.GameOption != null ? modpack.GameOption.GameType.ToString() : null,
                modDll = modDllName,
                buildHost = Application.platform.ToString()
            };

            if (modpack != null)
            {
                var guid = modpack.GetGuid();
                var modpackEntry = new ModpackEntry
                {
                    metadataGuid = guid,
                    metadataName = modpack.ModpackName
                };
                manifest.modpack = modpackEntry;
            }
            else
            {
                Debug.LogWarning("Modpack ScriptableObject is null, cannot generate manifest entry.");
            }

            var manifestJson = JsonUtility.ToJson(manifest, true);
            var manifestPath = Path.Combine(buildPath, "modpack.json");
            File.WriteAllText(manifestPath, manifestJson);
        }

        private static string Sanitize(string modpackName)
        {
            foreach (var invalidPathChar in Path.GetInvalidFileNameChars())
            {
                modpackName = modpackName.Replace(invalidPathChar, '_');
            }

            return modpackName.Replace(" ", "_").ToLowerInvariant();
        }

        private static string CopyModAssemblyToBuild(BaseModpackSO modpack, string buildPath)
        {
            var modpackPath = AssetDatabase.GetAssetPath(modpack);
            if (string.IsNullOrEmpty(modpackPath))
            {
                Debug.LogWarning("Modpack asset path is empty; cannot resolve mod assembly.");
                return null;
            }

            var modRoot = Path.GetDirectoryName(modpackPath);
            if (string.IsNullOrEmpty(modRoot) || !Directory.Exists(modRoot))
            {
                Debug.LogWarning($"Modpack directory not found at: {modRoot}");
                return null;
            }

            var asmdefPaths = Directory.GetFiles(modRoot, "*.asmdef", SearchOption.AllDirectories);
            if (asmdefPaths.Length == 0)
            {
                Debug.LogWarning($"No asmdef found under modpack directory: {modRoot}");
                return null;
            }

            var asmdefPath = SelectAsmdef(asmdefPaths, modpack.ModpackName);
            if (string.IsNullOrEmpty(asmdefPath))
            {
                Debug.LogWarning($"Could not determine asmdef for modpack '{modpack.ModpackName}'.");
                return null;
            }

            var asmdefJson = File.ReadAllText(asmdefPath);
            var asmdefInfo = JsonUtility.FromJson<AsmdefInfo>(asmdefJson);
            if (asmdefInfo == null || string.IsNullOrEmpty(asmdefInfo.name))
            {
                Debug.LogWarning($"Asmdef at '{asmdefPath}' does not contain a valid name.");
                return null;
            }

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var dllPath = Path.Combine(projectRoot ?? string.Empty, "Library", "ScriptAssemblies", asmdefInfo.name + ".dll");
            if (!File.Exists(dllPath))
            {
                Debug.LogWarning($"Compiled mod DLL not found at: {dllPath}. Ensure the scripts compile first.");
                return null;
            }

            var destinationPath = Path.Combine(buildPath, Path.GetFileName(dllPath));
            File.Copy(dllPath, destinationPath, true);
            return Path.GetFileName(destinationPath);
        }

        private static string SelectAsmdef(string[] asmdefPaths, string modpackName)
        {
            if (asmdefPaths.Length == 1)
            {
                return asmdefPaths[0];
            }

            var targetName = Sanitize(modpackName);
            foreach (var path in asmdefPaths)
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(path), targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }

            Debug.LogWarning(
                $"Multiple asmdefs found for '{modpackName}'. Using first match at: {asmdefPaths[0]}");
            return asmdefPaths[0];
        }

        [Serializable]
        private class AsmdefInfo
        {
            public string name;
        }
    }
}
