using System.Collections.Generic;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.Settings;
using MoSimLib;
using RobotFramework.Motors;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace RobotFramework.Controllers.Drivetrain
{
    /// <summary>
    /// The robot's drivetrain. This class implements behavior for swerve, differential,
    /// and mecanum drivetrains. Wheels are assigned by specifying a Drivetrain Parent game
    /// object, which will be indexed automatically by this class. Drivetrain behavior is
    /// derived from its motor configuration, which is used to simulate the output torque
    /// of individual powertrains and apply realistic forces at the robot's wheel colliders.
    ///
    /// This class runs during fixed updates, polling player control input from RobotBase
    /// and subsequently applying the drivetrain's control output to the physics system.
    /// </summary>
    public class DriveController : MonoBehaviour
    {
        protected RobotBase _robotBase;
        protected Rigidbody _rb;
        private readonly List<Rigidbody> _robotRigidbodies = new();
        private float _tractionMass;
        private float _motorForceScale = 1f;
        // Grounded rolling resistance for roughly 40% speed loss per second while unpowered.
        private const float DisabledRollingDrag = 0.51082562f; // -ln(0.6)
        private float _lastPassiveFixedTime = float.NegativeInfinity;
        protected Transform _wheelChild;
        protected int _fieldLayerMask;

        protected AudioSource _gearSource;
        protected AudioSource _treadSource;

        // Configuration
        [SerializeField] protected GameObject drivetrainParent;
        [SerializeField] protected DriveType drivetrainType = DriveType.Swerve;
        [Tooltip("The maximum velocity of a single wheel on the drivetrain, in meters/second")]
        [SerializeField] protected SecureFloat maxVelocity = 4f;
        [Tooltip("The maximum rotational velocity of the robot, in degrees/second. Values of 0 or lower default to using the maximum kinematically feasible rotational velocity.")]
        [SerializeField] protected float maxOmega = 0f;

        [Header("Hardware Configuration")]
        [Tooltip("Configuration for the drive motor used by the drivetrain")]
        [SerializeField] protected Motor motor;

        [Tooltip("Total real robot weight in pounds, including battery and mechanisms. 0 preserves legacy tuning. Positive values scale drivetrain force using all active, non-kinematic rigidbodies under this robot; those bodies must move with the chassis. Unity masses, suspension and collision response are unchanged.")]
        [SerializeField, Min(0f)] protected float realWorldWeightLbs = 0f;
        [Tooltip("The number of motors per powertrain (ex: 1 for swerve, 2 for 4 motor differential, etc)")]
        [SerializeField] protected int motorsPerPowertrain = 1;
        [Tooltip("The gear ratio from the drive motor to the wheel. Should be a number greater than 1 (ex: SDS MK5n R2 would use a value of 6.03)")]
        [SerializeField] protected float gearRatio = 6.03f;
        [Tooltip("The diameter of the drivetrain's wheels, in inches")]
        [SerializeField] protected float wheelDiameterInches = 4f;
        [Tooltip("Rotational inertia per driven wheel at the wheel shaft, in kg m^2. Used to track wheelspin independently of chassis motion; include reflected drivetrain inertia.")]
        [SerializeField, Min(0.0001f)] protected float wheelRotationalInertia = 0.002f;
        [Tooltip("The percentage of motor torque lost through the drivetrain before reaching the wheels")]
        [Range(0f, 100f)] [SerializeField] protected float mechanicalLossPercent = 1.5f;

        [Header("Wheel Traction")]
        [Tooltip("Tread friction coefficient. Combined drive and sideways force is limited to this coefficient times normal load, with the bite boost below.")]
        [SerializeField, Min(0f)] protected float frictionCoefficient = 1.3f;
        [Tooltip("Extra grip from the slightly spiked tread at rest. 0 is smooth tread; 0.3 adds 30% to the friction limit at rest. Bite decays exponentially with tread speed (~5% remaining at top speed), then linearly with combined forward/sideways slip. Full slip removes only the extra bite. Does not increase motor torque.")]
        [SerializeField, Min(0f)] protected float biteFactor = 0.2f;
        [Tooltip("Width sampled by the three contact rows across each wheel's tread, in inches.")]
        [SerializeField, Min(0.01f)] protected float contactPatchWidthInches = 1.5f;
        [Tooltip("Contact tolerance in meters beyond the curved tread surface. Keep small to avoid traction while airborne.")]
        [SerializeField, Min(0f)] protected float contactTolerance = 0.01f;
        [Tooltip("Time in seconds for sideways grip to oppose slip, subject to the shared friction limit.")]
        [SerializeField, Min(0.01f)] protected float lateralGripResponse = 0.1f;

        [Header("Audio")]
        [SerializeField] protected AudioMixerGroup audioMixerGroup;
        [SerializeField] protected AudioResource gearAudioResource;
        [SerializeField] protected AudioResource treadAudioResource;

        [Header("Debug")]
        [Tooltip("Enables reporting wheel velocities and setpoints to the Debug Plotter")]
        [SerializeField] protected bool debugWheelVelocity = false;
        [Tooltip("Enables reporting motor torque, wheel torque, and applied ground force to the Debug Plotter")]
        [SerializeField] protected bool debugWheelTorque = false;
        #if UNITY_EDITOR
        private Vector3 _debugNetGroundForce;
        private float _debugGroundForceSum;
        private int _debugGroundedWheels;
        #endif
        [Tooltip("Enables reporting the chassis position to the Debug Plotter")]
        [SerializeField] protected bool debugChassisPosition = false;
        [Tooltip("Enables reporting the chassis velocity to the Debug Plotter")]
        [SerializeField] protected bool debugChassisVelocity = false;

        // State
        public bool IsTouchingGround { get; protected set; }
        protected bool _setupError;

        // Anticheat: PhysX briefly pushes a robot past the velocity clamp or through geometry on
        // depenetration and hard collisions, so a violation only counts once it has held. The margin
        // stays tight and the window short - those spikes last a frame or two, while a robot actually
        // running fast holds it.
        protected const float SpeedTolerance = 1.05f;
        protected const float ViolationTimeToFlag = 0.125f;
        protected float _violationTime;
        protected float _groundSpeed;

        // Wheel data
        protected DriveWheel[] _wheels;
        private WheelSetpoint[] _wheelSetpoints;
        private WheelContactPatch[] _contactPatches;
        private readonly RaycastHit[] _contactQuery = new RaycastHit[16];
        private readonly Collider[] _obstacleQuery = new Collider[64];
        private MeshCollider _wheelContactShape;
        private Mesh _wheelContactMesh;
        private float _wheelContactRadius;
        private float _wheelContactHalfWidth;
        private Collider[] _robotContactColliders;
        private Vector3 _contactLinearVelocity;
        private Vector3 _contactAngularVelocity;
        private Vector3 _contactCenterOfMass;
        #if UNITY_EDITOR
        private int _wheelContactShapeBuilds;
        #endif
        protected float _farthestWheel = 0f;

        // Utility getters
        protected float _rotationsPerMeter { get => gearRatio / (_wheelDiameter * Mathf.PI); }
        protected float _wheelDiameter { get => wheelDiameterInches * 0.0254f; }

        // Inputs
        protected ChassisVelocity? _override = null;
        protected float _turnAssist = 0f;
        protected bool _turnOverride = false;
        protected float _driveMp;
        protected float _turnMp;
        protected float? _brakeOverride = null;
        private float? _brakeOverrideOnce = null;
        protected float _actionKeyMp;
        protected int _latchedActionKey;

        // Last input state
        protected ChassisVelocity _lastTarget = new();
        protected float _lastNorm = 0f;
        private float _lastTargetFixedTime = float.NegativeInfinity;

        protected readonly float MEC_MP = 1f / Mathf.Sqrt(2f);

        protected virtual void Start()
        {
            _robotBase = gameObject.GetComponent<RobotBase>() ?? gameObject.AddComponent<RobotBase>();
            _rb = gameObject.GetComponent<Rigidbody>();

            ValidateSetup();
            RefreshRobotRigidbodies();
            if (!_setupError)
            {
                _wheels = new DriveWheel[_wheelChild.childCount];
                _contactPatches = new WheelContactPatch[_wheels.Length];
                for (int i = 0; i < _wheels.Length; i++)
                {
                    var wheelTransform = _wheelChild.GetChild(i);
                    _wheels[i] = wheelTransform.GetComponent<DriveWheel>();
                    _contactPatches[i] = new WheelContactPatch();

                    Vector3 pos = _wheels[i].transform.localPosition;
                    _farthestWheel = Mathf.Max(_farthestWheel, Mathf.Sqrt(pos.x * pos.x + pos.z * pos.z));
                }
            }

            _fieldLayerMask = 1 << LayerMask.NameToLayer("Robot");

            _treadSource = CreateAudioSource(treadAudioResource);
            _gearSource = CreateAudioSource(gearAudioResource);

            _override = null;
            _turnAssist = 0f;
            _turnOverride = false;
            _driveMp = 1f;
            _turnMp = 1f;
            _brakeOverride = null;
            _brakeOverrideOnce = null;
            _actionKeyMp = 1f;
            _latchedActionKey = -1;

            _lastTarget = new();
            _lastNorm = 0f;
        }

        protected virtual void OnDisable()
        {
            ClearDriveTargets();
        }

        protected virtual void OnDestroy()
        {
            if (_wheelContactShape != null)
            {
                if (Application.isPlaying) Destroy(_wheelContactShape.gameObject);
                else DestroyImmediate(_wheelContactShape.gameObject);
            }
            if (_wheelContactMesh != null)
            {
                if (Application.isPlaying) Destroy(_wheelContactMesh);
                else DestroyImmediate(_wheelContactMesh);
            }
        }

        private void ClearDriveTargets()
        {
            _override = null;
            _brakeOverrideOnce = null;
            _turnAssist = 0f;
            _turnOverride = false;
            _lastTarget = new();
            _lastNorm = 0f;
            _lastTargetFixedTime = float.NegativeInfinity;
        }

        // DriveWheel calls this even when this component is disabled. Contact physics
        // stays alive without polling inputs, steering, or running the motor controller.
        internal void UpdatePassiveTraction()
        {
            if (_setupError || _rb == null || _wheels == null || !gameObject.activeInHierarchy) return;
            if (isActiveAndEnabled && BaseGameManager.Instance != null &&
                BaseGameManager.Instance.RobotState == RobotState.Enabled) return;
            if (_lastPassiveFixedTime == Time.fixedTime) return;
            _lastPassiveFixedTime = Time.fixedTime;

            ClearDriveTargets();
            UpdateWheelContacts();
            for (int i = 0; i < _wheels.Length; i++)
                if (_contactPatches[i].Count > 0)
                    ApplyDriveTorque(_wheels[i], _contactPatches[i], 0f, DisabledRollingDrag);
        }

        protected virtual void Update()
        {
            UpdateAudio();
            UpdateActionKeys();
        }

        protected virtual void FixedUpdate()
        {
            if (_setupError) return;
            if (BaseGameManager.Instance == null || BaseGameManager.Instance.RobotState != RobotState.Enabled)
            {
                UpdatePassiveTraction();
                return;
            }

            CheckForTampering();
            RunDrivetrain();
        }

        /// <summary>
        /// Returns the maximum translational velocity of the drivetrain, in meters/second.
        /// </summary>
        public float GetMaxVelocity()
        {
            return maxVelocity.Value;
        }

        /// <summary>
        /// Estimates braking from a calibrated low-speed value using the same
        /// speed-dependent bite as wheel traction. Slip, motor limits and contact
        /// loading can reduce actual braking further; this is a profile estimate.
        /// </summary>
        public float GetBrakingDeceleration(float lowSpeedDeceleration, float speed)
        {
            // Contact grip is friction * (1 + bite), so friction cancels in
            // the ratio to the low-speed grip. Do not multiply by friction twice.
            float bite = Mathf.Max(0f, biteFactor);
            float grip = Mathf.Max(0f, frictionCoefficient)
                * (1f + CalculateEffectiveBite(bite, speed, GetMaxVelocity()));
            float calibrated = Mathf.Max(0f, lowSpeedDeceleration)
                * (1f + CalculateEffectiveBite(bite, speed, GetMaxVelocity())) / (1f + bite);
            // A tuning value cannot create more braking than the contact model permits.
            return Mathf.Min(calibrated, grip * Physics.gravity.magnitude);
        }

        /// <summary>
        /// Returns the current physics step's translation command in world meters/second.
        /// </summary>
        public Vector3 GetTargetWorldVelocity()
        {
            if (!isActiveAndEnabled || _rb == null || _setupError || BaseGameManager.Instance == null ||
                BaseGameManager.Instance.RobotState != RobotState.Enabled)
            {
                return Vector3.zero;
            }

            // Preview current input if drive has not run yet; otherwise reuse its processed target
            // so overrides are retained and brake limiting is not applied a second time.
            ChassisVelocity target = _lastTargetFixedTime == Time.fixedTime
                ? _lastTarget
                : CalculateTargetVelocity(out _);
            // Chassis vx is forward (Unity Z), vy is strafe (Unity X).
            return Quaternion.AngleAxis(_rb.rotation.eulerAngles.y, Vector3.up) *
                   new Vector3(target.vy, 0f, target.vx);
        }

        /// <summary>
        /// Returns the maximum rotational velocity of the drivetrain, in degrees/second.
        /// </summary>
        public float GetMaxOmega()
        {
            return maxOmega > 1e-4f ? maxOmega : (GetMaxVelocity() / _farthestWheel * Mathf.Rad2Deg);
        }

        /// <summary>
        /// Returns the drivetrain architecture being simulated by the DriveController.
        /// </summary>
        public DriveType GetDrivetrainType()
        {
            return drivetrainType;
        }

        /// <summary>
        /// Overrides the robot's movement control.
        /// </summary>
        /// <param name="vx">The desired X (forward/reverse) velocity of the robot in meters/second.</param>
        /// <param name="vy">The desired Y (strafe) velocity of the robot in meters/second. "Z" velocity in Unity's coordinate frame.</param>
        /// <param name="omega">The desired rotational velocity of the robot in degrees/second.</param>
        /// <param name="perspective">The perspective of the specified speeds.</param>
        public void OverrideInput(float vx, float vy, float omega, DrivePerspective perspective)
        {
            var o = new ChassisVelocity();

            if (perspective == DrivePerspective.RobotRelative)
            {
                o.vx = vx;
                o.vy = vy;
            }
            else
            {
                Vector3 input = new Vector3(vx, 0, vy);
                var robotRelative = Quaternion.AngleAxis(-_rb.rotation.eulerAngles.y, Vector3.up) * input;

                o.vx = robotRelative.z;
                o.vy = robotRelative.x;
            }

            o.omega = omega;
            _override = o;
        }

        /// <summary>
        /// Adds additional velocity to the drivetrain's rotational setpoint, while still allowing the player to control x/y movement.
        /// Turn assistance is <i>not</i> ignored when <c>OverrideInput</c> is in use.
        /// </summary>
        /// <param name="omega">The desired rotational velocity of the robot in degrees/second.</param>
        /// <param name="ignorePlayerInput">If the player's turn input should be ignored.</param>
        public void TurnAssist(float omega, bool ignorePlayerInput)
        {
            _turnAssist += omega;
            if (ignorePlayerInput) _turnOverride = true;
        }

        /// <summary>
        /// Sets the multiplier to apply to player driving (x/y) input.
        /// This does not affect <c>OverrideInput</c> if it is in use.
        /// </summary>
        /// <param name="value">A multiplier in the range [0, 1].</param>
        public void SetDriveMp(float value)
        {
            _driveMp = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Sets the multiplier to apply to player turning input.
        /// This does not affect <c>OverrideInput</c> or <c>OverrideTurnInput</c> if they are in use.
        /// </summary>
        /// <param name="value">A multiplier in the range [0, 1].</param>
        public void SetTurnMp(float value)
        {
            _turnMp = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Overrides the brake time player setting.
        /// </summary>
        /// <param name="value">The new brake time, in seconds. Ignored if enable is false.</param>
        /// <param name="enable">If the override should be enabled.</param>
        public void OverrideBrakeTime(float value, bool enable)
        {
            _brakeOverride = enable ? value : null;
        }

        /// <summary>
        /// Overrides brake time for the next drive update only. Call each physics step to sustain it.
        /// Takes precedence over the persistent override; target previews do not consume it.
        /// </summary>
        public void OverrideBrakeTime(float value)
        {
            _brakeOverrideOnce = value;
        }

        /// <summary>
        /// Checks prerequisites for this class to function.
        /// </summary>
        protected void ValidateSetup()
        {
            _setupError = false;

            if (drivetrainParent == null)
            {
                Debug.LogError("Please add a Drivetrain Parent");
                _setupError = true;
            }

            if (drivetrainParent != null && drivetrainParent.transform.Find("Wheels") != null)
            {
                _wheelChild = drivetrainParent.transform.Find("Wheels");
            }
            else
            {
                Debug.LogError("Please add a Wheels Child to the Drivetrain Parent");
                _setupError = true;
            }

            if (_rb == null)
            {
                Debug.LogError($"Rigidbody not found on {gameObject.name}. Adding temporary Rigidbody");
                _rb = gameObject.AddComponent<Rigidbody>();
                _rb.mass = 98f;
                _rb.drag = 1.6f;
                _rb.angularDrag = 0.2f;
                _setupError = true;
            }
        }

        protected void UpdateActionKeys()
        {
            if (_robotBase == null) return;

            var key1 = _robotBase.DriveSpeed1Action;
            var key2 = _robotBase.DriveSpeed2Action;
            var key3 = _robotBase.DriveSpeed3Action;
            if (key1 == null && key2 == null && key3 == null) return;

            var mode = (ActionKeyMode)SettingsAccess.ReadActionKeyMode();

            if (mode == ActionKeyMode.Toggle)
            {
                if (key1 != null && key1.WasPressedThisFrame()) ToggleLatch(0);
                else if (key2 != null && key2.WasPressedThisFrame()) ToggleLatch(1);
                else if (key3 != null && key3.WasPressedThisFrame()) ToggleLatch(2);

                _actionKeyMp = _latchedActionKey < 0 ? 1f : SettingsAccess.ReadDriveActionKeyPercent(_latchedActionKey);
            }
            else
            {
                if (key1 != null && key1.IsPressed()) _actionKeyMp = SettingsAccess.ReadDriveActionKeyPercent(0);
                else if (key2 != null && key2.IsPressed()) _actionKeyMp = SettingsAccess.ReadDriveActionKeyPercent(1);
                else if (key3 != null && key3.IsPressed()) _actionKeyMp = SettingsAccess.ReadDriveActionKeyPercent(2);
                else _actionKeyMp = 1f;

                _latchedActionKey = -1;
            }
        }
        protected void ToggleLatch(int index)
        {
            _latchedActionKey = _latchedActionKey == index ? -1 : index;
        }

        /// <summary>
        /// Checks for irregular chassis velocities.
        /// </summary>
        protected void CheckForTampering()
        {
            if (_robotBase != null)
            {
                _robotBase.MoveSpeedMultiplier = Mathf.Clamp01(_robotBase.MoveSpeedMultiplier);
                _robotBase.RotationSpeedMultiplier = Mathf.Clamp01(_robotBase.RotationSpeedMultiplier);
            }

            var pos = _rb.position;

            // The rigidbody is clamped to exactly maxSpeed, so comparing against it with no headroom
            // flagged every robot that simply reached top speed.
            var violating = _groundSpeed > GetMaxVelocity() * SpeedTolerance ||
                            pos.x < -8.5f || pos.x > 8.5f || pos.z < -4f || pos.z > 4f;

            if (!violating)
            {
                _violationTime = 0f;
                return;
            }

            // Already flagged for this run of frames; nothing left to count.
            if (_violationTime >= ViolationTimeToFlag) return;

            _violationTime += Time.fixedDeltaTime;

            if (_violationTime < ViolationTimeToFlag) return;

            Debug.Log($"[Anticheat] speed {_groundSpeed} (max {GetMaxVelocity()}) at {pos}");
            BaseGameManager.Instance.wasCheated = true;
        }

        /// <summary>
        /// Calculates the input target without advancing drivetrain state.
        /// </summary>
        private ChassisVelocity CalculateTargetVelocity(out float norm)
        {
            var v = new ChassisVelocity();

            // If disabled, override controls to 0. Otherwise, calculate input speeds
            if (BaseGameManager.Instance.RobotState == RobotState.Enabled)
            {
                if (_override != null) // Override active
                {
                    v.vx = _override.Value.vx;
                    v.vy = _override.Value.vy;
                    v.omega = _override.Value.omega;
                }
                else
                {
                    if (_robotBase.IsFieldCentric && drivetrainType != DriveType.Differential) // Field centric calculation
                    {
                        var translationValue = _robotBase.TranslateAction.ReadValue<Vector2>();
                        var driveInput = new Vector3(translationValue.y, 0f, translationValue.x);

                        // Calculate field-relative angle based on camera
                        var yaw = _rb.rotation.eulerAngles.y;
                        if (_robotBase.ThirdPersonCam != null)
                        {
                            yaw -= _robotBase.ThirdPersonCam.CameraObject.transform.eulerAngles.y;
                        }
                        else if (_robotBase.DriverStationCam != null)
                        {
                            yaw += _robotBase.Alliance == Alliance.Blue ? 90f : -90f;
                        }

                        if (_robotBase.ThirdPersonCam != null)
                        {
                            yaw = _rb.rotation.eulerAngles.y + -_robotBase.ThirdPersonCam.CameraObject.transform.eulerAngles.y;
                        } else if (_robotBase.DriverStationCam != null)
                        {
                            yaw = _rb.rotation.eulerAngles.y+ (_robotBase.Alliance == Alliance.Blue ? 90f : -90f);
                        }

                        var robotRelative = Quaternion.AngleAxis(yaw, Vector3.up) * driveInput;

                        v.vx = robotRelative.x;
                        v.vy = robotRelative.z;
                    }
                    else // Robot relative calculation
                    {
                        var translationValue = _robotBase.TranslateAction.ReadValue<Vector2>();
                        if (_robotBase.IsCameraFlipped)
                            translationValue = -translationValue;
                        var driveInput = new Vector3(translationValue.y, 0f, translationValue.x);

                        v.vx = driveInput.x;
                        v.vy = driveInput.z;
                    }

                    // Apply drive multiplier, exponent curve, and units
                    float exp = Mathf.Pow(v.vx * v.vx + v.vy * v.vy, 0.5f * ((_robotBase?.MoveSpeedExponent ?? 2f) - 1f));
                    float effectiveMp = _driveMp * _actionKeyMp * (_robotBase?.MoveSpeedMultiplier ?? 1f) * exp;

                    v.vx *= effectiveMp * GetMaxVelocity();
                    v.vy *= effectiveMp * GetMaxVelocity();

                    // Get rotation speed
                    v.omega = -_robotBase.RotateAction.ReadValue<float>() * (_robotBase?.RotationSpeedMultiplier ?? 1f) * _turnMp * GetMaxOmega();
                }
            }

            // Apply turn assist
            v.omega = !_turnOverride ? v.omega + _turnAssist : _turnAssist;

            // Normalize x/y input if it is out of bounds
            norm = Mathf.Sqrt(v.vx * v.vx + v.vy * v.vy);
            if (norm > GetMaxVelocity())
            {
                float k = GetMaxVelocity() / norm;
                v.vx *= k;
                v.vy *= k;
                norm = GetMaxVelocity();
            }

            // Clamp omega input if it is out of bounds
            v.omega = Mathf.Clamp(v.omega, -GetMaxOmega(), GetMaxOmega());

            // Apply brake ratelimit if necessary
            float brakeTime = _robotBase?.BrakeTime ?? 0f;
            if (_brakeOverrideOnce.HasValue)
            {
                brakeTime = _brakeOverrideOnce.Value;
            }
            else if (_brakeOverride.HasValue)
            {
                brakeTime = _brakeOverride.Value;
            }

            if (brakeTime > 1e-4f && _override == null)
            {
                float dx = v.vx - _lastTarget.vx;
                float dy = v.vy - _lastTarget.vy;
                float delta_2 = dx * dx + dy * dy;
                float a_brake = GetMaxVelocity() / brakeTime * Time.fixedDeltaTime;
                if (delta_2 > (a_brake * a_brake))
                {
                    float k = a_brake / Mathf.Sqrt(delta_2);
                    v.vx = _lastTarget.vx + (k * dx);
                    v.vy = _lastTarget.vy + (k * dy);

                    norm = Mathf.Sqrt(v.vx * v.vx + v.vy * v.vy);
                }
            }

            return v;
        }

        /// <summary>
        /// Updates the drivetrain based on inputs.
        /// </summary>
        protected void RunDrivetrain()
        {
            #if UNITY_EDITOR
            _debugNetGroundForce = Vector3.zero;
            _debugGroundForceSum = 0f;
            _debugGroundedWheels = 0;
            #endif
            _rb.maxLinearVelocity = GetMaxVelocity();
            ChassisVelocity v = CalculateTargetVelocity(out float norm);

            // Apply input to the drivetrain
            switch (drivetrainType)
            {
                case DriveType.Swerve:
                    DriveSwerve(v.vx, v.vy, v.omega);
                    break;
                case DriveType.Differential:
                    float omega = Mathf.Clamp(v.vy / GetMaxVelocity() + v.omega / GetMaxOmega(), -1f, 1f) * GetMaxOmega();
                    DriveDifferential(v.vx, omega);
                    break;
                case DriveType.Mecanum:
                    DriveMecanum(v.vx, v.vy, v.omega);
                    break;
            }

            // Save current speeds
            _groundSpeed = Mathf.Sqrt(_rb.velocity.x * _rb.velocity.x + _rb.velocity.z * _rb.velocity.z);
            _lastTarget = v;
            _lastTargetFixedTime = Time.fixedTime;
            _lastNorm = norm;

            // Reset inputs
            _override = null;
            _brakeOverrideOnce = null;
            _turnAssist = 0f;
            _turnOverride = false;

            // Debug
            #if UNITY_EDITOR
            if (debugWheelTorque)
            {
                DebugPlotter.Add("drive_force_net", _debugNetGroundForce.magnitude);
                DebugPlotter.Add("drive_force_sum", _debugGroundForceSum);
                DebugPlotter.Add("drive_grounded_wheels", _debugGroundedWheels);
            }
            if (debugChassisPosition)
            {
                DebugPlotter.Add("x", _rb.position.x);
                DebugPlotter.Add("y", _rb.position.z);
                DebugPlotter.Add("theta", _rb.rotation.eulerAngles.y);
            }

            if (debugChassisVelocity)
            {
                DebugPlotter.Add("v", _groundSpeed);
                DebugPlotter.Add("vx", _rb.velocity.x);
                DebugPlotter.Add("vy", _rb.velocity.z);
                DebugPlotter.Add("omega", _rb.angularVelocity.y * Mathf.Rad2Deg);
                DebugPlotter.Add("v_ref", norm);
                DebugPlotter.Add("vx_ref", v.vx);
                DebugPlotter.Add("vy_ref", v.vy);
                DebugPlotter.Add("omega_ref", v.omega);
            }
            #endif
        }

        /// <summary>
        /// Generates setpoints for a swerve drivetrain and applies them to the wheel colliders.
        /// </summary>
        /// <param name="vx">The desired X (forward/reverse) velocity in meters/second.</param>
        /// <param name="vy">The desired Y (strafe) velocity in meters/second. "Z" velocity in Unity's coordinate frame.</param>
        /// <param name="omega">The desired rotational velocity in degrees/second.</param>
        protected void DriveSwerve(float vx, float vy, float omega)
        {
            float omega_rad = omega * Mathf.Deg2Rad;

            // Calculate wheel setpoints
            var setpoints = GetWheelSetpoints();
            for (int i = 0; i < _wheels.Length; i++)
            {
                Vector3 pos = _wheels[i].transform.localPosition;
                float vx_m = vx - omega_rad * pos.x;
                float vy_m = vy + omega_rad * pos.z;

                float velocity = Mathf.Sqrt(vx_m * vx_m + vy_m * vy_m);

                if (velocity > 1e-4f)
                {
                    float angle = Mathf.Atan2(vy_m, vx_m) * Mathf.Rad2Deg;

                    // Optimize setpoint
                    float modulo = (_wheels[i].transform.localRotation.eulerAngles.y - angle + 360f) % 360;
                    if (modulo > 90f && modulo < 270f)
                    {
                        velocity *= -1f;
                        angle = (angle + 180f) % 360f;
                    }

                    setpoints[i].velocity = velocity;
                    setpoints[i].angle = angle;
                }
                else
                {
                    setpoints[i].velocity = 0f;
                    // Stop driving without changing the module's braking direction.
                    setpoints[i].angle = _wheels[i].transform.localRotation.eulerAngles.y;
                }
            }

            // Desaturate wheel speeds, if necessary
            float v_peak = 0f;
            foreach (var setpoint in setpoints) v_peak = Mathf.Max(v_peak, Mathf.Abs(setpoint.velocity));
            if (v_peak > GetMaxVelocity())
            {
                float k_t = Mathf.Sqrt(vx * vx + vy * vy) / GetMaxVelocity();
                float k_omega = Mathf.Abs(omega) / GetMaxOmega();
                float k = Mathf.Min(Mathf.Max(k_t, k_omega) * GetMaxVelocity() / v_peak, 1f);
                for (int i = 0; i < setpoints.Length; i++) setpoints[i].velocity *= k;
            }

            // Orient the tread before sampling its contact patch.
            for (int i = 0; i < _wheels.Length; i++)
                _wheels[i].transform.localEulerAngles = new Vector3(0f, setpoints[i].angle, 0f);
            UpdateWheelContacts();
            for (int i = 0; i < _wheels.Length; i++)
            {
                var wheel = _wheels[i];
                if (_contactPatches[i].Active)
                {
                    var setpoint = setpoints[i];
            
                    wheel.transform.localEulerAngles = new Vector3(0f, setpoint.angle, 0f);

                    float s_v = GetDrivenWheelSpeed(wheel, _contactPatches[i]);
                    float tau = motor.Velocity(setpoint.velocity * _rotationsPerMeter, s_v * _rotationsPerMeter) * motorsPerPowertrain;

                    #if UNITY_EDITOR
                    if (debugWheelVelocity)
                    {
                        DebugPlotter.Add($"wheel{i}_ref", setpoint.velocity);
                        DebugPlotter.Add($"wheel{i}_vel", s_v);
                    }

                    if (debugWheelTorque)
                    {
                        DebugPlotter.Add($"wheel{i}_tau", tau);
                    }
                    #endif

                    ApplyDriveTorque(wheel, _contactPatches[i], tau, targetSpeed: setpoint.velocity);

                    wheel.WheelAngle = wheel.transform.localRotation.eulerAngles.y;

                }
            }
        }

        /// <summary>
        /// Generates setpoints for a differential drivetrain and applies them to the wheel colliders.
        /// </summary>
        /// <param name="vx">The desired X (forward/reverse) velocity in meters/second.</param>
        /// <param name="omega">The desired rotational velocity in degrees/second</param>
        protected void DriveDifferential(float vx, float omega)
        {
            // Find wheel position extremes
            float r_b = 0f;

            for (int i = 0; i < _wheels.Length; i++)
            {
                Vector3 pos = _wheels[i].transform.localPosition;
                r_b = Mathf.Max(r_b, Mathf.Abs(pos.x));
                _wheels[i].transform.localEulerAngles = Vector3.zero;
            }
            UpdateWheelContacts();

            // Calculate differential velocities
            float omega_rad = omega * Mathf.Deg2Rad;
            float v_l = Mathf.Clamp(vx + omega_rad * r_b, -GetMaxVelocity(), GetMaxVelocity());
            float v_r = Mathf.Clamp(vx - omega_rad * r_b, -GetMaxVelocity(), GetMaxVelocity());

            // Average only contacting wheels, relative to the surfaces on each side.
            float s_vl = 0f, s_vr = 0f;
            int count_l = 0, count_r = 0;
            for (int i = 0; i < _wheels.Length; i++)
            {
                if (!_contactPatches[i].Active) continue;
                float speed = GetDrivenWheelSpeed(_wheels[i], _contactPatches[i]);
                if (_wheels[i].transform.localPosition.x < 0f) { s_vl += speed; count_l++; }
                else { s_vr += speed; count_r++; }
            }
            s_vl /= Mathf.Max(1, count_l);
            s_vr /= Mathf.Max(1, count_r);

            // Calculate torque to apply per wheel
            float motorMp = motorsPerPowertrain / (_wheels.Length / 2f);
            float tau_l = motor.Velocity(v_l * _rotationsPerMeter, s_vl * _rotationsPerMeter) * motorMp;
            float tau_r = motor.Velocity(v_r * _rotationsPerMeter, s_vr * _rotationsPerMeter) * motorMp;

            // Debug
            #if UNITY_EDITOR
            if (debugWheelVelocity)
            {
                DebugPlotter.Add("wheelL_ref", v_l);
                DebugPlotter.Add("wheelR_ref", v_r);
                DebugPlotter.Add("wheelL_vel", s_vl);
                DebugPlotter.Add("wheelR_vel", s_vr);
            }

            if (debugWheelTorque)
            {
                DebugPlotter.Add($"wheelL_tau", tau_l);
                DebugPlotter.Add($"wheelR_tau", tau_r);
            }
            #endif

            // Iterate through wheels and apply setpoints
            for (int i = 0; i < _wheels.Length; i++)
            {
                var wheel = _wheels[i];
                if (_contactPatches[i].Active)
                {
                    wheel.transform.localEulerAngles = new Vector3();

                    float tau = wheel.transform.localPosition.x < 0f ? tau_l : tau_r;

                    ApplyDriveTorque(wheel, _contactPatches[i], tau,
                        targetSpeed: wheel.transform.localPosition.x < 0f ? v_l : v_r);

                    wheel.WheelAngle = wheel.transform.localRotation.eulerAngles.y;

                }
            }
        }

        /// <summary>
        /// Generates setpoints for a mecanum drivetrain and applies them to the wheel colliders.
        /// </summary>
        /// <param name="vx">The desired X (forward/reverse) velocity in meters/second.</param>
        /// <param name="vy">The desired Y (strafe) velocity in meters/second. "Z" velocity in Unity's coordinate frame.</param>
        /// <param name="omega">The desired rotational velocity in degrees/second.</param>
        protected void DriveMecanum(float vx, float vy, float omega)
        {
            float omega_rad = omega * Mathf.Deg2Rad;

            // Calculate wheel setpoints
            var setpoints = GetWheelSetpoints();
            for (int i = 0; i < _wheels.Length; i++)
            {
                Vector3 pos = _wheels[i].transform.localPosition;
                if (pos.x < 0f ^ pos.z < 0f)
                {
                    setpoints[i].velocity = MEC_MP * (vx + vy + omega_rad * (pos.z - pos.x));
                    setpoints[i].angle = 45f;
                }
                else
                {
                    setpoints[i].velocity = MEC_MP * (vx - vy - omega_rad * (pos.z + pos.x));
                    setpoints[i].angle = 315f;
                }

                setpoints[i].velocity = Mathf.Clamp(setpoints[i].velocity, -GetMaxVelocity(), GetMaxVelocity());
            }

            // Mecanum's forward vector represents the driven roller direction.
            for (int i = 0; i < _wheels.Length; i++)
                _wheels[i].transform.localEulerAngles = new Vector3(0f, setpoints[i].angle, 0f);
            UpdateWheelContacts();
            for (int i = 0; i < _wheels.Length; i++)
            {
                var wheel = _wheels[i];
                if (_contactPatches[i].Active)
                {
                    var setpoint = setpoints[i];
            
                    wheel.transform.localEulerAngles = new Vector3(0f, setpoint.angle, 0f);

                    float s_v = GetDrivenWheelSpeed(wheel, _contactPatches[i]);
                    float tau = motor.Velocity(setpoint.velocity * _rotationsPerMeter, s_v * _rotationsPerMeter) * motorsPerPowertrain;

                    #if UNITY_EDITOR
                    if (debugWheelVelocity)
                    {
                        DebugPlotter.Add($"wheel{i}_ref", setpoint.velocity);
                        DebugPlotter.Add($"wheel{i}_vel", s_v);
                    }

                    if (debugWheelTorque)
                    {
                        DebugPlotter.Add($"wheel{i}_tau", tau);
                    }
                    #endif

                    ApplyDriveTorque(wheel, _contactPatches[i], tau, targetSpeed: setpoint.velocity);

                    wheel.WheelAngle = wheel.transform.localRotation.eulerAngles.y;

                }
            }
        }

        private WheelSetpoint[] GetWheelSetpoints()
        {
            if (_wheelSetpoints == null || _wheelSetpoints.Length != _wheels.Length)
                _wheelSetpoints = new WheelSetpoint[_wheels.Length];
            return _wheelSetpoints;
        }

        // motorTorque is the combined motor output allocated to this wheel.
        // Legacy motor torque includes fixed mass compensation. An explicit real-world mass
        // replaces it with the ratio of the whole simulated robot's mass to its real mass.
        private void ApplyDriveTorque(DriveWheel wheel, WheelContactPatch patch, float motorTorque,
            float rollingDrag = 0f, float? targetSpeed = null)
        {
            float efficiency = 1f - Mathf.Clamp01(mechanicalLossPercent / 100f);
            float wheelTorque = motorTorque * gearRatio * efficiency * _motorForceScale;
            float radius = Mathf.Max(0.0001f, _wheelDiameter * 0.5f);
            float groundForce = wheelTorque / radius;
            float surfaceSpeed = GetDrivenWheelSpeed(wheel, patch);
            float effectiveBite = CalculateEffectiveBite(biteFactor, surfaceSpeed, GetMaxVelocity());
            // I/r^2 is the wheel's equivalent translating mass. Scale inertia into the
            // same Unity force units as motor torque when real-world weight is supplied.
            float inertiaScale = realWorldWeightLbs > 0f
                ? _tractionMass / (realWorldWeightLbs * 0.45359237f)
                : MotorConstants.SimulationTorqueScale;
            float wheelMass = Mathf.Max(0.0001f, wheelRotationalInertia) / (radius * radius) * inertiaScale;
            float dt = Mathf.Max(0.0001f, Time.fixedDeltaTime);
            float forwardForceSum = 0f;
            float rollingResistanceSum = 0f;
            Vector3 netForce = Vector3.zero;
            float forceSum = 0f;
            for (int i = 0; i < patch.Count; i++)
            {
                WheelContact hit = patch.Hits[i];
                Vector3 relativeVelocity = GetContactVelocity(hit);
                Vector3 forward = Vector3.Cross(wheel.transform.right, hit.normal).normalized;
                Vector3 sideways = Vector3.Cross(hit.normal, forward).normalized;
                float forwardSpeed = Vector3.Dot(relativeVelocity, forward);
                float sidewaysSpeed = Vector3.Dot(relativeVelocity, sideways);
                bool freeSideways = drivetrainType == DriveType.Mecanum;
                float contactBite = CalculateSlipBite(effectiveBite, surfaceSpeed, forwardSpeed,
                    sidewaysSpeed, freeSideways);
                // Share both motor output and load among contacts. More probes do not create more grip.
                float normalLoad = hit.normalLoad;
                float contactMass = _tractionMass / (_groundedWheelCount * patch.Count);
                if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                    contactMass = 1f / (1f / contactMass + 1f / hit.rigidbody.mass);
                float contactMotorForce = groundForce / patch.Count;
                if (rollingDrag > 0f)
                {
                    float resistance = forwardSpeed * contactMass * rollingDrag;
                    contactMotorForce -= resistance;
                    rollingResistanceSum += resistance;
                }
                float contactDriveForce = CalculateRollingForce(contactMotorForce,
                    surfaceSpeed, forwardSpeed, wheelMass / patch.Count, contactMass, dt);
                Vector3 force = CalculateContactForce(wheel.transform.right, hit.normal, relativeVelocity,
                    contactDriveForce, normalLoad, frictionCoefficient, contactBite, contactMass,
                    Mathf.Max(Time.fixedDeltaTime, lateralGripResponse), freeSideways);
                _rb.AddForceAtPosition(force, hit.point, ForceMode.Force);
                if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                    hit.rigidbody.AddForceAtPosition(-force, hit.point, ForceMode.Force);
                netForce += force;
                forceSum += force.magnitude;
                // The ground's reaction torque slows a spinning wheel or spins up a
                // freely rolling wheel. Excess motor torque becomes wheelspin, not grip.
                forwardForceSum += Vector3.Dot(force, forward);
            }
            patch.SurfaceSpeed = AdvanceWheelSpeed(surfaceSpeed, groundForce - rollingResistanceSum, forwardForceSum,
                wheelMass, dt, targetSpeed);
            #if UNITY_EDITOR
            if (debugWheelTorque)
            {
                DebugPlotter.Add($"{wheel.name}_wheel_tau", wheelTorque);
                DebugPlotter.Add($"{wheel.name}_ground_force", netForce.magnitude);
                DebugPlotter.Add($"{wheel.name}_normal_load", patch.NormalLoad);
                DebugPlotter.Add($"{wheel.name}_contacts", patch.Count);
                DebugPlotter.Add($"{wheel.name}_tread_speed", patch.SurfaceSpeed);
                _debugNetGroundForce += netForce;
                _debugGroundForceSum += forceSum;
                if (patch.Count > 0) _debugGroundedWheels++;
            }
            #endif
        }

        private static float CalculateEffectiveBite(float bite, float wheelGroundSpeed, float topSpeed)
        {
            // Fixed decay curve: full bite at rest, ~22% at half speed, ~5% at top speed.
            // Use actual contact speed rather than the command, including reverse and turns.
            float speedRatio = Mathf.Abs(wheelGroundSpeed) / Mathf.Max(0.0001f, Mathf.Abs(topSpeed));
            return Mathf.Max(0f, bite) * Mathf.Exp(-3f * speedRatio);
        }

        private static float CalculateSlipBite(float bite, float surfaceSpeed, float forwardSpeed,
            float sidewaysSpeed, bool freeSideways)
        {
            // Normalize contact slip by rolling speed, with a 0.5 m/s floor so tiny
            // velocities near rest cannot instantly remove all bite. Mecanum's free
            // roller motion is not tread slip and must not reduce driven-direction bite.
            float referenceSpeed = Mathf.Max(0.5f, Mathf.Max(Mathf.Abs(surfaceSpeed), Mathf.Abs(forwardSpeed)));
            float forwardSlip = surfaceSpeed - forwardSpeed;
            float sideSlip = freeSideways ? 0f : sidewaysSpeed;
            float slip = Mathf.Sqrt(forwardSlip * forwardSlip + sideSlip * sideSlip) / referenceSpeed;
            return Mathf.Max(0f, bite) * (1f - Mathf.Clamp01(slip));
        }

        private static float CalculateRollingForce(float motorForce, float surfaceSpeed, float groundSpeed,
            float wheelMass, float contactMass, float dt)
        {
            // Solve for the force that makes wheel and chassis velocities match next
            // step. Static rolling transfers torque without inventing wheelspin.
            return (motorForce + (surfaceSpeed - groundSpeed) * wheelMass / dt)
                / (1f + wheelMass / contactMass);
        }

        private static float AdvanceWheelSpeed(float surfaceSpeed, float motorForce, float groundForce,
            float wheelMass, float dt, float? targetSpeed)
        {
            float nextSpeed = surfaceSpeed + (motorForce - groundForce) * dt / wheelMass;
            // Model the velocity controller stopping motor-driven acceleration at its
            // target instead of letting a small wheel inertia overshoot in one fixed step.
            // External ground forces are still allowed to backdrive the wheel.
            if (targetSpeed.HasValue && (motorForce - groundForce) * motorForce > 0f)
            {
                float remaining = targetSpeed.Value - surfaceSpeed;
                if (remaining * motorForce >= 0f && (nextSpeed - targetSpeed.Value) * motorForce > 0f)
                    nextSpeed = targetSpeed.Value;
            }
            return nextSpeed;
        }

        private static Vector3 CalculateContactForce(Vector3 wheelAxle, Vector3 normal, Vector3 relativeVelocity,
            float driveForce, float normalLoad, float friction, float bite, float contactMass,
            float responseTime, bool freeSideways)
        {
            // A wheel drives along its rim's tangent, including up an obstacle face.
            Vector3 forward = Vector3.Cross(wheelAxle, normal).normalized;
            if (forward.sqrMagnitude < 0.5f) return Vector3.zero;
            Vector3 sideways = Vector3.Cross(normal, forward).normalized;
            // Mecanum rollers remain free to slip perpendicular to their driven direction.
            float sidewaysForce = freeSideways ? 0f : -Vector3.Dot(relativeVelocity, sideways)
                * contactMass / Mathf.Max(0.0001f, responseTime);
            float coefficient = Mathf.Max(0f, friction) * (1f + Mathf.Max(0f, bite));
            return Vector3.ClampMagnitude(forward * driveForce + sideways * sidewaysForce,
                coefficient * Mathf.Max(0f, normalLoad));
        }

        private int _groundedWheelCount;

        private sealed class WheelContactPatch
        {
            public readonly WheelContact[] Hits = new WheelContact[73];
            public int Count;
            public int FloorCount;
            public float NormalLoad;
            public bool Active;
            public bool SpeedInitialized;
            public float SurfaceSpeed;
            public Collider[] PhysicalColliders;
        }

        private struct WheelContact
        {
            public Collider collider;
            public Vector3 point;
            public Vector3 normal;
            public float depth;
            public float normalLoad;
            public bool physicalSupport;
            public Rigidbody rigidbody;
        }

        private Vector3 GetContactVelocity(WheelContact hit)
        {
            Vector3 velocity = _contactLinearVelocity
                + Vector3.Cross(_contactAngularVelocity, hit.point - _contactCenterOfMass);
            if (hit.rigidbody != null) velocity -= hit.rigidbody.GetPointVelocity(hit.point);
            return velocity;
        }

        private float GetDrivenWheelSpeed(DriveWheel wheel, WheelContactPatch patch)
        {
            if (!patch.SpeedInitialized)
            {
                patch.SurfaceSpeed = GetWheelGroundSpeed(wheel, patch);
                patch.SpeedInitialized = true;
            }
            return patch.SurfaceSpeed;
        }

        private float GetWheelGroundSpeed(DriveWheel wheel, WheelContactPatch patch)
        {
            float speed = 0f;
            for (int i = 0; i < patch.Count; i++)
            {
                WheelContact hit = patch.Hits[i];
                Vector3 forward = Vector3.Cross(wheel.transform.right, hit.normal).normalized;
                speed += Vector3.Dot(GetContactVelocity(hit), forward);
            }
            return speed / Mathf.Max(1, patch.Count);
        }

        private static float CalculateMotorForceScale(float simulationMass, float realWeightLbs)
        {
            const float kilogramsPerPound = 0.45359237f;
            return realWeightLbs > 0f
                ? simulationMass / (realWeightLbs * kilogramsPerPound) / MotorConstants.SimulationTorqueScale
                : 1f;
        }

        /// <summary>
        /// Rebuilds the cached robot body list. Call after adding, removing or reparenting
        /// rigidbodies, wheel colliders or nested DriveControllers at runtime. Call after
        /// moving solid colliders into a wheel's vicinity. Active state, kinematic state
        /// and mass changes on existing bodies do not require a refresh.
        /// </summary>
        public void RefreshRobotRigidbodies()
        {
            _robotContactColliders = null;
            if (_contactPatches != null)
                foreach (WheelContactPatch patch in _contactPatches) patch.PhysicalColliders = null;
            GetComponentsInChildren(true, _robotRigidbodies);
            for (int i = _robotRigidbodies.Count - 1; i >= 0; i--)
            {
                if (_robotRigidbodies[i].GetComponentInParent<DriveController>(true) != this)
                    _robotRigidbodies.RemoveAt(i);
            }
        }

        private void UpdateTractionMass()
        {
            _tractionMass = _rb.mass;
            if (realWorldWeightLbs > 0f)
            {
                // Membership is cached at startup. Read current values without hierarchy
                // searches so mass and active/kinematic changes still take effect.
                float totalMass = 0f;
                foreach (Rigidbody body in _robotRigidbodies)
                {
                    if (body != null && body.gameObject.activeInHierarchy && !body.isKinematic)
                        totalMass += body.mass;
                }
                _tractionMass = Mathf.Max(_rb.mass, totalMass);
            }
            _motorForceScale = CalculateMotorForceScale(_tractionMass, realWorldWeightLbs);
        }

        private void UpdateWheelContacts()
        {
            UpdateTractionMass();
            // All contacts in this update see the same pre-simulation chassis motion.
            _contactLinearVelocity = _rb.velocity;
            _contactAngularVelocity = _rb.angularVelocity;
            _contactCenterOfMass = _rb.worldCenterOfMass;
            _groundedWheelCount = 0;
            int floorWheelCount = 0;
            PhysicsScene physics = gameObject.scene.GetPhysicsScene();
            for (int i = 0; i < _wheels.Length; i++)
            {
                DriveWheel wheel = _wheels[i];
                WheelContactPatch patch = _contactPatches[i];
                patch.Count = 0;
                patch.FloorCount = 0;
                patch.NormalLoad = 0f;
                WheelCollider collider = wheel.Collider;
                bool active = wheel.isActiveAndEnabled && collider != null && collider.enabled;
                patch.Active = active;
                wheel.SetCustomTraction(active);
                if (!active)
                {
                    patch.SpeedInitialized = false;
                    continue;
                }
                collider.GetWorldPose(out Vector3 center, out _);
                float radius = collider.radius * Mathf.Abs(wheel.transform.lossyScale.y);
                float halfWidth = Mathf.Max(0.01f, contactPatchWidthInches) * 0.0254f * 0.5f;
                Vector3 up = wheel.transform.up;
                float tolerance = Mathf.Max(0f, contactTolerance);
                bool nativeContact = collider.GetGroundHit(out WheelHit groundHit);
                bool nativeSupport = nativeContact && groundHit.force > 0f;
                // Physical wheel/chassis colliders can carry the robot instead of the
                // WheelCollider suspension. Carpet sampling must work in either case.
                {
                    for (int row = -1; row <= 1; row++)
                    {
                        float along = row * radius * 0.5f;
                        float treadDepth = Mathf.Sqrt(Mathf.Max(0f, radius * radius - along * along));
                        for (int column = -1; column <= 1; column++)
                        {
                            Vector3 origin = center + wheel.transform.forward * along
                                + wheel.transform.right * (column * halfWidth) + up * tolerance;
                            int count = physics.Raycast(origin, -up, _contactQuery,
                                treadDepth + 2f * tolerance, ~_fieldLayerMask, QueryTriggerInteraction.Ignore);
                            float nearest = float.PositiveInfinity;
                            int best = -1;
                            for (int j = 0; j < count; j++)
                            {
                                RaycastHit hit = _contactQuery[j];
                                if (hit.rigidbody == _rb || Vector3.Dot(hit.normal, up) <= 0.1f) continue;
                                if (hit.distance < nearest) { nearest = hit.distance; best = j; }
                            }
                            if (best >= 0)
                            {
                                RaycastHit hit = _contactQuery[best];
                                patch.Hits[patch.Count++] = new WheelContact { collider = hit.collider, rigidbody = hit.rigidbody, point = hit.point, normal = hit.normal };
                            }
                        }
                    }
                    if (patch.Count == 0 && nativeSupport)
                        patch.Hits[patch.Count++] = new WheelContact { collider = groundHit.collider, rigidbody = groundHit.collider.attachedRigidbody, point = groundHit.point, normal = groundHit.normal };
                    patch.FloorCount = patch.Count;
                    patch.NormalLoad = nativeSupport ? groundHit.force : 0f;
                    if (patch.FloorCount > 0) floorWheelCount++;
                }
                AddObstacleContacts(physics, wheel, center, radius, halfWidth, patch, nativeContact, groundHit);
                if (patch.Count > 0) _groundedWheelCount++;
            }
            IsTouchingGround = false;
            if (_groundedWheelCount == 0) return;
            float dt = Mathf.Max(0.0001f, Time.fixedDeltaTime);
            float weightPerFloorWheel = floorWheelCount > 0
                ? _tractionMass * Physics.gravity.magnitude / floorWheelCount : 0f;
            Vector3 gravityUp = -Physics.gravity.normalized;
            for (int i = 0; i < _wheels.Length; i++)
            {
                WheelContactPatch patch = _contactPatches[i];
                if (patch.FloorCount > 0)
                {
                    if (patch.NormalLoad <= 0f)
                    {
                        // Preserve legacy tread grip when solid colliders, rather than
                        // native suspension, support the robot's weight. Obstacle-only
                        // contacts must not dilute this floor load or receive this fallback.
                        float support = 0f;
                        for (int j = 0; j < patch.FloorCount; j++)
                            support += Mathf.Max(0f, Vector3.Dot(patch.Hits[j].normal, gravityUp));
                        patch.NormalLoad = weightPerFloorWheel * support / patch.FloorCount;
                    }
                    for (int j = 0; j < patch.FloorCount; j++)
                        patch.Hits[j].normalLoad = patch.NormalLoad / patch.FloorCount;
                }
                int obstacleCount = patch.Count - patch.FloorCount;
                for (int j = patch.FloorCount; j < patch.Count; j++)
                {
                    WheelContact hit = patch.Hits[j];
                    Rigidbody ground = hit.rigidbody;
                    float inverseMass = GetInverseContactMass(_rb, _tractionMass, hit.point, hit.normal);
                    if (ground != null && !ground.isKinematic)
                        inverseMass += GetInverseContactMass(ground, ground.mass, hit.point, hit.normal);
                    float contactMass = 1f / (inverseMass * _groundedWheelCount * obstacleCount);
                    Vector3 acceleration = _rb.useGravity ? Physics.gravity : Vector3.zero;
                    if (ground != null && !ground.isKinematic && ground.useGravity) acceleration -= Physics.gravity;
                    hit.normalLoad = CalculateObstacleLoad(Vector3.Dot(GetContactVelocity(hit), hit.normal),
                        Vector3.Dot(acceleration, hit.normal), hit.depth, contactMass, dt);
                    patch.Hits[j] = hit;
                    patch.NormalLoad += hit.normalLoad;
                    Vector3 support = hit.normal * hit.normalLoad;
                    // Solid wheel colliders already receive a collision impulse from
                    // PhysX. Applying the query's reaction too would kick the chassis.
                    if (!hit.physicalSupport)
                    {
                        _rb.AddForceAtPosition(support, hit.point, ForceMode.Force);
                        if (ground != null && !ground.isKinematic)
                            ground.AddForceAtPosition(-support, hit.point, ForceMode.Force);
                    }
                }
                if (patch.NormalLoad > 0f) IsTouchingGround = true;
            }
        }

        private void AddObstacleContacts(PhysicsScene physics, DriveWheel wheel, Vector3 center,
            float radius, float halfWidth, WheelContactPatch patch, bool nativeContact, WheelHit groundHit)
        {
            const float skin = 0.0005f;
            EnsureWheelContactShape(radius + skin, halfWidth + skin);
            Quaternion rotation = wheel.transform.rotation;
            int count = physics.OverlapBox(center, new Vector3(halfWidth + skin, radius + skin, radius + skin),
                _obstacleQuery, rotation, ~_fieldLayerMask, QueryTriggerInteraction.Ignore);
            for (int j = 0; j < count && patch.Count < patch.Hits.Length; j++)
            {
                Collider other = _obstacleQuery[j];
                if (other == null || other.attachedRigidbody == _rb || other.transform.IsChildOf(transform)
                    || (_fieldLayerMask & (1 << other.gameObject.layer)) != 0
                    || Physics.GetIgnoreLayerCollision(wheel.gameObject.layer, other.gameObject.layer)
                    || Physics.GetIgnoreCollision(wheel.Collider, other)) continue;
                // A box face covering the full wheel footprint cannot also present an
                // edge here. Avoid convex penetration tests against ordinary carpet.
                if (other is BoxCollider floorBox)
                {
                    bool interiorFloor = false;
                    for (int floor = 0; floor < patch.FloorCount; floor++)
                    {
                        if (patch.Hits[floor].collider != other) continue;
                        interiorFloor = IsInteriorFloorBox(floorBox, center, wheel.transform.right,
                            radius + skin, halfWidth + skin, patch.Hits[floor].normal);
                        break;
                    }
                    if (interiorFloor) continue;
                }
                if (!Physics.ComputePenetration(_wheelContactShape, center, rotation,
                    other, other.transform.position, other.transform.rotation, out Vector3 normal, out float depth)) continue;
                // Do not apply a second support force where the native suspension is
                // already supporting this same face. Other faces of a step still count.
                // A pitched chassis must not turn ordinary carpet into an obstacle.
                // Native suspension/solid colliders already provide that floor support.
                if (Vector3.Dot(normal, -Physics.gravity.normalized) > 0.98f
                    || (nativeContact && other == groundHit.collider && Vector3.Dot(normal, groundHit.normal) > 0.98f)) continue;
                bool sampledFloor = false;
                for (int floor = 0; floor < patch.FloorCount; floor++)
                    if (patch.Hits[floor].collider == other && Vector3.Dot(normal, patch.Hits[floor].normal) > 0.98f)
                    { sampledFloor = true; break; }
                if (sampledFloor) continue;
                Vector3 axle = wheel.transform.right;
                Vector3 radial = Vector3.ProjectOnPlane(normal, axle).normalized;
                float axial = Vector3.Dot(normal, axle);
                Vector3 point = center - radial * radius;
                if (Mathf.Abs(axial) > 0.001f) point -= axle * (Mathf.Sign(axial) * halfWidth);
                bool physicalSupport = HasPhysicalWheelSupport(point,
                    GetPhysicalWheelColliders(patch, center, radius + halfWidth));
                patch.Hits[patch.Count++] = new WheelContact
                {
                    collider = other, rigidbody = other.attachedRigidbody,
                    point = point, normal = normal, physicalSupport = physicalSupport,
                    depth = physicalSupport ? 0f : Mathf.Max(0f, depth - skin)
                };
            }
        }

        private Collider[] GetPhysicalWheelColliders(WheelContactPatch patch, Vector3 center, float reach)
        {
            if (patch.PhysicalColliders != null) return patch.PhysicalColliders;
            if (_robotContactColliders == null) _robotContactColliders = GetComponentsInChildren<Collider>(true);
            var nearby = new List<Collider>();
            float reachSquared = reach * reach;
            foreach (Collider solid in _robotContactColliders)
            {
                if (solid == null || solid is WheelCollider || solid.attachedRigidbody != _rb) continue;
                if (!(solid is BoxCollider) && !(solid is SphereCollider) && !(solid is CapsuleCollider)
                    && !(solid is MeshCollider mesh && mesh.convex)) continue;
                if (solid.bounds.SqrDistance(center) <= reachSquared
                    || (solid.transform.position - center).sqrMagnitude <= reachSquared) nearby.Add(solid);
            }
            return patch.PhysicalColliders = nearby.ToArray();
        }

        private bool HasPhysicalWheelSupport(Vector3 point, Collider[] colliders)
        {
            // Existing robots offset their solid wheel meshes from the WheelCollider
            // tread probes. Use the same small tolerance when assigning support ownership.
            float surfaceTolerance = Mathf.Clamp(contactTolerance, 0.002f, 0.01f);
            float toleranceSquared = surfaceTolerance * surfaceTolerance;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider solid = colliders[i];
                if (solid == null || !solid.enabled || !solid.gameObject.activeInHierarchy
                    || solid.isTrigger || solid.attachedRigidbody != _rb) continue;
                if (solid.bounds.SqrDistance(point) <= toleranceSquared
                    && (solid.ClosestPoint(point) - point).sqrMagnitude <= toleranceSquared) return true;
            }
            return false;
        }

        private static bool IsInteriorFloorBox(BoxCollider box, Vector3 center, Vector3 axle,
            float radius, float halfWidth, Vector3 contactNormal)
        {
            Transform frame = box.transform;
            Vector3 up = frame.up;
            if (Vector3.Dot(up, -Physics.gravity.normalized) < 0.999f
                || Vector3.Dot(contactNormal, up) < 0.999f) return false;
            Vector3 local = frame.InverseTransformPoint(center) - box.center;
            if (local.y < 0f) return false;
            Vector3 scale = frame.lossyScale;
            float alongX = Vector3.Dot(frame.right, axle);
            float alongZ = Vector3.Dot(frame.forward, axle);
            float extentX = (halfWidth * Mathf.Abs(alongX)
                + radius * Mathf.Sqrt(Mathf.Max(0f, 1f - alongX * alongX))) / Mathf.Max(0.0001f, Mathf.Abs(scale.x));
            float extentZ = (halfWidth * Mathf.Abs(alongZ)
                + radius * Mathf.Sqrt(Mathf.Max(0f, 1f - alongZ * alongZ))) / Mathf.Max(0.0001f, Mathf.Abs(scale.z));
            return Mathf.Abs(local.x) + extentX < box.size.x * 0.5f
                && Mathf.Abs(local.z) + extentZ < box.size.z * 0.5f;
        }

        private void EnsureWheelContactShape(float radius, float halfWidth)
        {
            if (_wheelContactShape != null && _wheelContactRadius == radius && _wheelContactHalfWidth == halfWidth) return;
            #if UNITY_EDITOR
            _wheelContactShapeBuilds++;
            #endif
            const int segments = 64;
            var vertices = new Vector3[segments * 2 + 2];
            var triangles = new int[segments * 12];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * (2f * Mathf.PI / segments);
                vertices[i] = new Vector3(-halfWidth, radius * Mathf.Cos(angle), radius * Mathf.Sin(angle));
                vertices[i + segments] = new Vector3(halfWidth, radius * Mathf.Cos(angle), radius * Mathf.Sin(angle));
                int next = (i + 1) % segments;
                int t = i * 12;
                triangles[t] = i; triangles[t + 1] = next; triangles[t + 2] = i + segments;
                triangles[t + 3] = next; triangles[t + 4] = next + segments; triangles[t + 5] = i + segments;
                triangles[t + 6] = segments * 2; triangles[t + 7] = next; triangles[t + 8] = i;
                triangles[t + 9] = segments * 2 + 1; triangles[t + 10] = i + segments; triangles[t + 11] = next + segments;
            }
            vertices[segments * 2] = Vector3.left * halfWidth;
            vertices[segments * 2 + 1] = Vector3.right * halfWidth;
            if (_wheelContactMesh == null)
                _wheelContactMesh = new Mesh { name = "Wheel contact cylinder", hideFlags = HideFlags.HideAndDontSave };
            _wheelContactMesh.vertices = vertices;
            _wheelContactMesh.triangles = triangles;
            _wheelContactMesh.RecalculateBounds();
            if (_wheelContactShape == null)
            {
                var query = new GameObject("Wheel contact query") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                // ComputePenetration needs an enabled mesh collider in this Unity version.
                // Park a trigger outside the field; explicit query poses position the wheel.
                query.transform.position = new Vector3(0f, -10000f, 0f);
                SceneManager.MoveGameObjectToScene(query, gameObject.scene);
                _wheelContactShape = query.AddComponent<MeshCollider>();
                _wheelContactShape.convex = true;
                _wheelContactShape.isTrigger = true;
            }
            _wheelContactShape.sharedMesh = null;
            _wheelContactShape.sharedMesh = _wheelContactMesh;
            _wheelContactRadius = radius;
            _wheelContactHalfWidth = halfWidth;
        }

        private static float GetInverseContactMass(Rigidbody body, float mass, Vector3 point, Vector3 direction)
        {
            Quaternion axes = body.rotation * body.inertiaTensorRotation;
            Quaternion inverseAxes = new Quaternion(-axes.x, -axes.y, -axes.z, axes.w);
            Vector3 torque = inverseAxes * Vector3.Cross(point - body.worldCenterOfMass, direction);
            Vector3 inertia = body.inertiaTensor;
            return 1f / Mathf.Max(0.0001f, mass)
                + (inertia.x > 0f ? torque.x * torque.x / inertia.x : 0f)
                + (inertia.y > 0f ? torque.y * torque.y / inertia.y : 0f)
                + (inertia.z > 0f ? torque.z * torque.z / inertia.z : 0f);
        }

        private static float CalculateObstacleLoad(float normalSpeed, float normalAcceleration,
            float depth, float contactMass, float dt)
        {
            dt = Mathf.Max(0.0001f, dt);
            float separationSpeed = Mathf.Min(1f, 0.2f * Mathf.Max(0f, depth) / dt);
            return Mathf.Max(0f, contactMass * ((separationSpeed - normalSpeed) / dt - normalAcceleration));
        }

        protected AudioSource CreateAudioSource(AudioResource resource)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = audioMixerGroup;
            source.spatialBlend = 0.4f;
            source.playOnAwake = false;
            source.resource = resource;
            source.loop = true;
            return source;
        }

        protected void UpdateAudio()
        {
            if (_rb.velocity.magnitude > 0f || Mathf.Abs(_rb.angularVelocity.magnitude) > 0f)
            {
                PlayAudio();
            }
            else
            {
                StopAudio();
            }
        }

        protected void PlayAudio()
        {
            var velocityFactor = Mathf.Clamp01(_rb.velocity.magnitude / _rb.maxLinearVelocity);
            var rotationFactor = Mathf.Clamp01(Mathf.Abs(_rb.angularVelocity.magnitude) / 6);
            var accelerationFactor = Mathf.Clamp(1f + velocityFactor, 1f, 2f);

            var volume = velocityFactor + rotationFactor * 0.25f;
            var pitch = Mathf.Max(accelerationFactor, rotationFactor);

            _treadSource.volume = volume * 0.5f;
            _treadSource.pitch = pitch * 0.7f;
            _gearSource.volume = volume * 0.2f;

            if (!_treadSource.isPlaying)
            {
                _treadSource.Play();
                _gearSource.Play();
            }
        }

        protected void StopAudio()
        {
            if (_treadSource.isPlaying)
            {
                _treadSource.Stop();
                _gearSource.Stop();
            }
        }

        /// <summary>
        /// Represents the velocity of the chassis.
        /// </summary>
        protected struct ChassisVelocity
        {
            /// <summary>
            /// X (forward/reverse) velocity in meters/second.
            /// </summary>
            public float vx;
            /// <summary>
            /// Y (strafe) velocity in meters/second. "Z" velocity in Unity's coordinate frame.
            /// </summary>
            public float vy;
            /// <summary>
            /// Rotational velocity in degrees/second.
            /// </summary>
            public float omega;
        }

        /// <summary>
        /// A setpoint for a single drivetrain wheel.
        /// </summary>
        protected struct WheelSetpoint
        {
            /// <summary>
            /// Angle of the wheel in degrees.
            /// </summary>
            public float angle;
            /// <summary>
            /// Velocity of the wheel in meters/second.
            /// </summary>
            public float velocity;
        }

        /// <summary>
        /// Represents a drivetrain architecture.
        /// </summary>
        public enum DriveType
        {
            Swerve,
            Differential,
            Mecanum
        }

        /// <summary>
        /// Represents the perspective for a set of drivetrain velocities.
        /// </summary>
        public enum DrivePerspective
        {
            FieldOriented,
            RobotRelative
        }
    }
}
