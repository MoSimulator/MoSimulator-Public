using System;
using Games.Rebuilt.Components;
using Games.Rebuilt.Robots;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using UnityEngine;

namespace Prefabs.Games.Rebuilt.Robots.ReBlitz
{
    public class ReBlitz : RebuiltRobotBase
    {
        private const float HopperTargetInches = -12f;
        private const float HopperLockPositionMeters = -0.299f;

        [Header("Joints")]
        [SerializeField] private GenericJoint hopperJoint;
        [SerializeField] private GenericJoint intakePivot;
        [SerializeField] private GenericJoint hoodPivot;

        [Header("Rollers")]
        [SerializeField] private RollerGroup shooterRollers;
        [SerializeField] private RollerGroup hoodRollers;
        [SerializeField] private RollerGroup floorRollers;
        [SerializeField] private RollerGroup uptakeRollers;
        [SerializeField] private RollerGroup intakeRollers;
        [SerializeField] private RollerGroup kickerRoller;

        [Header("PIDS")]
        [SerializeField] private PidConstants hopperPidConstants;
        [SerializeField] private PidConstants intakePidConstants;
        [SerializeField] private PidConstants intakeBringupPid;
        [SerializeField] private PidConstants hoodPidConstants;
        [SerializeField] private PidConstants hoodShootingPidConstants;

        [Header("Alliance Zone Interpolation")]
        [SerializeField] private RebuiltInterpolation allianceZoneFlywheelInterpolation;
        [SerializeField] private RebuiltInterpolation allianceZoneHoodInterpolation;

        [Header("Pass Zone Interpolation")]
        [SerializeField] private RebuiltInterpolation passZoneFlywheelInterpolation;
        [SerializeField] private RebuiltInterpolation passZoneHoodInterpolation;

        [Header("Bump Passing")]
        [SerializeField] private Vector2 bumpAimPoint = new(3.64f, 1.675f);
        [SerializeField] private float passLandingX = 5.91f;
        [SerializeField] private float passLandingMaxZ = 3.5f;
        [SerializeField] private float minBumpPassDistance = 1f;

        [Header("Hopper Values")]
        [SerializeField] private JointAxis hopperAxis = JointAxis.Z;

        [Header("Intake Values")]
        [SerializeField] private float intakeAngle;
        [SerializeField] private float intakeRpm = 2000f;
        
        [Header("Hopper Uptake Logic")] // :)
        [SerializeField] private LayerMask fuelLayerMask; // layer of the fuel to make the physics box less intensive (should be gamepiece 1)
        [SerializeField] private BoxCollider hopperCANRange; // isTrigger collider located in the location of the beam of the canrange in the hopper on the actual robot
        [SerializeField] private BoxCollider uptakeCANRange; // isTrigger collider located in the location of the beam of the canrange in the uptake on the actual robot
        [SerializeField] [Tooltip("Seconds")] private float hopperCanRangeDebounce = 0.25f; // 0.25s is taken from 2910's ReBlitz code release, can be tuned if needed

        [Header("Intake Bringup")]
        [SerializeField] private float intakeBringupAngle;
        [SerializeField, Min(0f)] private float intakeBringupDelay = 0.15f;
        [SerializeField, Min(0.01f)] private float intakeBringupSpeed = 0.5f;

        [Header("Shooter Values")]
        [SerializeField] private float uptakeRpm = 2000f;
        [SerializeField] private float floorRpm = 2000f;
        [Tooltip("Maximum hood pivot angle error in degrees before feed rollers are allowed to run.")]
        [SerializeField, Min(0f)] private float hoodAngleTolerance = 0.5f;
        [SerializeField, Min(0f)] private float hoodAngleLockTolerance = 0.1f;
        [Tooltip("How long all shot readiness checks must remain true before feeding.")]
        [SerializeField, Min(0f)] private float feedReadyDebounceTime = 0.06f;
        [SerializeField] private Vector2 hoodAngleLimits = new(0f, 35f);
        [SerializeField, Min(0.01f)] private float hoodRpmReduction = 1.5f;

