using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Enums;
using RobotFramework.Interfaces;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Audio;

namespace RobotFramework.Components
{
    [DefaultExecutionOrder(1000)]
    public class InteractionRoller : MonoBehaviour, IRoller
    {
#if UNITY_EDITOR
        private const int MaxTrajectories = 60;
        private const int MaxTrajectoryPoints = 512;
        private const float TrajectoryFollowSeconds = 1f;
        private const float TrajectoryVisibleSeconds = 4f;

        private struct TrajectorySample
        {
            public Vector3 Position;
            public Vector3 ContactPoint;
            public Vector3 Normal;
            public float Compression;
            public bool InContact;
        }

        private sealed class ContactTrajectory
        {
            public Rigidbody Body;
            public bool InContact = true;
            public bool Recording = true;
            public float LastContactTime;
            public float CompletedTime;
            public readonly List<TrajectorySample> Points = new(64);
        }

        private sealed class TrajectoryState
        {
            public readonly List<ContactTrajectory> Traces = new(MaxTrajectories);
            public readonly HashSet<Rigidbody> Contacts = new();
            public readonly HashSet<Rigidbody> PreviousContacts = new();
            public readonly Dictionary<Rigidbody, TrajectorySample> ContactDetails = new();
        }

        // Allocate only while tracing; player builds contain no trajectory state.
        private TrajectoryState _trajectoryState;
#endif

        [Header("Wheel Dimensions (Inches)")]
        [SerializeField] private float radius = 4.0f;
        [SerializeField] private float length = 2.0f;

        [Header("Belt Configuration")]
        [SerializeField] private bool useBeltMode;
        [SerializeField] private float beltLength = 10.0f;

        [Header("Physics Properties")]
        [Tooltip("REMEMBER the game piece is solid, set this higher than you have set in your cad")]
        [SerializeField] private float maxSquish = 0.5f;

        [Range(0,1)][SerializeField] private float coreFriction = 1;
        [Range(0,1)][SerializeField] private float coreBounce = 0;
        [SerializeField] private float rpm = 800;
        [SerializeField] private float compressionStiffness = 500f;
        [SerializeField] private float compressionDamping = 50f;
        [Tooltip("Percentage of the piece's lateral velocity relative to the roller that is cancelled each " +
                 "step. 100 cancels it exactly and cannot overshoot; the value is a true fraction, independent " +
                 "of the timestep and of where the contact sits on either body.")]
        [SerializeField, Range(0,100)] private float scrubForces = 0.0f;

        [Tooltip("Slip, as a fraction of the wheel's surface speed, at which the motor already makes full stall " +
                 "torque. Stands in for the wheel inertia that isn't simulated: real flywheeled rollers hold near " +
                 "stall through the whole contact. Lower = more aggressive speed matching. " +
                 "1 is the legacy full-range ramp — leave it there unless you are tuning a specific roller.")]
        [SerializeField, Range(0.01f, 1f)] private float motorSlipResponse = 1f;

        [Tooltip("Bound the tangential (grip) impulse by frictionCoefficient * normal force instead of by motor " +
                 "torque alone. Makes grip follow the squish, so it fades to zero at release rather than cutting " +
                 "off at full strength, and spreads delivery across the contact instead of allowing it in one " +
                 "frame. Raise compressionStiffness when enabling this — at low stiffness the cap is several " +
                 "times tighter than the motor limit and will feel like lost authority. Note this makes " +
                 "compressionStiffness a grip parameter as well as a squish parameter.")]
        [SerializeField] private bool frictionCoupledToNormal;

        [Tooltip("Coulomb mu for the cap above. Urethane on foam/rubber is genuinely >1.5; this is also " +
                 "absorbing the fact that the contact patch is modelled as a single point, so values well " +
                 "above textbook are expected. Unused unless frictionCoupledToNormal is on.")]
        [SerializeField, Range(0.1f, 20f)] private float frictionCoefficient = 3f;

        [SerializeField] private bool oppositeReaction = true;
        [SerializeField] private LayerMask ignoreLayers;

        [Header("Motor Constraints")] [SerializeField]
        private RollerMotor motor;

        [Tooltip("Allow commanded RPM to exceed the selected motor's default maximum. Motor torque limits still apply.")]
        [SerializeField] private bool ignoreDefaultMaxRpm;

        [Header("Models")] [SerializeField] private RollerModel[] models;

        [Header("Audio Settings")]
        [SerializeField]
        [Tooltip("Audio clip to play when roller is moving.")]
        private AudioClip rollerSound;
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Clip playback speed multiplier (0 pauses playback, 1 uses the RPM-based pitch).")]
        private float playbackSpeed = 1f;
        [SerializeField]
        private float pitchMultiplier = 1/2000f;
        [SerializeField]
        [Tooltip("Min/max pitch range for roller audio (x=min, y=max).")]
        private Vector2 movementAudioPitchRange = new(0.95f, 1.1f);
        [SerializeField]
        private float volumeMultiplier = 1/2000f;
        [SerializeField]
        [Tooltip("Min/max volume range for roller audio (x=min, y=max).")]
        private Vector2 movementAudioVolumeRange = new(0.1f, 0.25f);
        [SerializeField]
        [Tooltip("Smoothing speed for pitch transitions.")]
        private float audioPitchSmoothSpeed = 8f;
        [SerializeField]
        [Tooltip("Smoothing speed for volume transitions.")]
        private float audioVolumeSmoothSpeed = 8f;
        [SerializeField]
        [Tooltip("Exponent for audio response curve (higher = more aggressive).")]
        private float audioResponseExponent = 0.75f;
        [SerializeField]
        [Tooltip("Allow roller audio when rotating at negative RPM.")]
        private bool negativeRangeAudio = false;

#if UNITY_EDITOR
        // Editor-only read-out of the volume this roller builds at runtime. Nothing exists to look at
        // before Start: the trigger collider is added there, so an editor tool wanting to draw or
        // hit-test a roller has no collider to go on and has to rebuild the shape from these numbers -
        // exactly as Start and the overlap queries do. Exposed here rather than reconstructed by the
        // tool so the inch-to-metre conversion lives in one place and the two cannot drift apart.
        //
        // These describe the UNSCALED world volume, which is what the overlap queries sweep. The
        // trigger collider itself is scaled by the transform, so this component enforces world scale 1.
        public bool EditorUsesBeltMode => useBeltMode;

        /// <summary>Metres. Capsule radius, or in belt mode the reach either side of the surface.</summary>
        public float EditorRadius => radius * 0.0254f;

        /// <summary>Metres along local X. Capsule height, or in belt mode the belt's width.</summary>
        public float EditorLength => length * 0.0254f;

        /// <summary>Metres along local Z. The belt's travel span; belt mode only.</summary>
        public float EditorBeltLength => beltLength * 0.0254f;

        /// <summary>Metres; matches the solid inner core created at runtime.</summary>
        public float EditorCoreRadius => Mathf.Max(0.001f, (radius - maxSquish) * 0.0254f);
        public float EditorCoreBeltLength => (beltLength - maxSquish * 2f) * 0.0254f;
#endif

        private Rigidbody[] _bodyReferences;

        private struct ContactCandidate
        {
            public Rigidbody Body;
            public Collider Collider;
            public Vector3 AxisNearest;
            public Vector3 ClosestPoint;
            public float Distance;
        }

        private struct ContactSortKey
        {
            public float Distance;
            public int CandidateIndex;
            public string BodyKey;
            public int BodyId;
        }

        private readonly List<ContactCandidate> _candidates = new(64);
        private readonly List<ContactSortKey> _sortKeys = new(64);
        private readonly Dictionary<Rigidbody, int> _candidateIndices = new(64);
        private static readonly Comparison<ContactCandidate> SmallCandidateComparison = CompareSmallCandidates;
        private static readonly Comparison<ContactSortKey> DistanceComparison = CompareSortDistances;
        private static readonly IComparer<ContactSortKey> CandidateKeyComparer =
            Comparer<ContactSortKey>.Create(CompareCandidateKeys);

