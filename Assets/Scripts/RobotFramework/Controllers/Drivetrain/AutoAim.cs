using UnityEngine;

namespace RobotFramework.Controllers.Drivetrain
{
    /// <summary>
    /// A basic drivetrain controller for governing the robot's heading via closed-loop control.
    /// Can be configured to target its setpoint using either a traditional PID controller with
    /// its reference as the final target rotation, or with an S-curve motion profile if smoother
    /// motion is desired.
    /// </summary>
    public abstract class AutoAim : MonoBehaviour
    {
        [Tooltip("Overrides the reference point on the robot to aim from. If left unspecified, defaults to the robot's center")]
        [SerializeField] private Transform rotationReference;
        [Tooltip("Enables reporting the controller's targets to the Debug Plotter")]
        [SerializeField] protected bool debugEnabled = false;

        [Header("Rotation PID")]
        [Tooltip("Proportional PID gain for rotational alignment (degrees/second/degrees of error)")]
        [SerializeField] private float K_p = 0f;
        [Tooltip("Integral PID gain for rotational alignment (degrees/second/accumulated degrees of error)")]
        [SerializeField] private float K_i = 0f;
        [Tooltip("Derivative PID gain for rotational alignment (degrees/second/degrees of error/time)")]
        [SerializeField] private float K_d = 0f;
        [Tooltip("The maximum allowable angular velocity of the robot when using PID control, in degrees/second. Values of 0 or lower defaults to the maximum angular velocity allowed by the drivetrain")]
        [SerializeField] private float PIDMax = 0f;
        [Tooltip("The maximum magnitude of absolute positional error in which to enable the integral gain, in degrees. When positional error is outside of this range, accumulated error will reset to 0. Values of 0 or lower disables integral zone functionality")]
        [SerializeField] private float I_zone = 0f;
        [Tooltip("The maximum contribution of the integral term (absolute), in degrees/second.")]
        [SerializeField] private float integratorRange = 0f;

        [Header("Profiled Rotation")]
        [Tooltip("The maximum allowable angular velocity of the robot, in degrees/second. Values of 0 or lower defaults to the maximum angular velocity allowed by the drivetrain")]
        [SerializeField] private float maxOmega = 0f;
        [Tooltip("The desired angular deceleration rate of the robot, in degrees/second^2")]
        [SerializeField] private float maxAlpha = 0f;
        [Tooltip("The maximum allowable angular jerk of the robot, in degrees/second^3. Values of 0 or lower disables jerk limiting")]
        [SerializeField] private float maxAlphaDot = 0f;
        [Tooltip("Minimum approach time near the target, in seconds. Increase this if profiled heading oscillates.")]
        [SerializeField, Min(0.01f)] private float rotationApproachTime = 0.2f;
        [Tooltip("Angular velocity lookahead in seconds for Predictive control. Limited to half the approach time to keep feedback bounded.")]
        [SerializeField, Min(0f)] private float rotationPredictionTime = 0.06f;

        protected Rigidbody _rb;
        protected DriveController _driveController;

        private float _lastRotationTarget = 0f;
        private float _lastRotationError = 0f;
        private float _lastTime = 0f;
        private float _totalError = 0f;

        protected virtual void Start()
        {
            _rb = gameObject.GetComponent<Rigidbody>();
            _driveController = gameObject.GetComponent<DriveController>();

            if (!rotationReference && _rb != null) rotationReference = _rb.transform;
        }

        protected virtual void FixedUpdate()
        {
            #if UNITY_EDITOR
            if (debugEnabled) {
                DebugPlotter.Add("aim_theta", _lastRotationTarget);
                DebugPlotter.Add("aim_error", _lastRotationError);
            }
            #endif
        }

        /// <summary>
        /// Commands the drivetrain to face a specified target location.
        /// </summary>
        /// <param name="x">The field-relative X position to face the robot towards, in meters.</param>
        /// <param name="y">The field-relative Y position to face the robot towards, in meters. "Z" position in Unity's coordinate frame.</param>
        /// <param name="ff">A feedforward to inject into the controller's output, in degrees/second.</param>
        /// <param name="control">The control strategy to use for aiming.</param>
        protected void AimAt(float x, float y, float ff, AimControl control)
        {
            if (rotationReference == null)
            {
                Debug.LogError($"AutoAim reference not assigned to {gameObject.name}");
                return;
            }

            float theta_rad = Mathf.Atan2(x - rotationReference.position.x, y - rotationReference.position.z);
            AimRotation(theta_rad * Mathf.Rad2Deg, ff, control);
        }

