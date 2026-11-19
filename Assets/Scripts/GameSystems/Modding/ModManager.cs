using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameSystems.Management;
using MoSimCore.GameSelection;
using UnityEngine;
using Microsoft.Win32;
using Steamworks;

namespace GameSystems.Modding
{
    public class ModManager : MonoBehaviour
    {
        public static string ModsPath => Path.Combine(Application.persistentDataPath, "Mods");
        
        /// <summary>
        /// Gets the Steam Workshop mods path based on the current platform.
        /// Typically: {SteamAppsPath}/workshop/content/{AppID}/
        /// </summary>
        public static string SteamWorkshopModsPath
        {
            get
            {
                var steamAppsPath = GetSteamAppsPath();
                if (string.IsNullOrEmpty(steamAppsPath))
                    return null;
                var dir = new DirectoryInfo(steamAppsPath);
                var steamApps = dir.Parent?.Parent;
                if (steamApps == null || steamApps.Name != "steamapps")
                    return null;
                return Path.Combine(steamApps.FullName, "workshop", "content", SteamClient.AppId.Value.ToString());
            }
        }

        /// <summary>
        /// Gets the platform-specific folder name for asset bundles (e.g., "standalonewindows64")
        /// </summary>
        private static string PlatformBundleFolder
        {
            get
            {
#if UNITY_STANDALONE_WIN
                return "standalonewindows64";
#elif UNITY_STANDALONE_OSX
                return "standaloneosx";
#elif UNITY_STANDALONE_LINUX
                return "standalonelinux64";
#else
                return null;
#endif
            }
        }
        
        private RobotLoader _robotLoader;

        public List<LoadedMod> LoadedMods { get; } = new();

        /// <summary>Folders with no manifest, reported together rather than one warning each.</summary>
        private readonly List<string> _skippedMods = new();

        public bool ModsLoaded { get; private set; }

        private void Start()
        {
            _robotLoader = FindFirstObjectByType<RobotLoader>();
        }

        private void ReportProgress(float progress, string message)
        {
            if (!_robotLoader)
            {
                _robotLoader = FindFirstObjectByType<RobotLoader>();
            }

            _robotLoader?.ReportProgress(progress, message);
        }

