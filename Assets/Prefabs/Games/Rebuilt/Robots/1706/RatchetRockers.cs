using System;
using Games.Rebuilt.Components;
using Games.Rebuilt.Robots;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using UnityEngine;
using UnityEngine.Serialization;

namespace Prefabs.Games.Rebuilt.Robots._1706
{
    public class RatchetRockers : RebuiltRobotBase
    {
        [Header("Joints")]
        [SerializeField] private DualTurretWrapper turretsWrapper;

        [SerializeField] private GenericJoint intakeJoint;

        [SerializeField] private GenericJoint hopperJoint;

        [SerializeField] private GenericJoint climber;

        [SerializeField] private RollerGroup intakeRollers;

        [SerializeField] private RollerGroup leftIndexerRollers;
        [SerializeField] private RollerGroup rightIndexerRollers;

        [SerializeField] private RollerGroup leftFeederRollers;
        [SerializeField] private RollerGroup rightFeederRollers;

        [SerializeField] private EChain leftEchain;
        [SerializeField] private EChain rightEchain;

        [Serializable]
        private struct EChain
        {
            [SerializeField] public GameObject EChainGameObject;
            [SerializeField] public Transform BottomTransform;
            [SerializeField] public Transform TopTransform;
        }

        [Header("PIDS")]
        [SerializeField] private PidConstants intakePidConstants;
        [SerializeField] private PidConstants intakeAgitationPidConstants;
        [SerializeField] private PidConstants hopperPidConstants;
        [SerializeField] private PidConstants climberPidConstants;

        [Header("Interpolation Values")]
        [SerializeField] private float manualShotRpm = 300f;
        [FormerlySerializedAs("leftFlyWheelInterpolation")]
        [SerializeField] public RebuiltInterpolation leftAZFlyWheelInterpolation;
        [FormerlySerializedAs("leftHoodInterpolation")]
        [SerializeField] private RebuiltInterpolation leftAZHoodInterpolation;
        [SerializeField] private RebuiltInterpolation leftNZFlyWheelInterpolation;
        [SerializeField] private RebuiltInterpolation leftNZHoodInterpolation;

        [SerializeField] private RebuiltInterpolation leftAZTofInterpolation;
        [SerializeField] private RebuiltInterpolation leftNZTofInterpolation;
        [SerializeField] private float uptakeRpm;

        [Header("Intake Values")]
        [SerializeField] private float intakeAngle;
        [SerializeField] private float agitationAngle = 30f;
        [SerializeField] private float intakeRpm = 2000f;
        [SerializeField] private float intakeWobbleDelay = 1.25f;

        [Header("Hopper Values")]
        [SerializeField] private float extendedDistance;

        [Header("Turret + SOTM")]
        [SerializeField] private bool enableShootOnMove = true;
        [SerializeField] private float velocityDampeningValue;
        [SerializeField, Range(0f, 1f)] private float shootingDriveMultiplier = 0.12f;
        [SerializeField, Range(0f, 1f)] private float shootingTurnMultiplier = 0.2f;

        [SerializeField, Range(0f, 1f)] private float passingDriveMultiplier = 0.6f;
        [SerializeField, Range(0f, 1f)] private float passingTurnMultiplier = 0.3f;
        [SerializeField, Min(0f)] private float turretAngleTolerance = 15f;
        [SerializeField, Min(0f)] private float turretAngleToleranceNZ = 25f; // Dynamic NZ Turret Tolerance

        [Header("Miscellaneous")]
        [SerializeField] public float hoodRollerReduction = 0.5f;
        [SerializeField] public float hoodAtTargetTolerance = 0.3f;
        [SerializeField] public float hoodAtTargetToleranceNZ = 0.8f; // Dynamic NZ Hood Tolerance

        [SerializeField] private Transform leftTurretTransform;
        [SerializeField] private Transform rightTurretTransform;

        private float _targetIntakeAngle;
        private bool _intakeDeployed;
        private bool _intakeWobble;
        private bool _hopperLockedRetracted;
        private Rigidbody robotRigidbody;

        private Vector3 leftSmoothedVel;
        private Vector3 leftLastVelocity;
        private Vector3 rightSmoothedVel;
        private Vector3 rightLastVelocity;

        private RebuiltAutoAimController _aim;

        private bool _deployClimb;

        private float _climberTarget;

        private Transform targetHub;

        private RebuiltInterpolation rightAZHoodInterpolation;
        private RebuiltInterpolation rightAZFlyWheelInterpolation;
        private RebuiltInterpolation rightNZHoodInterpolation;
        private RebuiltInterpolation rightNZFlyWheelInterpolation;

