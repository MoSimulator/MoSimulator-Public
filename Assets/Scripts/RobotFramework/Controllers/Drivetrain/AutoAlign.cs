using System;
using UnityEngine;

namespace RobotFramework.Controllers.Drivetrain
{
    /// <summary>
    /// A controller for holonomic drivetrains that moves the robot to a specified field
    /// location using PID, an S-curve profile, hybrid, or predictive velocity control.
    /// Extends AutoAim to also target a specified heading.
    /// </summary>
    public abstract class AutoAlign : AutoAim
    {
        [Header("Positional Alignment")]
        [Tooltip("The control strategy for linear alignment. Predictive uses measured velocity to brake ahead of the target without PID correction.")]
        [SerializeField] private AlignControl alignControl = AlignControl.Profiled;

        [Header("Position PID")]
        [Tooltip("Proportional gain for linear alignment (meters/second per meter of error).")]
        [SerializeField] private float positionK_p = 0f;
        [Tooltip("Integral gain for linear alignment (meters/second per accumulated meter-second of error).")]
        [SerializeField] private float positionK_i = 0f;
        [Tooltip("Derivative gain for linear alignment (meters/second per meter/second of error).")]
        [SerializeField] private float positionK_d = 0f;
        [Tooltip("Maximum PID correction speed in meters/second. Values of 0 or lower use the drivetrain maximum velocity.")]
        [SerializeField] private float positionPIDMax = 0f;
        [Tooltip("Maximum distance error in meters for integral accumulation. Values of 0 or lower disable the integral zone.")]
        [SerializeField] private float positionI_zone = 0f;
        [Tooltip("Maximum magnitude of the integral contribution in meters/second.")]
        [SerializeField] private float positionIntegratorRange = 0f;

        [Header("Profiled / Predictive Position")]
        [Tooltip("The maximum allowable velocity of the robot, in meters/second. Values of 0 or lower defaults to the maximum velocity allowed by the drivetrain")]
        [SerializeField] private float maxVelocity = 0f;
        [Tooltip("Deceleration in meters/second^2. For Predictive, calibrate this at low speed; braking is reduced at higher speeds using the drivetrain bite curve.")]
        [SerializeField] private float maxDeceleration = 0f;
        [Tooltip("S-curve braking parameter in meters/second^3, used by Profiled and Hybrid. Values of 0 or lower disable it. Predictive uses deceleration and velocity lookahead instead.")]
        [SerializeField] private float maxJerk = 0f;
        [Tooltip("Minimum approach time near the target, in seconds. Increase this if profiled position oscillates.")]
        [SerializeField, Min(0.01f)] private float positionApproachTime = 0.2f;
        [Tooltip("Velocity lookahead in seconds for Predictive control. Compensates drivetrain response delay. The effective approach time is at least twice this value to keep feedback bounded.")]
        [SerializeField, Min(0f)] private float positionPredictionTime = 0.06f;
        [Tooltip("Fraction of estimated braking used for Predictive stopping distance. Grip is a ceiling, not guaranteed motor braking. Lower this to start braking earlier on long, full-speed moves.")]
        [SerializeField, Range(0.1f, 1f)] private float positionBrakingSafetyFactor = 0.7f;
        [Tooltip("Direction error in degrees where overspeed recovery begins. Braking increases smoothly toward 180 degrees. Observe align_effort in the Debug Plotter. Values of 0 or lower disable recovery.")]
        [SerializeField] private float recoveryThreshold = 4f;

        private Vector2 _lastPositionTarget = new();
        private float _lastPositionError = 0f;
        private float _lastEffort = 0f;
        private Vector2 _previousPositionError;
        private Vector2 _totalPositionError;
        private float _lastPositionTime;
        private AlignControl _lastAlignControl;
        private bool _positionPidInitialized;
        private Vector2 _predictiveVelocity;

        protected override void FixedUpdate()
        {
            base.FixedUpdate();

            #if UNITY_EDITOR
            if (debugEnabled)
            {
                DebugPlotter.Add("align_x", _lastPositionTarget.x);
                DebugPlotter.Add("align_y", _lastPositionTarget.y);
                DebugPlotter.Add("align_error", _lastPositionError);
                DebugPlotter.Add("align_effort", _lastEffort);
            }
            #endif
        }

        /// <summary>
        /// Commands the drivetrain to target a specified position.
        /// </summary>
        /// <param name="x">The field-relative X position to target, in meters.</param>
        /// <param name="y">The field-relative Y position to target, in meters. "Z" position in Unity's coordinate frame.</param>
        /// <param name="theta">The field-relative rotation to target, in degrees.</param>
        /// <param name="aimControl">The control strategy to use for aiming.</param>
        protected void AlignPosition(float x, float y, float theta, AimControl aimControl)
        {
            AlignPosition(x, y, theta, aimControl, alignControl);
        }

