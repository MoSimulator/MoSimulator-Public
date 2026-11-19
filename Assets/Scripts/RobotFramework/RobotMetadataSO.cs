using System;
using System.Collections.Generic;
using MoSimCore.GameSelection;
using MoSimLib;
using UnityEngine;

namespace RobotFramework
{
    /// <summary>
    /// ScriptableObject that holds metadata for a robot.
    /// Inherit and create concrete assets per game. DO NOT INSTANTIATE THIS TYPE IN CODE.
    /// </summary>
    public abstract class RobotMetadataSO : ScriptableObject
    {
        // Identity
        [Header("Identity")] [SerializeField] [Tooltip("The team number associated with this robot.")]
        private int teamNumber;

        [SerializeField] [Tooltip("The team name associated with this robot.")]
        private string teamName;

        [SerializeField] [Tooltip("The image/icon representing this robot in UI elements.")]
        private Sprite robotImage;

        [Header("Main Robot")] [SerializeField] [Tooltip("The main robot variant for this robot metadata.")]
        private RobotVariant mainRobot;

        public bool IsModded { get; set; } = false;

        // Public read-only metadata API.
        public int TeamNumber => teamNumber;
        public string TeamName => teamName;
        public string RobotName => mainRobot.DisplayName;
        public GameObject RobotPrefab => mainRobot.RobotPrefab;
        public GameObject RobotPreloadGamePiece => mainRobot.PreloadGamePiece;
        public GameObject MainMenuPrefab => mainRobot.MainMenuPrefab;
        public GameObject SpawnSelectionPrefab => mainRobot.SpawnSelectionPrefab;
        public abstract IReadOnlyList<RobotVariant> AlternateRobots { get; }
        public Sprite RobotImage => robotImage;
        public IReadOnlyList<GameObject> TutorialCards => mainRobot.TutorialCards;

        public abstract int GameYear { get; }

        /// <summary>The game this robot belongs to. Override in games that are not FRC.</summary>
        public virtual GameType GameType => GameType.FRC;

        public RobotVariant GetAlternateVariant(int selectedVariantIndex)
        {
            var alternateIndex = selectedVariantIndex - 1;
            return AlternateRobots != null && alternateIndex >= 0 && alternateIndex < AlternateRobots.Count
                ? AlternateRobots[alternateIndex]
                : null;
        }

        public GameObject GetRobotPrefabForVariant(int selectedVariantIndex)
        {
            var variant = GetAlternateVariant(selectedVariantIndex);
            return variant?.RobotPrefab ?? RobotPrefab;
        }

        public GameObject GetMainMenuPrefabForVariant(int selectedVariantIndex)
        {
            var variant = GetAlternateVariant(selectedVariantIndex);
            return variant == null ? MainMenuPrefab : variant.MainMenuPrefab ?? variant.RobotPrefab ?? RobotPrefab;
        }

        public GameObject GetSpawnSelectionPrefabForVariant(int selectedVariantIndex)
        {
            var variant = GetAlternateVariant(selectedVariantIndex);
            return variant == null
                ? SpawnSelectionPrefab
                : variant.SpawnSelectionPrefab ?? variant.RobotPrefab ?? RobotPrefab;
        }

        /// <summary>
        /// Returns the preload game piece for the selected variant, falling back to the main robot's
        /// preload when an alternate variant does not define its own.
        /// </summary>
        public GameObject GetPreloadGamePieceForVariant(int selectedVariantIndex)
        {
            var variant = GetAlternateVariant(selectedVariantIndex);
            return variant?.PreloadGamePiece ?? RobotPreloadGamePiece;
        }

        public IReadOnlyList<GameObject> GetTutorialCardsForVariant(int selectedVariantIndex)
        {
            return GetAlternateVariant(selectedVariantIndex)?.TutorialCards ?? TutorialCards;
        }

        /// <summary>
        /// Returns a string representation of the robot in the format "TeamNumber - TeamName".
        /// </summary>
        public override string ToString()
        {
            return $"{TeamNumber} - {TeamName}";
        }

        /// <summary>
        /// Generates a GUID string for the robot metadata. Non-FRC games append their game type so robots
        /// from different games sharing a year never collide. FRC keeps the original seed so existing IDs stay valid.
        /// </summary>
        public string GetGuid()
        {
            var seed = $"{name}|{teamNumber}|{teamName ?? string.Empty}|{GameYear}|{GameType}";
            return Utils.CreateDeterministicGuid(seed).ToString();
        }

        /// <summary>
        /// Editor-only validation to keep values consistent and avoid null references.
        /// Validates required prefabs, warns on missing assets, and ensures alternate settings are consistent.
        /// </summary>
        protected virtual void OnValidate()
        {
            // Clamp team number to non-negative
            if (teamNumber < 0) teamNumber = 0;

            // Validate required prefabs
            if (mainRobot.RobotPrefab == null)
            {
                Debug.LogWarning($"Robot metadata '{name}' is missing the main RobotPrefab!", this);
            }

            if (mainRobot.MainMenuPrefab == null)
            {
                Debug.LogWarning($"Robot metadata '{name}' is missing the MainMenuPrefab!", this);
            }

            if (mainRobot.SpawnSelectionPrefab == null)
            {
                Debug.LogWarning($"Robot metadata '{name}' is missing the SpawnSelectionPrefab!", this);
            }

            foreach (var variant in AlternateRobots)
            {
                if (variant == null)
                {
                    Debug.LogWarning($"Robot metadata '{name}' contains a null alternate variant!", this);
                    continue;
                }

                if (variant.RobotPrefab == null)
                {
                    Debug.LogWarning($"Robot metadata '{name}' has an alternate variant without a RobotPrefab!", this);
                }

                if (variant.MainMenuPrefab == null)
                {
                    Debug.LogWarning($"Robot metadata '{name}' has an alternate variant without a MainMenuPrefab!",
                        this);
                }

                if (variant.SpawnSelectionPrefab == null)
                {
                    Debug.LogWarning(
                        $"Robot metadata '{name}' has an alternate variant without a SpawnSelectionPrefab!", this);
                }
            }
        }

        [Serializable]
        public class RobotVariant
        {
            [Tooltip("The robot variant name for the alternate robot menu. Leave blank for auto versioning.")]
            [SerializeField]
            private string displayName;

            [SerializeField] private GameObject robotPrefab;
            [SerializeField] private GameObject mainMenuPrefab;
            [SerializeField] private GameObject spawnSelectionPrefab;

            [Tooltip("The game piece the robot starts with preloaded (optional). Leave blank to use the main robot's.")]
            [SerializeField]
            private GameObject preloadGamePiece;

            [SerializeField] private List<GameObject> tutorialCards = new();

            public string DisplayName => displayName;
            public GameObject RobotPrefab => robotPrefab;
            public GameObject PreloadGamePiece => preloadGamePiece;
            public GameObject MainMenuPrefab => mainMenuPrefab;
            public GameObject SpawnSelectionPrefab => spawnSelectionPrefab;

            public IReadOnlyList<GameObject> TutorialCards => tutorialCards != null
                ? tutorialCards
                : Array.Empty<GameObject>();
        }
    }
}