        [Header("Shot Readiness")]
        [Tooltip("Maximum auto-aim yaw error in degrees before feeding a hub shot from the alliance zone.")]
        [SerializeField, Min(0f)] private float scoringAimTolerance = 4f;
        [Tooltip("Maximum auto-aim yaw error in degrees before feeding a pass.")]
        [SerializeField, Min(0f)] private float passingAimTolerance = 15f;
        [Tooltip("Maximum chassis speed in m/s before feeding a hub shot from the alliance zone.")]
        [SerializeField, Min(0f)] private float scoringSpeedTolerance = 0.15f;
        [Tooltip("Maximum chassis speed in m/s before feeding a pass.")]
        [SerializeField, Min(0f)] private float passingSpeedTolerance = 10f;
        [SerializeField, Min(0f)] private float hubNoShootingDist = 1.5f;

        [Header("Trench Avoidance")]
        [SerializeField] private Transform[] shooterCorners;

        [Header("Shoot On The Move")]
        [SerializeField] private SOTMSettings sotmSettings;
        [SerializeField, Range(0f, 1f)] private float shootingTurnMultiplier = 0.3f;
        [SerializeField, Range(0f, 1f)] private float passingDriveMultiplier = 0.5f;
        [SerializeField, Range(0f, 1f)] private float passingTurnMultiplier = 0.3f;
        [Tooltip("Scales the aim and speed tolerances used to decide when the shooting drive limit kicks in.")]
        [SerializeField, Min(1f)] private float driveLimitToleranceScale = 2f;

        [Header("Debug")]
        [SerializeField] private float aimErrorDebug;
        [SerializeField] private bool alignedToShootDebug;

        private float targetIntakeAngle;
        private float targetHoodAngle;
        private bool intakeDeployed;
        private float intakeBringupDelayTimer;
        private int agitationCounter;
        private bool shooterReadyLatched;
        private bool feedLatched;
        private float intakePidMaxDefault;
        private RebuiltAutoAimController aim;
        private Rigidbody robotRigidbody;
        private bool _intakeDeployed;
        private bool _wantsShooting;

        private bool _hopperFull;
        private float _hopperCanRangeTimer;
        private bool _preloading;
        private bool _hopperLocked;

        private float _allianceSign = 1f;
        private float _passNodeHeight;

        protected override void Init()
        {
            intakePivot.SetPid(intakePidConstants);
            hoodPivot.SetPid(hoodPidConstants);
            hopperJoint.SetPid(hopperPidConstants);
            intakePidMaxDefault = intakePidConstants.Max;

            allianceZoneFlywheelInterpolation.Initialize(this);
            allianceZoneHoodInterpolation.Initialize(this);
            passZoneFlywheelInterpolation.Initialize(this);
            passZoneHoodInterpolation.Initialize(this);

            targetIntakeAngle = 0f;
            targetHoodAngle = 0f;
            intakeDeployed = false;
            _intakeDeployed = false;
            _wantsShooting = false;

            _hopperFull = false;
            _hopperCanRangeTimer = 0f;
            _hopperLocked = false;

            if (!TryGetComponent(out aim))
            {
                Debug.LogError($"{nameof(ReBlitz)} requires a {nameof(RebuiltAutoAimController)}.", this);
            }

            robotRigidbody = GetComponent<Rigidbody>();

            InitBumpPassing();

            Scheduler.AddSubsystems(HandleAutoAim, Shooter, Intake);
            Scheduler.AddProtectedSystem(UpdateJoints, UpdateDriveSpeed, Hopper);
            Scheduler.AddLateRecursive(UpdatePids);
        }

