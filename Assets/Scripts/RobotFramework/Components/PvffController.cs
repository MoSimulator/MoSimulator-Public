using System;
using System.Collections.Generic;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Interfaces;
using UnityEngine;

namespace RobotFramework.Components
{
    [RequireComponent(typeof(InteractionRoller))]
    public class PvffController : MonoBehaviour, IRoller
    {
        #region Constants
        private const float c_AtSpeedRpmFloor = 10f;
        #endregion

        #region Serialized Fields
        [Header("Velocity Model")]
        [Tooltip("Baseline roller command as a multiplier of the requested target RPM.")]
        [SerializeField, Range(0f, 2f)] private float m_VelocityFeedForward = 1f;
        [Tooltip("RPM command added per RPM of modeled velocity error.")]
        [SerializeField, Range(0f, 2f)] private float m_ProportionalGain = 0.35f;
        [Tooltip("Maximum modeled flywheel acceleration in RPM per second.")]
        [SerializeField, Min(1f)] private float m_MaxAccelerationRpmPerSecond = 65000f;

        [Header("Game Piece Load")]
        [Tooltip("Leave at Nothing to react to any non-self rigidbody.")]
        [SerializeField] private LayerMask m_GamePieceLayers;
        [Tooltip("Instant RPM drop when a game piece first enters the flywheel contact region.")]
        [SerializeField, Range(0f, 0.5f)] private float m_EntryDipFraction = 0.02f;
        [Tooltip("Approximate fractional RPM drain per second per game piece in contact.")]
        [SerializeField, Range(0f, 2f)] private float m_ContactDragFractionPerSecond = 0.25f;
        [Tooltip("Extra RPM command per game piece currently contacting the flywheel.")]
        [SerializeField, Min(0f)] private float m_ContactFeedForwardRpm = 250f;

        [Header("At-Speed Tolerance")]
        [Tooltip("Fraction of target RPM (0.02 = within 2%).")]
        [SerializeField, Range(0f, 0.2f)] private float m_AtSpeedTolerance = 0.02f;
        #endregion

        #region Private Fields
        private readonly Dictionary<Rigidbody, int> m_ContactCounts = new();

        private InteractionRoller m_Roller;
        private Rigidbody m_SelfRb;
        private float m_TargetRpm;
        private float m_CurrentRpm;
        private float m_CommandedRpm;
        private float? m_OverrideRpm;
        #endregion

        #region Public Properties
        public float CurrentRpm => m_CurrentRpm;
        public float TargetRpm => m_TargetRpm;
        public float CommandedRpm => m_CommandedRpm;

        public bool IsAtSpeed
        {
            get
            {
                if (Mathf.Abs(m_TargetRpm) < c_AtSpeedRpmFloor)
                {
                    return Mathf.Abs(m_CurrentRpm) < c_AtSpeedRpmFloor
                           && Mathf.Abs(m_CommandedRpm) < c_AtSpeedRpmFloor;
                }

                float currentError = Mathf.Abs((m_CurrentRpm - m_TargetRpm) / m_TargetRpm);
                float commandError = Mathf.Abs((m_CommandedRpm - GetSteadyStateCommand() - GetContactFeedForward(m_ContactCounts.Count)) / m_TargetRpm);
                return currentError <= m_AtSpeedTolerance && commandError <= m_AtSpeedTolerance;
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
                ResetController();
                return;
            }

            if (m_OverrideRpm.HasValue)
            {
                float overrideRpm = m_OverrideRpm.Value;
                SetRollerRpm(overrideRpm);
                m_CurrentRpm = overrideRpm;
                m_OverrideRpm = null;
                return;
            }

            UpdateVelocityModel();
        }

        private void OnTriggerEnter(Collider _other)
        {
            if (!IsGamePiece(_other)) return;

            Rigidbody rb = _other.attachedRigidbody;
            if (m_ContactCounts.TryGetValue(rb, out int count))
            {
                m_ContactCounts[rb] = count + 1;
                return;
            }

            m_ContactCounts.Add(rb, 1);
            m_CurrentRpm -= m_CurrentRpm * m_EntryDipFraction;
        }