        [Header("other stuff")]
        [SerializeField, Range(0, 0.2f)] private float atRangeTollerance = 0.02f;

#if UNITY_EDITOR
        [Header("Trajectory Gizmos")]
        [Tooltip("Trace each contacted body's centre of mass through contact and for one second after release. Green/yellow/red show penetration from zero to Max Squish; cyan shows movement after release. Arrows show contact normals, not force magnitude. Keeps the latest 60 traces for four simulation seconds after recording ends; pauses preserve them.")]
        [SerializeField] private bool trajectoryTracer;
#endif

        // ── private state ─────────────────────────────────────────────────────
        private Collider    _triggerCollider;
        private Rigidbody   _selfRb;
        private int         _layerMask;
        private float       _rotationalVelocity;
        private Motor       _selectedMotor;
        private string      _resolverKey;
        private int         _resolverId;
        private float?      _overrideRpm;
        private float       _lastTime;
        private AudioSource _audioSource;
        [HideInInspector] public float realRpm;
        [HideInInspector] public bool  IsAtSpeed => true;
        [HideInInspector] public float TargetRpm;

        // ── solver buffers ────────────────────────────────────────────────────
        // The deepest contacts are selected deterministically when more bodies
        // overlap than the fixed Burst buffers can hold.
        private const int MaxContacts = 64;

        private NativeArray<RbSnapshot>    _rbSnapshots;
        private NativeArray<ImpulseResult> _impulseResults;

        // Audio smoothing state. Previously round-tripped through a scheduled
        // IJob, which cost a scheduler sync per frame to run two lerps and
        // introduced a one-frame lag reading the result back off the
        // AudioSource. Burst direct call does the same math inline.
        private float _smoothedPitch  = 1f;
        private float _smoothedVolume;

        private Collider[] _overlapBuffer = new Collider[64];

        private Vector3 _halfExtents;
        private float   _mRadius;
        private float   _mLength;

        private void Start()
        {
            EnforceUnitWorldScale();
            _resolverId = InteractionRollerImpulseResolver.RegisterRoller();
            _resolverKey = InteractionRollerOrderKey.ForComponent(this);
            _selfRb = GetComponentInParent<Rigidbody>();

            if (useBeltMode)
            {
                // Size has to be written explicitly: an added BoxCollider defaults to a 1 m cube,
                // which is far larger than the belt the gizmo draws and than the volume the overlap
                // query sweeps. Matches _halfExtents below, doubled - BoxCollider.size is full extents.
                var box = Utils.TryGetAddComponent<BoxCollider>(gameObject);
                box.center = Vector3.zero;
                box.size = new Vector3(length * 0.0254f, radius * 0.0254f * 2f, beltLength * 0.0254f);
                _triggerCollider = box;
            }
            else
            {
                var cap = Utils.TryGetAddComponent<CapsuleCollider>(gameObject);
                cap.direction = 0;
                _triggerCollider = cap;
                cap.height = length * 0.0254f;
                cap.radius = radius * 0.0254f;
            }

            _triggerCollider.isTrigger = true;
            _selectedMotor = GetRollerMotor(motor);

            CreateSolidCollider();
            _layerMask = ~ignoreLayers;

            _audioSource = Utils.TryGetAddComponent<AudioSource>(gameObject);
            _audioSource.outputAudioMixerGroup = Resources.Load<AudioMixerGroup>("MainAudioMixer")
                .audioMixer.FindMatchingGroups("RobotSounds")[0];
            _audioSource.playOnAwake = false;
            _audioSource.loop  = true;
            _audioSource.clip  = rollerSound;
            _audioSource.Stop();

            _mRadius     = radius   * 0.0254f;
            _mLength     = length   * 0.0254f;
            _halfExtents = new Vector3(_mLength * 0.5f, _mRadius, beltLength * 0.0254f * 0.5f);

            EnsureBuffers();
        }

        // Allocation is paired with OnEnable, not Start. Re-enabling a
        // GameObject fires OnEnable without re-running Start, so allocating in
        // Start and freeing in OnDisable would leave FixedUpdate writing into
        // disposed memory after the first disable/enable cycle.
        private void OnEnable()
        {
            EnforceUnitWorldScale();
            EnsureBuffers();
        }

        /// <summary>Compensates for inherited scaling so this roller's world scale remains one.</summary>
        /// <returns>Whether the local scale changed. A zero-scale parent cannot be compensated.</returns>
        public bool EnforceUnitWorldScale()
        {
            Transform t = transform;
            Vector3 original = t.localScale;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                Vector3 world = t.lossyScale;
                if ((world - Vector3.one).sqrMagnitude < 0.0000000001f) break;
                Vector3 local = t.localScale;
                if (Mathf.Abs(local.x) < 0.000001f || Mathf.Abs(local.y) < 0.000001f || Mathf.Abs(local.z) < 0.000001f)
                {
                    t.localScale = Vector3.one;
                    local = Vector3.one;
                    world = t.lossyScale;
                }
                if (Mathf.Abs(world.x) < 0.000001f || Mathf.Abs(world.y) < 0.000001f || Mathf.Abs(world.z) < 0.000001f)
                {
                    t.localScale = original;
                    return false;
                }
                // Use Unity's actual world scale: reciprocal parent.lossyScale is insufficient
                // for rotated rollers under nonuniformly scaled or mirrored hierarchies.
                t.localScale = new Vector3(local.x / world.x, local.y / world.y, local.z / world.z);
            }
            return (t.localScale - original).sqrMagnitude > 0.000000000001f;
        }

        private void LateUpdate() => EnforceUnitWorldScale();
        private void OnDisable()
        {
            ReleaseBuffers();
#if UNITY_EDITOR
            _trajectoryState = null;
#endif
        }
        private void OnDestroy() => ReleaseBuffers();

        private void EnsureBuffers()
        {
            if (_rbSnapshots.IsCreated) return;
            _rbSnapshots     = new NativeArray<RbSnapshot>(MaxContacts, Allocator.Persistent);
            _impulseResults  = new NativeArray<ImpulseResult>(MaxContacts, Allocator.Persistent);
            _bodyReferences  = new Rigidbody[MaxContacts];
        }

        private void ReleaseBuffers()
        {
            // No job handles to join: all solver work is a synchronous Burst
            // direct call, so nothing can still be reading these.
            if (_rbSnapshots.IsCreated)    _rbSnapshots.Dispose();
            if (_impulseResults.IsCreated) _impulseResults.Dispose();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Drops the roller's own loop so a sibling sharing this GameObject's AudioSource owns it.
        /// </summary>
        public void MuteRollerAudio()
        {
            rollerSound = null;

            if (_audioSource != null)
            {
                _audioSource.Stop();
            }
        }

        public MoSimResult<MoSimLib.Void> SetRpm(float value)
        {
            var version = MoSim.ActiveVersion;
            SetRpmV1(value);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetRpmV1(float value) { TargetRpm = value; rpm = value; }

        public MoSimResult<bool> OverrideRpm(float value)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(OverrideRpmV1(value));
        }

        private bool OverrideRpmV1(float value)
        {
            if (_overrideRpm != null) return false;
            _overrideRpm = value;
            return true;
        }

        public MoSimResult<bool> FlipRpm()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(FlipRpmV1());
        }

        private bool FlipRpmV1() => OverrideRpmV1(-rpm);

        public MoSimResult<MoSimLib.Void> Stop()
        {
            var version = MoSim.ActiveVersion;
            StopV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void StopV1() => _overrideRpm = 0;

        /// <summary>
        /// Scales this frame's commanded rpm by 1/divider. Non-destructive: <see cref="rpm"/> is
        /// untouched and the roller returns to it the first frame this is not called. Stacks on
        /// top of an override already staged this frame instead of being rejected by it.
        /// </summary>
        public MoSimResult<bool> DivideSpeed(float divider)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(DivideSpeedV1(divider));
        }

        private bool DivideSpeedV1(float divider)
        {
            if (Mathf.Abs(divider) < 1e-6f) return false;
            _overrideRpm = (_overrideRpm ?? rpm) / divider;
            return true;
        }

