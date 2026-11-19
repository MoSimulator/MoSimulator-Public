using System;
using Games.Rebuilt.Components;
using Games.Rebuilt.Robots;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using UnityEngine;
using UnityEngine.Serialization;

namespace Prefabs.Games.Rebuilt.Robots.CitrusCircuits
{
    public class CitrusCircuits : RebuiltRobotBase
    {
        [Header("Joints")]
        [SerializeField] private GenericJoint verticalHopperJoint;
        [SerializeField] private GenericJoint horizontalHopperJoint;
        [SerializeField] private GenericJoint intakePivot;
        [SerializeField] private GenericJoint hoodPivot;

        [Header("Rollers")]
        [SerializeField] private RollerGroup shooterRollers;
        [SerializeField] private RollerGroup hoodRollers;
        [FormerlySerializedAs("indexerRollers")]
        [SerializeField] private RollerGroup floorRollers;
        [SerializeField] private RollerGroup uptakeRollers;
        [SerializeField] private RollerGroup intakeRollers;

        [Header("PIDS")]
        [SerializeField] private PidConstants verticalHopperPidConstants;
        [SerializeField] private PidConstants verticalHopperBringdownPidConstants;
        [SerializeField] private PidConstants horizontalHopperPidConstants;
        [SerializeField] private PidConstants intakePidConstants;
        [SerializeField] private PidConstants hoodPidConstants;
        [SerializeField] private PidConstants hoodShootingPidConstants;

        [Header("Alliance Zone Interpolation")]
        [FormerlySerializedAs("flywheelInterpolation")]
        [SerializeField] private RebuiltInterpolation allianceZoneFlywheelInterpolation;
        [FormerlySerializedAs("hoodInterpolation")]
        [SerializeField] private RebuiltInterpolation allianceZoneHoodInterpolation;

        [Header("Pass Zone Interpolation")]
        [SerializeField] private RebuiltInterpolation passZoneFlywheelInterpolation;
        [SerializeField] private RebuiltInterpolation passZoneHoodInterpolation;

        [Header("Hopper Values")]
        [SerializeField] private JointAxis verticalHopperAxis = JointAxis.Y;
        [SerializeField] private JointAxis horizontalHopperAxis = JointAxis.Z;
        [SerializeField] private float verticalHopperRetractedDistance;
        [FormerlySerializedAs("verticalHopperDistance")]
        [SerializeField] private float verticalHopperExtendedDistance;
        [FormerlySerializedAs("horizontalHopperDistance")]
        [SerializeField, Min(0f)] private float hopperBringdownDelay = 0.1f;
        [SerializeField] private float verticalHopperIndexDistance;
        [SerializeField] private float horizontalHopperExtendedDistance;
        [SerializeField] private float horizontalHopperRetractedDistance;
        [SerializeField] private GameObject horizontalHopper;
        [SerializeField] private BoxCollider extraFrontCollider;

        [Header("Intake Values")]
        [SerializeField] private float intakeAngle;
        [SerializeField] private float intakeRpm = 2000f;

        [Header("Intake Bringup")]
        [SerializeField] private float intakeBringupAngle = -40f;
        [SerializeField] private float intakeAgitationAngle;
        [SerializeField, Min(0f)] private float intakeBringupDelay = 0.15f;
        [SerializeField, Min(0.01f)] private float intakeBringupSpeed = 0.5f;
        [SerializeField, Min(0.01f)] private float intakeAgitateSpeed = 0.5f;
        [SerializeField, Min(0.01f)] private float horizontalHopperBringupSpeed = 0.5f;

