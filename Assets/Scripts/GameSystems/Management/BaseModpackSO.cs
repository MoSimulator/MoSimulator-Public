using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MoSimCore.GameSelection;
using MoSimLib;
using RobotFramework;
using UnityEngine;

namespace GameSystems.Management
{
    /// <summary>
    /// Base ScriptableObject representing a modpack containing multiple robots and associated metadata.
    /// Inherit from this class to create game-specific modpack implementations.
    /// DO NOT INSTANTIATE THIS TYPE IN CODE.
    /// </summary>
    public abstract class BaseModpackSO : ScriptableObject
    {
        [Header("Modpack Info")]
        [SerializeField]
        [Tooltip("The name of the modpack.")]
        private string modpackName;
        
        [SerializeField]
        [Tooltip("The version of the modpack in semantic versioning format (e.g., 1.0.0).")]
        private string modpackVersion;
        
        [SerializeField]
        [Tooltip("The author or creator of the modpack.")]
        private string authorName;
        
        [SerializeField]
        [Tooltip("A brief description of the modpack and its contents.")]
        private string description;

        [SerializeField]
        [Tooltip("Artwork on this modpack's card in the mod browser. Used by official packs; Workshop mods show their Workshop preview. Falls back to the game's background when empty.")]
        private Sprite thumbnail;
        
        [Header("Included Robots")]
        [SerializeField]
        [Tooltip("List of robot metadata objects included in this modpack (maximum of 6).")]
        private List<RobotMetadataSO> robots;

        /// <summary>The maximum number of robots a modpack may contain.</summary>
        public const int MaxRobots = 6;

        
        /// <summary>Gets the name of the modpack.</summary>
        public string ModpackName => modpackName;
        
        /// <summary>Gets the version string of the modpack.</summary>
        public string ModpackVersion => modpackVersion;
        
        /// <summary>Gets the author name of the modpack.</summary>
        public string AuthorName => authorName;
        
        /// <summary>Gets the description of the modpack.</summary>
        public string Description => description;

        /// <summary>Gets the card artwork for this modpack, or null to use the game's background.</summary>
        public Sprite Thumbnail => thumbnail;
        
        /// <summary>
        /// Gets the associated GameOption for this modpack. Must be implemented in derived classes to specify the game context.
        /// </summary>
        public abstract GameOption GameOption { get; }
        
        /// <summary>Gets the list of robots included in this modpack.</summary>
        public List<RobotMetadataSO> Robots => robots;

        /// <summary>
        /// Returns a formatted string representation of the modpack.
        /// </summary>
        /// <returns>A string in the format "ModpackName vModpackVersion by AuthorName".</returns>
        public override string ToString()
        {
            return $"{ModpackName} v{ModpackVersion} by {AuthorName}";
        }

        public string GetGuid()
        {
            var seed = $"{ModpackName}|{AuthorName}|{GameOption}";
            return Utils.CreateDeterministicGuid(seed).ToString();
        }

        /// <summary>
        /// Validates semantic versioning format and modpack data consistency.
        /// Checks version format (X.Y.Z), removes duplicate robots, and enforces a maximum of <see cref="MaxRobots"/> robots.
        /// </summary>
        private void OnValidate()
        {
            // Normalize strings
            if (!string.IsNullOrEmpty(modpackName)) modpackName = modpackName.Trim();
            if (!string.IsNullOrEmpty(authorName)) authorName = authorName.Trim();
            if (!string.IsNullOrEmpty(description)) description = description.Trim();

            // Validate and normalize modpack version
            if (!string.IsNullOrEmpty(modpackVersion))
            {
                modpackVersion = modpackVersion.Trim();
                
                // Semantic versioning regex: matches X.Y.Z with optional pre-release and metadata
                // Examples: 1.0.0, 1.0.0-alpha, 1.0.0+build.123
                const string semanticVersionPattern = @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$";
                
                if (!Regex.IsMatch(modpackVersion, semanticVersionPattern))
                {
                    Debug.LogWarning(
                        $"Modpack '{name}' version '{modpackVersion}' does not follow semantic versioning format (e.g., 1.0.0).",
                        this);
                }
            }

            // Ensure robots list exists
            robots ??= new List<RobotMetadataSO>();

            var seenRobots = new HashSet<RobotMetadataSO>();

            for (int i = robots.Count - 1; i >= 0; i--)
            {
                if (seenRobots.Add(robots[i])) continue;
                Debug.LogWarning(
                    $"Modpack '{name}' contains duplicate robot metadata '{robots[i].name}'. Removing duplicate.",
                    this);
                robots.RemoveAt(i);
            }

            if (robots.Count > MaxRobots)
            {
                Debug.LogWarning(
                    $"Modpack '{name}' contains {robots.Count} robots, which exceeds the maximum of {MaxRobots}. Removing extra robots.",
                    this);
                robots.RemoveRange(MaxRobots, robots.Count - MaxRobots);
            }
        }
    }
}