        public MoSimResult<MoSimLib.Void> ChangeMotor(RollerMotor selectedMotor)
        {
            var version = MoSim.ActiveVersion;
            ChangeMotorV1(selectedMotor);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void ChangeMotorV1(RollerMotor selectedMotor) => _selectedMotor = GetRollerMotor(selectedMotor);

        [Obsolete("This method is reserved for Group-only contexts.")]
        public MoSimResult<IRoller> GetRoller(int id)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<IRoller>.Success(GetRollerV1(id));
        }

        private IRoller GetRollerV1(int id) => this;

        // ── Update ────────────────────────────────────────────────────────────
        private void Update()
        {
            if (models?.Length > 0)
            {
                foreach (var model in models) {
                    model.GetModel().transform.Rotate(model.GetRotationalAxis(), model.GetMp() * (model.IsReversed() ? -1 : 1) * _rotationalVelocity * Mathf.Rad2Deg * Time.deltaTime);
                }
            }

            if (!rollerSound) return;

            float audioRpm = negativeRangeAudio ? math.abs(realRpm) : math.max(realRpm, 0f);

            if (BaseGameManager.Instance is null || BaseGameManager.Instance.RobotState == RobotState.Disabled ||
                (audioRpm <= 5 && _audioSource.isPlaying))
            {
                _audioSource.Stop();
            }
            else if (audioRpm > 5 && !_audioSource.isPlaying)
            {
                if (_audioSource.clip != null)
                    _audioSource.time = UnityEngine.Random.Range(0f, _audioSource.clip.length);
                _audioSource.Play();
            }

            var timestep = Mathf.Clamp(Time.time - _lastTime, 0.001f, 10000f);
            _lastTime = Time.time;

            RollerSolver.SmoothAudio(
                audioRpm, timestep,
                pitchMultiplier,  new float2(movementAudioPitchRange.x,  movementAudioPitchRange.y),
                volumeMultiplier, new float2(movementAudioVolumeRange.x, movementAudioVolumeRange.y),
                audioPitchSmoothSpeed, audioVolumeSmoothSpeed,
                ref _smoothedPitch, ref _smoothedVolume);

            _audioSource.pitch  = _smoothedPitch * playbackSpeed;
            _audioSource.volume = _smoothedVolume;
        }

        // ── FixedUpdate ───────────────────────────────────────────────────────
        private void FixedUpdate()
        {
            EnforceUnitWorldScale();
#if UNITY_EDITOR
            int contactCount = SimulateContacts();
            if (trajectoryTracer)
            {
                var state = _trajectoryState ??= new TrajectoryState();
                state.Contacts.Clear();
                state.ContactDetails.Clear();
                RecordTrajectoryContacts(state, contactCount);
                UpdateTrajectories(state);
                state.PreviousContacts.Clear();
                state.PreviousContacts.UnionWith(state.Contacts);
            }
            else if (_trajectoryState != null)
            {
                // Drop retained bodies and buffers once when tracing is switched off.
                _trajectoryState = null;
            }
#else
            SimulateContacts();
#endif
        }

