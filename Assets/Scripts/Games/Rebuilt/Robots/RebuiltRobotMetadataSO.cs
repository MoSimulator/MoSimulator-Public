using System.Collections.Generic;
using RobotFramework;
using UnityEngine;

namespace Games.Rebuilt.Robots
{
    /// <summary>
    /// Rebuilt-specific robot metadata.
    /// Create via Assets > Create > Games > Rebuilt > RebuiltRobotMetadata.
    /// </summary>
    [CreateAssetMenu(fileName = "RebuiltRobotMetadata", menuName = "Games/Rebuilt/RebuiltRobotMetadata", order = 10)]
    public class RebuiltRobotMetadataSO : RobotMetadataSO
    {
        [Header("Alternate Robot(s)")] [SerializeField]
        private List<RobotVariant> alternateRobots = new();

        public override IReadOnlyList<RobotVariant> AlternateRobots => alternateRobots;
        public override int GameYear => 2026;
    }
}