        private RebuiltInterpolation rightAZTofInterpolation;
        private RebuiltInterpolation rightNZTofInterpolation;

        private bool wasShooting;
        private int stuterCount;
        private float _shootingTimer;

        protected override void Init()
        {
            stuterCount = 1;
            intakeJoint.SetPid(intakePidConstants);
            hopperJoint.SetPid(hopperPidConstants);

            turretsWrapper.Initialize(this, velocityDampeningValue, hoodAtTargetTolerance, hoodRollerReduction);

            turretsWrapper.LeftHood.noWrap = true;
            turretsWrapper.LeftHood.noWrapAngle = 300f;
            turretsWrapper.RightHood.noWrap = true;
            turretsWrapper.RightHood.noWrapAngle = 300f;

            Scheduler.AddSubsystems(Intake, Hopper, Shooter, TurretTrack, HandleEChains, UpdateValues);
            Scheduler.AddProtectedSystem(UpdateJoints, UpdateDriveSpeed, turretsWrapper.SubsystemLoop);
            Scheduler.AddLateRecursive(UpdatePids);
            Scheduler.AddRecursive(turretsWrapper.HandleDampedVelocity);

            _intakeDeployed = false;
            _targetIntakeAngle = 110;
            _hopperLockedRetracted = true;

            _deployClimb = false;
            _shootingTimer = 0f;

            leftAZFlyWheelInterpolation.Initialize(this);
            leftAZHoodInterpolation.Initialize(this);

            leftNZFlyWheelInterpolation.Initialize(this);
            leftNZHoodInterpolation.Initialize(this);

            leftAZTofInterpolation.Initialize(this);
            leftNZTofInterpolation.Initialize(this);

            rightAZFlyWheelInterpolation = new RebuiltInterpolation(this, leftAZFlyWheelInterpolation.interpolationMap);
            rightAZFlyWheelInterpolation.Initialize(this);
            rightAZFlyWheelInterpolation.transformOverride = turretsWrapper.RightTurret.transform;

            rightNZFlyWheelInterpolation = new RebuiltInterpolation(this, leftNZFlyWheelInterpolation.interpolationMap);
            rightNZFlyWheelInterpolation.Initialize(this);
            rightNZFlyWheelInterpolation.transformOverride = turretsWrapper.RightTurret.transform;

            rightAZHoodInterpolation = new RebuiltInterpolation(this, leftAZHoodInterpolation.interpolationMap);
            rightAZHoodInterpolation.Initialize(this);
            rightAZHoodInterpolation.transformOverride = turretsWrapper.RightTurret.transform;

            rightNZHoodInterpolation = new RebuiltInterpolation(this, leftNZHoodInterpolation.interpolationMap);
            rightNZHoodInterpolation.Initialize(this);
            rightNZHoodInterpolation.transformOverride = turretsWrapper.RightTurret.transform;

            rightAZTofInterpolation = new RebuiltInterpolation(this, leftAZTofInterpolation.interpolationMap);
            rightAZTofInterpolation.Initialize(this);
            rightAZTofInterpolation.transformOverride = turretsWrapper.RightTurret.transform;

            rightNZTofInterpolation = new RebuiltInterpolation(this, leftNZTofInterpolation.interpolationMap);
            rightNZTofInterpolation.Initialize(this);
            rightNZTofInterpolation.transformOverride = turretsWrapper.RightTurret.transform;

            TryGetComponent(out robotRigidbody);
        }

        private enum InterpType
        {
            Flywheel,
            Hood,
            Tof
        }

        private RebuiltInterpolation GetInterp(InterpType type, DualTurretWrapper.Turret side)
        {
            var sideBasedAZHoodInterp = side == DualTurretWrapper.Turret.LEFT ? leftAZHoodInterpolation : rightAZHoodInterpolation;
            var sideBasedNZHoodInterp = side == DualTurretWrapper.Turret.LEFT ? leftNZHoodInterpolation : rightNZHoodInterpolation;
            var sideBasedAZFlywheelInterp = side == DualTurretWrapper.Turret.LEFT ? leftAZFlyWheelInterpolation : rightAZFlyWheelInterpolation;
            var sideBasedNZFlywheelInterp = side == DualTurretWrapper.Turret.LEFT ? leftNZFlyWheelInterpolation : rightNZFlyWheelInterpolation;
            var sideBasedAZTofInterp = side == DualTurretWrapper.Turret.LEFT ? leftAZTofInterpolation : rightAZTofInterpolation;
            var sideBasedNZTofInterp = side == DualTurretWrapper.Turret.LEFT ? leftNZTofInterpolation : rightNZTofInterpolation;

            var shouldReturnNZ = !InAllianceZone() || AutoPass;

            if (type == InterpType.Flywheel)
            {
                return shouldReturnNZ ? sideBasedNZFlywheelInterp : sideBasedAZFlywheelInterp;
            }
            if (type == InterpType.Hood)
            {
                return shouldReturnNZ ? sideBasedNZHoodInterp : sideBasedAZHoodInterp;
            }
            if (type == InterpType.Tof)
            {
                return shouldReturnNZ ? sideBasedNZTofInterp : sideBasedAZTofInterp;
            }

            else return null;
        }