        private int SimulateContacts()
        {
            if (BaseGameManager.Instance is null || BaseGameManager.Instance.RobotState == RobotState.Disabled)
                _overrideRpm = 0;

            if (!ignoreDefaultMaxRpm)
                rpm = Mathf.Clamp(rpm, -_selectedMotor.MaxRpm, _selectedMotor.MaxRpm);

            if (!_selfRb) return 0;

            if (_overrideRpm.HasValue)
            {
                _rotationalVelocity = _overrideRpm.Value * (2f * Mathf.PI / 60f);
                _overrideRpm = null;
            }
            else
            {
                _rotationalVelocity = rpm * (2f * Mathf.PI / 60f);
            }

            realRpm = _rotationalVelocity / (2f * Mathf.PI / 60f);

            Transform  cachedTransform = transform;
            Vector3    position        = cachedTransform.position;
            Vector3    right           = cachedTransform.right;

            // ── Overlap query ─────────────────────────────────────────────────
            Vector3 axisDirection = right;
            Vector3 p0 = Vector3.zero, p1 = Vector3.zero;
            int hitCount;

            if (useBeltMode)
            {
                hitCount = Physics.OverlapBoxNonAlloc(
                    position, _halfExtents, _overlapBuffer,
                    cachedTransform.rotation, _layerMask, QueryTriggerInteraction.Ignore);
            }
            else
            {
                float internalSideLength = Mathf.Max(0, _mLength * 0.5f - _mRadius);
                p0 = position - axisDirection * internalSideLength;
                p1 = position + axisDirection * internalSideLength;
                hitCount = Physics.OverlapCapsuleNonAlloc(
                    p0, p1, _mRadius, _overlapBuffer, _layerMask, QueryTriggerInteraction.Ignore);
            }

            // ── Snapshot gather ───────────────────────────────────────────────
            _candidates.Clear();
            if (_candidateIndices.Count != 0)
                _candidateIndices.Clear();
            if (hitCount == 0) return 0;

            var rollerPos   = position;
            var rollerRight = right;
            var rollerUp    = cachedTransform.up;

            bool indexedCandidates = false;

            // One contact per rigidbody, and it has to be the *deepest* one. A
            // compound-collider game piece produces several hits here and
            // OverlapCapsuleNonAlloc does not guarantee a stable order between
            // frames, so keeping whichever collider happened to come back first
            // let the contact point — and with it the normal and the friction
            // direction — jump around from one FixedUpdate to the next. That
            // reads as a mushy, buzzing grip. Ranking by penetration picks the
            // same collider every frame as long as the geometry has not moved.
            //
            // Rank all bodies before truncating to MaxContacts. Overlap query
            // order must not decide which pieces are simulated in a busy tick.
            for (int i = 0; i < hitCount; i++)
            {
                var col      = _overlapBuffer[i];
                var targetRb = col.attachedRigidbody;
                if (!targetRb || targetRb == _selfRb) continue;

                // A body that cannot respond to a contact impulse must not be
                // given one. AddForceAtPosition is silently dropped for it, but
                // the equal-and-opposite reaction still lands on _selfRb in full,
                // so every such contact leaves an uncancelled impulse that
                // accelerates the whole robot once per FixedUpdate.
                //
                // GamePieceController.IgnoreRobot() puts a held piece in exactly
                // this state — detectCollisions off, and Controlled() rewrites
                // velocity and angularVelocity to zero every frame, which makes
                // it effectively infinite-mass while staying non-kinematic. A
                // plain isKinematic check does not catch it.
                //
                // NetSolver.ProcessDeferredImpulses guards its own
                // action/reaction pair the same way.
                if (targetRb.isKinematic || !targetRb.detectCollisions) continue;

                // Probe from the collider's own bounds centre, not the rigidbody's
                // centre of mass. On a compound body the COM can sit far from the
                // collider that is actually touching — and on a long collider it
                // can sit far from the touching end — which aimed the axis probe,
                // and therefore ClosestPoint, at the wrong place along the roller.
                float3 axisNearest = useBeltMode
                    ? rollerPos
                    : ClosestPointOnSegment(p0, p1, col.bounds.center);

                // Measured with the same metric the solver uses for currentDist,
                // so "deepest here" and "deepest in SolveOne" cannot disagree.
                float3 closest   = col.ClosestPoint(axisNearest);
                float3 toContact = closest - axisNearest;
                float  dist      = useBeltMode
                    ? math.abs(math.dot(toContact, rollerUp))
                    : math.length(toContact);

                var candidate = new ContactCandidate
                {
                    Body = targetRb,
                    Collider = col,
                    AxisNearest = axisNearest,
                    ClosestPoint = closest,
                    Distance = dist,
                };

                // A short reference scan is cheaper for the common one or two
                // contacts. Build the index only when a crowded overlap would
                // make repeated scans more expensive than dictionary lookups.
                int candidateIndex = -1;
                if (_candidates.Count < 8)
                {
                    for (int j = 0; j < _candidates.Count; j++)
                    {
                        if (!ReferenceEquals(_candidates[j].Body, targetRb)) continue;
                        candidateIndex = j;
                        break;
                    }
                }
                else
                {
                    if (!indexedCandidates)
                    {
                        for (int j = 0; j < _candidates.Count; j++)
                            _candidateIndices.Add(_candidates[j].Body, j);
                        indexedCandidates = true;
                    }
                    if (_candidateIndices.TryGetValue(targetRb, out int index))
                        candidateIndex = index;
                }

                if (candidateIndex >= 0)
                {
                    var previous = _candidates[candidateIndex];
                    if (dist < previous.Distance ||
                        (dist == previous.Distance && CompareStableComponents(col, previous.Collider) < 0))
                        _candidates[candidateIndex] = candidate;
                }
                else
                {
                    if (indexedCandidates)
                        _candidateIndices.Add(targetRb, _candidates.Count);
                    _candidates.Add(candidate);
                }
            }

            if (_candidates.Count > 1 && _candidates.Count <= 8)
            {
                _candidates.Sort(SmallCandidateComparison);
            }
            else if (_candidates.Count > 8)
            {
                // Keep the larger contact records in place and sort compact
                // indices. This costs one retained buffer but moves much less
                // data while sorting a crowded overlap.
                _sortKeys.Clear();
                for (int i = 0; i < _candidates.Count; i++)
                    _sortKeys.Add(new ContactSortKey
                    {
                        Distance = _candidates[i].Distance,
                        CandidateIndex = i,
                    });

                _sortKeys.Sort(DistanceComparison);
                for (int start = 0; start < _sortKeys.Count;)
                {
                    int end = start + 1;
                    while (end < _sortKeys.Count &&
                           _sortKeys[end].Distance.CompareTo(_sortKeys[start].Distance) == 0)
                        end++;

                    if (end - start > 1)
                    {
                        // Fetch hierarchy keys once per tied body instead of on
                        // every comparison in the sort. Distinct distances need
                        // no hierarchy lookups at all.
                        for (int i = start; i < end; i++)
                        {
                            var key = _sortKeys[i];
                            var body = _candidates[key.CandidateIndex].Body;
                            key.BodyKey = InteractionRollerOrderKey.ForComponent(body);
                            key.BodyId = body.GetInstanceID();
                            _sortKeys[i] = key;
                        }
                        _sortKeys.Sort(start, end - start, CandidateKeyComparer);
                    }
                    start = end;
                }
            }
            int snapshotCount = Mathf.Min(MaxContacts, _candidates.Count);
            for (int i = 0; i < snapshotCount; i++)
            {
                var candidate = _candidates[_candidates.Count > 8 ? _sortKeys[i].CandidateIndex : i];
                var targetRb = candidate.Body;
                GetInverseMassProperties(targetRb, out var targetInvMass, out var targetInvInertia);
                _bodyReferences[i] = targetRb;
                _rbSnapshots[i] = new RbSnapshot
                {
                    WorldCOM        = targetRb.worldCenterOfMass,
                    Velocity        = targetRb.velocity,
                    AngularVelocity = targetRb.angularVelocity,
                    InvMass         = targetInvMass,
                    InvInertiaWorld = targetInvInertia,
                    ClosestPoint    = candidate.ClosestPoint,
                    AxisNearest     = candidate.AxisNearest,
                };
            }

            if (snapshotCount == 0) return 0;

            // ── Solve ─────────────────────────────────────────────────────────
            // Point velocities must be measured about the centre of mass, not the
            // transform origin, or a rotating robot reports the wrong surface speed.
            var selfCom = _selfRb.worldCenterOfMass;
            var selfVel = _selfRb.velocity;
            var selfAngVel = _selfRb.angularVelocity;

            // Burst direct call, not a scheduled job. At <=64 contacts the job
            // body is ~2us of math; scheduling an IJobParallelFor and blocking
            // on it immediately cost several times that in scheduler round-trip
            // and safety-handle churn. This is the same Burst-compiled machine
            // code with none of that overhead, and being a plain static
            // function it is directly callable from the offline test bench.
            var solverParams = new RollerParams
            {
                MRadius              = _mRadius,
                RotationalVelocity   = _rotationalVelocity,
                MaxStallTorque       = _selectedMotor.MaxStallTorque,
                CompressionStiffness = compressionStiffness,
                CompressionDamping   = compressionDamping,
                ScrubPercent         = scrubForces,
                MotorSlipResponse    = math.max(motorSlipResponse, 0.01f),
                FrictionCoefficient  = math.max(frictionCoefficient, 0f),
                FrictionCoupledToNormal = frictionCoupledToNormal,
                UseBeltMode          = useBeltMode,
            };

            var frame = new RollerFrame
            {
                SelfCOM             = selfCom,
                SelfVel             = selfVel,
                SelfAngVel          = selfAngVel,
                RollerPos           = rollerPos,
                RollerRight         = rollerRight,
                RollerUp            = rollerUp,
                Dt                  = Time.fixedDeltaTime,
            };

            RollerSolver.SolveContacts(solverParams, frame, _rbSnapshots, _impulseResults, snapshotCount);

            for (int i = 0; i < snapshotCount; i++)
            {
                var res = _impulseResults[i];
                // The result carries the slot it belongs to rather than relying
                // on positional correspondence with _bodyReferences. An early
                // exit added to either loop can no longer silently apply a
                // contact's impulse to the wrong rigidbody.
                if (res.BodySlot < 0) continue;

                var rb = _bodyReferences[res.BodySlot];
                if (!rb) continue;

                InteractionRollerImpulseResolver.Enqueue(
                    rb, _selfRb, res.TotalImpulse, res.ContactPoint, oppositeReaction,
                    _resolverKey, _resolverId, i);
            }
            return snapshotCount;
        }

        // ── Gizmos ────────────────────────────────────────────────────────────
#if UNITY_EDITOR
        private void RecordTrajectoryContacts(TrajectoryState state, int contactCount)
        {
            if (contactCount == 0) return;

            var rollerTransform = transform;
            var frame = new RollerFrame
            {
                RollerPos = rollerTransform.position,
                RollerRight = rollerTransform.right,
                RollerUp = rollerTransform.up
            };
            for (int i = 0; i < contactCount; i++)
            {
                var contact = _impulseResults[i];
                if ((uint)contact.BodySlot >= (uint)contactCount) continue;
                var body = _bodyReferences[contact.BodySlot];
                if (!body || !state.Contacts.Add(body)) continue;

                // Reconstruct diagnostics only when enabled. Keep them out of the
                // solver output so disabled tracing adds no per-contact data/work.
                var snapshot = _rbSnapshots[contact.BodySlot];
                RollerSolver.ContactGeometry(useBeltMode, frame, snapshot, out var normal, out var distance);
                state.ContactDetails[body] = new TrajectorySample
                {
                    ContactPoint = contact.ContactPoint,
                    Normal = normal,
                    Compression = Mathf.Clamp01((_mRadius - distance) / Mathf.Max(0.0001f, maxSquish * 0.0254f))
                };
                RecordTrajectoryContact(state, body);
            }
        }

        private void RecordTrajectoryContact(TrajectoryState state, Rigidbody body)
        {
            // Remember contacts independently of retained traces: evicting an
            // ongoing contact must not make it look like a new hit next tick.
            if (state.PreviousContacts.Contains(body)) return;
            // A return after release starts a separate trace, even if the previous
            // trace is still following this same body through its one-second tail.
            if (state.Traces.Count == MaxTrajectories) state.Traces.RemoveAt(0);
            state.Traces.Add(new ContactTrajectory
            {
                Body = body,
                LastContactTime = Time.fixedTime
            });
        }

