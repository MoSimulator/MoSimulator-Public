using UnityEngine;

namespace Prefabs.Reefscape.Robots.Mods.TestingMod._9496
{
    [CreateAssetMenu(fileName = "Setpoint", menuName = "Robot/Lynk Setpoint", order = 0)]
    public class LynkSetpoint : ScriptableObject
    {
        [Tooltip("Inches")] public float elevatorHeight;
    }
}