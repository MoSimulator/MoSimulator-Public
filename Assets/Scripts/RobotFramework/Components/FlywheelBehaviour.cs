using System;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Interfaces;
using UnityEngine;

namespace RobotFramework.Components
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(InteractionRoller))]
    public class FlywheelBehaviour : MonoBehaviour, IRoller
    {
        #region Constants
        private const float c_AtSpeedRpmFloor = 10f;
        #endregion

        #region Serialized Fields
        [Header("Spin Profile")]
        [SerializeField, Range(0.05f, 5f)] private float m_SpinUpTime = 1.2f;
        [SerializeField, Range(0.05f, 5f)] private float m_SpinDownTime = 0.6f;

        [Header("Ball-Through Variation")]
        [Tooltip("Fraction of current RPM lost per ball contact (0.08 = 8%).")]
        [SerializeField, Range(0f, 0.5f)] private float m_BallDipFraction = 0.08f;
        [SerializeField, Range(0.02f, 2f)] private float m_RecoveryTime = 0.25f;

        [Header("Ball Detection")]
        [Tooltip("Leave at Nothing to react to any non-self rigidbody.")]
        [SerializeField] private LayerMask m_GamePieceLayers;

        [Header("At-Speed Tolerance")]
        [Tooltip("Fraction of target RPM (0.02 = within 2%).")]
        [SerializeField, Range(0f, 0.2f)] private float m_AtSpeedTolerance = 0.02f;
        #endregion

        #region Private Fields
        private InteractionRoller m_Roller;
        private Rigidbody m_SelfRb;

        private float m_TargetRpm;
        private float m_CurrentRpm;
        private float m_SmoothVelocity;
        private float m_DipRpm;
        private float m_DipVelocity;
        private float? m_OverrideRpm;
        #endregion

        #region Public Properties
        public float CurrentRpm => m_CurrentRpm;
        public float TargetRpm  => m_TargetRpm;

        public bool IsAtSpeed
        {
            get
            {
                if (Mathf.Abs(m_TargetRpm) < c_AtSpeedRpmFloor)
                    return Mathf.Abs(m_CurrentRpm) < c_AtSpeedRpmFloor;

                return Mathf.Abs((m_CurrentRpm - m_TargetRpm) / m_TargetRpm) <= m_AtSpeedTolerance;
            }
        }
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            m_Roller = GetComponent<InteractionRoller>();
            m_SelfRb = GetComponentInParent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (BaseGameManager.Instance is null || BaseGameManager.Instance.RobotState == RobotState.Disabled)
            {
                m_TargetRpm   = 0f;
                m_OverrideRpm = null;
            }

            m_DipRpm = Mathf.SmoothDamp(m_DipRpm, 0f, ref m_DipVelocity, m_RecoveryTime, Mathf.Infinity, Time.fixedDeltaTime);

            if (m_OverrideRpm.HasValue)
            {
                // Keep the internal model coherent with what we actually commanded,
                // so that when the override clears we don't snap back to a stale value.
                float overrideRpm = m_OverrideRpm.Value;
                m_Roller.SetRpm(overrideRpm + m_DipRpm);
                m_CurrentRpm     = overrideRpm;
                m_SmoothVelocity = 0f;
                m_OverrideRpm    = null;
                return;
            }

            // Spin-up and spin-down use separate time constants
            float smoothTime = Mathf.Abs(m_CurrentRpm) < Mathf.Abs(m_TargetRpm)
                ? m_SpinUpTime
                : m_SpinDownTime;

            m_CurrentRpm = Mathf.SmoothDamp(m_CurrentRpm, m_TargetRpm, ref m_SmoothVelocity, smoothTime, Mathf.Infinity, Time.fixedDeltaTime);

            m_Roller.SetRpm(m_CurrentRpm + m_DipRpm);
        }

        private void OnTriggerEnter(Collider _other)
        {
            if (!IsGamePiece(_other)) return;
            // Signed subtraction so the dip always opposes the current spin direction.
            m_DipRpm -= m_CurrentRpm * m_BallDipFraction;
        }
        #endregion

        #region Private Methods
        private bool IsGamePiece(Collider _other)
        {
            var rb = _other.attachedRigidbody;
            if (!rb || rb == m_SelfRb) return false;
            if (m_GamePieceLayers == 0) return true;
            return (m_GamePieceLayers.value & (1 << _other.gameObject.layer)) != 0;
        }
        #endregion

        #region IRoller
        public MoSimResult<MoSimLib.Void> SetRpm(float _rpm)
        {
            var version = MoSim.ActiveVersion;
            SetRpmV1(_rpm);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetRpmV1(float _rpm) => m_TargetRpm = _rpm;

        public MoSimResult<MoSimLib.Void> Stop()
        {
            var version = MoSim.ActiveVersion;
            StopV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void StopV1() => m_TargetRpm = 0f;

        public MoSimResult<bool> FlipRpm()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(FlipRpmV1());
        }

        private bool FlipRpmV1() => OverrideRpmV1(-m_TargetRpm);

        public MoSimResult<bool> OverrideRpm(float _rpm)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(OverrideRpmV1(_rpm));
        }

        private bool OverrideRpmV1(float _rpm)
        {
            if (m_OverrideRpm.HasValue) return false;
            m_OverrideRpm = _rpm;
            return true;
        }

        /// <summary>
        /// Scales this frame's commanded rpm by 1/divider without disturbing the target.
        /// Stacks on an override already staged this frame.
        /// </summary>
        public MoSimResult<bool> DivideSpeed(float _divider)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(DivideSpeedV1(_divider));
        }

        private bool DivideSpeedV1(float _divider)
        {
            if (Mathf.Abs(_divider) < 1e-6f) return false;
            m_OverrideRpm = (m_OverrideRpm ?? m_TargetRpm) / _divider;
            return true;
        }

        public MoSimResult<MoSimLib.Void> ChangeMotor(InteractionRoller.RollerMotor _selectedMotor)
        {
            var version = MoSim.ActiveVersion;
            ChangeMotorV1(_selectedMotor);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void ChangeMotorV1(InteractionRoller.RollerMotor _selectedMotor) =>
            m_Roller.ChangeMotor(_selectedMotor);

        [Obsolete("Reserved for Group-only contexts.")]
        public MoSimResult<IRoller> GetRoller(int _id)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<IRoller>.Success(GetRollerV1(_id));
        }

        private IRoller GetRollerV1(int _id) => this;
        #endregion
    }
}