        [Header("Shooter Values")]
        [SerializeField] private float uptakeRpm = 2000f;
        [FormerlySerializedAs("indexerRpm")]
        [SerializeField] private float floorRpm = 2000f;
        [Tooltip("Maximum auto-aim yaw error in degrees before feed rollers are allowed to run.")]
        [SerializeField] private float aimTolerance = 2f;
        [Tooltip("Maximum hood pivot angle error in degrees before feed rollers are allowed to run.")]
        [SerializeField, Min(0f)] private float hoodAngleTolerance = 0.5f;
        [SerializeField, Min(0f)] private float hoodAngleLockTolerance = 0.1f;
        [Tooltip("How long all shot readiness checks must remain true before feeding.")]
        [SerializeField, Min(0f)] private float feedReadyDebounceTime = 0.06f;
        [SerializeField] private Vector2 hoodAngleLimits = new(0f, 35f);
        [SerializeField] private float manualShotRpm = 2000f;
        [SerializeField] private float manualHoodAngle = 25f;
        [SerializeField, Min(0.01f)] private float hoodRpmReduction = 1.5f;
        [SerializeField, Min(0.01f)] private float uptakeRpmReduction = 1.5f;
        [SerializeField, Min(0.01f)] private float floorRpmReduction = 1.5f;

        [Header("Shoot On The Move")]
        [SerializeField] private SOTMSettings sotmSettings;
        
        [Header("UnderTrench Points")]
        [SerializeField] private UnderTrenchPoint[] UnderTrenchPoints;

        [Header("Debug")]
        [SerializeField] private float aimErrorDebug;
        [SerializeField] private bool alignedToShootDebug;

        private float targetIntakeAngle;
        private float targetHoodAngle;
        private bool intakeDeployed;
        private float intakeBringupDelayTimer;
        private bool intakeBringupAgitatingIn = true;
        private int agitationCounter;
        private bool shooterReadyLatched;
        private bool feedLatched;
        private bool hopperWantsOpen = false;
        private float feedReadyTimer;
        private float intakePidMaxDefault;
        private float horizontalHopperPidMaxDefault;
        private bool verticalHopperExtended;
        private bool verticalHopperHeldDownForShot;
        private RebuiltAutoAimController aim;
        private Rigidbody robotRigidbody;
        private bool _intakeFullyDeployedFirst;
        private bool _wantsShooting;

        protected override void Init()
        {
            verticalHopperJoint.SetPid(verticalHopperPidConstants);
            // horizontalHopperJoint.SetPid(horizontalHopperPidConstants);
            intakePivot.SetPid(intakePidConstants);
            hoodPivot.SetPid(hoodPidConstants);
            intakePidMaxDefault = intakePidConstants.Max;
            horizontalHopperPidMaxDefault = horizontalHopperPidConstants.Max;

            allianceZoneFlywheelInterpolation.Initialize(this);
            allianceZoneHoodInterpolation.Initialize(this);
            passZoneFlywheelInterpolation.Initialize(this);
            passZoneHoodInterpolation.Initialize(this);

            targetIntakeAngle = 0f;
            targetHoodAngle = 0f;
            intakeDeployed = false;
            verticalHopperExtended = false;
            // horizontalHopperJoint.freeLinearAxis(horizontalHopperAxis);
            _intakeFullyDeployedFirst = false;
            _wantsShooting = false;

            if (!TryGetComponent(out aim))
            {
                Debug.LogError($"{nameof(CitrusCircuits)} requires a {nameof(RebuiltAutoAimController)}.", this);
            }

            robotRigidbody = GetComponent<Rigidbody>();

            Scheduler.AddSubsystems(HandleAutoAim, Shooter, Intake);
            Scheduler.AddProtectedSystem(UpdateJoints, UpdateDriveSpeed, HandleHorizontalHopper, HandleVerticalHopper);
            Scheduler.AddLateRecursive(UpdatePids);
        }

