using System;
using System.Collections.Generic;
using System.IO;
using MoSimCore.GameSelection;
using MoSimCore.Settings;
using UnityEngine;
using UnityEngine.Audio;

namespace GameSystems.Settings
{
    public class SettingsService : MonoBehaviour
    {
        private const int CurrentSettingsVersion = 4;
        private const string AppSettingsFileName = "AppSettings.json";
        private const string LegacySettingsFileName = "GameSettings.json";
        private const float MinVolume = 0.0001f;
        private const float MinDrivePercent = 0f;

        private static readonly int[] FpsTargets = { 30, 60, 90, 144, 200, -1 };

        public static SettingsService Instance { get; private set; }

        public AppSettings AppSettings { get; private set; }

        private AudioMixer _audioMixer;
        private bool _deleteMigratedPlayerPrefsAfterSave;

        private static string AppSettingsPath => Path.Combine(Application.persistentDataPath, AppSettingsFileName);
        private static string LegacySettingsPath => Path.Combine(Application.persistentDataPath, LegacySettingsFileName);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureInstanceExists()
        {
            if (Instance != null || FindFirstObjectByType<SettingsService>() != null)
            {
                return;
            }
            
            new GameObject("SettingsService").AddComponent<SettingsService>();
        }

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            RegisterSettingsAccess();
            Load();
        }

        private void OnApplicationQuit()
        {
            Save();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Save();
        }

