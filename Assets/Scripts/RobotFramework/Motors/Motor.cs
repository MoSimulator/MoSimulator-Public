using System;
using UnityEngine;

namespace RobotFramework.Motors
{
    /// <summary>
    /// A stateless utility class for simulating the behavior of a DC motor.
    /// Calculates output torque at the rotor, for various control modes. Supports open
    /// loop control (volts or duty cycle), as well as closed loop position and velocity
    /// control, including profiled motion control with a trapezoid motion profile.
    /// </summary>
    [Serializable]
    public class Motor
    {
        // Notation is my passion

        [SerializeField] private MotorConstants.MotorType motorType = MotorConstants.MotorType.KrakenX60;
        [SerializeField] private MotorConfig config = new MotorConfig();

        // TODO integral and derivative feedback gains, ie stateful refactor, if deemed necessary.

        private MotorConstants? _k = null;
        private MotorConstants.MotorType? _mt = null;
        private MotorConstants _constants
        {
            get
            {
                if (_mt != motorType || _k == null) {
                    _k = MotorConstants.FromMotorType(motorType);
                    _mt = motorType;
                }

                return _k ?? new MotorConstants();
            }
        }

        /// <summary>
        /// Targets a specified position using a trapezoidal motion profile.
        /// </summary>
        /// <param name="position">The position to target, in rotations at the rotor.</param>
        /// <param name="measurement">The current position, in rotations at the rotor.</param>
        /// <param name="dt">The amount of time to advance towards the desired position along the profile, in seconds.</param>
        /// <param name="w">The current speed of the rotor in rotations/second.</param>
        /// <returns>The output torque of the motor in Nm.</returns>
        public float ProfiledPosition(float position, float measurement, float dt, float w)
        {
            float w_c = w;
            float p_g = position;

            // Adjust reference to constraints

            float t_rec = 0f;
            float violation = Mathf.Abs(w_c) - config.MaxVelocity;
            if (violation > 0f)
            {
                // Slide setpoint to smoothly return to a mechanism
                // state within configured profile constraints
                t_rec = violation / config.MaxAcceleration;
                p_g += w_c * t_rec + config.MaxAcceleration * Mathf.Sign(-w_c) * t_rec * t_rec / 2f;
                w_c = config.MaxVelocity * Mathf.Sign(w_c);
            }

            float dx = p_g - measurement;
            float d = Mathf.Abs(w_c) * w_c / (2f * config.MaxAcceleration); // Threshold displacement
            float sign = Mathf.Abs(dx - d) < 1e-4f || dx > d ? 1f : -1f; // Epsilon to prevent oscillation near setpoint

            float a = sign * config.MaxAcceleration;
            float v_l = sign * config.MaxVelocity;

            // Generate profile

            float t_1, t_2, t_3;

            float v_peak = sign * Mathf.Sqrt(Mathf.Max(a * dx + w_c * w_c / 2f, 0f));
            if (sign * v_peak > config.MaxVelocity)
            {
                // Velocity constrained case
                t_1 = (v_l - w_c) / a;
                t_2 = (dx - ((v_l * v_l - w_c * w_c) / (2f * a)) - (v_l * v_l / (2f * a))) / v_l;
                t_3 = v_l / a;
            }
            else
            {
                // Velocity limit is not reached
                t_1 = (v_peak - w_c) / a;
                t_2 = 0f;
                t_3 = v_peak / a;
            }

            t_1 += t_rec;

            // Advance through profile

            float r = measurement;
            float w_i = w_c;
            float a_i = t_rec > 0f && sign * w_i > 0f ? -a : a;

            float t_1b = Mathf.Min(dt, t_1);
            r += w_i * t_1b + a_i / 2f * t_1b * t_1b;
            w_i += a_i * dt;

            if (dt > t_1)
            {
                dt -= t_1;

                r += w_i * Mathf.Min(dt, t_2);
                a_i = 0f;

                if (dt > t_2)
                {
                    dt -= t_2;

                    float t_3b = Mathf.Min(dt, t_3);

                    r += w_i * t_3b - a / 2f * t_3b * t_3b;
                    w_i += -a * dt;
                    a_i = -a;

                    if (dt > t_3) {
                        r = p_g;
                        w_i = 0f;
                        a_i = 0f;
                    }
                }
            }

            return Position(r, measurement, w_i, a_i, w);
        }

        /// <summary>
        /// Targets a specified position.
        /// </summary>
        /// <param name="position">The position to target, in rotations at the rotor.</param>
        /// <param name="measurement">The current position, in rotations at the rotor.</param>
        /// <param name="w">The current speed of the rotor in rotations/second.</param>
        /// <returns>The output torque of the motor in Nm.</returns>
        public float Position(float position, float measurement, float w)
        {
            return Position(position, measurement, 0f, 0f, w);
        }

