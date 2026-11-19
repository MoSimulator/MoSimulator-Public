using MoSimCore.BaseClasses;
using Unity.Cinemachine;
using UnityEngine;

namespace GameSystems.Cameras
{
    public class DriverStationVCam : BaseVCamScript
    {
        private const float c_InputDeadzone = 0.01f;

        /// <summary>
        /// How far below the top of the head a person's eyes sit, in inches. About 11.5 cm on an adult
        /// and near enough the same whatever their height, so the setting can be a stature - which is
        /// the number someone knows about themselves - and the camera goes this far under it.
        /// </summary>
        public const float EyeBelowCrownInches = 4.5f;

        private const float MetresPerInch = 0.0254f;

        /// <summary>
        /// The band the driver leans in across, in inches between the robot and the glass: still upright
        /// at the far end, all the way in at the near one, and part way in between. Walking stops at the
        /// wall, and from there a robot close to the glass is below what the driver can see over it.
        /// </summary>
        private const float LeanFullyWithinInches = 36f;
        private const float LeanStartsWithinInches = 72f;

        /// <summary>
        /// Where the lean puts the camera, relative to the driver: in toward the glass, and down, as
        /// someone dropping their shoulders and putting their face to it would move. Far enough forward
        /// to reach the glass rather than stopping short of it.
        /// </summary>
        private const float LeanForwardInches = 10.8f;
        private const float LeanDownInches = 2f;

        /// <summary>
        /// How long the driver takes to move the whole way into the lean, in seconds. Needed because
        /// easing on the robot's distance alone moves the camera as fast as the robot does, and a robot
        /// driving straight at the glass made that a snap rather than a lean.
        /// </summary>
        private const float LeanSeconds = 0.25f;

        /// <summary>How far ahead to look for the wall. The driver stands within arm's reach of it.</summary>
        private const float WallSearchMetres = 5f;

        /// <summary>
        /// How much room to leave between the driver and whatever is in front of them when they are
        /// stood at the window, in inches. Enough that the physics has nothing to push back out of.
        /// </summary>
        private const float WindowClearanceInches = 2f;

        /// <summary>How far into the lean the driver is now, as opposed to where they are heading.</summary>
        private float _lean;

        /// <summary>The robot this driver leans in to look at, and where the camera rests without leaning.</summary>
        private Transform _leanSubject;
        private Vector3 _restLocalPosition;
        private bool _restCaptured;

        public Vector2 TranslationValue { get; set; }
        private Vector3 _startingDirection;
        private Vector3 _startingRotation;

        private Rigidbody _rb;
        private bool _rigidbodyConfigured;

        [SerializeField] private CinemachineCamera vcam;

        [SerializeField] private float movementSpeed = 5f;

        /// <summary>
        /// Puts the camera at the eye height of a driver this tall. Only the camera moves: the rig it
        /// hangs on is what carries the driver walking left and right, and it sits at a height of its
        /// own that the scene chose. Set in world space for that reason, rather than as an offset.
        /// </summary>
        /// <param name="statureInches">The driver's height, top of the head to the floor.</param>
        public void SetDriverHeight(float statureInches)
        {
            if (vcam == null)
            {
                vcam = GetComponentInChildren<CinemachineCamera>(true);
                if (vcam == null)
                {
                    Debug.LogWarning($"{name}: no CinemachineCamera to place at the driver's eye height.");
                    return;
                }
            }

            var eyeHeight = (statureInches - EyeBelowCrownInches) * MetresPerInch;
            var at = vcam.transform.position;
            vcam.transform.position = new Vector3(at.x, eyeHeight, at.z);

            // This is now where the camera rests, so the lean offsets from here rather than from
            // wherever the scene left it.
            _restCaptured = false;
            CaptureRestPosition();
        }

