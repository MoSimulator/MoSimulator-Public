using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using RobotFramework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace GameSystems.Management
{
    public class RobotLoader : MonoBehaviour
    {
        public static RobotLoader Instance { get; private set; }

        private const string RobotMetadataLabel = "robot_metadata";
        private const string ModpackMetadataLabel = "modpack_metadata";

        public readonly List<RobotMetadataSO> LoadedRobots = new();
        public readonly List<BaseModpackSO> LoadedModpacks = new();

        public event Action<float, string> OnProgressUpdated;

        public bool AllRobotsLoaded => RobotLoadingCoroutine == null && ModLoadingCoroutine == null;

        public Coroutine RobotLoadingCoroutine { get; private set; }
        public Coroutine ModLoadingCoroutine { get; private set; }
        // public bool ModdedRobotsLoaded { get; private set; }

        [SerializeField] private BaseModpackSO defaultModpack;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (LoadedRobots.Count > 0)
            {
                Debug.LogWarning("RobotLoader already has loaded robots, skipping load.");
                return;
            }

            RobotLoadingCoroutine = StartCoroutine(LoadRobotMetadata());
            ModLoadingCoroutine = StartCoroutine(LoadModAssets());
        }

        public IEnumerator LoadRobotMetadata()
        {
            OnProgressUpdated?.Invoke(0f, "Finding robot metadata...");

            if (defaultModpack != null && !LoadedModpacks.Contains(defaultModpack))
            {
                LoadedModpacks.Add(defaultModpack);
            }

            var locationHandle =
                Addressables.LoadResourceLocationsAsync(RobotMetadataLabel, typeof(RobotMetadataSO));
            yield return locationHandle;

            if (locationHandle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"Failed to find resource locations for label {RobotMetadataLabel}");
                OnProgressUpdated?.Invoke(0.5f, $"Failed to find robot metadata: {locationHandle.Status}");
                // AllRobotsLoaded = true;
                RobotLoadingCoroutine = null;
                yield break;
            }

            var locations = locationHandle.Result;
            var totalLocations = locations.Count;
            if (totalLocations == 0)
            {
                OnProgressUpdated?.Invoke(0.5f, "No robot metadata found.");
            }

            for (var i = 0; i < totalLocations; i++)
            {
                var location = locations[i];
                var loadHandle = Addressables.LoadAssetAsync<RobotMetadataSO>(location);

                yield return loadHandle;

                if (loadHandle.Status == AsyncOperationStatus.Succeeded)
                {
                    var robot = loadHandle.Result;
                    if (!LoadedRobots.Contains(robot))
                    {
                        robot.IsModded = false;
                        LoadedRobots.Add(robot);
                        var key = location.PrimaryKey;
                        Debug.Log($"Loaded robot metadata: {robot} with key {key}");
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"Robot metadata {robot} already loaded, skipping.");
                    }

                    if (!defaultModpack.Robots.Contains(robot))
                    {
                        defaultModpack.Robots.Add(robot);
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"Robot metadata {robot} already in default modpack, skipping.");
                    }
                }
                else
                {
                    Debug.LogError($"Failed to load robot metadata from {location.PrimaryKey}");
                }

                var phaseProgress = totalLocations > 0 ? ((i + 1f) / totalLocations) * 0.5f : 0.5f;
                OnProgressUpdated?.Invoke(phaseProgress,
                    $"Loading robot metadata {i + 1}/{Math.Max(1, totalLocations)}...");
                Debug.Log($"Loading robot metadata {i + 1}/{Math.Max(1, totalLocations)}...");
            }

            LoadedRobots.Sort((a, b) => a.TeamNumber.CompareTo(b.TeamNumber));
            if (defaultModpack != null && defaultModpack.Robots != null)
            {
                defaultModpack.Robots.Sort((a, b) =>
                    a.TeamNumber.CompareTo(b.TeamNumber));
            }

            OnProgressUpdated?.Invoke(0.5f, "Finished loading robot metadata.");
            Debug.Log("Finished loading base robot metadata.");

            Addressables.Release(locationHandle);

            RobotLoadingCoroutine = null;
        }

        public IEnumerator LoadModAssets()
        {
            yield return new WaitUntil(() => RobotLoadingCoroutine == null);

            Debug.Log("Starting to load mod assets...");

            var modPath = Path.Combine(Application.persistentDataPath, "Mods");

            if (!Directory.Exists(modPath))
            {
                Debug.LogWarning($"Mod directory {modPath} does not exist. Creating mod directory.");
                Directory.CreateDirectory(modPath);
                OnProgressUpdated?.Invoke(1f, "No mods found.");
                // ModdedRobotsLoaded = true;
                ModLoadingCoroutine = null;
                yield break;
            }
            

            var modDirs = Directory.GetDirectories(modPath);
            if (modDirs.Length == 0)
            {
                OnProgressUpdated?.Invoke(1f, "No mods found.");
                // ModdedRobotsLoaded = true;
                ModLoadingCoroutine = null;
                yield break;
            }

            Debug.Log($"Mod directories found: {string.Join(", ", modDirs)}");
            for (var i = 0; i < modDirs.Length; i++)
            {
                var modDir = modDirs[i];

                LoadModAssembly(modDir);

                var catalogPath = Path.Combine(modDir, "catalog.json");

                if (!File.Exists(catalogPath))
                {
                    Debug.LogWarning($"Modded robot at {modDir} not found.");
                    var partial = 0.5f + ((i + 1f) / modDirs.Length) * 0.5f;
                    OnProgressUpdated?.Invoke(partial, $"Skipping mod ({i + 1}/{modDirs.Length}).");
                    continue;
                }

                Debug.Log($"Preparing to load mod catalog file: {catalogPath}");

                var json = File.ReadAllText(catalogPath);

                json = json.Replace("\\", "/");

                var modAaPlatformDir = Path.Combine(modDir, "aa", "StandaloneWindows64") + Path.DirectorySeparatorChar;
                var modAaPlatformUri = new Uri(Path.GetFullPath(modAaPlatformDir)).AbsoluteUri;

                var persistentDataUriPrefix =
                    new Uri(Application.persistentDataPath + Path.DirectorySeparatorChar).AbsoluteUri;

                json = Regex.Replace(json,
                    @"\{UnityEngine\.AddressableAssets\.Addressables\.RuntimePath\}[/\\]StandaloneWindows64[/\\]",
                    modAaPlatformUri,
                    RegexOptions.IgnoreCase);

                json = Regex.Replace(json,
                    @"\{UnityEngine\.Application\.persistentDataPath\}[/\\]",
                    persistentDataUriPrefix,
                    RegexOptions.IgnoreCase);

                json = Regex.Replace(json,
                    @"(?<!https?:\/\/)[/\\]?aa[/\\]StandaloneWindows64[/\\]",
                    modAaPlatformUri,
                    RegexOptions.IgnoreCase);

                var tempCatalog = Path.Combine(Path.GetTempPath(), $"catalog_mod_{Guid.NewGuid()}.json");
                File.WriteAllText(tempCatalog, json);

                var catalogUri = new Uri(Path.GetFullPath(tempCatalog)).AbsoluteUri;
                Debug.Log($"Loading rewritten catalog URI={catalogUri}");

                var loadCatalogHandle = Addressables.LoadContentCatalogAsync(catalogUri, false);
                yield return loadCatalogHandle;

                if (loadCatalogHandle.Status == AsyncOperationStatus.Succeeded)
                {
                    Debug.Log($"Successfully loaded mod catalog from {catalogUri}");

                    var locationHandle =
                        Addressables.LoadResourceLocationsAsync(ModpackMetadataLabel, typeof(BaseModpackSO));
                    yield return locationHandle;

                    if (locationHandle.Status == AsyncOperationStatus.Succeeded)
                    {
                        var locations = locationHandle.Result;
                        for (var j = 0; j < locations.Count; j++)
                        {
                            var location = locations[j];
                            var loadHandle = Addressables.LoadAssetAsync<BaseModpackSO>(location);
                            yield return loadHandle;

                            if (loadHandle.Status == AsyncOperationStatus.Succeeded)
                            {
                                if (!LoadedModpacks.Contains(loadHandle.Result))
                                {
                                    LoadedModpacks.Add(loadHandle.Result);
                                    Debug.Log($"Loaded modpack metadata: {loadHandle.Result}");
                                }

                                foreach (var robotMetadataSo in loadHandle.Result.Robots)
                                {
                                    if (robotMetadataSo == null)
                                    {
                                        Debug.LogWarning(
                                            $"Modpack {loadHandle.Result} contains a null robot reference, skipping.");
                                        continue;
                                    }
                                    
                                    robotMetadataSo.IsModded = true;
                                    if (!LoadedRobots.Contains(robotMetadataSo))
                                    {
                                        LoadedRobots.Add(robotMetadataSo);
                                        Debug.Log(
                                            $"Loaded modded robot metadata: {robotMetadataSo} from modpack {loadHandle.Result}");
                                    }
                                    else
                                    {
                                        Debug.LogWarning(
                                            $"Robot metadata {robotMetadataSo} from modpack {loadHandle.Result} already loaded, skipping.");
                                    }
                                }
                            }
                            else
                            {
                                Debug.LogError($"Failed to load modpack metadata from {location.PrimaryKey}");
                            }
                        }
                        
                        Addressables.Release(locationHandle);
                    }
                    else
                    {
                        Debug.LogError($"Failed to find resource locations for label {ModpackMetadataLabel}");
                    }
                }
                else
                {
                    Debug.LogError($"Failed to load catalog from {catalogUri}");
                }
                
                Addressables.Release(loadCatalogHandle);

                try
                {
                    if (File.Exists(tempCatalog))
                    {
                        File.Delete(tempCatalog);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to unload mod catalog from {catalogUri}: {e.Message}");
                }

                var progress = 0.5f + ((i + 1f) / modDirs.Length) * 0.5f;
                OnProgressUpdated?.Invoke(progress, $"Loading mods ({i + 1}/{modDirs.Length})...");
            }


            LoadedRobots.Sort((firstRobot, secondRobot) => firstRobot.TeamNumber.CompareTo(secondRobot.TeamNumber));
            LoadedModpacks.ForEach(modPack => modPack.Robots.Sort((firstRobot, secondRobot) =>
                firstRobot.TeamNumber.CompareTo(secondRobot.TeamNumber)));
            // ModdedRobotsLoaded = true;
            ModLoadingCoroutine = null;
            OnProgressUpdated?.Invoke(1f, "Finished loading mods.");
            Debug.Log("Finished loading all mod assets.");
        }

        private void LoadModAssembly(string modDir)
        {
            var dllPaths = Directory.GetFiles(modDir, "*.dll");

            if (dllPaths.Length == 0)
            {
                Debug.LogWarning($"No DLLs found in mod directory {modDir}, skipping assembly load.");
                return;
            }

            foreach (var dllPath in dllPaths)
            {
                if (!File.Exists(dllPath))
                {
                    Debug.LogWarning($"Mod assembly DLL not found at {dllPath}, skipping assembly load.");
                    return;
                }

                try
                {
                    var assembly = Assembly.LoadFrom(dllPath);
                    Debug.Log($"Successfully loaded mod assembly from {dllPath}: {assembly.FullName}");

                    var patchesType = assembly.GetType("Patches");
                    if (patchesType != null) {
                        Debug.Log($"Found Patches class for {assembly.FullName}");

                        var initializeMethod = patchesType.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public);
                        if (initializeMethod != null) {
                            Debug.Log($"Found Patches.Initialize method for {assembly.FullName}, attempting to invoke");
                            initializeMethod.Invoke(null, null);
                            Debug.Log($"Successfully invoked Patches.Initialize method for {assembly.FullName}");
                        } else {
                            Debug.Log($"No Patches.Initialize method found for {assembly.FullName}");
                        }
                    } else {
                        Debug.Log($"No Patches class found for {assembly.FullName}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load mod assembly from {dllPath}:");
                    Debug.LogException(e);
                }
            }
        }
    }
}