        /// <summary>
        /// Commands a position and heading with independently selected linear and rotation control.
        /// </summary>
        protected void AlignPosition(float x, float y, float theta, AimControl aimControl, AlignControl control)
        {
            if (_rb == null || _driveController == null)
            {
                Debug.LogError($"Rigidbody and/or DriveController not found on {gameObject.name}");
                return;
            }

            var target = new Vector2(x, y);
            float now = Time.inFixedTimeStep ? Time.fixedTime : Time.time;
            float dt = Time.inFixedTimeStep ? Time.fixedDeltaTime : Mathf.Max(now - _lastPositionTime, 0f);
            bool resetTrajectory = !_positionPidInitialized || control != _lastAlignControl
                || now - _lastPositionTime > Time.fixedDeltaTime * 1.5f;
            bool resetPid = resetTrajectory || (target - _lastPositionTarget).sqrMagnitude > 1e-8f;
            if (resetPid)
                _totalPositionError = Vector2.zero;
            _lastPositionTarget = target;

            var error = _lastPositionTarget - new Vector2(_rb.position.x, _rb.position.z);
            var e_norm = error.magnitude;

            float vx = 0f, vy = 0f, d_theta = 0f;
            float v_max = maxVelocity > 1e-4f ? maxVelocity : _driveController.GetMaxVelocity();
            float recovery = 0f;
            if ((control == AlignControl.Profiled || control == AlignControl.Hybrid) && e_norm > 1e-4f)
            {
                float v = CalculateSCurve(e_norm, v_max, maxDeceleration, maxJerk);
                // A stopping-distance curve alone has unbounded gain at zero error
                // without jerk limiting. Use a linear terminal approach to let the drivetrain settle.
                v = Mathf.Min(v, e_norm / Mathf.Max(positionApproachTime, Mathf.Max(dt, 0.01f)));

                float speedSquared = _rb.velocity.x * _rb.velocity.x + _rb.velocity.z * _rb.velocity.z;
                float speed = Mathf.Sqrt(speedSquared);

                float error_d = Mathf.Atan2(error.y, error.x) * Mathf.Rad2Deg;
                float rb_d = speed > 0.01f ? Mathf.Atan2(_rb.velocity.z, _rb.velocity.x) * Mathf.Rad2Deg : error_d;
                d_theta = (error_d - rb_d + 540f) % 360f - 180f;

                // Blend into braking instead of switching between full speed and zero
                // whenever measured speed or direction crosses the recovery threshold.
                if (recoveryThreshold > 1e-4f && recoveryThreshold < 180f)
                {
                    float overspeed = Mathf.Clamp01((speed - v) / Mathf.Max(speed, 0.01f));
                    recovery = overspeed * Mathf.InverseLerp(recoveryThreshold, 180f, Mathf.Abs(d_theta));
                }
                vx = v * (error.x / e_norm);
                vy = v * (error.y / e_norm);
            }

            var profileVelocity = new Vector2(vx, vy);
            var pidVelocity = Vector2.zero;
            float pidMax = positionPIDMax > 1e-4f ? positionPIDMax : _driveController.GetMaxVelocity();
            if (control == AlignControl.PID || control == AlignControl.Hybrid)
            {
                _totalPositionError = recovery <= 0f && (positionI_zone <= 1e-4f || e_norm <= positionI_zone)
                    && positionK_i > 1e-4f && e_norm > 1e-4f
                    ? Vector2.ClampMagnitude(_totalPositionError + error * dt,
                        Mathf.Max(positionIntegratorRange, 0f) / positionK_i)
                    : Vector2.zero;

                var derivative = !resetPid && dt > 1e-4f ? (error - _previousPositionError) / dt : Vector2.zero;
                pidVelocity = positionK_p * error + positionK_i * _totalPositionError + positionK_d * derivative;
                pidVelocity = Vector2.ClampMagnitude(pidVelocity, pidMax);
            }
            else
                _totalPositionError = Vector2.zero;

            Vector2 velocity;
            switch (control)
            {
                case AlignControl.Profiled:
                    velocity = profileVelocity * (1f - recovery);
                    break;
                case AlignControl.PID:
                    velocity = pidVelocity;
                    break;
                case AlignControl.Hybrid:
                    velocity = Vector2.ClampMagnitude(profileVelocity + pidVelocity, v_max) * (1f - recovery);
                    break;
                case AlignControl.Predictive:
                    var measuredVelocity = new Vector2(_rb.velocity.x, _rb.velocity.z);
                    if (resetTrajectory) _predictiveVelocity = measuredVelocity;
                    float lookahead = Mathf.Max(positionPredictionTime, 0f);
                    // Honor the requested prediction horizon. Clipping it to half
                    // the configured approach time silently prevented earlier braking.
                    float approach = Mathf.Max(positionApproachTime, 2f * Mathf.Max(lookahead, Mathf.Max(dt, 0.01f)));
                    _predictiveVelocity = CalculatePredictiveVelocity(error, measuredVelocity, _predictiveVelocity,
                        v_max, maxDeceleration, approach, lookahead, dt, _driveController, positionBrakingSafetyFactor);
                    velocity = _predictiveVelocity;
                    break;
                default:
                    velocity = Vector2.zero;
                    break;
            }

            _driveController.OverrideInput(velocity.x, velocity.y, 0f, DriveController.DrivePerspective.FieldOriented);
            AimRotation(theta, 0f, aimControl);

            _lastPositionError = e_norm;
            _lastEffort = d_theta;
            _previousPositionError = error;
            _lastPositionTime = now;
            _lastAlignControl = control;
            _positionPidInitialized = true;
        }