        private void OnTriggerExit(Collider _other)
        {
            Rigidbody rb = _other.attachedRigidbody;
            if (!rb || !m_ContactCounts.TryGetValue(rb, out int count))
            {
                return;
            }

            if (count <= 1)
            {
                m_ContactCounts.Remove(rb);
                return;
            }

            m_ContactCounts[rb] = count - 1;
        }
        #endregion

        #region IRoller
        public MoSimResult<MoSimLib.Void> SetRpm(float _rpm)
        {
            var version = MoSim.ActiveVersion;
            SetRpmV1(_rpm);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetRpmV1(float _rpm)
        {
            m_TargetRpm = _rpm;
            ApplyRollerCommand();
        }

        public MoSimResult<MoSimLib.Void> Stop()
        {
            var version = MoSim.ActiveVersion;
            StopV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void StopV1()
        {
            m_TargetRpm = 0f;
            ApplyRollerCommand();
        }

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
        /// The override path also pins the modeled current rpm, so the controller does not
        /// fight the divided command with feed-forward.
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

        #region Private Methods
        private void UpdateVelocityModel()
        {
            RemoveDestroyedContacts();

            int contactCount = m_ContactCounts.Count;
            if (contactCount > 0 && !Mathf.Approximately(m_CurrentRpm, 0f))
            {
                float loadDrop = Mathf.Abs(m_CurrentRpm) * m_ContactDragFractionPerSecond * contactCount * Time.fixedDeltaTime;
                m_CurrentRpm -= Mathf.Sign(m_CurrentRpm) * loadDrop;
            }

            float maxDelta = m_MaxAccelerationRpmPerSecond * Time.fixedDeltaTime;
            m_CurrentRpm = Mathf.MoveTowards(m_CurrentRpm, m_TargetRpm, maxDelta);

            ApplyRollerCommand();
        }

        private void ApplyRollerCommand()
        {
            if (!m_Roller || !Application.isPlaying)
            {
                return;
            }

            RemoveDestroyedContacts();

            float velocityError = m_TargetRpm - m_CurrentRpm;
            float commandedRpm = GetSteadyStateCommand()
                                 + (velocityError * m_ProportionalGain)
                                 + GetContactFeedForward(m_ContactCounts.Count);

            SetRollerRpm(commandedRpm);
        }

        private float GetSteadyStateCommand()
        {
            return m_TargetRpm * m_VelocityFeedForward;
        }

        private float GetContactFeedForward(int _contactCount)
        {
            if (Mathf.Abs(m_TargetRpm) < c_AtSpeedRpmFloor)
            {
                return 0f;
            }

            return Mathf.Sign(m_TargetRpm) * m_ContactFeedForwardRpm * _contactCount;
        }

        private void SetRollerRpm(float _rpm)
        {
            m_CommandedRpm = _rpm;
            m_Roller.SetRpm(_rpm);
        }

        private void ResetController()
        {
            m_TargetRpm = 0f;
            m_CurrentRpm = 0f;
            m_CommandedRpm = 0f;
            m_OverrideRpm = null;
            m_ContactCounts.Clear();
            m_Roller.SetRpm(0f);
        }

        private bool IsGamePiece(Collider _other)
        {
            Rigidbody rb = _other.attachedRigidbody;
            if (!rb || rb == m_SelfRb) return false;
            if (m_GamePieceLayers == 0) return true;
            return (m_GamePieceLayers.value & (1 << _other.gameObject.layer)) != 0;
        }

        private void RemoveDestroyedContacts()
        {
            s_ContactsToRemove.Clear();
            foreach (Rigidbody rb in m_ContactCounts.Keys)
            {
                if (!rb)
                {
                    s_ContactsToRemove.Add(rb);
                }
            }

            foreach (Rigidbody rb in s_ContactsToRemove)
            {
                m_ContactCounts.Remove(rb);
            }
        }
        #endregion

        #region Static Fields
        private static readonly List<Rigidbody> s_ContactsToRemove = new();
        #endregion
    }
}
