using RobotFramework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Games.Reefscape.Robots
{
    /// <summary>
    /// Reefscape-specific robot metadata. Extends RobotMetadataSO with auto-climb flags.
    /// Create via Assets > Create > Games > Reefscape > ReefscapeRobotMetadata.
    /// </summary>
    [CreateAssetMenu(fileName = "ReefscapeRobotMetadata", menuName = "Games/Reefscape/ReefscapeRobotMetadata",
        order = 100)]
    public class ReefscapeRobotMetadataSO : RobotMetadataSO
    {
        [Header("Alternate Robot(s)")] [SerializeField]
        private List<ReefscapeRobotVariant> alternateRobots = new();

        [Header("Reefscape Settings")]
        [SerializeField]
        [Tooltip("Whether the primary robot configuration can automatically climb.")]
        private bool autoClimbs;

        public bool AutoClimbs => autoClimbs;
        public override IReadOnlyList<RobotVariant> AlternateRobots => alternateRobots;
        public IReadOnlyList<ReefscapeRobotVariant> TypedAlternateRobots => alternateRobots;

        public override int GameYear => 2025;
    }

    [Serializable]
    public class ReefscapeRobotVariant : RobotMetadataSO.RobotVariant
    {
        [SerializeField] [Tooltip("Whether this robot variant can automatically climb.")]
        private bool autoClimbs;

        public bool AutoClimbs => autoClimbs;
    }
}