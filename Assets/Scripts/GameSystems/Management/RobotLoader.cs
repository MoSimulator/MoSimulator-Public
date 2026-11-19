using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Modding;
using GameSystems.Settings;
using MoSimCore.GameSelection;
using RobotFramework;
using UnityEngine;

namespace GameSystems.Management
{
    public class RobotLoader : MonoBehaviour
    {
        public static RobotLoader Instance { get; private set; }

        private ModManager _modManager;

        [field: SerializeField]
        public List<RobotMetadataSO> LoadedRobots = new();
        [field: SerializeField]
        public List<BaseModpackSO> LoadedModpacks = new();
        public event Action<float, string> OnProgressUpdated;

        /// <summary>Where the loading bar hands over from the base packs to mods, which get the last stretch.</summary>
        public const float ModProgressStart = 0.75f;

        public bool AllRobotsLoaded => _baseRobotsLoaded && _modManager != null && _modManager.ModsLoaded;
        private bool _baseRobotsLoaded;

        public void ReportProgress(float progress, string message)
        {
            OnProgressUpdated?.Invoke(Mathf.Clamp01(progress), message);
        }

        /// <summary>
        /// Bundles handed over by menus that get destroyed on scene change. Unloading an AssetBundle
        /// closes its file handle, and audio streams out of that file lazily rather than being pulled
        /// into memory up front - so a bundle that is unloaded while its robots are still in use plays
        /// no sound and spams FMOD file-not-found. Holding the reference here keeps it open for the
        /// session, which is how locally installed mods already behave.
        /// </summary>
        private readonly List<AssetBundle> _retainedModBundles = new();

        /// <summary>
        /// Takes ownership of a mod's AssetBundle so it stays loaded for as long as its assets might
        /// be used. Safe to call repeatedly with the same bundle.
        /// </summary>
        public void RetainModBundle(AssetBundle bundle)
        {
            if (bundle == null || _retainedModBundles.Contains(bundle))
            {
                return;
            }

            _retainedModBundles.Add(bundle);
        }

        /// <summary>
        /// Drops a retained bundle and unloads it, releasing the file handle. Needed when a mod is
        /// uninstalled: while the bundle is open the OS will not let Steam delete the files, which is
        /// why a "reinstall" afterwards completes instantly without downloading anything.
        /// Loaded assets survive - only the bundle's own file and compressed data go away.
        /// </summary>
        public void ReleaseModBundle(AssetBundle bundle)
        {
            if (bundle == null)
            {
                return;
            }

            _retainedModBundles.Remove(bundle);
            bundle.Unload(false);
        }

        /// <summary>
        /// Removes a modpack and its robots from the loaded lists, for when a mod is uninstalled.
        /// A robot still listed by another registered modpack is kept, since packs may share one.
        /// </summary>
        public void UnregisterModpack(BaseModpackSO modpack)
        {
            if (modpack == null)
            {
                return;
            }

            LoadedModpacks.Remove(modpack);

            if (modpack.Robots == null)
            {
                return;
            }

            foreach (var robot in modpack.Robots)
            {
                if (robot == null)
                {
                    continue;
                }

                var ownedElsewhere = LoadedModpacks.Any(other =>
                    other != null && other.Robots != null && other.Robots.Contains(robot));

                if (!ownedElsewhere)
                {
                    LoadedRobots.Remove(robot);
                }
            }
        }

        /// <summary>Registers a loaded modpack and its robots exactly once.</summary>
        public void RegisterModpack(BaseModpackSO modpack)
        {
            if (modpack == null)
            {
                return;
            }

            if (!LoadedModpacks.Contains(modpack))
            {
                LoadedModpacks.Add(modpack);
            }

            if (modpack.Robots == null)
            {
                return;
            }

            foreach (var robot in modpack.Robots)
            {
                if (robot != null && !LoadedRobots.Contains(robot))
                {
                    LoadedRobots.Add(robot);
                }
            }
        }
        
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
            _modManager = FindFirstObjectByType<ModManager>();

            StartCoroutine(LoadAllRobots());
        }

        private IEnumerator LoadAllRobots()
        {
            // Allow singleton Start methods, including SelectedGameManager's default selection, to complete.
            yield return null;

            yield return LoadBaseRobots();

            _modManager ??= FindFirstObjectByType<ModManager>();
            
            if (!_modManager)
            {
                Debug.LogError("ModManager not found.");
                yield break;
            }
            
            yield return _modManager.LoadMods(GetSelectedModpackGuids());

            EnsureSelectedModpackFallbacks();
            
            _baseRobotsLoaded = true;
        }