        /// <summary>
        /// Walks the driver up to the window, once, so a match does not start with that to do by hand.
        /// Stops where walking would have stopped: swept forward with the driver's own shape, so it is
        /// whatever actually blocks them that decides, which differs between the two games.
        /// </summary>
        public void StandAtTheWindow()
        {
            if (!TryGetComponent(out BoxCollider body))
            {
                return;
            }

            var half = Vector3.Scale(body.size, transform.lossyScale) * 0.5f;
            var centre = transform.TransformPoint(body.center);

            if (!Physics.BoxCast(centre, half, transform.forward, out var blocked, transform.rotation,
                    WallSearchMetres, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            var step = blocked.distance - WindowClearanceInches * MetresPerInch;
            if (step <= 0f)
            {
                return;
            }

            transform.position += transform.forward * step;
            StopRigidbodyDrift();

        }

        /// <summary>
        /// The robot this driver watches, for deciding when to lean in. Passed in rather than taken from
        /// <see cref="BaseVCamScript.TargetRobot"/>, which is a fixed focus point in the static view.
        /// </summary>
        public void SetLeanSubject(Transform robot)
        {
            _leanSubject = robot;
        }

        private void CaptureRestPosition()
        {
            if (_restCaptured || vcam == null)
            {
                return;
            }

            _restLocalPosition = vcam.transform.localPosition;
            _restCaptured = true;
        }

        /// <summary>
        /// Leans the driver in toward the glass as a robot gets close enough to it to be hard to see
        /// over, by as much as the robot is close. Taken at the driver's own pace rather than the
        /// robot's, so a robot driving straight at the glass is still a lean and not a snap.
        ///
        /// The offset is relative to the driver, so it rides along as they walk the length of the
        /// station, and it is taken from the wall itself rather than from a fixed position: the two
        /// games put their stations in different places, and the driver can walk into the wall.
        /// </summary>
        private void UpdateLean()
        {
            if (vcam == null || !_restCaptured)
            {
                return;
            }

            var rest = transform.TransformPoint(_restLocalPosition);

            _lean = Mathf.MoveTowards(_lean, LeanTarget(rest), Time.deltaTime / LeanSeconds);

            vcam.transform.position = rest
                                      + transform.forward * (LeanForwardInches * MetresPerInch * _lean)
                                      + Vector3.down * (LeanDownInches * MetresPerInch * _lean);
        }

        /// <summary>
        /// How far into the lean the robot's distance calls for, from nothing to all of it. Measured from
        /// where the camera rests rather than from where it currently is: leaning carries it past the
        /// wall, and measuring from there put the wall's far side in front of it instead of its near
        /// side, which read as the robot being closer and drove the lean the rest of the way in.
        /// </summary>
        private float LeanTarget(Vector3 rest)
        {
            if (_leanSubject == null)
            {
                return 0f;
            }

            // The wall in front of the driver is whatever they cannot walk through, so it is found by
            // looking rather than configured: the stations are named differently in each game.
            if (!Physics.Raycast(rest, transform.forward, out var wall, WallSearchMetres,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                return 0f;
            }

            var robotAhead = Vector3.Dot(_leanSubject.position - rest, transform.forward);
            var robotFromWall = (robotAhead - wall.distance) / MetresPerInch;

            return 1f - Mathf.Clamp01((robotFromWall - LeanFullyWithinInches) /
                                      (LeanStartsWithinInches - LeanFullyWithinInches));
        }

        public override void SetTranslationValue(Vector2 value)
        {
            TranslationValue = value.sqrMagnitude > c_InputDeadzone * c_InputDeadzone ? value : Vector2.zero;
        }

        private void Start()
        {
            if (!EnsureRigidbodyConfigured())
            {
                Debug.LogError("Rigidbody component not found on this GameObject.");
            }

            if (vcam == null)
            {
                vcam = GetComponentInChildren<CinemachineCamera>();
                if (vcam == null)
                {
                    Debug.LogError("CinemachineCamera component not found on this GameObject.");
                    return;
                }
            }

            _startingDirection = transform.forward;
            _startingRotation = transform.right;
            CaptureRestPosition();
            ResetMovement();
        }

        private void OnEnable()
        {
            ResetMovement();
        }

        private void OnDisable()
        {
            ResetMovement();
        }

        private void Update()
        {
            if (TargetRobot is not null && vcam != null && vcam.LookAt != TargetRobot)
            {
                vcam.LookAt = TargetRobot;
            }

            UpdateLean();
        }

        private void FixedUpdate()
        {
            if (TranslationValue == Vector2.zero)
            {
                StopRigidbodyDrift();
                return;
            }

            var moveDirection = _startingDirection * TranslationValue.y + _startingRotation * TranslationValue.x;

            if (_rb is not null)
            {
                _rb.MovePosition(_rb.position + moveDirection * (movementSpeed * Time.fixedDeltaTime));
            }
            else
            {
                Debug.LogWarning("Rigidbody is not assigned, movement will not be applied.");
            }
        }

        public override void SetCameraTarget(Transform target)
        {
            if (target == null)
            {
                Debug.LogError("Target is null");
                return;
            }

            TargetRobot = target;
            ResetMovement();

            if (vcam == null)
            {
                Debug.LogError("Virtual Camera is not assigned.");

                vcam = GetComponentInChildren<CinemachineCamera>();

                if (vcam == null)
                {
                    Debug.LogError("CinemachineCamera component not found on this GameObject.");
                    return;
                }
            }

            vcam.LookAt = target;
        }

        private void ConfigureRigidbody()
        {
            _rb.useGravity = false;
            _rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            _rigidbodyConfigured = true;
            StopRigidbodyDrift();
        }

        private bool EnsureRigidbodyConfigured()
        {
            if (_rigidbodyConfigured)
            {
                return true;
            }

            if (_rb == null && !TryGetComponent(out _rb))
            {
                return false;
            }

            ConfigureRigidbody();
            return true;
        }

        private void ResetMovement()
        {
            TranslationValue = Vector2.zero;
            _lean = 0f;
            EnsureRigidbodyConfigured();
            StopRigidbodyDrift();
        }

        private void StopRigidbodyDrift()
        {
            if (_rb == null)
            {
                return;
            }

            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }


}