        private void UpdateTrajectories(TrajectoryState state)
        {
            float now = Time.fixedTime;
            for (int i = state.Traces.Count - 1; i >= 0; i--)
            {
                var trajectory = state.Traces[i];
                if (!trajectory.Recording)
                {
                    if (now - trajectory.CompletedTime >= TrajectoryVisibleSeconds)
                        state.Traces.RemoveAt(i);
                    continue;
                }

                trajectory.InContact = trajectory.InContact && trajectory.Body &&
                    state.Contacts.Contains(trajectory.Body);
                if (trajectory.InContact) trajectory.LastContactTime = now;

                if (trajectory.Body)
                {
                    var points = trajectory.Points;
                    // Bound memory even when a body rests against the roller for
                    // minutes. Decimation retains the whole path and its start.
                    if (points.Count == MaxTrajectoryPoints)
                    {
                        for (int j = 1; j < points.Count / 2; j++)
                            points[j] = points[j * 2];
                        points.RemoveRange(points.Count / 2, points.Count / 2);
                    }
                    var sample = new TrajectorySample
                    {
                        Position = trajectory.Body.worldCenterOfMass,
                        InContact = trajectory.InContact
                    };
                    if (trajectory.InContact && state.ContactDetails.TryGetValue(trajectory.Body, out var contact))
                    {
                        sample.ContactPoint = contact.ContactPoint;
                        sample.Normal = contact.Normal;
                        sample.Compression = contact.Compression;
                    }
                    // Keep changes in compression even when the body is stationary.
                    if (points.Count == 0 || trajectory.InContact ||
                        points[points.Count - 1].InContact ||
                        (points[points.Count - 1].Position - sample.Position).sqrMagnitude > 0.000001f)
                        points.Add(sample);
                }

                if (!trajectory.Body || (!trajectory.InContact &&
                    now - trajectory.LastContactTime >= TrajectoryFollowSeconds))
                {
                    trajectory.Recording = false;
                    trajectory.CompletedTime = now;
                    trajectory.Body = null;
                }
            }
        }

        private void DrawTrajectoryGizmos()
        {
            if (!trajectoryTracer || !Application.isPlaying || _trajectoryState == null) return;
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Color oldColor = Gizmos.color;
            Gizmos.matrix = Matrix4x4.identity;
            for (int i = 0; i < _trajectoryState.Traces.Count; i++)
            {
                var trajectory = _trajectoryState.Traces[i];
                for (int j = 1; j < trajectory.Points.Count; j++)
                {
                    Gizmos.color = TrajectorySampleColor(trajectory.Points[j]);
                    Gizmos.DrawLine(trajectory.Points[j - 1].Position, trajectory.Points[j].Position);
                }
                // Space historical arrows out so the path remains readable.
                for (int j = 0; j < trajectory.Points.Count; j += 5)
                    DrawContactNormalGizmo(trajectory.Points[j]);
                if (trajectory.Points.Count > 0)
                {
                    DrawContactNormalGizmo(trajectory.Points[trajectory.Points.Count - 1]);
                    Gizmos.color = TrajectorySampleColor(trajectory.Points[0]);
                    Gizmos.DrawWireSphere(trajectory.Points[0].Position, 0.015f);
                }
            }
            Gizmos.matrix = oldMatrix;
            Gizmos.color = oldColor;
        }

        private static Color TrajectorySampleColor(TrajectorySample sample)
        {
            if (!sample.InContact) return Color.cyan;
            return sample.Compression < 0.5f
                ? Color.Lerp(Color.green, Color.yellow, sample.Compression * 2f)
                : Color.Lerp(Color.yellow, Color.red, (sample.Compression - 0.5f) * 2f);
        }

        private static void DrawContactNormalGizmo(TrajectorySample sample)
        {
            if (!sample.InContact) return;
            Gizmos.color = TrajectorySampleColor(sample);
            Vector3 normal = sample.Normal.normalized;
            Vector3 tip = sample.ContactPoint + normal * 0.06f;
            Vector3 side = Vector3.Cross(normal, Vector3.up);
            if (side.sqrMagnitude < 0.001f) side = Vector3.Cross(normal, Vector3.right);
            side = side.normalized * 0.008f;
            Gizmos.DrawLine(sample.ContactPoint, tip);
            Gizmos.DrawLine(tip, tip - normal * 0.015f + side);
            Gizmos.DrawLine(tip, tip - normal * 0.015f - side);
        }
#endif

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            if (trajectoryTracer) DrawTrajectoryGizmos();
#endif
            var mRadius = radius * 0.0254f;
            var mHeight = length * 0.0254f;
            Gizmos.color = GizmoColor();