        private void UpdatePids()
        {
            intakePivot.UpdatePid(intakeBringupDelayTimer >= intakeBringupDelay ? intakeBringupPid : intakePidConstants);
            hoodPivot.UpdatePid(WantsToShoot() && HoodAtAngle() ? hoodShootingPidConstants : hoodPidConstants);
            hopperJoint.UpdatePid(hopperPidConstants);
        }

        private bool CanRangeTriggered(BoxCollider canRange)
        {
            Vector3 halfExtents = Vector3.Scale(canRange.size, canRange.transform.lossyScale) * 0.5f;
            Vector3 center = canRange.transform.TransformPoint(canRange.center);
            
            return Physics.CheckBox(center, halfExtents, canRange.transform.rotation, fuelLayerMask); // returns true if a fuel (gamepiece1 aka fuellayermask) is within the bounds of an isTrigger collider (canrange)
        }

        private void HopperCanRangeDebouncer()
        {
            if (CanRangeTriggered(hopperCANRange)) // will run if the hopper can detects a fuel
            {
                _hopperCanRangeTimer += Time.fixedDeltaTime; // add the difference in time from last time this ran
                if (_hopperCanRangeTimer >= hopperCanRangeDebounce) // will run once the time a fuel is within the hopper canrange box exceeds hoppercantangedebounce
                {
                    _hopperFull = true; // sets hopper to full when the fuel is detected for longer than hoppercanrangedebounce seconds
                }
            }
            else // the moment no fuel is detected in hopper can range this will run
            {
                _hopperCanRangeTimer = 0f; // reset the timer to 0
                _hopperFull = false; // hopper is not full because there is no fuel detected
            }
        }
        
        private void Hopper()
        {
            HopperCanRangeDebouncer(); // run the timer for the canrange to detect if the hopper is full
            
            if (_intakeDeployed && !_hopperLocked)
            {
                hopperJoint.SetLinearTarget(HopperTargetInches).withAxis(hopperAxis);
                float hopperPositionMeters = hopperJoint.GetAxisLocation(hopperAxis);
                if (hopperPositionMeters < HopperLockPositionMeters)
                {
                    hopperJoint.lockAllAxis();
                    _hopperLocked = true;
                }
            }

            if (_hopperFull && !CanRangeTriggered(uptakeCANRange)) // if the hopper is full and the fuel has not yet reached the uptake canrange
            {
                _preloading = true; // allow preloading, this will run uptake and floor at 0.07x the regular speed
            }
            else // if the hopper isnt full or the fuel has reached uptake canrange
            {
                _preloading = false; // do not preload
            }
        }

        private void Intake()
        {
            if (intakeDeployed)
            {
                _intakeDeployed = true;
            }
            
            if (WantsToShoot() && intakeDeployed)
            {
                intakeRollers.SetRpm(intakeRpm);
                kickerRoller.SetRpm(1300f);

                if (ShooterUpToSpeedForBringup())
                {
                    shooterReadyLatched = true;
                }

                if (shooterReadyLatched && (feedLatched || ManualShoot) && !IntakeAction)
                {
                    intakeBringupDelayTimer += Time.fixedDeltaTime;
                }
                else if (IntakeAction)
                {
                    intakeBringupDelayTimer = 0f;
                }

                if (intakeBringupDelayTimer >= intakeBringupDelay)
                {
                    intakeRollers.SetRpm(intakeRpm);
                    kickerRoller.SetRpm(1300f);

                    intakePidConstants.Max = intakeBringupSpeed * intakeBringupPid.Max;
                    targetIntakeAngle = intakeBringupAngle;
                }
                else
                {
                    intakePidConstants.Max = intakePidMaxDefault;
                    targetIntakeAngle = intakeAngle;
                }
            }
            else
            {
                intakeBringupDelayTimer = 0f;
                shooterReadyLatched = false;
                intakePidConstants.Max = intakePidMaxDefault;

                if (IntakeAction)
                {
                    intakeDeployed = true;
                    targetIntakeAngle = intakeAngle;
                    intakeRollers.SetRpm(intakeRpm);
                    kickerRoller.SetRpm(1300f);
                }
                else if (intakeDeployed)
                {
                    targetIntakeAngle = intakeAngle;
                    intakeRollers.SetRpm(intakeRpm / 12);
                    kickerRoller.SetRpm(0);
                }
                else
                {
                    targetIntakeAngle = 0f;
                    intakeRollers.SetRpm(intakeRpm / 12);
                    kickerRoller.SetRpm(0);
                }
            }
        }