        /// <summary>
        /// Targets a specified position.
        /// </summary>
        /// <param name="position">The position to target, in rotations at the rotor.</param>
        /// <param name="measurement">The current position, in rotations at the rotor.</param>
        /// <param name="velocity">The target velocity, in rotations/second at the rotor. Typically used by motion profiles.</param>
        /// <param name="acceleration">The target acceleration, in rotations/second^2 at the rotor. Typically used by motion profiles.</param>
        /// <param name="w">The current speed of the rotor in rotations/second.</param>
        /// <returns>The output torque of the motor in Nm.</returns>
        public float Position(float position, float measurement, float velocity, float acceleration, float w)
        {
            float e_k = position - measurement;
            float u_k =
                (e_k * config.K_p)
                + (Mathf.Abs(e_k) > 1e-4f ? Mathf.Sign(e_k) * config.K_s : 0f)
                + (velocity * config.K_v)
                + (acceleration * config.K_a);

            return Voltage(u_k, w);
        }

        /// <summary>
        /// Targets a specified velocity.
        /// </summary>
        /// <param name="velocity">The velocity to target, in rotations/second at the rotor.</param>
        /// <param name="w">The current speed of the rotor in rotations/second.</param>
        /// <returns>The output torque of the motor in Nm.</returns>
        public float Velocity(float velocity, float w)
        {
            return Velocity(velocity, 0f, w);
        }

        /// <summary>
        /// Targets a specified velocity.
        /// </summary>
        /// <param name="velocity">The velocity to target, in rotations/second at the rotor.</param>
        /// <param name="acceleration">The target acceleration, in rotations/second^2 at the rotor. Typically used by motion profiles.</param>
        /// <param name="w">The current speed of the rotor in rotations/second.</param>
        /// <returns>The output torque of the motor in Nm.</returns>
        public float Velocity(float velocity, float acceleration, float w)
        {
            float u_k =
                ((velocity - w) * config.K_p)
                + (Mathf.Abs(velocity) > 1e-4f ? Mathf.Sign(velocity) * config.K_s : 0f)
                + (velocity * config.K_v)
                + (acceleration * config.K_a);

            return Voltage(u_k, w);
        }

        /// <summary>
        /// Requests a specified voltage.
        /// </summary>
        /// <param name="volts">The voltage to request.</param>
        /// <param name="w">The current speed of the rotor in rotations/second.</param>
        /// <returns>The output torque of the motor in Nm.</returns>
        public float Voltage(float volts, float w)
        {
            return DutyCycle(volts / _constants.v_u, w);
        }

        /// <summary>
        /// Requests a specified duty cycle.
        /// </summary>
        /// <param name="percent">The percent duty cycle to request, in the range [-1.0, 1.0].</param>
        /// <param name="w">The current speed of the rotor in rotations/second.</param>
        /// <returns>The output torque of the motor in Nm.</returns>
        public float DutyCycle(float percent, float w)
        {
            percent = Mathf.Clamp(percent, -1f, 1f);

            // τ = Kₜ • clamp(Iₜₛ, -I_limit, I_limit), where
            //
            // I_limit = min(Iₜₗ, Iₜₘ)
            // Iₜₗ is given
            // Iₜₘ = (−IΔ • |ωₜ| + √((IΔ • |ωₜ|)² + 4IₛIₐ)) / 2
            // Iₜₛ = Iₛ • (D − ωₜ) + Iₘωₜ
            //
            // and
            //
            // IΔ = Iₛ − Iₘ
            // ωₜ = ω/ωₘ

            float i_delta = _constants.i_s - _constants.i_m;
            float w_t = Mathf.Clamp(w / _constants.w_max, -1f, 1f);
            float wt_abs = Mathf.Abs(w_t);

            float i_tm = (-i_delta * wt_abs + Mathf.Sqrt(i_delta * wt_abs * (i_delta * wt_abs) + 4f * _constants.i_s * config.SupplyLimit)) / 2f;
            float i_ts = _constants.i_s * (percent - w_t) + _constants.i_m * w_t;

            // Back-EMF can reverse current even while the applied voltage is positive.
            float currentLimit = Mathf.Min(config.StatorLimit, i_tm);
            float tau = _constants.k_t * Mathf.Clamp(i_ts, -currentLimit, currentLimit);
            return tau;
        }

        /// <summary>
        /// Returns the maximum theoretical speed of the rotor (free speed), in rotations per second.
        /// </summary>
        /// <returns>The rotor's free speed, in rotations/second.</returns>
        public float GetMaxSpeed()
        {
            return _constants.w_max;
        }
    }
}