        public IEnumerator LoadMods(IReadOnlyCollection<string> selectedModpackGuids)
        {
            LoadedMods.Clear();
            _skippedMods.Clear();
            ModsLoaded = false;

            var selectedGuids = new HashSet<string>(selectedModpackGuids ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            _robotLoader ??= FindFirstObjectByType<RobotLoader>();

            ReportProgress(RobotLoader.ModProgressStart, "Loading modpacks...");

            var modDirectories = new List<string>();
            var steamWorkshopModDirectories = new List<string>();

            // Collect local mods
            if (Directory.Exists(ModsPath))
            {
                modDirectories.AddRange(Directory.GetDirectories(ModsPath));
            }
            // else TODO: Delete as deprecated once no longer needed
            // {
            //     Directory.CreateDirectory(ModsPath);
            //     Debug.Log("Created Mods directory at: " + ModsPath);
            // }

            // Collect Steam Workshop mods if available
            if (Directory.Exists(SteamWorkshopModsPath))
            {
                Debug.Log($"Found Steam Workshop mods directory: {SteamWorkshopModsPath}");
                var workshopContentPath = SteamWorkshopModsPath;
                var appIdDirectories = Directory.GetDirectories(workshopContentPath);
                steamWorkshopModDirectories.AddRange(OrderNewestFirst(appIdDirectories));
                // foreach (var appIdDir in appIdDirectories)
                // {
                //     var modFolders = Directory.GetDirectories(appIdDir);
                //     steamWorkshopModDirectories.AddRange(modFolders);
                // }
            }
            else
            {
                Debug.LogWarning($"Steam Workshop mods directory not found at {SteamWorkshopModsPath}. Mods will not be loaded from Steam Workshop.");
            }

            var totalModDirectories = modDirectories.Count + steamWorkshopModDirectories.Count;
            
            if (totalModDirectories == 0)
            {
                ModsLoaded = true;
                ReportProgress(1f, "No mods found.");
                yield break;
            }

            var modsToLoad = new List<(string BundleSearchPath, ModpackManifest Manifest)>();

            // Local mods
            foreach (var modDir in modDirectories)
            {
                if (TryResolveMod(modDir, selectedGuids, false, out var bundleSearchPath, out var modManifest))
                {
                    modsToLoad.Add((bundleSearchPath, modManifest));
                }
            }

            // Steam Workshop mods
            foreach (var workshopModDir in steamWorkshopModDirectories)
            {
                Debug.Log($"Found Steam Workshop mod directory: {workshopModDir}");
                if (TryResolveMod(workshopModDir, selectedGuids, true, out var bundleSearchPath, out var modManifest))
                {
                    modsToLoad.Add((bundleSearchPath, modManifest));
                }
            }

            // Only the selected packs take any time, so they share the mods' stretch of the bar between them.
            for (var i = 0; i < modsToLoad.Count; i++)
            {
                var progressStart = Mathf.Lerp(RobotLoader.ModProgressStart, 1f, i / (float)modsToLoad.Count);
                var progressEnd = Mathf.Lerp(RobotLoader.ModProgressStart, 1f, (i + 1f) / modsToLoad.Count);
                yield return LoadModBundle(modsToLoad[i].BundleSearchPath, modsToLoad[i].Manifest, progressStart,
                    progressEnd);
            }

            if (!_robotLoader)
            {
                _robotLoader = FindFirstObjectByType<RobotLoader>();
                if (_robotLoader == null)
                {
                    Debug.LogError("ModManager could not find RobotLoader component.");
                }
            }

            if (_robotLoader)
            {
                foreach (var loadedMod in LoadedMods)
                {
                    if (loadedMod.ModpackSO)
                    {
                        _robotLoader.RegisterModpack(loadedMod.ModpackSO);
                    }
                }
            }
            else
            {
                Debug.LogWarning("ModManager could not register loaded modpacks because RobotLoader component was not found.");
            }
            
            if (_skippedMods.Count > 0)
            {
                Debug.LogWarning($"Skipped {_skippedMods.Count} mod folder(s) with no modpack.json: " +
                                 string.Join(", ", _skippedMods));
            }

            ModsLoaded = true;
            ReportProgress(1f, "Finished loading mods.");
        }

        private bool TryResolveMod(string modDirectory, ISet<string> selectedModpackGuids, bool isWorkshopMod,
            out string bundleSearchPath, out ModpackManifest modManifest)
        {
            modManifest = null;
            string manifestPath;

            if (isWorkshopMod)
            {
                // For Steam Workshop mods: look for manifest in platform-specific directory
                bundleSearchPath = Path.Combine(modDirectory, PlatformBundleFolder);
                manifestPath = Path.Combine(bundleSearchPath, "modpack.json");
            }
            else
            {
                // For local mods: prefer a root manifest, then fall back to the platform folder.
                manifestPath = Path.Combine(modDirectory, "modpack.json");
                bundleSearchPath = modDirectory;

                if (!File.Exists(manifestPath))
                {
                    bundleSearchPath = Path.Combine(modDirectory, PlatformBundleFolder);
                    manifestPath = Path.Combine(bundleSearchPath, "modpack.json");
                }
            }

            if (!File.Exists(manifestPath))
            {
                _skippedMods.Add(Path.GetFileName(modDirectory));
                return false;
            }

            try
            {
                var manifestJson = File.ReadAllText(manifestPath);
                modManifest = JsonUtility.FromJson<ModpackManifest>(manifestJson);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to read mod manifest at '{manifestPath}': {exception.Message}");
                return false;
            }

            var modpackGuid = modManifest?.modpack?.metadataGuid;
            if (string.IsNullOrWhiteSpace(modpackGuid) || !selectedModpackGuids.Contains(modpackGuid))
            {
                Debug.Log($"Skipping unselected mod '{modManifest?.name ?? modDirectory}'.");
                return false;
            }

            var gameId = modManifest.gameId;
            if (!string.IsNullOrWhiteSpace(gameId) && SelectedGameManager.Instance?.GameOptions != null &&
                !SelectedGameManager.Instance.GameOptions.Any(option => option != null && option.name == gameId))
            {
                Debug.LogWarning($"Mod '{modManifest.name}' targets game '{gameId}', which is not loaded.");
            }

            return true;
        }

        private IEnumerator LoadModBundle(string modDirectory, ModpackManifest modManifest, float progressStart,
            float progressEnd)
        {
            var bundlePath = Path.Combine(modDirectory, modManifest.id);
            var assetsStart = Mathf.Lerp(progressStart, progressEnd, 0.2f);

            // Which robot is loading cannot be told from the assets finishing, so the count follows the bar.
            var robotCount = ModBundles.RememberedRobotCount(modManifest.modpack?.metadataGuid);
            string ProgressMessage(float assetProgress)
            {
                if (robotCount == 0)
                {
                    return $"Loading {modManifest.name}...";
                }

                var robot = Mathf.Min(robotCount, Mathf.FloorToInt(assetProgress * robotCount) + 1);
                return $"Loading {modManifest.name} robot {robot}/{robotCount}...";
            }

            ReportProgress(progressStart, ProgressMessage(0f));

            // The assembly has to be in place before the bundle is read. Deserialising the bundle first
            // means any prefab backed by a mod script cannot resolve it, which leaves the robot without
            // its materials and renders it pink. The runtime path in ModBrowser already loads in this
            // order, which is why selecting a mod from the browser produced a correct model.
            var modAssembly = TryLoadAssembly(modDirectory, modManifest);

            // Already open from an earlier visit to this menu, or by the Workshop browser: Unity would refuse
            // a second load and hand back nothing.
            var openBundle = ModBundles.FindLoaded(modManifest.id);
            var ownsBundle = openBundle == null;
            if (ownsBundle)
            {
                var request = AssetBundle.LoadFromFileAsync(bundlePath);
                if (request == null)
                {
                    Debug.LogError($"Failed to load AssetBundle at path: {bundlePath}");
                    ReportProgress(progressEnd, $"Failed to load mod '{modManifest.name}'.");
                    yield break;
                }

                while (!request.isDone)
                {
                    ReportProgress(Mathf.Lerp(progressStart, assetsStart, request.progress), ProgressMessage(0f));
                    yield return null;
                }

                openBundle = request.assetBundle;
            }

            if (openBundle == null)
            {
                Debug.LogError($"Failed to load AssetBundle at path: {bundlePath}");
                ReportProgress(progressEnd, $"Failed to load mod '{modManifest.name}'.");
                yield break;
            }

            var loadedMod = new LoadedMod
            {
                Directory = modDirectory,
                ModManifest = modManifest,
                LoadedBundle = openBundle,
                ModAssembly = modAssembly
            };
            
            // Also for a bundle someone else opened: in the editor, one left from an earlier play session is
            // open with its assets unloaded.
            yield return ModBundles.PreloadAssets(openBundle, progress =>
                ReportProgress(Mathf.Lerp(assetsStart, progressEnd, progress), ProgressMessage(progress)));

            var modpacks = openBundle.LoadAllAssets<BaseModpackSO>();
            if (modpacks.Length == 0)
            {
                Debug.LogWarning($"No modpack metadata found in mod '{modManifest.name}'");
                ReportProgress(progressEnd, $"No modpack metadata found for '{modManifest.name}'.");
                yield break;
            }
            
            var modpackMetadata = modpacks
                .FirstOrDefault(m => m.ModpackName == modManifest.name);

            // A second Workshop item holding the same mod (a test upload beside the real one) shares its bundle
            // name, so it resolves to the copy already open. That one is already in the list.
            if (modpackMetadata && !ownsBundle && LoadedMods.Any(mod => mod.ModpackSO == modpackMetadata))
            {
                ReportProgress(progressEnd, $"Skipped duplicate of '{modManifest.name}'.");
                yield break;
            }

            if (modpackMetadata)
            {
                if (ownsBundle)
                {
                    ModBundles.UseGameShadersIfNeeded(openBundle, modManifest, modManifest.name);
                }

                loadedMod.ModpackSO = modpackMetadata;
                LoadedMods.Add(loadedMod);
                ModBundles.RememberRobotCount(modpackMetadata);
                Debug.Log($"Successfully loaded mod '{modManifest.name}' with modpack '{modpackMetadata.ModpackName}'.");
                ReportProgress(progressEnd, $"Loaded mod '{modManifest.name}'.");
            }
            else
            {
                Debug.LogWarning($"Modpack metadata '{modManifest.name}' not found in mod '{modManifest.name}'");
                ReportProgress(progressEnd, $"Skipped mod '{modManifest.name}': metadata not found.");
            }
        }

        /// <summary>
        /// Two Workshop items can hold the same mod - a test upload beside the real one - and they share a
        /// bundle name, so only the first one opened can be used. Opening the highest version first, then
        /// the most recently downloaded, makes that the same one every time rather than whichever the
        /// folder listing happens to start with.
        /// </summary>
        private static List<string> OrderNewestFirst(IEnumerable<string> directories)
        {
            (Version version, DateTime written) NewestKey(string directory)
            {
                var manifestPath = Path.Combine(directory, PlatformBundleFolder ?? string.Empty, "modpack.json");
                var version = new Version(0, 0, 0);
                var written = DateTime.MinValue;
                try
                {
                    if (File.Exists(manifestPath))
                    {
                        written = File.GetLastWriteTimeUtc(manifestPath);
                        var text = JsonUtility.FromJson<ModpackManifest>(File.ReadAllText(manifestPath))?.version;
                        var core = (text ?? string.Empty).Split('-', '+')[0].Trim().TrimStart('v', 'V');
                        if (Version.TryParse(core, out var parsed))
                        {
                            version = parsed;
                        }
                    }
                }
                catch (Exception)
                {
                    // An unreadable manifest is reported when its mod is loaded.
                }

                return (version, written);
            }

            return directories
                .Select(directory => (directory, key: NewestKey(directory)))
                .OrderByDescending(entry => entry.key.version)
                .ThenByDescending(entry => entry.key.written)
                .Select(entry => entry.directory)
                .ToList();
        }

        /// <summary>
        /// Closes every loaded mod whose files live under <paramref name="installFolder"/> so Steam can delete them.
        /// </summary>
        public void ReleaseModsInFolder(string installFolder, RobotLoader robotLoader)
        {
            if (string.IsNullOrWhiteSpace(installFolder))
            {
                return;
            }

            var folder = Path.GetFullPath(installFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;

            for (var i = LoadedMods.Count - 1; i >= 0; i--)
            {
                var loadedMod = LoadedMods[i];
                if (loadedMod == null || string.IsNullOrEmpty(loadedMod.Directory) ||
                    !Path.GetFullPath(loadedMod.Directory).StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (loadedMod.ModpackSO != null)
                {
                    robotLoader?.UnregisterModpack(loadedMod.ModpackSO);
                }

                if (loadedMod.LoadedBundle != null)
                {
                    loadedMod.LoadedBundle.Unload(false);
                    loadedMod.LoadedBundle = null;
                }

                LoadedMods.RemoveAt(i);
            }
        }

        private Assembly TryLoadAssembly(string modDirectory, ModpackManifest modManifest)
        {
            Debug.Log($"Mod directory: {modDirectory}");
            if (string.IsNullOrEmpty(modManifest.modDll))
            {
                Debug.LogError($"Mod manifest for '{modManifest.name}' does not specify a mod DLL.");
                return null;
            }

            var dllPath = Path.Combine(modDirectory, modManifest.modDll);

            var alreadyLoaded = ModBundles.FindLoadedAssembly(modManifest.modDll);
            if (alreadyLoaded != null)
            {
                return alreadyLoaded;
            }

            if (!File.Exists(dllPath))
            {
                Debug.LogError($"Mod DLL not found at path: {dllPath}");
                return null;
            }

            try
            {
                var assemblyBytes = File.ReadAllBytes(dllPath);
                var assembly = Assembly.Load(assemblyBytes);
                Debug.Log($"Successfully loaded mod assembly from DLL at path: {dllPath}");
                return assembly;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load mod assembly from DLL at path: {dllPath}\n{e}");
                return null;
            }
        }

        /// <summary>
        /// Attempts to locate the Steam installation directory based on the current platform.
        /// </summary>
        /// <returns>Path to the Steam directory, or null if not found.</returns>
        private static string GetSteamAppsPath()
        {
            if (!SteamClient.IsValid)
            {
                return null;
            }
            
            var result = SteamApps.AppInstallDir(SteamClient.AppId);
            Debug.Log($"SteamApps.AppInstallDir returned: {result}");
            return result;
        }
    }

    public class LoadedMod
    {
        public string Directory;
        public ModpackManifest ModManifest;
        public BaseModpackSO ModpackSO;
        public Assembly ModAssembly;

        public AssetBundle LoadedBundle;
    }
}