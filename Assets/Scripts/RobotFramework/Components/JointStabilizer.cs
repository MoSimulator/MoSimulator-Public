using System.Collections.Generic;
using MoSimLib;
using UnityEngine;

namespace RobotFramework.Components
{
    /// <summary>
    /// Configures physics solver settings for joint hierarchies to ensure stability.
    /// Prevents joint oscillations and flipping through damping, solver iterations, and spring settings.
    /// Automatically attached to robots during initialization.
    /// </summary>
    public class JointStackStabilizer : MonoBehaviour
    {
        [Header("Joint Settings")]
        [Tooltip("Angular damping to reduce oscillations (0.95 recommended).")]
        public float angularDamping = 0.95f;
        
        [Tooltip("Maximum angular velocity to prevent extreme rotations.")]
        public float maxAngularVelocity = 6500f;

        [Header("Solver Settings")]
        [Tooltip("Physics solver iterations for more accurate joint simulation.")]
        public int solverIterations = 12;
        
        [Tooltip("Velocity solver iterations for smooth motion.")]
        public int solverVelocityIterations = 8;

        private List<Rigidbody> rigidbodies = new List<Rigidbody>();
        private List<Joint> joints = new List<Joint>();
        private float deltaTime;
        public float springDampingRatio = 1f;
        public float springForce = 999999f;

        private void Awake()
        {
            // Get all Rigidbodies and Joints in the hierarchy, including the current object.
            GetComponentsInChildren<Rigidbody>(rigidbodies);
            GetComponentsInChildren<Joint>(joints);

            // Apply initial settings.  These are applied once, in Start.
            foreach (Rigidbody rb in rigidbodies)
            {
                rb.maxAngularVelocity = maxAngularVelocity;
                rb.solverIterations = solverIterations;
                rb.solverVelocityIterations = solverVelocityIterations;
            }

            foreach (Joint joint in joints)
            {
                if (joint is SpringJoint)
                {
                    SpringJoint springJoint = (SpringJoint)joint;
                    springJoint.spring = 0f; //remove spring
                    springJoint.damper = springDampingRatio * 2f * Mathf.Sqrt(springForce * springJoint.massScale);
                }
                else if (joint is ConfigurableJoint)
                {
                    ConfigurableJoint configurableJoint = (ConfigurableJoint)joint;

                    // Store the original motion settings.
                    ConfigurableJointMotion originalXMotion = configurableJoint.angularXMotion;
                    ConfigurableJointMotion originalYMotion = configurableJoint.angularYMotion;
                    ConfigurableJointMotion originalZMotion = configurableJoint.angularZMotion;

                    // Set the spring and damping for the *drive* of the rotation.
                    JointDrive angularXDrive = configurableJoint.angularXDrive;
                    angularXDrive.positionSpring = 0; // Remove spring
                    angularXDrive.positionDamper =  angularXDrive.positionDamper == 0 ? springDampingRatio * 2f * Mathf.Sqrt(springForce * 1) : angularXDrive.positionDamper;
                    configurableJoint.angularXDrive = angularXDrive;

                    JointDrive angularYZDrive = configurableJoint.angularYZDrive;
                    angularYZDrive.positionSpring = 0; // Remove Spring
                    angularYZDrive.positionDamper = angularYZDrive.positionDamper == 0 ? springDampingRatio * 2f * Mathf.Sqrt(springForce * 1) : angularYZDrive.positionDamper;
                    configurableJoint.angularYZDrive = angularYZDrive;

                    // Also set the damping for the linear drive
                    JointDrive linearDrive = configurableJoint.xDrive;
                    linearDrive.positionSpring = 0;
                    linearDrive.positionDamper = linearDrive.positionDamper == 0 ? springDampingRatio * 2f * Mathf.Sqrt(springForce * 1): linearDrive.positionDamper;
                    configurableJoint.xDrive = linearDrive;

                    linearDrive = configurableJoint.yDrive;
                    linearDrive.positionSpring = 0;
                    linearDrive.positionDamper = linearDrive.positionDamper == 0 ? springDampingRatio * 2f * Mathf.Sqrt(springForce * 1): linearDrive.positionDamper;
                    configurableJoint.yDrive = linearDrive;

                    linearDrive = configurableJoint.zDrive;
                    linearDrive.positionSpring = 0;
                    linearDrive.positionDamper = linearDrive.positionDamper == 0 ? springDampingRatio * 2f * Mathf.Sqrt(springForce * 1): linearDrive.positionDamper;
                    configurableJoint.zDrive = linearDrive;

                    // Restore the original motion settings.
                    configurableJoint.angularXMotion = originalXMotion;
                    configurableJoint.angularYMotion = originalYMotion;
                    configurableJoint.angularZMotion = originalZMotion;

                    configurableJoint.linearLimit = configurableJoint.linearLimit.limit == 0 ? new SoftJointLimit() { limit = 180f, bounciness = 0f } : configurableJoint.linearLimit;
                }
            }
            deltaTime = Time.fixedDeltaTime;
        }

