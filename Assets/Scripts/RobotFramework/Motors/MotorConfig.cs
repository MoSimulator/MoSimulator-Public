using System;
using UnityEngine;

namespace RobotFramework.Motors
{
    /// <summary>
    /// Configuration options for DC motor simulation.
    /// </summary>
    [Serializable]
    public class MotorConfig
    {
        [Header("Closed Loop Config")]
        [Tooltip("Proportional PID gain (Volts/Unit of Error)")] [SerializeField] public float K_p = 0f;
        [Tooltip("Static feedforward gain (Volts)")] [SerializeField] public float K_s = 0f;
        [Tooltip("Velocity feedforward gain (Volts/RPS)")] [SerializeField] public float K_v = 0f;
        [Tooltip("Acceleration feedforward gain (Volts/RPS^2)")] [SerializeField] public float K_a = 0f;

        [Header("Current Limit Config")]
        [Tooltip("The stator current limit to simulate (Amps)")] [SerializeField] public float StatorLimit = 100f;
        [Tooltip("The supply current limit to simulate (Amps)")] [SerializeField] public float SupplyLimit = 80f;

        [Header("Profiled Motion Config")]
        [Tooltip("The velocity to constrain motion profiled control to, in rotations/second")] [SerializeField] public float MaxVelocity = 0f;
        [Tooltip("The target acceleration for motion profiled control, in rotations/second^2")] [SerializeField] public float MaxAcceleration = 0f;
    }
}
