using System;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Interfaces;
using UnityEngine;

namespace RobotFramework.Components
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(HingeJoint))]
    public class GenericRoller : MonoBehaviour, IRoller
    {
        private Rigidbody _rb;

        private HingeJoint _hj;

        private float targetVelocity;
        private bool overidenVelocity;
    
        // Start is called before the first frame update
        void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _hj = GetComponent<HingeJoint>();

            targetVelocity = _hj.motor.targetVelocity;
        }

        // Update is called once per frame
        void FixedUpdate()
        {
       
            if (BaseGameManager.Instance.RobotState == RobotState.Disabled)
            {
                JointMotor jm = _hj.motor;
                jm.targetVelocity = 0;
                _hj.motor = jm;
                return;
            }

            if (overidenVelocity)
            {
                overidenVelocity = false;
                return;
            }
        
            if (!Mathf.Approximately(_hj.motor.targetVelocity, targetVelocity))
            {
                JointMotor jm = _hj.motor;
                jm.targetVelocity = targetVelocity;
                _hj.motor = jm;
            }
        }
        /// <summary>
        /// Set the constant angular velocity
        /// </summary>
        /// <param name="velocity"></param>
        /// <returns></returns>
        public MoSimResult<MoSimLib.Void> SetRpm(float velocity)
        {
            var version = MoSim.ActiveVersion;

            SetRpmV1(velocity);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetRpmV1(float velocity)
        {
            targetVelocity = velocity;
        }

        /// <summary>
        /// Flip the direction of travel when called, this is non destructive and will revert the first frame it is not called.
        /// </summary>
        public MoSimResult<bool> FlipRpm()
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<bool>.Success(FlipRpmV1());
        }

        private bool FlipRpmV1()
        {
            JointMotor jm = _hj.motor;
            jm.targetVelocity = -targetVelocity;
            _hj.motor = jm;
            overidenVelocity = true;
            return true;
        }

        /// <summary>
        /// set the velocity to 0 without overiding the current setpoint. it will return to setpoint velocity the first frame this is not called
        /// </summary>
        public MoSimResult<MoSimLib.Void> Stop()
        {
            var version = MoSim.ActiveVersion;

            StopV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void StopV1()
        {
            JointMotor jm = _hj.motor;
            jm.targetVelocity = 0;
            _hj.motor = jm;
            overidenVelocity = true;
        }

        /// <summary>
        /// Set the angular velocity until the next frame this is not called at which point it returns to the default value
        /// </summary>
        /// <param name="rpm"></param>
        public MoSimResult<bool> OverrideRpm(float rpm)
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<bool>.Success(OverrideRpmV1(rpm));
        }

        private bool OverrideRpmV1(float rpm)
        {
            JointMotor jm = _hj.motor;
            jm.targetVelocity = rpm;
            _hj.motor = jm;
            overidenVelocity = true;
            return true;
        }
        
        /// <summary>
        /// Scale this frame's motor velocity by 1/divider without overriding the current setpoint.
        /// It will return to setpoint velocity the first frame this is not called. Stacks on top of
        /// a Stop/Flip/Override already applied this frame, since that value is what is currently
        /// staged on the joint motor.
        /// </summary>
        public MoSimResult<bool> DivideSpeed(float divider)
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<bool>.Success(DivideSpeedV1(divider));
        }

        private bool DivideSpeedV1(float divider)
        {
            if (Mathf.Abs(divider) < 1e-6f) return false;

            JointMotor jm = _hj.motor;
            jm.targetVelocity = (overidenVelocity ? jm.targetVelocity : targetVelocity) / divider;
            _hj.motor = jm;
            overidenVelocity = true;
            return true;
        }

        /// <summary>
        /// This implementation is a placeholder for Group-based logic.
        /// It returns a reference to the current instance to prevent breakage when swapping
        /// </summary>
        [Obsolete("This method is reserved for Group-only contexts. Use with caution.")]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public MoSimResult<IRoller> GetRoller(int id)
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<IRoller>.Success(GetRollerV1(id));
        }

        private IRoller GetRollerV1(int id)
        {
            return this;
        }

        /// <summary>
        /// THIS DOES NOTHING. Generic Rollers DO NOT POSSESS a MOTOR type
        /// </summary>
        /// <param name="selectedMotor"></param>
        [Obsolete]
        public MoSimResult<MoSimLib.Void> ChangeMotor(InteractionRoller.RollerMotor selectedMotor)
        {
            var version = MoSim.ActiveVersion;

            ChangeMotorV1(selectedMotor);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void ChangeMotorV1(InteractionRoller.RollerMotor selectedMotor)
        {
            return;
        }
    }
}
