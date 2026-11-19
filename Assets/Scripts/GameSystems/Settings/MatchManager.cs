using System;
using System.Collections.Generic;
using System.IO;
using GameSystems.Cameras;
using GameSystems.Management;
using MoSimCore.Enums;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameSystems.Settings
{
    /// <summary>
    /// Runs ahead of the rest so the match settings are loaded before anything asks for them. The
    /// scene's own Awake order is arbitrary, and a game manager that got there first used to read a
    /// null Instance and quietly fall back to defaults.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class MatchManager : MonoBehaviour
    {
        private static MatchManager _instance;

        /// <summary>
        /// The active match manager.
        ///
        /// Resolves and initialises the scene's instance on demand rather than relying on Awake having
        /// run. Unity does not order Awake between objects, and MenuManager activates menu roots from
        /// inside its own Awake - so a menu's OnEnable can reach this before this component has
        /// started, which used to throw on startup and skip restoring the saved modpack.
        /// </summary>
        public static MatchManager Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                var found = FindFirstObjectByType<MatchManager>(FindObjectsInactive.Include);
                found?.Initialize();
                return _instance;
            }
        }

        public MatchSettings MatchSettings { get; private set; }

        private bool _initialized;

        /// <summary>
        /// Brings the manager up before the first scene loads.
        ///
        /// It is only placed in the menu and game scenes, so during the loading screen it did not exist
        /// yet - and RobotLoader asks it which modpacks are selected while loading mods. With no manager
        /// that list came back empty, every mod was treated as unselected and skipped, and the game
        /// always started on the default pack no matter what had been saved.
        /// Mirrors how SettingsService bootstraps itself.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureInstanceExists()
        {
            if (_instance != null)
            {
                return;
            }

            new GameObject(nameof(MatchManager)).AddComponent<MatchManager>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Initialize();
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Claims the singleton and loads settings. Idempotent, and safe to run before Awake so an
        /// early caller never sees a half-built manager.
        /// </summary>
        private void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _instance = this;

            Load();
        }

        private void Load()
        {
            var settingsPath = Path.Combine(Application.persistentDataPath, "MatchSettings.json");

            if (File.Exists(settingsPath))
            {
                try
                {
                    MatchSettings = JsonUtility.FromJson<MatchSettings>(File.ReadAllText(settingsPath));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    MatchSettings = null;
                }

                // FromJson returns null instead of throwing when the file is empty or truncated, so
                // the catch above does not cover it. Left unhandled, Migrate throws partway through
                // Awake and the manager ends up alive with null settings - which every menu that
                // reads MatchSettings then trips over on startup.
                if (MatchSettings == null)
                {
                    Debug.LogWarning(
                        $"[MatchManager]: '{settingsPath}' could not be read and has been reset to defaults.");
                }
            }

            MatchSettings ??= new MatchSettings();

            Migrate(MatchSettings);
        }

        /// <summary>
        /// Writes match settings via a temporary file so the real one is only replaced once a
        /// complete copy exists. Writing straight over it means a crash or a killed process leaves a
        /// truncated file behind, which then fails to parse on the next launch.
        /// </summary>
        public void SaveSettings()
        {
            var settingsPath = Path.Combine(Application.persistentDataPath, "MatchSettings.json");
            var tempPath = settingsPath + ".tmp";

            try
            {
                File.WriteAllText(tempPath, JsonUtility.ToJson(MatchSettings, prettyPrint: true));

                if (File.Exists(settingsPath))
                {
                    File.Delete(settingsPath);
                }

                File.Move(tempPath, settingsPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MatchManager]: Failed to save match settings: {e.Message}");
            }
        }

        public PlayerSettings GetOrCreatePlayerSettings(int index)
        {
            if (index < 0)
            {
                index = 0;
            }

            while (MatchSettings.playerSettings.Count <= index)
            {
                MatchSettings.playerSettings.Add(new PlayerSettings());
            }

            return MatchSettings.playerSettings[index];
        }

        public PlayerDevice GetDefaultDeviceForPlayer(int playerIndex)
        {
            if (playerIndex >= 0 && playerIndex < Gamepad.all.Count)
            {
                return (PlayerDevice)((int)PlayerDevice.Gamepad0 + playerIndex);
            }

            return PlayerDevice.Keyboard;
        }

        public void ApplyDefaultDevices(int playerCount)
        {
            for (var i = 0; i < playerCount; i++)
            {
                var settings = GetOrCreatePlayerSettings(i);
                if (!settings.deviceUserSet)
                {
                    settings.device = GetDefaultDeviceForPlayer(i);
                }
            }
        }

        private void Migrate(MatchSettings settings)
        {
            if (settings == null) settings = new MatchSettings();

            settings.playerSettings ??= new List<PlayerSettings>();
            if (settings.playerSettings.Count == 0)
            {
                settings.playerSettings.Add(new PlayerSettings());
            }

            for (int i = 0; i < settings.playerSettings.Count; i++)
            {
                if (settings.playerSettings[i] == null)
                    settings.playerSettings[i] = new PlayerSettings();

                var p = settings.playerSettings[i];
                p.ReefscapeSpawnpoints ??= new List<SavedSpawnpoint>();
                p.RebuiltSpawnpoints ??= new List<SavedSpawnpoint>();
            }

            MatchSettings = settings;
        }
    }

    [Serializable]
    public class MatchSettings
    {
        public GameMode gameMode = GameMode.Singleplayer;
        public List<PlayerSettings> playerSettings = new();
        public int settingsVersion = 1;
    }

    [Serializable]
    public class Spawn
    {
        public Vector3 position;
        public Quaternion rotation;

        public bool CollidesWith(Spawn otherSpawn, float minRadius = 1.5f)
        {
            // Horizontal-only (X/Z) check - ignore Y so height differences on the field
            // (ramps, platforms, etc.) don't affect whether two spawns are considered colliding.
            var dx = position.x - otherSpawn.position.x;
            var dz = position.z - otherSpawn.position.z;
            return dx * dx + dz * dz < minRadius * minRadius;
        }
    }

    [Serializable]
    public class SavedSpawnpoint
    {
        public string key;
        public Spawn spawn;
    }

    [Serializable]
    public class PlayerSettings
    {
        public string modpackGuid = "";
        public string robotGuid = "";
        public Alliance alliance = Alliance.Blue;
        public PlayerDevice device = PlayerDevice.Keyboard;
        public bool deviceUserSet = false;
        public CameraMode cameraMode = CameraMode.ThirdPerson;
        public float moveSpeed = 1f;
        public float moveExponent = 2f;
        public float brakeTime = 0f;
        public float rotationSpeed = 0.6f;
        public float controllerRumble = 0.5f;
        public bool perspectiveAutoAlign = true;
        public int robotVariantIndex = 0;

        /// <summary>
        /// How tall the driver is, in inches - not where the camera goes. The driver station camera sits
        /// at their eye height, which is this less the drop from the top of the head to the eyes. The
        /// default puts the camera back where every scene had it placed by hand.
        /// </summary>
        public const float MinDriverHeightInches = 48f;

        public const float MaxDriverHeightInches = 84f;
        public const float DefaultDriverHeightInches = 64f;

        public float driverHeightInches = DefaultDriverHeightInches;

        public List<SavedSpawnpoint> CrescendoSpawnpoints = new List<SavedSpawnpoint>();
        public List<SavedSpawnpoint> ReefscapeSpawnpoints = new List<SavedSpawnpoint>();
        public List<SavedSpawnpoint> RebuiltSpawnpoints = new List<SavedSpawnpoint>();

        public bool useVanity = false;

        public float outpostDistance = -1f;
        public bool outpostDistanceMax = false;

        /*******************************/
        /*         Functions           */
        /*******************************/

        private Spawn findSpawnpoint(List<SavedSpawnpoint> spawns, string key)
        {
            foreach (var s in spawns)
            {
                if (s.key == key) return s.spawn;
            }

            return null;
        }

        private void modifySpawnpoint(List<SavedSpawnpoint> spawns, string key, Spawn newSpawn)
        {
            foreach (var s in spawns)
            {
                if (s.key == key)
                {
                    s.spawn = newSpawn;
                }
            }
        }

        public List<SavedSpawnpoint> GetSpawnListByYear(int year)
        {
            var spawns = ReefscapeSpawnpoints;
            switch (year)
            {
                case 2024:
                    spawns = CrescendoSpawnpoints;
                    break;
                case 2025:
                    spawns = ReefscapeSpawnpoints;
                    break;
                case 2026:
                    spawns = RebuiltSpawnpoints;
                    break;
            }

            return spawns;
        }

        public Spawn GetSpawnpoint(int year)
        {
            List<SavedSpawnpoint> spawns = GetSpawnListByYear(year);

            string key = $"{robotVariantIndex}:{robotGuid}";

            var spawn = findSpawnpoint(spawns, key);

            return spawn;
        }

        public void SaveSpawnpoint(int year, Spawn spawnpoint)
        {
            List<SavedSpawnpoint> spawns = GetSpawnListByYear(year);

            string key = $"{robotVariantIndex}:{robotGuid}";

            if (findSpawnpoint(spawns, key) == null)
            {
                SavedSpawnpoint s = new SavedSpawnpoint();
                s.spawn = spawnpoint;
                s.key = key;
                spawns.Add(s);
            }
            else
            {
                modifySpawnpoint(spawns, key, spawnpoint);
            }

            MatchManager.Instance.SaveSettings();
        }
    }
}