        /// <summary>
        /// Saves when the window loses focus. Run In Background is enabled, so OnApplicationPause no
        /// longer fires on desktop when the player alt-tabs away; this keeps that safety save.
        /// </summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) Save();
        }

        public void Save()
        {
            Validate(AppSettings);
            File.WriteAllText(AppSettingsPath, JsonUtility.ToJson(AppSettings, true));

            if (_deleteMigratedPlayerPrefsAfterSave)
            {
                DeleteMigratedPlayerPrefs();
                _deleteMigratedPlayerPrefsAfterSave = false;
            }
        }

        public void SaveSettings() => Save();

        public void ApplyAndSave()
        {
            Validate(AppSettings);
            Apply(AppSettings);
            Save();
        }

        public void ResetAll()
        {
            AppSettings = new AppSettings();
            ApplySelectedGameDefault(AppSettings);
            Apply(AppSettings);
            Save();
        }

        public void ClearBindingOverrides(string gameGuid)
        {
            var entry = AppSettings.GetOrCreateGameRebinds(gameGuid);
            entry.Keyboard = "";
            entry.Controller = "";
            Save();
        }

        public string GetBindingOverrides(string gameGuid, bool isOtherControlsAsset)
        {
            var entry = AppSettings.GetOrCreateGameRebinds(gameGuid);
            return isOtherControlsAsset ? entry.Controller : entry.Keyboard;
        }

        public void SetBindingOverrides(string gameGuid, bool isOtherControlsAsset, string rebinds)
        {
            var entry = AppSettings.GetOrCreateGameRebinds(gameGuid);
            if (isOtherControlsAsset)
            {
                entry.Controller = rebinds ?? "";
            }
            else
            {
                entry.Keyboard = rebinds ?? "";
            }

            Save();
        }

        private void Load()
        {
            _audioMixer = Resources.Load<AudioMixer>("Audio/MasterMixer");
            AppSettings = LoadAppSettings();

            Migrate(AppSettings);
            Validate(AppSettings);
            Verify(AppSettings);
            Apply(AppSettings);
            ApplySelectedGameSelection(AppSettings);
            Save();
        }

        private void RegisterSettingsAccess()
        {
            SettingsAccess.GetBindingOverrides = GetBindingOverrides;
            SettingsAccess.SetBindingOverrides = SetBindingOverrides;
            SettingsAccess.ClearBindingOverrides = ClearBindingOverrides;
            SettingsAccess.GetSelectedControllerRebinding = () => AppSettings?.SelectedControllerRebinding ?? 0;
            SettingsAccess.GetDriveActionKeyPercent = GetDriveActionKeyPercent;
            SettingsAccess.GetActionKeyMode = GetActionKeyMode;
        }

        private float GetDriveActionKeyPercent(int index)
        {
            var entry = CurrentGameRebinds();
            if (entry == null) return 1f;
            switch (index)
            {
                case 0: return entry.DriveActionKey1Percent;
                case 1: return entry.DriveActionKey2Percent;
                case 2: return entry.DriveActionKey3Percent;
                default: return 1f;
            }
        }

        private int GetActionKeyMode()
        {
            return CurrentGameRebinds()?.ActionKeyMode ?? 0;
        }

        private GameRebinds CurrentGameRebinds()
        {
            return AppSettings?.GetOrCreateGameRebinds(SettingsAccess.ResolveSelectedGameGuid());
        }

        private AppSettings LoadAppSettings()
        {
            if (File.Exists(AppSettingsPath))
            {
                try
                {
                    return JsonUtility.FromJson<AppSettings>(File.ReadAllText(AppSettingsPath)) ?? new AppSettings();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    return new AppSettings();
                }
            }

            var settings = LoadLegacySettings();
            _deleteMigratedPlayerPrefsAfterSave = true;
            return settings;
        }

        private static AppSettings LoadLegacySettings()
        {
            if (!File.Exists(LegacySettingsPath))
            {
                return new AppSettings();
            }

            try
            {
                var legacy = JsonUtility.FromJson<LegacyGameSettings>(File.ReadAllText(LegacySettingsPath));
                if (legacy == null)
                {
                    return new AppSettings();
                }

                return new AppSettings
                {
                    WindowMode = legacy.windowMode,
                    ResolutionWidth = legacy.resolutionWidth,
                    ResolutionHeight = legacy.resolutionHeight,
                    FpsLimitIndex = legacy.fpsLimitIndex,
                    QualityLevel = legacy.qualityLevel,
                    MasterVolume = legacy.masterVolume,
                    SwerveVolume = legacy.robotVolume,
                    ScoringVolume = legacy.fieldVolume,
                    RobotVolume = legacy.robotVolume,
                    FieldVolume = legacy.fieldVolume,
                    SelectedGameGuid = legacy.selectedGameGuid,
                };
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return new AppSettings();
            }
        }

        private static void DeleteMigratedPlayerPrefs()
        {
            PlayerPrefs.DeleteKey("SelectedControllerRebinding");
            PlayerPrefs.DeleteKey("rebinds");
            PlayerPrefs.DeleteKey("rebinds2");
            PlayerPrefs.DeleteKey("ModdingEULAAccepted");
            PlayerPrefs.DeleteKey("QualityIndex");
            PlayerPrefs.DeleteKey("FpsLimitIndex");
            PlayerPrefs.Save();
        }

        private void Apply(AppSettings appSettings)
        {
            ApplyGraphics(appSettings);
            ApplyAudio(appSettings);
        }

        private static void ApplyGraphics(AppSettings appSettings)
        {
            FullScreenMode targetMode = appSettings.WindowMode switch
            {
                0 => FullScreenMode.ExclusiveFullScreen,
                1 => FullScreenMode.FullScreenWindow,
                2 => FullScreenMode.Windowed,
                _ => Screen.fullScreenMode
            };

            Screen.SetResolution(appSettings.ResolutionWidth, appSettings.ResolutionHeight, targetMode);
            ApplyFpsLimit(appSettings.FpsLimitIndex);
            QualitySettings.SetQualityLevel(appSettings.QualityLevel);
        }

        private static void ApplyFpsLimit(int index)
        {
            var target = FpsTargets[index];
            if (target == -1)
            {
                QualitySettings.vSyncCount = Screen.mainWindowDisplayInfo.refreshRate.value > 200 ? 0 : 1;
                Application.targetFrameRate = Screen.mainWindowDisplayInfo.refreshRate.value > 200
                    ? 200
                    : (int)Screen.mainWindowDisplayInfo.refreshRate.value;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = target;
        }

        private void ApplyAudio(AppSettings appSettings)
        {
            AudioListener.volume = appSettings.MasterVolume;

            if (_audioMixer == null)
            {
                Debug.LogWarning("Master audio mixer was not found at Resources/Audio/MasterMixer.");
                return;
            }

            _audioMixer.SetFloat("SwerveVolume", ToDb(appSettings.SwerveVolume));
            _audioMixer.SetFloat("ScoringVolume", ToDb(appSettings.ScoringVolume));
            _audioMixer.SetFloat("RobotVolume", ToDb(appSettings.RobotVolume));
            _audioMixer.SetFloat("FieldVolume", ToDb(appSettings.FieldVolume));
        }

        private static float ToDb(float linear) => Mathf.Log10(Mathf.Clamp(linear, MinVolume, 1f)) * 20f;

        private void ApplySelectedGameSelection(AppSettings settings)
        {
            if (string.IsNullOrEmpty(settings.SelectedGameGuid))
            {
                if (SelectedGameManager.Instance != null && SelectedGameManager.Instance.GameOptions.Count > 0)
                {
                    settings.SelectedGameGuid = SelectedGameManager.Instance.GameOptions[0].GetGuid();
                    Debug.LogWarning(
                        $"No selected game guid found in settings. Defaulting to first available game: {SelectedGameManager.Instance.GameOptions[0].name}");
                }
            }

            if (SelectedGameManager.Instance != null)
            {
                SelectedGameManager.Instance.SetSelectedGameFromGuid(settings.SelectedGameGuid);
            }
            else
            {
                SelectedGameManager.PendingSelectedGameGuid = settings.SelectedGameGuid;
            }
        }

        private static void Verify(AppSettings settings)
        {
            if (string.IsNullOrEmpty(settings.SelectedGameGuid))
            {
                if (SelectedGameManager.Instance == null)
                {
                    Debug.LogWarning("SelectedGameManager not initialized.");
                }
                else
                {
                    settings.SelectedGameGuid = SelectedGameManager.Instance.SelectedGame != null
                        ? SelectedGameManager.Instance.SelectedGame.GetGuid()
                        : "";
                }
            }
        }

        private static void ApplySelectedGameDefault(AppSettings settings)
        {
            if (SelectedGameManager.Instance != null && SelectedGameManager.Instance.SelectedGame != null)
            {
                settings.SelectedGameGuid = SelectedGameManager.Instance.SelectedGame.GetGuid();
            }
            else if (SelectedGameManager.Instance != null && SelectedGameManager.Instance.GameOptions.Count > 0)
            {
                settings.SelectedGameGuid = SelectedGameManager.Instance.GameOptions[0].GetGuid();
            }
        }

        private static void Validate(AppSettings settings)
        {
            settings.WindowMode = Mathf.Clamp(settings.WindowMode, 0, 2);
            settings.ResolutionWidth = Mathf.Max(1, settings.ResolutionWidth);
            settings.ResolutionHeight = Mathf.Max(1, settings.ResolutionHeight);
            settings.FpsLimitIndex = Mathf.Clamp(settings.FpsLimitIndex, 0, FpsTargets.Length - 1);
            settings.QualityLevel =
                Mathf.Clamp(settings.QualityLevel, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
            settings.FieldOfView = Mathf.Clamp(settings.FieldOfView, AppSettings.MinFieldOfView, AppSettings.MaxFieldOfView);
            settings.MasterVolume = Mathf.Clamp01(settings.MasterVolume);
            settings.SwerveVolume = Mathf.Clamp01(settings.SwerveVolume);
            settings.ScoringVolume = Mathf.Clamp01(settings.ScoringVolume);
            settings.RobotVolume = Mathf.Clamp01(settings.RobotVolume);
            settings.FieldVolume = Mathf.Clamp01(settings.FieldVolume);
            settings.SelectedControllerRebinding = Mathf.Clamp(settings.SelectedControllerRebinding, 0, 1);

            foreach (var entry in settings.GameRebinds)
            {
                entry.DriveActionKey1Percent = Mathf.Clamp(entry.DriveActionKey1Percent, MinDrivePercent, 1f);
                entry.DriveActionKey2Percent = Mathf.Clamp(entry.DriveActionKey2Percent, MinDrivePercent, 1f);
                entry.DriveActionKey3Percent = Mathf.Clamp(entry.DriveActionKey3Percent, MinDrivePercent, 1f);
                entry.ActionKeyMode = Mathf.Clamp(entry.ActionKeyMode, 0, (int)MoSimCore.Enums.ActionKeyMode.Toggle);
            }
        }

        private static void Migrate(AppSettings settings)
        {
            if (settings.SettingsVersion < 2 && !MigrateFlatRebindsToPerGame(settings))
            {
                return;
            }

            if (settings.SettingsVersion < 4 && settings.ModdingEulaAccepted)
            {
                Debug.LogWarning("Modding EULA accepted in settings.json. This is no longer supported. Please remove the setting.");
                settings.ModdingEulaAccepted = false;
            }

            settings.SettingsVersion = CurrentSettingsVersion;
        }

        private static bool MigrateFlatRebindsToPerGame(AppSettings settings)
        {
            if (string.IsNullOrEmpty(settings.Rebinds) && string.IsNullOrEmpty(settings.Rebinds2))
            {
                return true;
            }

            var migrationGuid = settings.SelectedGameGuid;
            if (string.IsNullOrEmpty(migrationGuid))
            {
                migrationGuid = SelectedGameManager.PendingSelectedGameGuid;
            }

            if (string.IsNullOrEmpty(migrationGuid)
                && SelectedGameManager.Instance != null
                && SelectedGameManager.Instance.GameOptions.Count > 0)
            {
                migrationGuid = SelectedGameManager.Instance.GameOptions[0].GetGuid();
            }

            if (string.IsNullOrEmpty(migrationGuid))
            {
                return false;
            }

            var entry = settings.GetOrCreateGameRebinds(migrationGuid);
            if (string.IsNullOrEmpty(entry.Keyboard))
            {
                entry.Keyboard = settings.Rebinds ?? "";
            }

            if (string.IsNullOrEmpty(entry.Controller))
            {
                entry.Controller = settings.Rebinds2 ?? "";
            }

            settings.Rebinds = "";
            settings.Rebinds2 = "";
            return true;
        }
    }

    [Serializable]
    public class AppSettings
    {
        public int WindowMode = 0;
        public int ResolutionWidth = 1920;
        public int ResolutionHeight = 1080;
        public int FpsLimitIndex = 5;
        public int QualityLevel = 0;

        /// <summary>
        /// Vertical field of view for the in-game cameras, in degrees. The default is the 75 every
        /// first person, third person and driver station camera in both games was built at, and the
        /// range either side of it is the one that suited these views.
        /// </summary>
        public const float MinFieldOfView = 50f;
        public const float MaxFieldOfView = 100f;
        public const float DefaultFieldOfView = 75f;

        public float FieldOfView = DefaultFieldOfView;
        public float MasterVolume = 1f;
        public float SwerveVolume = 1f;
        public float ScoringVolume = 1f;
        public float RobotVolume = 1f;
        public float FieldVolume = 1f;
        public int SelectedControllerRebinding = 0;

        public string Rebinds = "";
        public string Rebinds2 = "";
        public List<GameRebinds> GameRebinds = new();

        public string SelectedGameGuid = "";
        public bool ModdingEulaAccepted = false;
        public int SettingsVersion = 1;

        public GameRebinds GetOrCreateGameRebinds(string gameGuid)
        {
            if (string.IsNullOrEmpty(gameGuid))
            {
                gameGuid = "";
            }

            var entry = GameRebinds.Find(r => r.GameGuid == gameGuid);
            if (entry == null)
            {
                entry = new GameRebinds { GameGuid = gameGuid };
                GameRebinds.Add(entry);
            }

            return entry;
        }
    }

    [Serializable]
    public class GameRebinds
    {
        public string GameGuid = "";
        public string Keyboard = "";
        public string Controller = "";

        public float DriveActionKey1Percent = 0.3f;
        public float DriveActionKey2Percent = 0.65f;
        public float DriveActionKey3Percent = 1f;
        public int ActionKeyMode = (int)MoSimCore.Enums.ActionKeyMode.Hold;
    }

    [Serializable]
    internal class LegacyGameSettings
    {
        public int windowMode = 0;
        public int resolutionWidth = 1920;
        public int resolutionHeight = 1080;
        public int fpsLimitIndex = 5;
        public int qualityLevel = 0;
        public float masterVolume = 1f;
        public float musicVolume = 1f;
        public float fieldVolume = 1f;
        public float robotVolume = 1f;
        public string selectedGameGuid = "";
        public string selectedRobotGuid = "";
        public bool moddingEulaAccepted = false;
        public int settingsVersion = 1;
    }
}