        private IEnumerator LoadBaseRobots()
        {
            ReportProgress(0f, "Loading base robots...");
            
            var baseModpacks = Resources.LoadAll<BaseModpackSO>("BaseRobots");
            var selectedGuids = GetSelectedModpackGuids();

            if (selectedGuids.Count == 0 && TryGetDefaultBaseModpack(out var defaultModpack))
            {
                Debug.LogWarning($"No modpack GUID is selected. Falling back to '{defaultModpack.ModpackName}' for the selected game.");
                ApplyFallbackToEmptySelections(defaultModpack);
            }

            // Every game's official pack, not just the one selected at startup. Games are switched
            // without re-running loading, so anything skipped here is unavailable for the rest of the
            // session - which is what left Reefscape with no modpack after starting up in Rebuilt.
            // These ship in Resources and are cheap to hold; workshop mods stay filtered by selection
            // because they are the expensive ones.
            var baseModpacksToLoad = baseModpacks
                .Where(modpack => modpack != null)
                .ToList();

            var totalBaseModpacks = baseModpacksToLoad.Count;

            if (totalBaseModpacks == 0)
            {
                ReportProgress(ModProgressStart, "No base robots found.");
                _baseRobotsLoaded = true;
                yield break;
            }

            for (var i = 0; i < totalBaseModpacks; i++)
            {
                var modpack = baseModpacksToLoad[i];
                RegisterModpack(modpack);

                var progress = ((i + 1f) / totalBaseModpacks) * ModProgressStart;
                ReportProgress(progress, $"Loading base robot modpack {i + 1}/{totalBaseModpacks}...");

                yield return null;
            }
            
            _baseRobotsLoaded = true;
        }

        private HashSet<string> GetSelectedModpackGuids()
        {
            var selectedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var playerSettings = MatchManager.Instance?.MatchSettings?.playerSettings;
            if (playerSettings == null)
            {
                return selectedGuids;
            }

            foreach (var settings in playerSettings)
            {
                if (!string.IsNullOrWhiteSpace(settings?.modpackGuid))
                {
                    selectedGuids.Add(settings.modpackGuid);
                }
            }

            return selectedGuids;
        }

        private void EnsureSelectedModpackFallbacks()
        {
            var manager = MatchManager.Instance;
            var playerSettings = manager?.MatchSettings?.playerSettings;
            if (playerSettings == null)
            {
                return;
            }

            BaseModpackSO defaultModpack = null;
            var settingsChanged = false;
            foreach (var settings in playerSettings)
            {
                if (settings == null ||
                    (!string.IsNullOrWhiteSpace(settings.modpackGuid) &&
                     LoadedModpacks.Any(modpack => modpack != null &&
                                                   string.Equals(modpack.GetGuid(), settings.modpackGuid,
                                                       StringComparison.OrdinalIgnoreCase))))
                {
                    continue;
                }

                defaultModpack ??= GetDefaultBaseModpack();
                if (defaultModpack == null)
                {
                    Debug.LogWarning($"Could not resolve modpack GUID '{settings.modpackGuid}' because no official default modpack exists for the selected game.");
                    continue;
                }

                RegisterModpack(defaultModpack);
                Debug.LogWarning($"Modpack GUID '{settings.modpackGuid}' is empty or unavailable. Falling back to '{defaultModpack.ModpackName}' for the selected game.");
                settings.modpackGuid = defaultModpack.GetGuid();
                if (!defaultModpack.Robots.Any(robot => robot != null && robot.GetGuid() == settings.robotGuid))
                {
                    settings.robotGuid = string.Empty;
                }

                settingsChanged = true;
            }

            if (settingsChanged)
            {
                manager.SaveSettings();
            }
        }

        private void ApplyFallbackToEmptySelections(BaseModpackSO defaultModpack)
        {
            var manager = MatchManager.Instance;
            var playerSettings = manager?.MatchSettings?.playerSettings;
            if (playerSettings == null)
            {
                return;
            }

            var settingsChanged = false;
            foreach (var settings in playerSettings)
            {
                if (settings == null || !string.IsNullOrWhiteSpace(settings.modpackGuid))
                {
                    continue;
                }

                settings.modpackGuid = defaultModpack.GetGuid();
                settings.robotGuid = string.Empty;
                settingsChanged = true;
            }

            if (settingsChanged)
            {
                manager.SaveSettings();
            }
        }

        private bool TryGetDefaultBaseModpack(out BaseModpackSO defaultModpack)
        {
            defaultModpack = GetDefaultBaseModpack();
            return defaultModpack != null;
        }

        private static BaseModpackSO GetDefaultBaseModpack()
        {
            var selectedGame = SelectedGameManager.Instance?.SelectedGame;
            if (selectedGame == null)
            {
                Debug.LogWarning("No selected game is available; cannot choose a default modpack.");
                return null;
            }

            return Resources.LoadAll<BaseModpackSO>("BaseRobots")
                .FirstOrDefault(modpack => modpack != null && modpack.GameOption != null &&
                                           modpack.GameOption.GetGuid() == selectedGame.GetGuid());
        }
    }
}