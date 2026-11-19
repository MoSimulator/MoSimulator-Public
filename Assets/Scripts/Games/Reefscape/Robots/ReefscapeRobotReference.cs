using MoSimCore.Enums;
using MoSimLib;
using RobotFramework;
using UnityEngine;

namespace Games.Reefscape.Robots
{
    /// <summary>References either Reefscape robot implementation without component lookups for its bumpers.</summary>
    internal readonly struct ReefscapeRobotReference
    {
        private readonly ReefscapeRobotBase _legacy;
        private readonly ReefscapeCommandRobotBase _command;

        public ReefscapeRobotReference(RobotBase robot)
        {
            _legacy = robot as ReefscapeRobotBase;
            _command = robot as ReefscapeCommandRobotBase;
        }

        public RobotBase Robot => _legacy != null ? _legacy : (RobotBase)_command;
        public bool IsValid => Robot != null;
        public Alliance Alliance => Robot.Alliance;
        public Collider BumperCollider => _legacy != null ? _legacy.bumperCollider : _command.bumperCollider;

        public static ReefscapeRobotReference Find(GameObject source)
        {
            return new ReefscapeRobotReference(Utils.FindParentObjectComponent<RobotBase>(source, includeSelf: true));
        }
    }
}