        private void UpdateValues()
        {
            bool useNZTolerances = !InAllianceZone() || AutoPass;
            float currentHoodTolerance = useNZTolerances ? hoodAtTargetToleranceNZ : hoodAtTargetTolerance;

            turretsWrapper.UpdateValues(
                velocityDampeningValue,
                currentHoodTolerance,
                hoodRollerReduction);

            for (int i = 0; i < 3; i++)
            {
                float leftTof = GetShootOnMoveTof(DualTurretWrapper.Turret.LEFT);
                float rightTof = GetShootOnMoveTof(DualTurretWrapper.Turret.RIGHT);

                turretsWrapper.UpdateTimeOfFlight(leftTof, rightTof);
            }
        }

        private float GetShootOnMoveTof(DualTurretWrapper.Turret side)
        {
            var tofInterp = GetInterp(InterpType.Tof, side);

            if (!InAllianceZone() || AutoPass)
            {
                tofInterp.SetPassMapClosest();
            }
            else
            {
                tofInterp.SetDefaultMap(DefaultMap.Hub);
            }

            tofInterp.SetCustomTarget(turretsWrapper.GetShootOnMoveTarget(tofInterp.GetCurrentTarget(), side));
            return tofInterp.Interpolate();
        }

        private void TurretTrack()
        {
            bool inAllianceZone = InAllianceZone();
            turretsWrapper.HandleTurretsTracking(AutoPass || !inAllianceZone);
        }

        private void HandleEChains()
        {
            HandleEChain(leftEchain, turretsWrapper.LeftTurret);
            HandleEChain(rightEchain, turretsWrapper.RightTurret);
        }

        private void HandleEChain(EChain eChain, GenericTurret turret)
        {
            eChain.EChainGameObject.transform.localPosition = new Vector3(
                Mathf.Lerp(
                    eChain.BottomTransform.localPosition.x,
                    eChain.TopTransform.localPosition.x,
                    Math.Abs(turret.CurrentAngle()) / 200f
                ),
                Mathf.Lerp(
                    eChain.BottomTransform.localPosition.y,
                    eChain.TopTransform.localPosition.y,
                    Math.Abs(turret.CurrentAngle()) / 200f
                ),
                eChain.BottomTransform.transform.localPosition.z
            );
        }

        private void UpdatePids()
        {
            intakeJoint.UpdatePid(_intakeWobble ? intakeAgitationPidConstants : intakePidConstants);
            hopperJoint.UpdatePid(hopperPidConstants);
        }

        private void UpdateDriveSpeed()
        {
            bool isShooting = WantsToShoot() && InAllianceZone() && !AutoPass;
            bool isPassing = (WantsToShoot() && !InAllianceZone()) || AutoPass;

            float driveMp = 1f;
            float turnMp = 1f;
            
            if (isShooting)
            {
                driveMp = shootingDriveMultiplier;
                turnMp = shootingTurnMultiplier;
            }

            if (isPassing)
            {
                driveMp = passingDriveMultiplier;
                turnMp = passingTurnMultiplier;
            }

            DriveController.SetDriveMp(driveMp);
            DriveController.SetTurnMp(turnMp);
        }

        private void Intake()
        {
            if (_intakeWobble)
            {
                Agitation();
            }
            else if (IntakeAction || _intakeDeployed)
            {
                _targetIntakeAngle = intakeAngle;
                _intakeDeployed = true;
            }

            if (IntakeAction)
            {
                intakeRollers.SetRpm(intakeRpm);
            }
            else if (_intakeDeployed)
            {
                intakeRollers.SetRpm(intakeRpm * 0.2f);
            }
            else
            {
                intakeRollers.Stop();
            }
        }