        /// <summary>
        /// Commands the drivetrain to face a specified rotation.
        /// </summary>
        /// <param name="theta">The field-relative rotation to target, in degrees.</param>
        /// <param name="ff">A feedforward to inject into the controller's output, in degrees/second.</param>
        /// <param name="control">The control strategy to use for aiming.</param>
        protected void AimRotation(float theta, float ff, AimControl control)
        {
            if (rotationReference == null || _driveController == null)
            {
                Debug.LogError($"Rotation reference and/or DriveController not found on {gameObject.name}");
                return;
            }

            theta = Mathf.Repeat(theta, 360f);
            float error = (theta - rotationReference.rotation.eulerAngles.y + 540f) % 360f - 180f;
            float dt = Time.inFixedTimeStep ? Time.fixedDeltaTime : Mathf.Max(Time.time - _lastTime, 0f);

            _totalError = (I_zone < 1e-4f || Mathf.Abs(error) <= I_zone) && K_i > 1e-4f
                ? Mathf.Clamp(_totalError + error * dt, -integratorRange / K_i, integratorRange / K_i)
                : 0f;

            float omega = 0f, omega_max = 0f;
            switch (control)
            {
                case AimControl.PID:
                    omega_max = PIDMax > 1e-4f ? PIDMax : _driveController.GetMaxOmega();
                    omega = K_p * error + K_i * _totalError + K_d * (dt > 1e-4f ? ((error - _lastRotationError) / dt) : 0f);
                    break;
                case AimControl.Profiled:
                    omega_max = maxOmega > 1e-4f ? maxOmega : _driveController.GetMaxOmega();
                    omega = CalculateSCurve(error, omega_max, maxAlpha, maxAlphaDot);
                    float approachOmega = Mathf.Abs(error) / Mathf.Max(rotationApproachTime, Mathf.Max(dt, 0.01f));
                    omega = Mathf.Clamp(omega, -approachOmega, approachOmega);
                    break;
                case AimControl.Predictive:
                    omega_max = maxOmega > 1e-4f ? maxOmega : _driveController.GetMaxOmega();
                    float approach = Mathf.Max(rotationApproachTime, 2f * Mathf.Max(dt, 0.01f));
                    float lookahead = Mathf.Clamp(rotationPredictionTime, 0f, approach * 0.5f);
                    // Rigidbody angular velocity is radians/sec about the world Y axis.
                    float predictedError = error - (_rb != null ? _rb.angularVelocity.y * Mathf.Rad2Deg : 0f) * lookahead;
                    omega = Mathf.Sign(predictedError) * CalculatePredictiveSpeed(Mathf.Abs(predictedError), omega_max, maxAlpha, approach, dt);
                    break;
            }

            _driveController.TurnAssist(Mathf.Clamp(omega + ff, -omega_max, omega_max), true);

            _lastRotationTarget = theta;
            _lastRotationError = error;
            _lastTime = Time.inFixedTimeStep ? Time.fixedTime : Time.time;
        }

        /// <summary>
        /// Returns the latest target rotation, in degrees.
        /// </summary>
        public float GetRotationTarget()
        {
            return _lastRotationTarget;
        }

        /// <summary>
        /// Returns the distance from the latest target rotation, in degrees.
        /// </summary>
        public float GetRotationError()
        {
            return _lastRotationError;
        }

        public enum AimControl
        {
            PID,
            Profiled,
            Predictive
        }

        /// <summary>
        /// A discrete stopping-distance envelope with a finite-gain terminal approach.
        /// Predictive control does not add PID or use the S-curve jerk parameter:
        /// the drivetrain already limits motor force.
        /// </summary>
        protected static float CalculatePredictiveSpeed(float distance, float maxSpeed, float deceleration, float approachTime, float dt)
        {
            float speed = Mathf.Min(Mathf.Max(maxSpeed, 0f), distance / Mathf.Max(approachTime, 0.01f));
            if (deceleration > 1e-4f)
            {
                // Reserve one sample of travel before braking. Rationalize the root
                // of distance = v*dt + v*v/(2*a) to avoid cancellation near zero.
                float step = deceleration * Mathf.Max(dt, 0f);
                float brakingSpeed = 2f * deceleration * distance
                    / (Mathf.Sqrt(step * step + 2f * deceleration * distance) + step + 1e-8f);
                speed = Mathf.Min(speed, brakingSpeed);
            }
            return speed;
        }

        /// <summary>
        /// Calculates the output velocity of an S curve motion profile targeting a final position and velocity of zero.
        /// </summary>
        /// <param name="s">The current position from zero. Can be negative.</param>
        /// <param name="v">The maximum allowed velocity.</param>
        /// <param name="a">The maximum allowed deceleration.</param>
        /// <param name="j">The maximum allowed jerk.</param>
        protected static float CalculateSCurve(float s, float v, float a, float j)
        {
            float abs_s = Mathf.Abs(s);
            v = Mathf.Max(v, 0f);
            if (abs_s <= 0f || v <= 0f)
                return 0f;
            if (a <= 1e-4f)
                return v * Mathf.Sign(s);

            bool jerkEnabled = j > 1e-4f;
            if (abs_s < v * v / (2f * a) + (jerkEnabled ? v * a / (2f * j) : 0f))
            {
                float a_2 = a * a;

                // Solve s = v^2 / (2a) + va / (2j). Rationalizing the positive
                // root avoids cancellation near zero and keeps the cruise boundary continuous.
                v = jerkEnabled
                    ? 4f * a * abs_s / (Mathf.Sqrt(a_2 * a_2 / (j * j) + 8f * a * abs_s) + a_2 / j)
                    : Mathf.Sqrt(2f * a * abs_s);
            }

            return v * Mathf.Sign(s);
        }
    }
}