            if (useBeltMode)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(mHeight, mRadius * 2f, beltLength * 0.0254f));

                Gizmos.color = PositiveColor;
                DrawBeltDirectionGizmo(mRadius, beltLength * 0.0254f, MarkerRadius(mRadius));
            }
            else
            {
                Matrix4x4  oldMatrix = Gizmos.matrix;
                Quaternion rot       = transform.rotation * Quaternion.Euler(0, 0, 90);
                Gizmos.matrix = Matrix4x4.TRS(transform.position, rot, Vector3.one);
                DrawCylinderGizmo(mRadius, mHeight);

                // Drawn in the roller's own frame rather than the cylinder's: the cylinder is turned on
                // its side to be drawn along Y, and reading a sign through that turn is how one ends up
                // shipping an arrow pointing the wrong way.
                Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
                Gizmos.color  = PositiveColor;
                DrawSpinDirectionGizmo(MarkerRadius(mRadius));

                Gizmos.matrix = oldMatrix;
            }
        }

        /// <summary>
        /// Which way the roller turns at a positive <see cref="rpm"/>, drawn as the curl around its
        /// axis. Always the positive sense whatever the roller is currently commanded to do:
        /// it answers "which way is +", the question being asked while the value is being typed, and an
        /// arrow that flipped with the sign could not answer it at all once the sign was negative.
        /// </summary>
        /// <remarks>
        /// The solver spins about <c>transform.right</c> by the right-hand rule — its angular velocity
        /// is <c>right * rpm</c> — so the arc is the fingers of that rule, and the wheel's top surface
        /// travels the way the arc points as it passes over the top.
        /// </remarks>
        private static void DrawSpinDirectionGizmo(float marker)
        {
            const int   segments = 24;
            const float sweep    = Mathf.PI * 1.5f;

            Vector3 last = OnArc(marker, 0f);

            for (int i = 1; i <= segments; i++)
            {
                Vector3 next = OnArc(marker, sweep * i / segments);
                Gizmos.DrawLine(last, next);
                last = next;
            }

            DrawArrowHead(last, Tangent(sweep), last.normalized, marker * 0.7f);
        }

        /// <summary>
        /// How big the direction marks are drawn: a quarter inch across whatever the roller measures,
        /// so a flywheel does not get a swept arc the size of the wheel, and never wider than the
        /// roller itself, so one built smaller than the mark keeps the mark inside its own silhouette.
        /// </summary>
        private static float MarkerRadius(float mRadius)
        {
            return Mathf.Min(MarkerDiameter * 0.0254f, mRadius * 2f) * 0.5f;
        }

        /// <summary>
        /// Which way the belt's surface travels at a positive <see cref="rpm"/>, on both faces: a belt
        /// carries a game piece one way along the top and the other way along the bottom, and which
        /// face it is being fed from is the whole question being asked of the gizmo.
        /// </summary>
        private static void DrawBeltDirectionGizmo(float r, float beltLength, float marker)
        {
            float reach = beltLength * 0.4f;
            float head  = Mathf.Min(marker, beltLength * 0.1f);

            for (int face = -1; face <= 1; face += 2)
            {
                // Positive rpm turns the surface toward +Z where it faces +Y, so the far face runs back.
                var tip   = new Vector3(0f, r * face, reach * face);
                var start = new Vector3(0f, r * face, -reach * face);

                Gizmos.DrawLine(start, tip);
                DrawArrowHead(tip, Vector3.forward * face, Vector3.right, head);
            }
        }

        /// <summary>A point on the circle the roller's surface travels, measured the way it travels.</summary>
        private static Vector3 OnArc(float r, float angle)
        {
            return new Vector3(0f, Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }

        /// <summary>The direction the arc is going where it ends, which is where the head is put.</summary>
        private static Vector3 Tangent(float angle)
        {
            return new Vector3(0f, -Mathf.Sin(angle), Mathf.Cos(angle));
        }

        /// <summary>Two barbs at the tip, laid in the plane the given side vector spans with the travel.</summary>
        private static void DrawArrowHead(Vector3 tip, Vector3 travel, Vector3 side, float size)
        {
            Vector3 back = tip - travel.normalized * size;
            Gizmos.DrawLine(tip, back + side * (size * 0.5f));
            Gizmos.DrawLine(tip, back - side * (size * 0.5f));
        }

        /// <summary>
        /// Cyan for a wheel, yellow for a belt - and a colour of its own while selected, so the one
        /// being edited stands out of a robot carrying a dozen of them. Selection wins over the mode
        /// colour: which roller this is matters more at that moment, and the mode is still legible
        /// from the shape being drawn.
        /// </summary>
        /// <summary>How wide the direction marks are drawn, in inches, matching the roller's own units.</summary>
        private const float MarkerDiameter = 0.25f;

        /// <summary>
        /// Magenta for the direction marks. Nothing else in the Scene view is this colour: Unity draws
        /// colliders green, and the roller's own body is cyan, yellow or the selection pink — so the
        /// mark reads as an annotation on all of them rather than as part of any one of them.
        /// </summary>
        private static readonly Color PositiveColor = new Color(1f, 0.15f, 1f);

        private Color GizmoColor()
        {
#if UNITY_EDITOR
            // Deliberately not orange or yellow - either would read as a belt.
            if (UnityEditor.Selection.Contains(gameObject)) return new Color(1f, 0.3f, 0.55f);
#endif
            return useBeltMode ? Color.yellow : Color.cyan;
        }

        private static void DrawCylinderGizmo(float r, float h)
        {
            float   halfH      = h * 0.5f;
            int     segments   = 32;
            Vector3 lastTop    = new Vector3(r, halfH, 0);
            Vector3 lastBottom = new Vector3(r, -halfH, 0);
            for (int i = 1; i <= segments; i++)
            {
                float   a          = i * Mathf.PI * 2 / segments;
                Vector3 nextTop    = new Vector3(Mathf.Cos(a) * r, halfH, Mathf.Sin(a) * r);
                Vector3 nextBottom = new Vector3(nextTop.x, -halfH, nextTop.z);
                Gizmos.DrawLine(lastTop, nextTop);
                Gizmos.DrawLine(lastBottom, nextBottom);
                Gizmos.DrawLine(nextTop, nextBottom);
                lastTop    = nextTop;
                lastBottom = nextBottom;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static int CompareSmallCandidates(ContactCandidate a, ContactCandidate b)
        {
            int result = a.Distance.CompareTo(b.Distance);
            return result != 0 ? result : CompareStableComponents(a.Body, b.Body);
        }

        private static int CompareSortDistances(ContactSortKey a, ContactSortKey b)
            => a.Distance.CompareTo(b.Distance);

        private static int CompareCandidateKeys(ContactSortKey a, ContactSortKey b)
        {
            // The gather keeps one candidate per body, so a collider tie cannot
            // reach this sort. Collider order is resolved when deduplicating.
            int result = string.CompareOrdinal(a.BodyKey, b.BodyKey);
            return result != 0 ? result : a.BodyId.CompareTo(b.BodyId);
        }

        private static int CompareStableComponents(Component a, Component b)
        {
            int result = string.CompareOrdinal(
                InteractionRollerOrderKey.ForComponent(a),
                InteractionRollerOrderKey.ForComponent(b));
            return result != 0 ? result : a.GetInstanceID().CompareTo(b.GetInstanceID());
        }

        private static float3 ClosestPointOnSegment(float3 start, float3 end, float3 point)

        {

            float3 d = end - start;

            float sqrMag = math.dot(d, d);

            if (sqrMag < 0.0001f) return start;


            float t = math.dot(point - start, d) / sqrMag;

            return start + math.clamp(t, 0f, 1f) * d;

        }

        /// <summary>
        /// Inverse mass and world-space inverse inertia tensor, the two quantities an
        /// impulse solver needs to know how a body responds to an off-centre impulse.
        /// Kinematic bodies get zeroes, which makes them infinitely heavy.
        /// </summary>
        private static void GetInverseMassProperties(Rigidbody rb, out float invMass, out float3x3 invInertiaWorld)
        {
            if (rb.isKinematic)
            {
                invMass         = 0f;
                invInertiaWorld = float3x3.zero;
                return;
            }

            invMass = rb.mass > 1e-9f ? 1f / rb.mass : 0f;

            var it      = rb.inertiaTensor;
            var invDiag = new float3(
                it.x > 1e-9f ? 1f / it.x : 0f,
                it.y > 1e-9f ? 1f / it.y : 0f,
                it.z > 1e-9f ? 1f / it.z : 0f);

            // Unity stores the tensor diagonalised in its principal-axis frame, so
            // rotate it out to world: I⁻¹_world = R · diag(1/I) · Rᵀ
            var q = rb.rotation * rb.inertiaTensorRotation;
            var r = new float3x3(new quaternion(q.x, q.y, q.z, q.w));
            var s = new float3x3(invDiag.x, 0f, 0f,
                                 0f, invDiag.y, 0f,
                                 0f, 0f, invDiag.z);

            invInertiaWorld = math.mul(math.mul(r, s), math.transpose(r));
        }

        private void CreateSolidCollider()
        {
            var child = new GameObject("Wheel_InternalCore");
            child.layer = LayerMask.NameToLayer("Robot");
            child.transform.SetParent(transform, false);

            var coreRadiusMeters = Mathf.Max(0.001f, (radius - maxSquish) * 0.0254f);
            var mLen             = length * 0.0254f;

            if (useBeltMode)
            {
                var col  = child.AddComponent<BoxCollider>();
                col.size = new Vector3(mLen, coreRadiusMeters * 2f, (beltLength - maxSquish * 2) * 0.0254f);
            }
            else
            {
                var col       = child.AddComponent<CapsuleCollider>();
                col.radius    = coreRadiusMeters;
                col.height    = mLen;
                col.direction = 0;
            }

            var coreMat = new PhysicMaterial("WheelCore")
            {
                dynamicFriction = coreFriction,
                staticFriction  = coreFriction,
                bounciness = coreBounce,
                frictionCombine = PhysicMaterialCombine.Average,
                bounceCombine   = PhysicMaterialCombine.Minimum,
            };
            child.GetComponent<Collider>().sharedMaterial = coreMat;
        }

        // ── Motor types ───────────────────────────────────────────────────────
        private struct Motor
        {
            public readonly float MaxStallTorque;
            public readonly float MaxRpm;
            private Motor(float torque, float rpm) { MaxStallTorque = torque; MaxRpm = rpm; }
            public static readonly Motor FullSize  = new(7.0f,  6000f);
            public static readonly Motor Medium    = new(4.5f,  5500f);
            public static readonly Motor Small     = new(1.5f, 11000f);
            public static readonly Motor Tiny      = new(0.1f, 15000f);
            public static readonly Motor Shooter   = new(7.5f, 11000f);
            public static readonly Motor Unstable  = new(25f,  Mathf.Infinity);
            public static readonly Motor Undefined = new(0f,   0f);
        }

        [Serializable] public enum RollerMotor { FullSize, Medium, Small, Tiny, Shooter, Unstable }

        private static Motor GetRollerMotor(RollerMotor m) => m switch
        {
            RollerMotor.FullSize  => Motor.FullSize,
            RollerMotor.Medium    => Motor.Medium,
            RollerMotor.Small     => Motor.Small,
            RollerMotor.Unstable  => Motor.Unstable,
            RollerMotor.Tiny      => Motor.Tiny,
            RollerMotor.Shooter   => Motor.Shooter,
            _                     => Motor.Undefined,
        };

        // ═════════════════════════════════════════════════════════════════════
        // Job data structures
        // ═════════════════════════════════════════════════════════════════════

        private struct RbSnapshot
        {
            public float3 WorldCOM;
            public float3 Velocity;
            public float3 AngularVelocity;
            public float   InvMass;
            public float3x3 InvInertiaWorld;
            public float3 ClosestPoint;
            public float3 AxisNearest;
        }

        private struct ImpulseResult
        {
            public float3 TotalImpulse;
            public float3 ContactPoint;
            /// <summary>Index into _bodyReferences, or -1 when there is no contact.</summary>
            public int    BodySlot;
        }

        /// <summary>
        /// Tuning constants. Stable for the lifetime of the roller.
        ///
        /// The bools carry [MarshalAs(UnmanagedType.U1)] because this struct is
        /// passed by reference through a Burst direct call, and that crosses a
        /// function-pointer boundary where every field must be blittable. C#
        /// bool has no fixed unmanaged width, so Burst rejects it (BC1063)
        /// unless the width is pinned down explicitly.
        /// </summary>
        private struct RollerParams
        {
            public float MRadius;
            public float RotationalVelocity;
            public float MaxStallTorque;
            public float CompressionStiffness;
            public float CompressionDamping;
            public float ScrubPercent;
            public float MotorSlipResponse;
            public float FrictionCoefficient;
            [MarshalAs(UnmanagedType.U1)] public bool FrictionCoupledToNormal;
            [MarshalAs(UnmanagedType.U1)] public bool UseBeltMode;
        }

        /// <summary>Per-FixedUpdate state sampled off the owning rigidbody and transform.</summary>
        private struct RollerFrame
        {
            public float3   SelfCOM;
            public float3   SelfVel;
            public float3   SelfAngVel;
            public float3   RollerPos;
            public float3   RollerRight;
            public float3   RollerUp;
            public float    Dt;
        }

        // ═════════════════════════════════════════════════════════════════════
        // Solver — Burst direct call
        //
        // These are [BurstCompile] static methods, not jobs. Burst compiles
        // them to the same machine code it would generate inside a job, but
        // they are invoked as ordinary function calls from the main thread,
        // so there is no schedule, no sync point, and no safety handle to
        // acquire. That is the right trade for work that was going to block
        // on Complete() in the same frame anyway.
        // ═════════════════════════════════════════════════════════════════════

        [BurstCompile]
        private static class RollerSolver
        {
            [BurstCompile]
            // Every struct parameter of a direct-call entry point must be passed
            // by reference — Burst rejects by-value structs across the managed
            // boundary (BC1064). The arrays are then copied into locals once so
            // the loop below indexes a plain handle instead of going through a
            // readonly reference on every access. NativeArray is a handle over
            // unmanaged memory, so the copy still writes to the same buffer.
            public static void SolveContacts(
                in RollerParams p, in RollerFrame f,
                in NativeArray<RbSnapshot> snapshots,
                in NativeArray<ImpulseResult> results,
                int count)
            {
                var snaps = snapshots;
                var output = results;

                for (int i = 0; i < count; i++)
                    output[i] = SolveOne(p, f, snaps[i], i);
            }

            [BurstCompile]
            public static void SmoothAudio(
                float absRpm, float dt,
                float pitchMultiplier,  in float2 pitchRange,
                float volumeMultiplier, in float2 volumeRange,
                float pitchSmoothSpeed, float volumeSmoothSpeed,
                ref float pitch, ref float volume)
            {
                float targetPitch  = math.clamp(absRpm * pitchMultiplier,  pitchRange.x,  pitchRange.y);
                float targetVolume = math.clamp(absRpm * volumeMultiplier, volumeRange.x, volumeRange.y);

                pitch  = math.lerp(pitch,  targetPitch,  1f - math.exp(-pitchSmoothSpeed  * dt));
                volume = math.lerp(volume, targetVolume, 1f - math.exp(-volumeSmoothSpeed * dt));
            }

            // Shared with the enabled-only viewer so diagnostics need no extra
            // fields or writes in ImpulseResult.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void ContactGeometry(bool useBelt, in RollerFrame frame, in RbSnapshot snap,
                out float3 normal, out float distance)
            {
                if (useBelt)
                {
                    float3 toContact = snap.ClosestPoint - frame.RollerPos;
                    float side = math.dot(toContact, frame.RollerUp);
                    distance = math.abs(side);
                    if (math.abs(side) < 0.0001f)
                        side = math.dot(snap.WorldCOM - frame.RollerPos, frame.RollerUp);
                    normal = frame.RollerUp * (side < 0f ? -1f : 1f);
                }
                else
                {
                    float3 toContact = snap.ClosestPoint - snap.AxisNearest;
                    distance = math.length(toContact);
                    if (distance >= 0.0001f)
                    {
                        normal = toContact / distance;
                    }
                    else
                    {
                        // ClosestPoint can return the axis point from inside a
                        // collider. Use its radial direction, then roller up.
                        float3 radial = snap.WorldCOM - snap.AxisNearest;
                        radial -= math.dot(radial, frame.RollerRight) * frame.RollerRight;
                        normal = math.normalizesafe(radial, frame.RollerUp);
                    }
                }
            }

            private static ImpulseResult SolveOne(in RollerParams P, in RollerFrame F, in RbSnapshot snap, int slot)
            {
                var    noContact    = new ImpulseResult { BodySlot = -1 };
                float3 contactPoint = snap.ClosestPoint;

                // ── Normal & penetration depth ────────────────────────────────
                ContactGeometry(P.UseBeltMode, F, snap, out var normal, out var currentDist);

                if (currentDist >= P.MRadius) return noContact;

                // ── Relative velocity ─────────────────────────────────────────
                var    radiusVector    = normal * P.MRadius;
                var    wheelSurfaceVel = math.cross(F.RollerRight * P.RotationalVelocity, radiusVector);
                float3 selfPointVel    = GetPointVelocity(F.SelfCOM,     F.SelfVel,     F.SelfAngVel,         contactPoint);
                float3 targetPointVel  = GetPointVelocity(snap.WorldCOM, snap.Velocity, snap.AngularVelocity, contactPoint);
                var    relativeVel     = targetPointVel - (selfPointVel + wheelSurfaceVel);

                // ── Compression impulse ───────────────────────────────────────
                // Computed before friction rather than after: with
                // FrictionCoupledToNormal on, the tangential channel is bounded
                // by this contact's normal force, so the normal force has to
                // exist first. Hoisting max(0,·) out of the *Dt is algebraically
                // identical for Dt > 0, so the off path is unchanged.
                float  normalForce;
                float3 compressionImpulse;
                {
                    var penetration   = P.MRadius - currentDist;
                    var normalVel     = math.dot(relativeVel, normal);
                    // math.select eliminates the branch on normalVel sign,
                    // letting Burst emit a conditional move instruction.
                    var activeDamping = math.select(P.CompressionDamping, 0f, normalVel > 0f);
                    var springForce   = penetration * P.CompressionStiffness - normalVel * activeDamping;
                    normalForce        = math.max(0f, springForce);
                    compressionImpulse = normal * (normalForce * F.Dt);
                }

                // ── Friction impulse ──────────────────────────────────────────
                float3 frictionImpulse = float3.zero;
                {
                    // 1. Isolate the tangential velocity vector from the normal
                    float3 tangentialVel = relativeVel - math.dot(relativeVel, normal) * normal;

                    // 2. Separate tangential velocity into Longitudinal (Forward) and Lateral (Right) components
                    float lateralSpeed       = math.dot(tangentialVel, F.RollerRight);
                    float3 lateralVel        = F.RollerRight * lateralSpeed;
                    float3 longitudinalVel   = tangentialVel - lateralVel;

                    float slipSpeed = math.length(longitudinalVel);
                    if (slipSpeed > 1e-5f)
                    {
                        float3 slipDir = longitudinalVel / slipSpeed;

                        // 3. Effective mass along the slip direction. The impulse lands off
                        //    the piece's centre of mass, so part of it becomes spin rather
                        //    than linear motion; the angular term accounts for that. Using
                        //    the plain linear mass overstates what it takes to close the
                        //    slip, so the contact overshoots and buzzes instead of settling.
                        //    The mount is deliberately not part of this — see InvEffectiveMass.
                        float k = InvEffectiveMass(snap, contactPoint, slipDir);

                        if (k > 1e-9f)
                        {
                            // Impulse that would exactly match the contact point to the
                            // wheel surface in a single step.
                            float matchImpulse = slipSpeed / k;

                            // 4. Motor torque/speed curve. A motor makes stall torque when its
                            //    shaft is held still and nothing at free speed; the commanded
                            //    rpm is the free speed of the geared assembly, so the torque
                            //    available at the contact falls off linearly as the slip
                            //    closes. Back-driven — piece moving faster than the surface —
                            //    the same curve brakes it, which is what pulls an overspeeding
                            //    piece back down to the roller's speed instead of coasting.
                            //    A stopped roller (tip speed 0) holds full stall torque.
                            //
                            //    The falloff is compressed into the last MotorSlipResponse of
                            //    the surface speed rather than spanning all of it. Ramping over
                            //    the full range is only correct for a bare motor: it leaves the
                            //    roller delivering ~20% of stall once a piece is within 20% of
                            //    surface speed, so the match converges asymptotically and never
                            //    completes in the contact time one roller in a line actually
                            //    gets. Real rollers are flywheeled and hold near stall through
                            //    the whole contact; that inertia isn't simulated here, so the
                            //    curve stands in for it.
                            float tipSpeed     = math.abs(P.RotationalVelocity) * P.MRadius;
                            float slipRatio    = tipSpeed > 1e-4f ? slipSpeed / (tipSpeed * P.MotorSlipResponse) : 1f;
                            float motorImpulse = (P.MaxStallTorque / P.MRadius) * math.saturate(slipRatio) * F.Dt;

                            // Never push past the match: the motor may not overshoot the surface speed.
                            float impulseMag = math.min(matchImpulse, motorImpulse);

                            // 5. Coulomb cone, applied to the motored channel only.
                            //    Without it the tangential impulse is bounded by
                            //    motor torque alone, so grip is identical whether
                            //    the piece is grazing the outer millimetre or
                            //    squished to the core — the whole authority can
                            //    land in one frame, and it is still at full
                            //    strength on whichever frame the piece happens to
                            //    leave contact. That release step is a large,
                            //    nearly-tangential kick at an arbitrarily-timed
                            //    moment, and it shows up as exit spread.
                            //
                            //    Bounding by mu*N makes grip follow the squish: it
                            //    ramps in as the piece compresses and back to zero
                            //    at release, and spreads delivery across the whole
                            //    contact so the per-frame sampling of `normal` and
                            //    `rB` averages out instead of deciding the shot in
                            //    a single step. This reshapes the envelope rather
                            //    than reducing the area under it, so backspin
                            //    authority survives.
                            //
                            //    Off by default. At low CompressionStiffness the
                            //    cap is far tighter than the motor limit, so
                            //    enabling it without raising stiffness alongside
                            //    reads as lost authority rather than as a fix.
                            //    A roller with CompressionStiffness 0 gets zero
                            //    grip from this path, not unlimited grip.
                            if (P.FrictionCoupledToNormal)
                                impulseMag = math.min(impulseMag, P.FrictionCoefficient * normalForce * F.Dt);

                            frictionImpulse = -slipDir * impulseMag;
                        }
                    }

                    // 6. Lateral scrub is a separate, unmotored channel, and it is
                    //    deliberately left outside the cone above. Scrub is a
                    //    stabiliser whose magnitude is proportional to the lateral
                    //    velocity it is cancelling, so when it is working that
                    //    velocity is already near zero by release and it cannot
                    //    produce the release discontinuity the cone exists to
                    //    remove. Putting it inside the cone instead throttles the
                    //    lateral hold in exact proportion to how hard the cone is
                    //    biting, which scatters the piece sideways precisely when
                    //    grip is most limited.
                    //
                    //    Sized off the same inverse effective mass as the
                    //    longitudinal channel, not off the target's linear mass.
                    //    The old form ignored the angular term — the contact is off
                    //    the piece's centre of mass, so part of any impulse becomes
                    //    spin and the linear mass overstates what is needed — and
                    //    ignored the reaction landing on the parent body, which
                    //    moves the roller under the piece and closes the relative
                    //    velocity further still. Both errors point the same way:
                    //    the impulse overshot, reversed the relative lateral
                    //    velocity, and injected energy at a rate set by the
                    //    parent's mass and by how far the contact sat from either
                    //    centre of mass. That is why turning scrub down read as
                    //    more consistent — it was buying its way out of an
                    //    overshoot, and paying for it with the lateral hold.
                    //
                    //    ScrubPercent is now literally a percentage of exact
                    //    cancellation: 100 lands on zero relative lateral velocity
                    //    and cannot pass it, and nothing here scales with the
                    //    timestep any more.
                    float absLateral = math.abs(lateralSpeed);
                    if (absLateral > 1e-5f)
                    {
                        float3 lateralDir = F.RollerRight * math.sign(lateralSpeed);
                        float  kLat       = InvEffectiveMass(snap, contactPoint, lateralDir);

                        if (kLat > 1e-9f)
                            frictionImpulse -= lateralDir *
                                (absLateral / kLat * math.saturate(P.ScrubPercent * 0.01f));
                    }
                }

                return new ImpulseResult
                {
                    TotalImpulse = compressionImpulse + frictionImpulse,
                    ContactPoint = contactPoint,
                    BodySlot     = slot,
                };
            }

            /// <summary>
            /// Inverse effective mass of the <em>game piece</em> at the contact along
            /// <paramref name="dir"/>: how much its point velocity changes per unit impulse.
            /// Carries the angular term, so an impulse landing away from the piece's centre of
            /// mass is not costed as though it were purely linear. Dividing a relative speed by
            /// this gives the impulse that cancels exactly that speed and no more.
            /// </summary>
            /// <remarks>
            /// The roller's own mount is deliberately absent. It used to be added here as a free
            /// body, which is what a mount that is genuinely floating would contribute — but a
            /// roller is bolted to a mechanism, and where that mechanism is a jointed sub-body the
            /// free-body figure is badly wrong. PhysX applies the reaction and then runs the
            /// constraint solver within the same step, so the joint resists it immediately; the
            /// mount never recoils the way its own mass alone predicts.
            ///
            /// Left in, the term scaled every impulse by which sub-body a roller happened to hang
            /// off. On 2910B the chassis is 98 kg and the hood and intake pivots are 1-3 kg, so a
            /// pivot-mounted roller carried ~100x the mount compliance of a chassis-mounted one —
            /// plus an angular term that, for a light body with the contact well off its centre of
            /// mass, can exceed the linear one outright. Grip and lateral hold ended up set by
            /// masses chosen for joint tuning, with no intended relation to either.
            ///
            /// Sizing from the piece alone asks the question that actually matters — how much
            /// impulse changes the piece's velocity by this much — and makes a roller behave the
            /// same wherever it is mounted. The reaction is still applied to the mount; what the
            /// mount then does with it is the joint solver's business, not this function's.
            /// </remarks>
            private static float InvEffectiveMass(in RbSnapshot snap, float3 contactPoint, float3 dir)
            {
                float3 r     = contactPoint - snap.WorldCOM;
                float3 cross = math.cross(r, dir);

                return snap.InvMass + math.dot(cross, math.mul(snap.InvInertiaWorld, cross));
            }

            private static float3 GetPointVelocity(float3 com, float3 vel, float3 angVel, float3 point)
                => vel + math.cross(angVel, point - com);
        }
    }

    [Serializable]
    public class RollerModel
    {
        [Header("Roller Model")]
        [SerializeField] private GameObject model;
        [SerializeField] private JointAxis jointAxis;
        [SerializeField] private bool reversed;
        [SerializeField] private float multiplier = 1f;

        public GameObject GetModel() => model;
        public bool IsReversed() => reversed;
        public float GetMp() => multiplier;

        public Vector3 GetRotationalAxis()
        {
            Vector3 axis = new Vector3(jointAxis == JointAxis.X ? 1 : 0, jointAxis == JointAxis.Y ? 1 : 0,
                jointAxis == JointAxis.Z ? 1 : 0);
            return axis;
        }
    }
}
