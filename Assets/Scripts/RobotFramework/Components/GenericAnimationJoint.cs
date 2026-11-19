using System.Collections.Generic;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Enums;
using UnityEngine;

namespace RobotFramework.Components
{
    /// <summary>
    /// Joint that moves child GameObjects away from overlapping colliders using velocity and spring physics.
    /// Useful for preventing mechanism components from clipping through obstacles.
    /// </summary>
    public class GenericAnimationJoint : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Current velocity of this joint.")]
        private float CurrentVelocityF;
        
        [SerializeField]
        [Tooltip("Current force being applied to this joint.")]
        private float CurrentAppliedForce;

        [SerializeField]
        [Tooltip("BoxCollider defining the avoidance region.")]
        private BoxCollider moveAwayCollider;

        private Quaternion startingAngle;

        private VelocityJoint _vJoint;
        private Spring _Sjoint;
        private int _springRequestedFrame = -1;

        private Rigidbody rb;

        private float _currentPosition;
        private float _currentVelocity;

        [HideInInspector] public bool useMomentum;
        [HideInInspector] public float momentumAcceleration;
        [HideInInspector] public JointAxis rollerAxis;

        [HideInInspector] public JointAxis springAxis;
        [HideInInspector] public float allowedDirection;
        [HideInInspector] public float allowedVelocity;
        [HideInInspector] public float hardStopPoint;
        
        private OverlapBoxBounds moveAwayBounds;

        private List<GameObject> objects = new List<GameObject>();

        private GameObject[] _children;

        // Start is called before the first frame update
        void Start()
        {
            _children = new GameObject[transform.childCount];
            _currentPosition = 0;
            for (int i = 0; i < _children.Length; i++)
            {
                _children[i] = transform.GetChild(i).gameObject;
            }

            rb = Utils.FindParentObjectComponent<Rigidbody>(gameObject);

            startingAngle = transform.localRotation;
            _currentVelocity = 0;

            useMomentum = false;
            momentumAcceleration = 0;
            rollerAxis = JointAxis.X;

            springAxis = JointAxis.Y;
            allowedDirection = -1;
            allowedVelocity = 180;
            hardStopPoint = 0;

            _vJoint = new VelocityJoint(this);
            _Sjoint = new Spring(this);

            if (moveAwayCollider != null) moveAwayBounds = new OverlapBoxBounds(moveAwayCollider);
        }
        
        public class VelocityJoint
        {
            private GenericAnimationJoint _genericAnimationJoint;

            public VelocityJoint(GenericAnimationJoint genericAnimationJoint)
            {
                _genericAnimationJoint = genericAnimationJoint;
            }

            public VelocityJoint useAxis(JointAxis axis)
            {
                _genericAnimationJoint.rollerAxis = axis;
                return this;
            }

            public VelocityJoint useMomentum(float acceleration)
            {
                _genericAnimationJoint.useMomentum = true;
                return this;
            }
        }

        public VelocityJoint VelocityRoller(float speed)
        {
            var version = MoSim.ActiveVersion;
            return VelocityRollerV1(speed);
        }

        private VelocityJoint VelocityRollerV1(float speed)
        {
            Vector3 targetAxis;
            switch (rollerAxis)
            {
                case JointAxis.X:
                    targetAxis = Vector3.right;
                    break;
                case JointAxis.Y:
                    targetAxis = Vector3.up;
                    break;
                case JointAxis.Z:
                    targetAxis = Vector3.forward;
                    break;
                default:
                    targetAxis = Vector3.zero;
                    break;
            }

            if (!useMomentum)
            {
                if (BaseGameManager.Instance.RobotState == RobotState.Disabled)
                {
                    return _vJoint;
                }
                transform.Rotate(targetAxis, speed * Time.fixedDeltaTime);
            }
            else
            {
                if (BaseGameManager.Instance.RobotState == RobotState.Disabled)
                {
                    _currentVelocity -= momentumAcceleration;
                }
                if (_currentVelocity < speed)
                {
                    _currentVelocity += momentumAcceleration;
                    _currentVelocity = Mathf.Min(_currentVelocity, speed);
                }
                else if (_currentVelocity > speed)
                {
                    _currentVelocity -= momentumAcceleration;
                    _currentVelocity = Mathf.Max(_currentVelocity, speed);
                }

                transform.Rotate(targetAxis, _currentVelocity * Time.fixedDeltaTime);
            }

            return _vJoint;
        }

        public class Spring
        {
            private GenericAnimationJoint _joint;

            public Spring(GenericAnimationJoint joint)
            {
                _joint = joint;
            }

            public Spring WithAxis(JointAxis freeAxis)
            {
                _joint.springAxis = freeAxis;
                return this;
            }

            public Spring AllowedDirection(float direction)
            {
                _joint.allowedDirection = direction;
                return this;
            }

            public Spring RotationSpeed(float speed)
            {
                _joint.allowedVelocity = speed;
                return this;
            }