        void FixedUpdate()
        {
            foreach (Rigidbody rb in rigidbodies)
            {
                // Clamp angular velocity to prevent explosion.
                rb.maxAngularVelocity = maxAngularVelocity; // Make sure it gets applied every frame
                rb.angularVelocity = Vector3.ClampMagnitude(rb.angularVelocity, maxAngularVelocity);
                rb.solverIterations = solverIterations; //Keep the solver high
                rb.solverVelocityIterations = solverVelocityIterations;
            }

            foreach (Joint joint in joints)
            {
                // Apply damping to the joint's angular velocity.  This is the core of the stabilization.
                if (joint != null && joint.connectedBody != null)
                {
                    Vector3 relativeVelocity = joint.connectedBody.angularVelocity - joint.GetComponent<Rigidbody>().angularVelocity;
                    joint.GetComponent<Rigidbody>().angularVelocity += relativeVelocity * (1 - Mathf.Clamp01(angularDamping));
                }
                if (joint is SpringJoint)
                {
                    SpringJoint springJoint = (SpringJoint)joint;
                    springJoint.spring = 0f; //remove spring
                    springJoint.damper =  springDampingRatio * 2f * Mathf.Sqrt(springForce * springJoint.massScale);
                }
            }
        }

        // Add a public method to add a new joint to the stabilizer
        public MoSimResult<MoSimLib.Void> AddJoint(Joint newJoint)
        {
            var version = MoSim.ActiveVersion;
            AddJointV1(newJoint);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void AddJointV1(Joint newJoint)
        {
            if (newJoint == null) return;

            if (!joints.Contains(newJoint))
            {
                joints.Add(newJoint);
                if (newJoint is SpringJoint)
                {
                    SpringJoint springJoint = (SpringJoint)newJoint;
                    springJoint.spring = 0f; //remove spring
                    springJoint.damper =  springDampingRatio * 2f * Mathf.Sqrt(springForce * springJoint.massScale);
                }
                else if (newJoint is ConfigurableJoint)
                {
                    ConfigurableJoint configurableJoint = (ConfigurableJoint)newJoint;
                    // Store the original motion settings.
                    ConfigurableJointMotion originalXMotion = configurableJoint.angularXMotion;
                    ConfigurableJointMotion originalYMotion = configurableJoint.angularYMotion;
                    ConfigurableJointMotion originalZMotion = configurableJoint.angularZMotion;

                    // Set the spring and damping for the *drive* of the rotation.
                    JointDrive angularXDrive = configurableJoint.angularXDrive;
                    angularXDrive.positionSpring = 0; //remove spring
                    angularXDrive.positionDamper =  springDampingRatio * 2f * Mathf.Sqrt(springForce * 1);
                    configurableJoint.angularXDrive = angularXDrive;

                    JointDrive angularYZDrive = configurableJoint.angularYZDrive;
                    angularYZDrive.positionSpring = 0; // Remove Spring
                    angularYZDrive.positionDamper =  springDampingRatio * 2f * Mathf.Sqrt(springForce * 1);
                    configurableJoint.angularYZDrive = angularYZDrive;

                    JointDrive linearDrive = configurableJoint.xDrive;
                    linearDrive.positionSpring = 0;
                    linearDrive.positionDamper = springDampingRatio * 2f * Mathf.Sqrt(springForce * 1);
                    configurableJoint.xDrive = linearDrive;

                    linearDrive = configurableJoint.yDrive;
                    linearDrive.positionSpring = 0;
                    linearDrive.positionDamper = springDampingRatio * 2f * Mathf.Sqrt(springForce * 1);
                    configurableJoint.yDrive = linearDrive;

                    linearDrive = configurableJoint.zDrive;
                    linearDrive.positionSpring = 0;
                    linearDrive.positionDamper = springDampingRatio * 2f * Mathf.Sqrt(springForce * 1);
                    configurableJoint.zDrive = linearDrive;

                    // Restore the original motion settings.
                    configurableJoint.angularXMotion = originalXMotion;
                    configurableJoint.angularYMotion = originalYMotion;
                    configurableJoint.angularZMotion = originalZMotion;

                    configurableJoint.linearLimit = new SoftJointLimit() { limit = 180f, bounciness = 0f };
                }

            }

            Rigidbody rb = newJoint.GetComponent<Rigidbody>();
            if (rb != null && !rigidbodies.Contains(rb))
            {
                rigidbodies.Add(rb);
                rb.maxAngularVelocity = maxAngularVelocity;
                rb.solverIterations = solverIterations;
                rb.solverVelocityIterations = solverVelocityIterations;
            }
        }

        public MoSimResult<MoSimLib.Void> AddRigidbody(Rigidbody newRigidbody)
        {
            var version = MoSim.ActiveVersion;
            AddRigidbodyV1(newRigidbody);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void AddRigidbodyV1(Rigidbody newRigidbody)
        {
            if (newRigidbody == null) return;

            if (!rigidbodies.Contains(newRigidbody))
            {
                rigidbodies.Add(newRigidbody);
                newRigidbody.maxAngularVelocity = maxAngularVelocity;
                newRigidbody.solverIterations = solverIterations;
                newRigidbody.solverVelocityIterations = solverVelocityIterations;
            }
        }
    }
}

