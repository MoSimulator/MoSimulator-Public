namespace RobotFramework.Motors
{
    /// <summary>
    /// Represents the constants for a given DC motor.
    /// </summary>
    public struct MotorConstants
    {
        /// <summary>
        /// The motor's nominal supply voltage
        /// </summary>
        public float v_u;
        /// <summary>
        /// The motor's torque constant (Nm/A)
        /// </summary>
        public float k_t;
        /// <summary>
        /// The motor's stall current (Amps)
        /// </summary>
        public float i_s;
        /// <summary>
        /// The motor's free current (Amps)
        /// </summary>
        public float i_m;
        /// <summary>
        /// The motor's free speed (rotations/second)
        /// </summary>
        public float w_max;

        /// <summary>
        /// Represents an FRC and/or FTC legal DC motor.
        /// </summary>
        public enum MotorType
        {
            KrakenX60,
            KrakenX60_FOC,
            KrakenX44,
            KrakenX44_FOC,
            Minion,
            MinionAdvHall,
            NeoVortex,
            Neo1_1,
            Neo550,
            Falcon500,
            Falcon500_FOC,
            Vex775Pro,
            CIM,
            MiniCIM,
            Bag,
            SnowBlower,
            YellowJacket,
            SuperServoPlus,
            HDHex,
            AxonMiniMK2_4_8V,
            AxonMiniMK2_6_0V,
            AxonMiniMK2_7_4V,
            AxonMiniMK2_8_4V,
            AxonMaxMK2_4_8V,
            AxonMaxMK2_6_0V,
            AxonMaxMK2_7_4V,
            AxonMaxMK2_8_4V
        }

        // Scaling factor to adjust for inconsistencies between mass allocated
        // to rigidbodies in the sim vs their real life counterparts.
        private const float TORQUE_SCALAR = 1.276f;

        // Drivetrains with an explicit real-world mass replace this legacy compensation.
        internal static float SimulationTorqueScale => TORQUE_SCALAR;

        /// <summary>
        /// Loads the constants for a specified motor type.
        /// </summary>
        /// <param name="type">The motor to retrieve the constants of.</param>
        /// <returns>The constants for the specified motor.</returns>
        public static MotorConstants FromMotorType(MotorType type)
        {
            var m = new MotorConstants();

            switch (type)
            {
                case MotorType.KrakenX60:
                    m.v_u = 12f;
                    m.k_t = 0.01926f * TORQUE_SCALAR;
                    m.i_s = 374.4f;
                    m.i_m = 2.834f;
                    m.w_max = 6065f / 60f;
                    break;
                case MotorType.KrakenX60_FOC:
                    m.v_u = 12f;
                    m.k_t = 0.01981f * TORQUE_SCALAR;
                    m.i_s = 476.1f;
                    m.i_m = 3.496f;
                    m.w_max = 5785f / 60f;
                    break;
                case MotorType.KrakenX44:
                    m.v_u = 12f;
                    m.k_t = 0.01491f * TORQUE_SCALAR;
                    m.i_s = 279.1f;
                    m.i_m = 3.156f;
                    m.w_max = 7758f / 60f;
                    break;
                case MotorType.KrakenX44_FOC:
                    m.v_u = 12f;
                    m.k_t = 0.01537f * TORQUE_SCALAR;
                    m.i_s = 329.2f;
                    m.i_m = 3.231f;
                    m.w_max = 7368f / 60f;
                    break;
                case MotorType.Minion:
                    m.v_u = 12f;
                    m.k_t = 0.01515f * TORQUE_SCALAR;
                    m.i_s = 211.6f;
                    m.i_m = 2.206f;
                    m.w_max = 7704f / 60f;
                    break;
                case MotorType.MinionAdvHall:
                    m.v_u = 12f;
                    m.k_t = 0.01563f * TORQUE_SCALAR;
                    m.i_s = 207.2f;
                    m.i_m = 2.384f;
                    m.w_max = 7644f / 60f;
                    break;
                case MotorType.NeoVortex:
                    m.v_u = 12f;
                    m.k_t = 0.01545f * TORQUE_SCALAR;
                    m.i_s = 391.1f;
                    m.i_m = 5.434f;
                    m.w_max = 6826f / 60f;
                    break;
                case MotorType.Neo1_1:
                    m.v_u = 12f;
                    m.k_t = 0.01959f * TORQUE_SCALAR;
                    m.i_s = 216.3f;
                    m.i_m = 1.782f;
                    m.w_max = 5907f / 60f;
                    break;
                case MotorType.Neo550:
                    m.v_u = 12f;
                    m.k_t = 0.0097f * TORQUE_SCALAR;
                    m.i_s = 100f;
                    m.i_m = 1.4f;
                    m.w_max = 11000f / 60f;
                    break;
                case MotorType.Falcon500:
                    m.v_u = 12f;
                    m.k_t = 0.01825f * TORQUE_SCALAR;
                    m.i_s = 257f;
                    m.i_m = 1.5f;
                    m.w_max = 6380f / 60f;
                    break;
                case MotorType.Falcon500_FOC:
                    m.v_u = 12f;
                    m.k_t = 0.01921f * TORQUE_SCALAR;
                    m.i_s = 304f;
                    m.i_m = 1.5f;
                    m.w_max = 6080f / 60f;
                    break;
                case MotorType.Vex775Pro:
                    m.v_u = 12f;
                    m.k_t = 0.0053f * TORQUE_SCALAR;
                    m.i_s = 134f;
                    m.i_m = 0.7f;
                    m.w_max = 18730f / 60f;
                    break;
                case MotorType.CIM:
                    m.v_u = 12f;
                    m.k_t = 0.0182f * TORQUE_SCALAR;
                    m.i_s = 133f;
                    m.i_m = 2.7f;
                    m.w_max = 5310f / 60f;
                    break;
                case MotorType.MiniCIM:
                    m.v_u = 12f;
                    m.k_t = 0.01584f * TORQUE_SCALAR;
                    m.i_s = 89f;
                    m.i_m = 3f;
                    m.w_max = 5840f / 60f;
                    break;
                case MotorType.Bag:
                    m.v_u = 12f;
                    m.k_t = 0.00811f * TORQUE_SCALAR;
                    m.i_s = 53f;
                    m.i_m = 1.8f;
                    m.w_max = 13180f / 60f;
                    break;
                case MotorType.SnowBlower:
                    m.v_u = 12f;
                    m.k_t = 0.32958f * TORQUE_SCALAR;
                    m.i_s = 24f;
                    m.i_m = 5f;
                    m.w_max = 100f / 60f;
                    break;
                case MotorType.YellowJacket:
                    m.v_u = 12f;
                    m.k_t = 0.01567f * TORQUE_SCALAR;
                    m.i_s = 9.2f;
                    m.i_m = 0.25f;
                    m.w_max = 6000f / 60f;
                    break;
                case MotorType.SuperServoPlus:
                    m.v_u = 6f;
                    m.k_t = 0.05783f * TORQUE_SCALAR;
                    m.i_s = 3.9f;
                    m.i_m = 0.16f;
                    m.w_max = 1000f / 60f;
                    break;
                case MotorType.HDHex:
                    m.v_u = 12f;
                    m.k_t = 0.01235f * TORQUE_SCALAR;
                    m.i_s = 8.5f;
                    m.i_m = 0.4f;
                    m.w_max = 6000f / 60f;
                    break;
                case MotorType.AxonMiniMK2_4_8V:
                    m.v_u = 4.8f;
                    m.k_t = 0.61292f * TORQUE_SCALAR;
                    m.i_s = 3.2f;
                    m.i_m = 0.3f;
                    m.w_max = 90f / 60f;
                    break;
                case MotorType.AxonMiniMK2_6_0V:
                    m.v_u = 6f;
                    m.k_t = 0.64517f * TORQUE_SCALAR;
                    m.i_s = 3.8f;
                    m.i_m = 0.35f;
                    m.w_max = 111f / 60f;
                    break;
                case MotorType.AxonMiniMK2_7_4V:
                    m.v_u = 7.4f;
                    m.k_t = 0.68418f * TORQUE_SCALAR;
                    m.i_s = 4.3f;
                    m.i_m = 0.38f;
                    m.w_max = 125f / 60f;
                    break;
                case MotorType.AxonMiniMK2_8_4V:
                    m.v_u = 8.4f;
                    m.k_t = 0.73550f * TORQUE_SCALAR;
                    m.i_s = 4.4f;
                    m.i_m = 0.42f;
                    m.w_max = 142f / 60f;
                    break;
                case MotorType.AxonMaxMK2_4_8V:
                    m.v_u = 4.8f;
                    m.k_t = 0.76274f * TORQUE_SCALAR;
                    m.i_s = 3.6f;
                    m.i_m = 0.28f;
                    m.w_max = 71f / 60f;
                    break;
                case MotorType.AxonMaxMK2_6_0V:
                    m.v_u = 6f;
                    m.k_t = 0.83357f * TORQUE_SCALAR;
                    m.i_s = 4f;
                    m.i_m = 0.32f;
                    m.w_max = 83f / 60f;
                    break;
                case MotorType.AxonMaxMK2_7_4V:
                    m.v_u = 7.4f;
                    m.k_t = 0.88944f * TORQUE_SCALAR;
                    m.i_s = 4.3f;
                    m.i_m = 0.36f;
                    m.w_max = 100f / 60f;
                    break;
                case MotorType.AxonMaxMK2_8_4V:
                    m.v_u = 8.4f;
                    m.k_t = 0.95935f * TORQUE_SCALAR;
                    m.i_s = 4.6f;
                    m.i_m = 0.4f;
                    m.w_max = 111f / 60f;
                    break;
            }

            return m;
        }
    }
}
