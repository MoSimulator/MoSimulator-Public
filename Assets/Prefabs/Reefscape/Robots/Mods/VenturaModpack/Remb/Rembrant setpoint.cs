using UnityEngine;

namespace Prefabs.Reefscape.Robots.Mods._4481.Remb
{
    [CreateAssetMenu(fileName = "Setpoint", menuName = "Robot/Rembrant Setpoint", order = 0)]
    public class Rembrantsetpoint : ScriptableObject
    {
        [Tooltip("Inches")] public float elevatorHeight;
        [Tooltip("Degree")] public float armAngle;
        [Tooltip("Degree")] public float climbArmAngle;
        [Tooltip("Degree")] public float climbWristAngle;
        [Tooltip("Degree")] public float droppyThingAngle;
    }
}