        private void Hopper()
        {
            if (_intakeDeployed)
            {
                if (_hopperLockedRetracted)
                {
                    hopperJoint.freeLinearAxis(JointAxis.X);
                    _hopperLockedRetracted = false;
                }
                hopperJoint.SetLinearTarget(extendedDistance).withAxis(JointAxis.X);
                if (hopperJoint.GetAxisLocation(JointAxis.X) >= (extendedDistance - 1) * 0.0254f)
                {
                    hopperJoint.lockAllAxis();
                }
            }

            _intakeWobble = WantsToShoot() && !IntakeAction && (_shootingTimer >= intakeWobbleDelay);
        }

        private void Shooter()
        {
            SetInterpMap();

            bool useNZ = !InAllianceZone() || AutoPass;
            float currentTurretTolerance = useNZ ? turretAngleToleranceNZ : turretAngleTolerance;

            GetInterp(InterpType.Hood, DualTurretWrapper.Turret.LEFT).SetCustomTarget(turretsWrapper.GetShootOnMoveTarget(leftAZHoodInterpolation.GetCurrentTarget(), DualTurretWrapper.Turret.LEFT));
            var leftHoodAngle = GetInterp(InterpType.Hood, DualTurretWrapper.Turret.LEFT).Interpolate();

            GetInterp(InterpType.Hood, DualTurretWrapper.Turret.RIGHT).SetCustomTarget(turretsWrapper.GetShootOnMoveTarget(rightAZHoodInterpolation.GetCurrentTarget(), DualTurretWrapper.Turret.RIGHT));
            var rightHoodAngle = GetInterp(InterpType.Hood, DualTurretWrapper.Turret.RIGHT).Interpolate();
            var previousShoot = wasShooting;

            if (!WantsToShoot())
            {
                _shootingTimer = 0f;
                leftFeederRollers.FlipRpm();
                rightFeederRollers.FlipRpm();

                turretsWrapper.SetRpm(-2000);

                leftIndexerRollers.OverrideRpm(-1000);
                rightIndexerRollers.OverrideRpm(-1000);

                turretsWrapper.SetHoodAngle(0f);
                wasShooting = false;
                previousShoot = false;
                _intakeWobble = false;
                return;
            }

            if (IntakeAction)
            {
                _shootingTimer = 0f;
            }
            else
            {
                _shootingTimer += Time.deltaTime;
            }

            GetInterp(InterpType.Flywheel, DualTurretWrapper.Turret.LEFT).SetCustomTarget(turretsWrapper.GetShootOnMoveTarget(GetInterp(InterpType.Flywheel, DualTurretWrapper.Turret.LEFT).GetCurrentTarget(), DualTurretWrapper.Turret.LEFT));
            var leftShooterSpeed = UnderTrench(leftTurretTransform, 0.17f) ? -2000f : GetInterp(InterpType.Flywheel, DualTurretWrapper.Turret.LEFT).Interpolate();

            GetInterp(InterpType.Flywheel, DualTurretWrapper.Turret.RIGHT).SetCustomTarget(turretsWrapper.GetShootOnMoveTarget(rightAZFlyWheelInterpolation.GetCurrentTarget(), DualTurretWrapper.Turret.RIGHT));
            var rightShooterSpeed = UnderTrench(rightTurretTransform, 0.17f) ? -2000f : GetInterp(InterpType.Flywheel, DualTurretWrapper.Turret.RIGHT).Interpolate();

            turretsWrapper.SetShooterAndHood(turretsWrapper.GetLeftError() < currentTurretTolerance ? leftShooterSpeed : 0f, !UnderTrench(leftTurretTransform, 0.17f) ? Mathf.Clamp(leftHoodAngle, 0f, 33f) : 0f, DualTurretWrapper.Turret.LEFT);
            turretsWrapper.SetShooterAndHood(turretsWrapper.GetRightError() < currentTurretTolerance ? rightShooterSpeed : 0f, !UnderTrench(rightTurretTransform, 0.17f) ? Mathf.Clamp(rightHoodAngle, 0f, 33f) : 0f, DualTurretWrapper.Turret.RIGHT);

            Vector3 lookupTargetPosition = GetInterp(InterpType.Flywheel, DualTurretWrapper.Turret.LEFT).GetCurrentTarget();

            float distanceToTarget = robotRigidbody != null ? Vector3.Distance(robotRigidbody.position, lookupTargetPosition) : 1f;

            if (distanceToTarget <= 0f) distanceToTarget = 1f;

            float dynamicUptakeRpm = uptakeRpm;

            bool leftSideReadyToShoot = (useNZ || turretsWrapper.ShooterAndHoodAtTarget(DualTurretWrapper.Turret.LEFT)) && 
                                        turretsWrapper.LeftTurretEnabled && 
                                        !UnderTrench(leftTurretTransform, 0.17f) && 
                                        turretsWrapper.GetLeftError() < currentTurretTolerance;
            if (leftSideReadyToShoot || ManualShoot)
            {
                leftFeederRollers.SetRpm(dynamicUptakeRpm);
                wasShooting = true;
            }
            else
            {
                wasShooting = false;
                leftIndexerRollers.OverrideRpm(-1000);
            }

            bool rightSideReadyToShoot = (useNZ || turretsWrapper.ShooterAndHoodAtTarget(DualTurretWrapper.Turret.RIGHT)) && 
                                         turretsWrapper.RightTurretEnabled && 
                                         !UnderTrench(rightTurretTransform, 0.17f) && 
                                         turretsWrapper.GetRightError() < currentTurretTolerance;
            if (rightSideReadyToShoot || ManualShoot)
            {
                rightFeederRollers.SetRpm(dynamicUptakeRpm);
            }
            else
            {
                rightIndexerRollers.OverrideRpm(-1000);
            }

            if (ManualShoot)
            {
                turretsWrapper.SetShooterAndHood(turretsWrapper.GetLeftError() < currentTurretTolerance ? manualShotRpm : -2000f, !UnderTrench(leftTurretTransform, 0.17f) ? 17.8f : 0f, DualTurretWrapper.Turret.LEFT);
                turretsWrapper.SetShooterAndHood(turretsWrapper.GetRightError() < currentTurretTolerance ? manualShotRpm : -2000f, !UnderTrench(rightTurretTransform, 0.17f) ? 17.8f : 0f, DualTurretWrapper.Turret.RIGHT);
            }

            if (turretsWrapper.IsWrapping(DualTurretWrapper.Turret.LEFT) || turretsWrapper.GetLeftError() > currentTurretTolerance)
            {
                leftFeederRollers.FlipRpm();
                leftIndexerRollers.OverrideRpm(-1000);
            }

            if (turretsWrapper.IsWrapping(DualTurretWrapper.Turret.RIGHT) || turretsWrapper.GetRightError() > currentTurretTolerance)
            {
                rightFeederRollers.FlipRpm();
                rightIndexerRollers.OverrideRpm(-1000);
            }

            _intakeWobble = (_shootingTimer >= intakeWobbleDelay);

            if (previousShoot && !wasShooting)
            {
                Debug.LogWarning($"Stutter #{stuterCount}");
                stuterCount++;
            }
        }