        private bool IsIntakeBringupActive()
        {
            return WantsToShoot() && intakeDeployed && intakeBringupDelayTimer >= intakeBringupDelay;
        }

        private void HandleAutoAim()
        {
            if (aim == null)
            {
                return;
            }

            if (OuttakeAction && InAllianceZone())
            {
                aim.SetDefaultMap(DefaultMap.Hub);
                aim.SetCustomTarget(GetTimeOfFlightTarget(aim.GetCurrentTarget()));
                aim.Aim();
            }
            else if (IsPassShot())
            {
                if (TryGetBumpPassTarget(out Vector3 passTarget))
                {
                    aim.SetCustomTarget(passTarget);
                }
                else
                {
                    aim.SetPassMapClosest();
                }
                aim.SetCustomTarget(GetTimeOfFlightTarget(aim.GetCurrentTarget()));
                aim.Aim();
            }
            else
            {
                aim.SetDefaultMap(DefaultMap.Hub);
                aim.SetCustomTarget(GetTimeOfFlightTarget(aim.GetCurrentTarget()));
                allianceZoneFlywheelInterpolation.SetDefaultMap(DefaultMap.Hub);
                allianceZoneHoodInterpolation.SetDefaultMap(DefaultMap.Hub);
                ApplyTimeOfFlightToInterpolation(
                    allianceZoneFlywheelInterpolation,
                    allianceZoneHoodInterpolation);
            }
        }

        private void Shooter()
        {
            RebuiltInterpolation flywheelInterpolation = GetActiveFlywheelInterpolation();
            float shooterSpeed = flywheelInterpolation.Interpolate();
            
            if (!WantsToShoot())
            {
                StopShooter(shooterSpeed);
                targetHoodAngle = 0f;
                return;
            }

            SetInterpolationTarget();

            RebuiltInterpolation hoodInterpolation = GetActiveHoodInterpolation();
            float shooterAngle = hoodInterpolation.Interpolate();

            shooterRollers.SetRpm(shooterSpeed);
            hoodRollers.SetRpm(GetHoodRpm(shooterSpeed));
            targetHoodAngle = Mathf.Clamp(shooterAngle, hoodAngleLimits.x, hoodAngleLimits.y);

            if (ManualShoot)
            {
                uptakeRollers.SetRpm(GetUptakeRpm(shooterSpeed));
                floorRollers.SetRpm(GetFloorRpm(shooterSpeed));
                return;
            }

            UpdateFeedLatch();

            if (feedLatched)
            {
                uptakeRollers.SetRpm(GetUptakeRpm(shooterSpeed));
                floorRollers.SetRpm(GetFloorRpm(shooterSpeed));
            }
            else if (TooCloseToHub())
            {
                StopFeedRollers();
            }
        }

        private void UpdateFeedLatch()
        {
            float aimError = Math.Abs(aim.GetRotationError());
            bool ready = aimError <= GetAimTolerance() 
            && ChassisSpeed() <= GetSpeedTolerance()
            && HoodAtAngle()
            && !TooCloseToHub();

            aimErrorDebug = aimError;
            alignedToShootDebug = ready;

            feedLatched = ready;
        }

        private float GetAimTolerance()
        {
            return IsPassShot() ? passingAimTolerance : scoringAimTolerance;
        }

        private float GetSpeedTolerance()
        {
            return IsPassShot() ? passingSpeedTolerance : scoringSpeedTolerance;
        }

