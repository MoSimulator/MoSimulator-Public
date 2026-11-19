using Games.Rebuilt.Components;
using Games.Rebuilt.Robots;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using UnityEngine;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;

namespace Prefabs.Games.Rebuilt.Robots.CC
{
    public class CompetitiveConcept : RebuiltRobotBase
    {
        [Header("Joints")]
        [SerializeField] private RollerGroup feederRollers;
        [SerializeField] private GenericJoint intakeJoint;
        [SerializeField] private GenericJoint hopperJoint;
        [SerializeField] private GenericJoint hoodJoint;
        [SerializeField] private GenericJoint climber;
        [SerializeField] private RollerGroup intakeRollers;
        [SerializeField] private RollerGroup indexerRollers;
        [SerializeField] private RollerGroup shooterRoller;

        [Header("PIDS")]
        [SerializeField] private PidConstants intakePidConstants;
        [SerializeField] private PidConstants hopperPidConstants;
        [SerializeField] private PidConstants hoodPidConstants;
        [SerializeField] private PidConstants climberPidConstants;


        [Header("Interpolation Values")]
        [SerializeField] private RebuiltInterpolation FlyWheelInterpolation;
        [SerializeField] private RebuiltInterpolation HoodInterpolation;

        [Header("Intake Values")]
        [SerializeField] private float intakeAngle;

        [Header("Hopper Values")]
        [SerializeField] private float extendedDistance;



        private float _targetIntakeAngle;
        private bool _intakeDeployed;
        private bool _intakeWobble;

        private float _targetHoodAngle;

        private RebuiltAutoAimController _aim;

        private bool _deployClimb;

        private float _climberTarget;

        protected override void Init()
        {
            intakeJoint.SetPid(intakePidConstants);
            hopperJoint.SetPid(hopperPidConstants);
            hoodJoint.SetPid(hoodPidConstants);
            climber.SetPid(climberPidConstants);

            Scheduler.AddSubsystems(Intake, Hopper, Shooter, HandleAutoAim, Climber);
            Scheduler.AddProtectedSystem(UpdateJoints);
            Scheduler.AddLateRecursive(UpdatePids);

            _intakeDeployed = false;
            _targetIntakeAngle = 0;
            _targetHoodAngle = 0;

            _aim = GetComponent<RebuiltAutoAimController>();

            _deployClimb = false;

            FlyWheelInterpolation.Initialize(this);
            HoodInterpolation.Initialize(this);
        }

        private void UpdatePids()
        {
            intakeJoint.UpdatePid(intakePidConstants);
            hopperJoint.UpdatePid(hopperPidConstants);
            hoodJoint.UpdatePid(hoodPidConstants);
            climber.UpdatePid(climberPidConstants);
        }

        private void Climber()
        {
            if (ClimbAction.WasTriggered() && BaseGameManager.Instance.RobotState != RobotState.Disabled)
            {
                _deployClimb = !_deployClimb;
            }

            _climberTarget = _deployClimb ? 5 : 0;
        }

        private void Intake()
        {
            if (_intakeWobble && shooterRoller.AtSpeed())
            {
                WobbleIntake();
            }
            else if (IntakeAction || _intakeDeployed)
            {
                _targetIntakeAngle = intakeAngle;
            }

            if (!IntakeAction)
            {
                intakeRollers.Stop();
            }

            if (_targetIntakeAngle > 5)
            {
                _intakeDeployed = true;
            }
        }

        private void Hopper()
        {
            if (_intakeDeployed)
            {
                hopperJoint.SetLinearTarget(extendedDistance).withAxis(JointAxis.Z);
                if (hopperJoint.GetAxisLocation(JointAxis.Z) >= (extendedDistance - 1) * 0.0254f)
                {
                    hopperJoint.lockAllAxis();
                }
            }
            _intakeWobble = OuttakeAction || AutoPass || ManualShoot;
        }

        private void HandleAutoAim()
        {
            if (OuttakeAction && InAllianceZone())
            {
                _aim.SetDefaultMap(DefaultMap.Hub);
                _aim.Aim();
            }
            else if ((OuttakeAction && !InAllianceZone()) || AutoPass)
            {
                _aim.SetPassMapClosest();
                _aim.Aim();
            }
            else
            {
                _aim.SetDefaultMap(DefaultMap.Hub);
                FlyWheelInterpolation.SetDefaultMap(DefaultMap.Hub);
                HoodInterpolation.SetDefaultMap(DefaultMap.Hub);
            }
        }

        private void Shooter()
        {
            if ((!OuttakeAction && !AutoPass && !ManualShoot) || (Mathf.Abs(_aim.GetRotationError()) > 2 && !ManualShoot))
            {
                feederRollers.FlipRpm();
                shooterRoller.SetRpm(0);
                indexerRollers.Stop();
            }

            if (!shooterRoller.AtSpeed())
            {
                indexerRollers.Stop();
                feederRollers.FlipRpm();
            }

            SetInterpMap();

            if (AutoPass || OuttakeAction)
            {
                var shooterSpeed = FlyWheelInterpolation.Interpolate();
                shooterRoller.SetRpm(shooterSpeed);
                feederRollers.SetRpm(shooterSpeed * 2.3f);
            }

            float shooterAngle = HoodInterpolation.Interpolate();
            shooterAngle = Mathf.Clamp(shooterAngle, 0, 35);
            _targetHoodAngle = shooterAngle;

            if (ManualShoot)
            {
                shooterRoller.SetRpm(2400);
                _targetHoodAngle = 20f;
            }
        }

        private void SetInterpMap()
        {
            if (InAllianceZone() && OuttakeAction)
            {
                FlyWheelInterpolation.SetDefaultMap(DefaultMap.Hub);
                HoodInterpolation.SetDefaultMap(DefaultMap.Hub);
            }

            if ((OuttakeAction && !InAllianceZone()) || AutoPass)
            {
                FlyWheelInterpolation.SetPassMapClosest();
                HoodInterpolation.SetPassMapClosest();
            }
        }

        private void UpdateJoints()
        {
            intakeJoint.SetTargetAngle(_targetIntakeAngle).withAxis(JointAxis.X).useCustomStartingOffset(-107f);
            hoodJoint.SetTargetAngle(_targetHoodAngle).withAxis(JointAxis.X);
            climber.SetLinearTarget(_climberTarget).withAxis(JointAxis.Y).useAutomaticStartingOffset();
        }

        private void WobbleIntake()
        {
            var agitateUp = intakeAngle / 1.5f;
            if (_targetIntakeAngle == agitateUp &&
                Utils.InAngularRange(intakeJoint.GetAngle(), agitateUp, 2))
            {
                _targetIntakeAngle = intakeAngle;
            }
            else if (_targetIntakeAngle == intakeAngle &&
                     Utils.InAngularRange(intakeJoint.GetAngle(), intakeAngle, 2))
            {
                _targetIntakeAngle = agitateUp;
            }
        }
    }
}