        private bool WantsToShoot()
        {
            return OuttakeAction || AutoPass || ManualShoot;
        }

        private void SetInterpMap()
        {
            if (!InAllianceZone() || AutoPass)
            {
                leftNZFlyWheelInterpolation.SetPassMapClosest();
                rightNZFlyWheelInterpolation.SetPassMapClosest();

                leftNZHoodInterpolation.SetPassMapClosest();
                rightNZHoodInterpolation.SetPassMapClosest();
            }
            else
            {
                leftAZFlyWheelInterpolation.SetDefaultMap(DefaultMap.Hub);
                rightAZFlyWheelInterpolation.SetDefaultMap(DefaultMap.Hub);

                leftAZHoodInterpolation.SetDefaultMap(DefaultMap.Hub);
                rightAZHoodInterpolation.SetDefaultMap(DefaultMap.Hub);
            }
        }

        private void UpdateJoints()
        {
            intakeJoint.SetTargetAngle(_targetIntakeAngle).withAxis(JointAxis.Y);
            turretsWrapper.UpdateTurretJoints();

            turretsWrapper.LeftHood.freeAngularAxis(JointAxis.Z);
            turretsWrapper.RightHood.freeAngularAxis(JointAxis.Z);
        }

        private void Agitation()
        {
            if (IntakeAction)
            {
                _targetIntakeAngle = intakeAngle;
                return;
            }

            if (_targetIntakeAngle == agitationAngle &&
                Utils.InAngularRange(intakeJoint.GetAngle(), agitationAngle, 2))
            {
                _targetIntakeAngle = intakeAngle;
            }
            else if (_targetIntakeAngle == intakeAngle &&
                     Utils.InAngularRange(intakeJoint.GetAngle(), intakeAngle, 2))
            {
                _targetIntakeAngle = agitationAngle;
            }
        }
    }
}