        /// <summary>
        /// A velocity trajectory seeded from actual motion on engagement. Ramp
        /// acceleration and steering together, but apply speed reductions immediately
        /// so command smoothing does not delay the motor controller's braking.
        /// </summary>
        protected static Vector2 CalculatePredictiveVelocity(Vector2 error, Vector2 measuredVelocity, Vector2 previousCommand,
            float maxSpeed, float deceleration, float approachTime, float predictionTime, float dt, DriveController drive, float brakingSafetyFactor = 0.7f)
        {
            var predictedError = error - measuredVelocity * predictionTime;
            float distance = predictedError.magnitude;
            float speed = CalculateTractionLimitedSpeed(distance, maxSpeed, deceleration, approachTime,
                dt, Mathf.Max(measuredVelocity.magnitude, previousCommand.magnitude), drive, brakingSafetyFactor);
            var desired = distance > 1e-6f ? predictedError * (speed / distance) : Vector2.zero;
            if (deceleration <= 1e-4f) return desired;
            float availableAcceleration = drive.GetBrakingDeceleration(deceleration,
                Mathf.Max(measuredVelocity.magnitude, previousCommand.magnitude));
            // If engagement speed exceeds the alignment cruise limit, decelerate
            // into it continuously instead of clipping away the initial momentum.
            var ramped = Vector2.MoveTowards(previousCommand, desired, availableAcceleration * Mathf.Max(dt, 0f));
            // Motor targets are not actual chassis velocity. Slewing reductions
            // delays braking torque on top of the physical response delay. Keep
            // the gradual direction change, but allow speed reductions immediately.
            return desired.magnitude < previousCommand.magnitude
                ? Vector2.ClampMagnitude(ramped, desired.magnitude) : ramped;
        }

        /// <summary>
        /// Uses the weakest grip between the current and proposed speed throughout
        /// the stop. Grip improves as speed falls, making this conservative without
        /// applying top-speed braking to short, low-speed moves.
        /// </summary>
        protected static float CalculateTractionLimitedSpeed(float distance, float maxSpeed, float deceleration,
            float approachTime, float dt, float measuredSpeed, DriveController drive, float brakingSafetyFactor = 0.7f)
        {
            float upper = CalculatePredictiveSpeed(distance, maxSpeed, deceleration, approachTime, dt);
            if (deceleration <= 1e-4f || upper <= 0f) return upper;
            float StopDistance(float speed)
            {
                float braking = drive.GetBrakingDeceleration(deceleration, Mathf.Max(measuredSpeed, speed))
                    * Mathf.Clamp(brakingSafetyFactor, 0.1f, 1f);
                return speed * Mathf.Max(dt, 0f) + speed * speed / (2f * Mathf.Max(braking, 1e-6f));
            }
            if (StopDistance(upper) <= distance) return upper;
            // Include candidate speed as well as measured speed: planning from rest
            // must already account for the grip that will be lost at cruise speed.
            float lower = 0f;
            for (int i = 0; i < 12; i++)
            {
                float candidate = (lower + upper) * 0.5f;
                if (StopDistance(candidate) <= distance) lower = candidate;
                else upper = candidate;
            }
            return lower;
        }

        public enum AlignControl
        {
            Profiled,
            PID,
            Hybrid,
            Predictive
        }

        /// <summary>
        /// Returns the latest target position.
        /// </summary>
        public Vector2 GetPositionTarget()
        {
            return _lastPositionTarget;
        }

        /// <summary>
        /// Returns the distance from the latest target position, in meters.
        /// </summary>
        public float GetPositionError()
        {
            return _lastPositionError;
        }
    }
}