        private void UpdatePids()
        {
            verticalHopperJoint.UpdatePid(verticalHopperHeldDownForShot ? verticalHopperBringdownPidConstants : verticalHopperPidConstants);
            // horizontalHopperJoint.UpdatePid(horizontalHopperPidConstants);
            intakePivot.UpdatePid(intakePidConstants);
            hoodPivot.UpdatePid(WantsToShoot() && HoodAtAngle() ? hoodShootingPidConstants : hoodPidConstants);

            // allianceZoneHoodInterpolation.Interpolate();
            // allianceZoneFlywheelInterpolation.Interpolate();
            // Debug.Log(allianceZoneHoodInterpolation.Distance + " : " + allianceZoneHoodInterpolation.Output + " : " + allianceZoneFlywheelInterpolation.Distance + " : " + allianceZoneFlywheelInterpolation.Output);
            // Debug.Log(ShooterAligned() + ":" + shooterRollers.AtSpeed() + ":" + hoodRollers.AtSpeed() + ":" + HoodAtAngle());
            // Debug.Log(passZoneFlywheelInterpolation.Output + " : " + ((PvffController) shooterRollers.GetRoller(0)).TargetRpm + " : " + ((PvffController) shooterRollers.GetRoller(0)).CurrentRpm);
            // Debug.Log(agitationCounter);
        }

        private void Intake()
        {
            if (!WantsToShoot()) agitationCounter = 0;
            if (WantsToShoot() && intakeDeployed)
            {
                intakeRollers.SetRpm(intakeRpm);

                if (ShooterUpToSpeedForBringup())
                {
                    shooterReadyLatched = true;
                }

                if (shooterReadyLatched && !IntakeAction)
                {
                    intakeBringupDelayTimer += Time.fixedDeltaTime;
                }
                else if (IntakeAction)
                {
                    intakeBringupDelayTimer = 0f;
                    agitationCounter = 0;
                }

                if (intakeBringupDelayTimer >= (verticalHopperExtended ? intakeBringupDelay : intakeBringupDelay/2))
                {
                    intakeRollers.SetRpm(intakeRpm/1.5f);
                    if (intakeBringupDelayTimer <=
                        ((verticalHopperExtended ? intakeBringupDelay : intakeBringupDelay / 2) + .75f))
                    {
                        intakePidConstants.Max = intakeBringupSpeed;
                        targetIntakeAngle = intakeBringupAngle;
                        return;
                    }
                    
                    float currentAngle = intakePivot.GetAngle();
                    float rawTarget = intakeBringupAgitatingIn ? 360f + intakeAgitationAngle : 360f + intakeAngle;

                    if (Mathf.Abs(currentAngle - rawTarget) <= 2f && agitationCounter < 2)
                    {
                        intakeBringupAgitatingIn = !intakeBringupAgitatingIn;
                        agitationCounter++;
                    }
                    intakePidConstants.Max = intakeAgitateSpeed;
                    targetIntakeAngle = intakeBringupAgitatingIn ? intakeAgitationAngle : intakeAngle;
                }
                else
                {
                    intakeBringupAgitatingIn = true;
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
                }
                else if (intakeDeployed)
                {
                    targetIntakeAngle = intakeAngle;
                    intakeRollers.SetRpm(intakeRpm / 5);
                }
                else
                {
                    targetIntakeAngle = 0f;
                    intakeRollers.SetRpm(intakeRpm / 5);
                }
            }
        }
        
        private void HandleVerticalHopper()
        {
            bool pastExtended = (verticalHopperExtended && verticalHopperJoint.GetAxisLocation(verticalHopperAxis) >= (verticalHopperExtendedDistance) * 0.0254f);
            
            if (pastExtended && !verticalHopperHeldDownForShot)
            {
                verticalHopperJoint.lockAllAxis();
            }
            else
            {
                verticalHopperJoint.limitLinearAxis(verticalHopperAxis);
            }
            
            if (!WantsToShoot())
            {
                if (verticalHopperHeldDownForShot && verticalHopperExtended) verticalHopperExtended = false;
                verticalHopperHeldDownForShot = false;
            }
            else if (intakeDeployed && (intakeBringupDelayTimer >= (hopperBringdownDelay) || verticalHopperHeldDownForShot))
            {
                verticalHopperHeldDownForShot = true;
                if (verticalHopperExtended) MoveHopper(verticalHopperJoint, verticalHopperAxis, verticalHopperIndexDistance);
                else MoveHopper(verticalHopperJoint, verticalHopperAxis, verticalHopperIndexDistance/2f);
                return;
            }

            if (RobotSpecialTriggered() && BaseGameManager.Instance.RobotState != RobotState.Disabled)
            {
                if (!verticalHopperExtended && CitrusUnderTrench())
                {
                    hopperWantsOpen = !hopperWantsOpen;
                    return;
                }
                
                verticalHopperExtended = !verticalHopperExtended;
            }

            if (!CitrusUnderTrench() && hopperWantsOpen)
            {
                verticalHopperExtended = true;
                hopperWantsOpen = false;
            }

            float verticalTarget = verticalHopperExtended
                ? verticalHopperExtendedDistance
                : verticalHopperRetractedDistance;

            MoveHopper(verticalHopperJoint, verticalHopperAxis, verticalTarget);
        }