            public Spring HardStopPoint(float point)
            {
                _joint.hardStopPoint = point;
                return this;
            }
        }

        /// <summary>
        /// Default state is:
        /// springAxis = JointAxis.Y;
        ///allowedDirection = -1;
        ///allowedVelocity = 180;
        ///hardStopPoint = 0;
        /// In Update, enables simulation for the next frame's physics steps and returns configuration.
        /// In FixedUpdate, advances immediately. Contact and return motion share the physics timestep.
        /// </summary>
        /// <returns></returns>
        public Spring SpringLoaded()
        {
            var version = MoSim.ActiveVersion;
            if (!Time.inFixedTimeStep)
            {
                _springRequestedFrame = Time.frameCount;
                return _Sjoint;
            }

            return SpringLoadedV1();
        }

        private void FixedUpdate()
        {
            // Physics runs before Update. Keep the previous frame's signal through every
            // catch-up step, and expire it when the caller stops requesting spring motion.
            if (_springRequestedFrame >= 0 && _springRequestedFrame >= Time.frameCount - 1)
            {
                SpringLoadedV1();
            }
        }

        private void OnDisable()
        {
            _springRequestedFrame = -1;
        }

        private Spring SpringLoadedV1()
        {
            float deltaTime = Time.fixedDeltaTime;
            Vector3 axis;
            switch (springAxis)
            {
                case JointAxis.X:
                    axis = Vector3.right;
                    break;
                case JointAxis.Y:
                    axis = Vector3.up;
                    break;
                case JointAxis.Z:
                    axis = Vector3.forward;
                    break;
                default:
                    axis = Vector3.right;
                    break;
            }

            Collider[] collisions = moveAwayBounds.OverlapBox();

            bool notMoved = true;
            objects.Clear();
            if (collisions.Length > 0)
            {
                foreach (var collision in collisions)
                {
                    objects.Add(collision.gameObject);
                    if (collision.gameObject != gameObject)
                    {
                        foreach (var child in _children)
                        {
                            if (collision.gameObject != child && collision != moveAwayCollider &&
                                collision.gameObject.layer != gameObject.layer &&
                                collision.gameObject.layer != LayerMask.NameToLayer("Scoring"))
                            {
                                var speed = allowedVelocity + (rb.velocity.magnitude);
                                float rotation = allowedDirection * speed * deltaTime;
                                transform.Rotate(axis, rotation);
                                _currentPosition += rotation;
                                // Preserve the legacy release impulse, which stores the contact step.
                                _currentVelocity = rotation;
                                notMoved = false;
                            }
                        }
                    }
                }
            }

            if (notMoved)
            {
                float startAngle = GetAxisAngle(springAxis, startingAngle);
                float currentAngle = GetAxisAngle(springAxis, transform.localRotation);
                float angleDifference = AngleDifference(startAngle, currentAngle);
                var springForce = -allowedVelocity * angleDifference;

                float dampingFactor = 0.5f;
                var dampingForce = -dampingFactor * _currentVelocity;

                var netForce = springForce + dampingForce;

                _currentVelocity += netForce * deltaTime;


                if (Mathf.DeltaAngle(currentAngle, hardStopPoint) > 0 && _currentVelocity > 0)
                {
                    _currentVelocity = 0f;
                }
                else if (Mathf.DeltaAngle(currentAngle, hardStopPoint) < 0 && _currentVelocity < 0)
                {
                    _currentVelocity = 0f;
                }

                CurrentVelocityF = _currentVelocity;
                CurrentAppliedForce = netForce;
                transform.Rotate(axis, -_currentVelocity * deltaTime);
            }

            return _Sjoint;
        }

        public MoSimResult<float> AngleDifference(float a, float b)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<float>.Success(AngleDifferenceV1(a, b));
        }

        private float AngleDifferenceV1(float a, float b)
        {
            return (a - b + 540) % 360 - 180;
        }

        public MoSimResult<float> GetAxisAngle(JointAxis jointAxis, Quaternion quaternion)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<float>.Success(GetAxisAngleV1(jointAxis, quaternion));
        }

        private float GetAxisAngleV1(JointAxis jointAxis, Quaternion quaternion)
        {
            Vector3 targetAxis;

            switch (jointAxis)
            {
                case JointAxis.X:
                    targetAxis = Vector3.right;
                    break;
                case JointAxis.Y:
                    targetAxis = Vector3.up;
                    break;
                case JointAxis.Z:
                    targetAxis = Vector3.forward;
                    break;
                default:
                    targetAxis = Vector3.zero;
                    break;
            }

            Quaternion deltaRotation = quaternion;

            deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);


            float projection = Vector3.Dot(targetAxis, axis);

            //    - The signed angle around our target axis
            float signedAngle = angle * projection;

            signedAngle = Mathf.Repeat(signedAngle, 360);

            return signedAngle;
        }
    }
}