        private bool TooCloseToHub()
        {
            return !IsPassShot() && HubDistance() <= hubNoShootingDist;
        }

        private float HubDistance()
        {
            Transform origin = allianceZoneFlywheelInterpolation.transformOverride;
            Vector3 position = origin == null ? transform.position : origin.position;
            Vector3 offset = allianceZoneFlywheelInterpolation.GetCurrentTarget() - position;
            offset.y = 0f;
            return offset.magnitude;
        }

        private float ChassisSpeed()
        {
            if (robotRigidbody == null)
            {
                return 0f;
            }

            Vector3 velocity = robotRigidbody.velocity;
            velocity.y = 0f;
            return velocity.magnitude;
        }

        private void SetInterpolationTarget()
        {
            if (IsPassShot())
            {
                if (TryGetBumpPassTarget(out Vector3 passTarget))
                {
                    passZoneFlywheelInterpolation.SetCustomTarget(passTarget);
                    passZoneHoodInterpolation.SetCustomTarget(passTarget);
                }
                else
                {
                    passZoneFlywheelInterpolation.SetPassMapClosest();
                    passZoneHoodInterpolation.SetPassMapClosest();
                }
                ApplyTimeOfFlightToInterpolation(
                    passZoneFlywheelInterpolation,
                    passZoneHoodInterpolation);
                return;
            }

            allianceZoneFlywheelInterpolation.SetDefaultMap(DefaultMap.Hub);
            allianceZoneHoodInterpolation.SetDefaultMap(DefaultMap.Hub);
            ApplyTimeOfFlightToInterpolation(
                allianceZoneFlywheelInterpolation,
                allianceZoneHoodInterpolation);
        }

        private RebuiltInterpolation GetActiveFlywheelInterpolation()
        {
            return IsPassShot() ? passZoneFlywheelInterpolation : allianceZoneFlywheelInterpolation;
        }

        private RebuiltInterpolation GetActiveHoodInterpolation()
        {
            return IsPassShot() ? passZoneHoodInterpolation : allianceZoneHoodInterpolation;
        }

        private Vector3 GetTimeOfFlightTarget(Vector3 target)
        {
            if (robotRigidbody == null)
            {
                return target;
            }

            Vector3 fieldVelocity = robotRigidbody.velocity;
            fieldVelocity.y = 0f;
            return target - fieldVelocity * sotmSettings.timeOfFlight;
        }

        private void InitBumpPassing()
        {
            string alliance = Alliance.ToString().ToLower();
            foreach (var node in Utils.FindGameObjectsOnLayer("AutoAngleNodes"))
            {
                string nodeName = node.name.ToLower();
                if (!nodeName.Contains("pass") || !nodeName.Contains(alliance)) continue;

                Vector3 local = transform.parent.InverseTransformPoint(node.transform.position);
                _allianceSign = Mathf.Sign(local.x);
                _passNodeHeight = local.y;
                return;
            }
        }

        private bool TryGetBumpPassTarget(out Vector3 target)
        {
            target = default;
            Vector3 robot = transform.localPosition;
            float bumpX = _allianceSign * bumpAimPoint.x;
            float bumpZ = (robot.z >= 0f ? 1f : -1f) * bumpAimPoint.y;

            if (_allianceSign * (bumpX - robot.x) < minBumpPassDistance) return false;

            float landingX = _allianceSign * passLandingX;
            float t = (landingX - robot.x) / (bumpX - robot.x);
            float landingZ = Mathf.Clamp(robot.z + t * (bumpZ - robot.z), -passLandingMaxZ, passLandingMaxZ);

            target = transform.parent.TransformPoint(new Vector3(landingX, _passNodeHeight, landingZ));
            return true;
        }