        private void HandleHorizontalHopper()
        {
            if (!_intakeFullyDeployedFirst && !Utils.InAngularRange(intakePivot.GetSingleAxisAngle(JointAxis.X), 0f, 125f)) _intakeFullyDeployedFirst = true;
            
            float currentAngle = (-intakePivot.GetAngle() + 720) % 360;
            float horizontalExtension = 1.35692f * Mathf.Pow(10, -7) * Mathf.Pow(currentAngle, 3) + -0.000018253f * Mathf.Pow(currentAngle, 2) + -0.00223573f * currentAngle;
            horizontalHopper.transform.SetLocalPositionAndRotation(new Vector3(horizontalHopper.transform.localPosition.x, horizontalHopper.transform.localPosition.y, Mathf.Clamp(horizontalExtension, -0.3017f, 0f)), horizontalHopper.transform.localRotation);
            extraFrontCollider.enabled = _intakeFullyDeployedFirst;
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
                aim.SetPassMapClosest();
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
            
            if (!WantsToShoot())
            {
                StopShooter();
                targetHoodAngle = 0f;
                return;
            }

            if (ManualShoot)
            {
                float manualShotSpeed = manualShotRpm;
                shooterRollers.SetRpm(manualShotSpeed);
                hoodRollers.SetRpm(GetHoodRpm(manualShotSpeed));
                targetHoodAngle = manualHoodAngle;
                UpdateFeedLatch();
                if (feedLatched)
                {
                    uptakeRollers.SetRpm(GetUptakeRpm(manualShotSpeed));
                    floorRollers.SetRpm(GetFloorRpm(manualShotSpeed));
                }
                else
                {
                    StopFeedRollers();
                }

                // if (HoodAtLockAngle())
                // {
                //     hoodPivot.lockAllAxis();
                // }
                // else
                // {
                //     hoodPivot.freeAngularAxis(JointAxis.X);
                // }
                
                return;
            }

            SetInterpolationTarget();

            RebuiltInterpolation flywheelInterpolation = GetActiveFlywheelInterpolation();
            RebuiltInterpolation hoodInterpolation = GetActiveHoodInterpolation();
            float shooterSpeed = flywheelInterpolation.Interpolate();
            float shooterAngle = hoodInterpolation.Interpolate();

            shooterRollers.SetRpm(shooterSpeed);
            hoodRollers.SetRpm(GetHoodRpm(shooterSpeed));
            targetHoodAngle = Mathf.Clamp(shooterAngle, hoodAngleLimits.x, hoodAngleLimits.y);
            
            // if (HoodAtLockAngle())
            // {
            //     hoodPivot.lockAllAxis();
            // }
            // else
            // {
            //     hoodPivot.freeAngularAxis(JointAxis.X);
            // }

            UpdateFeedLatch();

            if (feedLatched)
            {
                uptakeRollers.SetRpm(GetUptakeRpm(shooterSpeed));
                floorRollers.SetRpm(GetFloorRpm(shooterSpeed));
            }
            else
            {
                StopFeedRollers();
            }
        }

