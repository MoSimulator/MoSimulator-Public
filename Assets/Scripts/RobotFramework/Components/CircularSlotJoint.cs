using UnityEngine;

namespace RobotFramework.Components
{
    /// <summary>
    /// A captive pin in a circular slot. Rebuilds a tangent constraint before each
    /// physics step; PhysX solves its reactions together with contacts and hinges.
    /// Geometry is in the slotted body's local space. No poses are prescribed.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CircularSlotJoint : MonoBehaviour
    {
        [SerializeField] private ConfigurableJoint constraint;
        [SerializeField] private Rigidbody pinBody;
        [SerializeField] private Vector3 pinAnchor;
        [SerializeField] private Vector3 center;
        [SerializeField] private Vector3 startRadius;
        [SerializeField] private Vector3 planeNormal = Vector3.right;
        [SerializeField, Range(0.01f, 179f)] private float sweepDegrees;
        [SerializeField] private Collider[] pinColliders = new Collider[0];
        [SerializeField] private Collider[] trackColliders = new Collider[0];
        private readonly System.Collections.Generic.List<(Collider, Collider)> ignoredPairs =
            new System.Collections.Generic.List<(Collider, Collider)>();

        public ConfigurableJoint Constraint => constraint;
        public Rigidbody PinBody => pinBody;
        public Vector3 PinWorld => pinBody.transform.TransformPoint(pinAnchor);

        public void Configure(ConfigurableJoint joint, Rigidbody pin, Vector3 anchor,
            Vector3 arcCenter, Vector3 radius, Vector3 normal, float sweep,
            Collider[] pinContacts, Collider[] trackContacts)
        {
            constraint = joint;
            pinBody = pin;
            pinAnchor = anchor;
            center = arcCenter;
            startRadius = radius;
            planeNormal = normal.normalized;
            sweepDegrees = sweep;
            pinColliders = pinContacts;
            trackColliders = trackContacts;
            UpdateConstraint();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            ApplyCollisionFilter();
            UpdateConstraint();
        }

        public void ApplyCollisionFilter()
        {
            foreach (var pin in pinColliders)
            foreach (var track in trackColliders)
                if (pin && track && !Physics.GetIgnoreCollision(pin, track))
                {
                    Physics.IgnoreCollision(pin, track);
                    ignoredPairs.Add((pin, track));
                }
        }

        private void Start() => UpdateConstraint();
        private void FixedUpdate() => UpdateConstraint();

        public Vector3 ClosestPoint(Vector3 localPoint)
        {
            var offset = Vector3.ProjectOnPlane(localPoint - center, planeNormal);
            float angle = offset.sqrMagnitude < 1e-12f ? 0f :
                Vector3.SignedAngle(startRadius, offset, planeNormal);
            // Sweep is less than 180 degrees. Points on the opposite side of the
            // circle must choose the nearer endpoint, not wrap onto the arc.
            if (angle < 0f || angle > sweepDegrees)
            {
                var end = Quaternion.AngleAxis(sweepDegrees, planeNormal) * startRadius;
                return center + ((offset - startRadius).sqrMagnitude <= (offset - end).sqrMagnitude
                    ? startRadius : end);
            }
            return center + Quaternion.AngleAxis(angle, planeNormal) * startRadius;
        }

        public void UpdateConstraint()
        {
            if (!constraint || !pinBody || startRadius.sqrMagnitude < 1e-12f) return;
            var closest = ClosestPoint(transform.InverseTransformPoint(PinWorld));
            var tangent = Vector3.Cross(planeNormal, closest - center).normalized;
            var end = center + Quaternion.AngleAxis(sweepDegrees, planeNormal) * startRadius;
            float first = Vector3.Dot(center + startRadius - closest, tangent);
            float last = Vector3.Dot(end - closest, tangent);
            constraint.autoConfigureConnectedAnchor = false;
            constraint.connectedBody = pinBody;
            constraint.connectedAnchor = pinAnchor;
            constraint.configuredInWorldSpace = false;
            constraint.axis = tangent;
            constraint.secondaryAxis = planeNormal;
            constraint.anchor = closest + tangent * ((first + last) * .5f);
            constraint.xMotion = ConfigurableJointMotion.Limited;
            constraint.yMotion = constraint.zMotion = ConfigurableJointMotion.Locked;
            constraint.angularXMotion = constraint.angularYMotion = constraint.angularZMotion = ConfigurableJointMotion.Free;
            // Joint limits are distances in world units, even for scaled models.
            constraint.linearLimit = new SoftJointLimit
            {
                limit = transform.TransformVector(tangent * ((last - first) * .5f)).magnitude,
                bounciness = 0f, contactDistance = .0002f
            };
            constraint.linearLimitSpring = new SoftJointLimitSpring();
            // JointStackStabilizer adds damping to all ConfigurableJoints. A slot
            // must remain passive and free to slide and rotate, including on enable.
            constraint.xDrive = constraint.yDrive = constraint.zDrive = new JointDrive();
            constraint.angularXDrive = constraint.angularYZDrive = constraint.slerpDrive = new JointDrive();
            constraint.projectionMode = JointProjectionMode.None;
            constraint.enablePreprocessing = false;
        }

        private void OnDisable()
        {
            foreach (var pair in ignoredPairs)
                if (pair.Item1 && pair.Item2) Physics.IgnoreCollision(pair.Item1, pair.Item2, false);
            ignoredPairs.Clear();
            if (!constraint) return;
            constraint.xMotion = constraint.yMotion = constraint.zMotion = ConfigurableJointMotion.Free;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 previous = transform.TransformPoint(center + startRadius);
            for (int i = 1; i <= 32; i++)
            {
                var next = transform.TransformPoint(center + Quaternion.AngleAxis(sweepDegrees * i / 32f, planeNormal) * startRadius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
            if (pinBody) Gizmos.DrawWireSphere(PinWorld, .003f);
        }
    }
}