        private void ApplyTimeOfFlightToInterpolation(
            RebuiltInterpolation flywheelInterpolation,
            RebuiltInterpolation hoodInterpolation)
        {
            Vector3 compensatedTarget = GetTimeOfFlightTarget(flywheelInterpolation.GetCurrentTarget());
            flywheelInterpolation.SetCustomTarget(compensatedTarget);
            hoodInterpolation.SetCustomTarget(compensatedTarget);
        }

        private void UpdateJoints()
        {
            intakePivot.SetTargetAngle(targetIntakeAngle).withAxis(JointAxis.X).useCustomStartingOffset(0);
            hoodPivot.SetTargetAngle(targetHoodAngle).withAxis(JointAxis.X);
            
            if (HoodAtLockAngle())
            {
                hoodPivot.lockAllAxis();
            }
            else
            {
                hoodPivot.freeAngularAxis(JointAxis.X);
            }
        }

        private void UpdateDriveSpeed()
        {
            bool nearlyReady = Math.Abs(aim.GetRotationError()) <= GetAimTolerance() * driveLimitToleranceScale
                && ChassisSpeed() <= GetSpeedTolerance() * driveLimitToleranceScale
                && !TooCloseToHub();
            bool isFeeding = WantsToShoot() && (nearlyReady || ManualShoot);
            bool isPassing = isFeeding && IsPassShot();
            bool isShooting = isFeeding && !isPassing;

            float driveMp = 1f;
            float turnMp = 1f;

            if (isShooting)
            {
                driveMp = sotmSettings.shootingDriveMultiplier;
                turnMp = shootingTurnMultiplier;
            }
            else if (isPassing)
            {
                driveMp = passingDriveMultiplier;
                turnMp = passingTurnMultiplier;
            }

            DriveController.SetDriveMp(driveMp);
            DriveController.SetTurnMp(turnMp);
        }

        private bool WantsToShoot()
        {
            if (!(OuttakeAction || AutoPass || ManualShoot)) _wantsShooting = false;
            else if (!BlitzUnderTrench()) _wantsShooting = true;
            return _wantsShooting && _intakeDeployed;
        }

        private bool IsPassShot()
        {
            return AutoPass || (OuttakeAction && !InAllianceZone());
        }

        private bool ShooterUpToSpeedForBringup()
        {
            if (!shooterRollers.AtSpeed())
            {
                return false;
            }

            return ManualShoot || (hoodRollers.AtSpeed() && Math.Abs(aim.GetRotationError()) <= GetAimTolerance());
        }

        private bool HoodAtAngle()
        {
            return Mathf.Abs(Mathf.DeltaAngle(hoodPivot.GetAngle(), targetHoodAngle)) <= hoodAngleTolerance;
        }
        
        private bool HoodAtLockAngle()
        {
            return Mathf.Abs(Mathf.DeltaAngle(hoodPivot.GetAngle(), targetHoodAngle)) <= hoodAngleLockTolerance;
        }

        private void StopShooter(float shooterSpeed)
        {
            feedLatched = false;
            shooterRollers.SetRpm(shooterSpeed);
            hoodRollers.SetRpm(GetHoodRpm(shooterSpeed));
            StopFeedRollers();
        }

        private void StopFeedRollers()
        {
            // if preloading is true (hopper is full AND fuel has not reached uptake can range), run floor and uptake at 0.07x regular speeds
            uptakeRollers.SetRpm(_preloading ? 0.01f * uptakeRpm : -15); //uptakeRpm / 5);
            floorRollers.SetRpm(_preloading ? 0.01f * floorRpm : -40f); //floorRpm / 5);
        }

        private float GetHoodRpm(float shooterRpm)
        {
            return shooterRpm / hoodRpmReduction;
        }
        
        private float GetUptakeRpm(float shooterRpm)
        {
            return uptakeRpm;
        }
        
        private float GetFloorRpm(float shooterRpm)
        {
            return floorRpm;
        }

        private bool BlitzUnderTrench()
        {
            foreach (var corner in shooterCorners)
            {
                if (UnderTrench(corner)) return true;
            }
            return false;
        }
    }
}