        private void UpdateFeedLatch()
        {
            if (feedLatched)
            {
                return;
            }

            if (CanRunFeed())
            {
                feedReadyTimer += Time.fixedDeltaTime;

                if (feedReadyTimer >= feedReadyDebounceTime)
                {
                    feedLatched = true;
                }

                return;
            }

            feedReadyTimer = 0f;
        }

        private void SetInterpolationTarget()
        {
            if (IsPassShot())
            {
                passZoneFlywheelInterpolation.SetPassMapClosest();
                passZoneHoodInterpolation.SetPassMapClosest();
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
            intakePivot.SetTargetAngle(targetIntakeAngle).withAxis(JointAxis.X).useCustomStartingOffset(-107f);
            hoodPivot.SetTargetAngle(targetHoodAngle).withAxis(JointAxis.X);
            
            if (HoodAtLockAngle())
            {
                // hoodPivot.lockAllAxis(); //NO stop it 
            }
            else
            {
                hoodPivot.freeAngularAxis(JointAxis.X);
            }
        }

        private void UpdateDriveSpeed()
        {
            DriveController.SetDriveMp(WantsToShoot() ? sotmSettings.shootingDriveMultiplier : 1f);
        }

        private void MoveHopper(GenericJoint joint, JointAxis axis, float distance)
        {
            joint.SetLinearTarget(distance).withAxis(axis);
        }

        private bool WantsToShoot()
        {
            if (!(OuttakeAction || AutoPass || ManualShoot)) _wantsShooting = false;
            else if (!CitrusUnderTrench() || verticalHopperExtended) _wantsShooting = true;
            return _wantsShooting;
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

            return ManualShoot || hoodRollers.AtSpeed();
        }

        private bool CanRunFeed()
        {
            return ShooterAligned() && shooterRollers.AtSpeed() && hoodRollers.AtSpeed() && HoodAtAngle();
        }

        private bool HoodAtAngle()
        {
            return Mathf.Abs(Mathf.DeltaAngle(hoodPivot.GetAngle(), targetHoodAngle)) <= hoodAngleTolerance;
        }
        
        private bool HoodAtLockAngle()
        {
            return Mathf.Abs(Mathf.DeltaAngle(hoodPivot.GetAngle(), targetHoodAngle)) <= hoodAngleLockTolerance;
        }

        public bool ShooterAligned()
        {
            if (ManualShoot)
            {
                alignedToShootDebug = true;
                aimErrorDebug = 0f;
                return true;
            }

            if (aim == null)
            {
                alignedToShootDebug = false;
                aimErrorDebug = 0f;
                return false;
            }

            aimErrorDebug = Mathf.Abs(aim.GetRotationError());
            alignedToShootDebug = aimErrorDebug <= aimTolerance;
            return alignedToShootDebug;
        }

        private void StopShooter()
        {
            feedLatched = false;
            feedReadyTimer = 0f;
            shooterRollers.SetRpm(-1000f);
            hoodRollers.SetRpm(-1000f);
            StopFeedRollers();
        }

        private void StopFeedRollers()
        {
            uptakeRollers.SetRpm(-uptakeRpm);
            floorRollers.Stop();
        }

        private float GetHoodRpm(float shooterRpm)
        {
            return shooterRpm / hoodRpmReduction;
        }
        
        private float GetUptakeRpm(float shooterRpm)
        {
            return uptakeRpm;
            return shooterRpm / uptakeRpmReduction;
        }
        
        private float GetFloorRpm(float shooterRpm)
        {
            return floorRpm;
            return shooterRpm / floorRpmReduction;
        }

        private bool CitrusUnderTrench()
        {
            foreach (var point in UnderTrenchPoints)
            {
                if (UnderTrench(point.transform, point.radius)) return true;
            }
            return false;
        }
    }
    
    [Serializable]
    public struct UnderTrenchPoint
    {
        public Transform transform;
        public float radius;
    }
}
