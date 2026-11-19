using System;
using System.Collections;
using System.Collections.Generic;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using UnityEngine;

namespace RobotFramework.Components
{
    public class DualTurretWrapper : MonoBehaviour
    {

        [Header("Left Turret")]
        [SerializeField] public bool LeftTurretEnabled = true;
        [SerializeField] public GenericTurret LeftTurret;
        [SerializeField] public GenericJoint LeftHood;
        [SerializeField] private JointAxis LeftHoodJointAxis;
        [SerializeField] private RollerGroup LeftHoodRollers;
        [SerializeField] private RollerGroup LeftShooterRollers;
        
        [Header("RightTurret")]
        [SerializeField] public bool RightTurretEnabled = true;
        [SerializeField] public GenericTurret RightTurret;
        [SerializeField] public GenericJoint RightHood;
        [SerializeField] private JointAxis RightHoodJointAxis;
        [SerializeField] private RollerGroup RightHoodRollers;
        [SerializeField] private RollerGroup RightShooterRollers;
        
        [Header("Pids")]
        [SerializeField] private PidConstants TurretPids;
        [SerializeField] private PidConstants TurretsWrappingPid;
        [SerializeField] private PidConstants HoodPids;
        
        [Header("Misc Settings")]
        [SerializeField] private float passWallBiasScalar = 1f;
        [SerializeField] private Vector2 passFieldWallZLimits = new(-4.105f, 4.105f);
        
        
        public enum Turret
        {
            LEFT,
            RIGHT,
            BOTH
        }
        
        private Rigidbody _rigidbody;

        private Vector3 _leftSmoothedVel;
        private Vector3 _leftLastVelocity;
        private Vector3 _rightSmoothedVel;
        private Vector3 _rightLastVelocity;

        private float _velocityDampeningValue;

        private float _leftHoodTarget;
        private float _rightHoodTarget;

        private float _hoodTolerance;
        private float _hoodReduction;

        private float _rightTof;
        private float _leftTof;

        private float _leftError;
        private float _rightError;
        
        public MoSimResult<float> GetLeftError()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<float>.Success(GetLeftErrorV1());
        }

        private float GetLeftErrorV1() => _leftError;

        public MoSimResult<float> GetRightError()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<float>.Success(GetRightErrorV1());
        }

        private float GetRightErrorV1() => _rightError;

        [HideInInspector]
        public RobotBase _robotBase;

        public MoSimResult<MoSimLib.Void> UpdateTimeOfFlight(Turret side, float tof)
        {
            var version = MoSim.ActiveVersion;
            UpdateTimeOfFlightV1(side, tof);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void UpdateTimeOfFlightV1(Turret side, float tof)
        {
            if (_leftTof != tof && (side == Turret.BOTH || side == Turret.LEFT)) _leftTof = tof;
            if (_rightTof != tof && (side == Turret.BOTH || side == Turret.RIGHT)) _rightTof = tof;
        }

        public MoSimResult<MoSimLib.Void> UpdateTimeOfFlight(float leftTof, float rightTof)
        {
            var version = MoSim.ActiveVersion;
            UpdateTimeOfFlightV1(leftTof, rightTof);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void UpdateTimeOfFlightV1(float leftTof, float rightTof)
        {
            if (_leftTof != leftTof) _leftTof = leftTof;
            if (_rightTof != rightTof) _rightTof = rightTof;
        }
        
        private void Awake()
        {
            TryGetComponent(out _rigidbody);
        }
        
        public MoSimResult<MoSimLib.Void> Initialize(RobotBase robotBase, float velocityDampeningValue, float tolerance, float reduction)
        {
            var version = MoSim.ActiveVersion;
            InitializeV1(robotBase, velocityDampeningValue, tolerance, reduction);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void InitializeV1(RobotBase robotBase, float velocityDampeningValue, float tolerance, float reduction)
        {
            _velocityDampeningValue = velocityDampeningValue;
            _hoodTolerance = tolerance;
            _hoodReduction = reduction;
            
            LeftTurret.Initialize(robotBase);
            RightTurret.Initialize(robotBase);

            SetHoodPids(HoodPids);
            SetTurretPids(TurretPids);
        }

        public MoSimResult<MoSimLib.Void> UpdateValues(float velocityDampening, float tolerance, float reduction)
        {
            var version = MoSim.ActiveVersion;
            UpdateValuesV1(velocityDampening, tolerance, reduction);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void UpdateValuesV1(float velocityDampening, float tolerance, float reduction)
        {
            if (_velocityDampeningValue != velocityDampening) _velocityDampeningValue = velocityDampening;
            if (_hoodTolerance != tolerance) _hoodTolerance = tolerance;
            if (_hoodReduction != reduction) _hoodReduction = reduction;
        }
        
        public void SubsystemLoop()
        {
            var version = MoSim.ActiveVersion;
            SubsystemLoopV1();
        }

        private void SubsystemLoopV1()
        {
            UpdatePids(TurretPids, TurretsWrappingPid, HoodPids);
            
            if (_rigidbody == null) return;

            Vector3 leftVel = _rigidbody.GetPointVelocity(LeftTurret.transform.position);
            _leftSmoothedVel = Vector3.Lerp(_leftLastVelocity, leftVel, _velocityDampeningValue);
            _leftLastVelocity = leftVel;

            Vector3 rightVel = _rigidbody.GetPointVelocity(RightTurret.transform.position);
            _rightSmoothedVel = Vector3.Lerp(_rightLastVelocity, rightVel, _velocityDampeningValue);
            _rightLastVelocity = rightVel;
            
            LeftTurret.SetTrackContinuously(LeftTurretEnabled);
            RightTurret.SetTrackContinuously(RightTurretEnabled);
            
            _leftError = Math.Abs(LeftTurret.GetError());
            _rightError = Math.Abs(RightTurret.GetError());
        }

        public MoSimResult<MoSimLib.Void> UpdateTurretJoints()
        {
            var version = MoSim.ActiveVersion;
            UpdateTurretJointsV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void UpdateTurretJointsV1()
        {
            if (LeftTurretEnabled) LeftHood.SetTargetAngle(_leftHoodTarget).withAxis(LeftHoodJointAxis);
            if (RightTurretEnabled) RightHood.SetTargetAngle(_rightHoodTarget).withAxis(RightHoodJointAxis);
        }
        
        public MoSimResult<MoSimLib.Void> SetTurretEnabled(Turret turret, bool enabled)
        {
            var version = MoSim.ActiveVersion;
            SetTurretEnabledV1(turret, enabled);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetTurretEnabledV1(Turret turret, bool enabled)
        {
            if (turret == Turret.LEFT || turret == Turret.BOTH)
            {
                LeftTurretEnabled = enabled;
                LeftTurret.SetTrackContinuously(enabled);
            }
            if (turret == Turret.RIGHT || turret == Turret.BOTH)
            {
                RightTurretEnabled = enabled;
                RightTurret.SetTrackContinuously(enabled);
            }
        }
        
        public MoSimResult<Vector3> GetShootOnMoveTarget(Vector3 target, Turret side)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<Vector3>.Success(GetShootOnMoveTargetV1(target, side));
        }

        private Vector3 GetShootOnMoveTargetV1(Vector3 target, Turret side)
        {
            Vector3 vel = side == Turret.LEFT ? _leftSmoothedVel : _rightSmoothedVel;
            float tof = side == Turret.LEFT ? _leftTof : _rightTof;
            return target - vel * tof;
        }

        public MoSimResult<MoSimLib.Void> HandleTurretsTracking(bool pass)
        {
            var version = MoSim.ActiveVersion;
            HandleTurretsTrackingV1(pass);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void HandleTurretsTrackingV1(bool pass)
        {
            if (pass)
            {
                SetPassMapClosest();

                if (LeftTurretEnabled) LeftTurret.SetCustomTarget(GetShootOnMoveTarget(GetWallBiasedPassTarget(LeftTurret.GetCurrentTarget(), LeftTurret), Turret.LEFT));
                if (RightTurretEnabled) RightTurret.SetCustomTarget(GetShootOnMoveTarget(GetWallBiasedPassTarget(RightTurret.GetCurrentTarget(), RightTurret), Turret.RIGHT));
            }
            else
            {
                SetDefaultMap(GenericTurret.DefaultMap.Hub);
                
                if (LeftTurretEnabled) LeftTurret.SetCustomTarget(GetShootOnMoveTarget(LeftTurret.GetCurrentTarget(), Turret.LEFT));
                if (RightTurretEnabled) RightTurret.SetCustomTarget(GetShootOnMoveTarget(RightTurret.GetCurrentTarget(), Turret.RIGHT));
            }
            
            if (LeftTurretEnabled) LeftTurret.Align();
            if (RightTurretEnabled) RightTurret.Align();
        }

        public MoSimResult<MoSimLib.Void> SetTurretPids(PidConstants pids)
        {
            var version = MoSim.ActiveVersion;
            SetTurretPidsV1(pids);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetTurretPidsV1(PidConstants pids)
        {
            LeftTurret.SetPid(pids);
            RightTurret.SetPid(pids);
        }

        public MoSimResult<MoSimLib.Void> SetHoodPids(PidConstants pids)
        {
            var version = MoSim.ActiveVersion;
            SetHoodPidsV1(pids);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetHoodPidsV1(PidConstants pids)
        {
            LeftHood.SetPid(pids);
            RightHood.SetPid(pids);
        }

        public MoSimResult<MoSimLib.Void> UpdatePids(PidConstants turretPids, PidConstants turretWrappingPids, PidConstants hoodPids)
        {
            var version = MoSim.ActiveVersion;
            UpdatePidsV1(turretPids, turretWrappingPids, hoodPids);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void UpdatePidsV1(PidConstants turretPids, PidConstants turretWrappingPids, PidConstants hoodPids)
        {
            PidConstants leftPid = LeftTurret.IsWrapping() ? TurretsWrappingPid : TurretPids;
            PidConstants rightPid = RightTurret.IsWrapping() ? TurretsWrappingPid : TurretPids;
            
            LeftTurret.UpdatePid(leftPid);
            RightTurret.UpdatePid(rightPid);
            
            LeftHood.UpdatePid(HoodPids);
            RightHood.UpdatePid(HoodPids);
        }
        
        public MoSimResult<MoSimLib.Void> SetTrackContinuously(bool _trackContinuously)
        {
            var version = MoSim.ActiveVersion;
            SetTrackContinuouslyV1(_trackContinuously);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetTrackContinuouslyV1(bool _trackContinuously)
        {
            if (LeftTurretEnabled) LeftTurret.SetTrackContinuously(_trackContinuously);
            if (RightTurretEnabled) RightTurret.SetTrackContinuously(_trackContinuously);
        }

        public MoSimResult<MoSimLib.Void> SetBothTarget(Transform _target)
        {
            var version = MoSim.ActiveVersion;
            SetBothTargetV1(_target);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetBothTargetV1(Transform _target)
        {
            if (LeftTurretEnabled) LeftTurret.SetTarget(_target);
            if (RightTurretEnabled) RightTurret.SetTarget(_target);
        }

        public MoSimResult<MoSimLib.Void> SetBothTarget(Vector3 _target)
        {
            var version = MoSim.ActiveVersion;
            SetBothTargetV1(_target);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetBothTargetV1(Vector3 _target)
        {
            if (LeftTurretEnabled) LeftTurret.SetTarget(_target);
            if (RightTurretEnabled) RightTurret.SetTarget(_target);
        }

        public MoSimResult<MoSimLib.Void> ClearAllTarget()
        {
            var version = MoSim.ActiveVersion;
            ClearAllTargetV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void ClearAllTargetV1()
        {
            LeftTurret.ClearTarget();
            RightTurret.ClearTarget();
        }
        
        public MoSimResult<MoSimLib.Void> SetDefaultMap(GenericTurret.DefaultMap map)
        {
            var version = MoSim.ActiveVersion;
            SetDefaultMapV1(map);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetDefaultMapV1(GenericTurret.DefaultMap map)
        {
            if (LeftTurretEnabled) LeftTurret.SetDefaultMap(map);
            if (RightTurretEnabled) RightTurret.SetDefaultMap(map);
        }

        public MoSimResult<MoSimLib.Void> SetPassMapClosest()
        {
            var version = MoSim.ActiveVersion;
            SetPassMapClosestV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetPassMapClosestV1()
        {
            if (LeftTurretEnabled) LeftTurret.SetPassMapClosest();
            if (RightTurretEnabled) RightTurret.SetPassMapClosest();
        }

        public MoSimResult<MoSimLib.Void> Align()
        {
            var version = MoSim.ActiveVersion;
            AlignV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void AlignV1()
        {
            LeftTurret.Align();
            RightTurret.Align();
        }

        public MoSimResult<MoSimLib.Void> SetRpm(float rpm, Turret turret = Turret.BOTH)
        {
            var version = MoSim.ActiveVersion;
            SetRpmV1(rpm, turret);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetRpmV1(float rpm, Turret turret = Turret.BOTH)
        {
            if (turret == Turret.BOTH)
            {
                SetRpmV1(rpm, Turret.LEFT);
                SetRpmV1(rpm, Turret.RIGHT);
            }
            else if (turret == Turret.RIGHT && RightTurretEnabled)
            {
                RightShooterRollers.SetRpm(rpm);
                if (RightHoodRollers != null) RightHoodRollers.SetRpm(rpm / _hoodReduction);
            }
            else if (turret == Turret.LEFT && LeftTurretEnabled)
            {
                LeftShooterRollers.SetRpm(rpm);
                if (LeftHoodRollers != null) LeftHoodRollers.SetRpm(rpm / _hoodReduction);
            }
        }
        
        public MoSimResult<MoSimLib.Void> SetHoodAngle(float angle, Turret turret = Turret.BOTH)
        {
            var version = MoSim.ActiveVersion;
            SetHoodAngleV1(angle, turret);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetHoodAngleV1(float angle, Turret turret = Turret.BOTH)
        {
            if (turret == Turret.BOTH)
            {
                SetHoodAngleV1(angle, Turret.LEFT);
                SetHoodAngleV1(angle, Turret.RIGHT);
            }
            else if (turret == Turret.RIGHT)
            {
                if (IsWrapping(Turret.RIGHT))
                {
                    return;
                }
                _rightHoodTarget = angle;
            }
            else
            {
                if (IsWrapping(Turret.LEFT))
                {
                    return;
                }
                _leftHoodTarget = angle;
            }
        }

        public MoSimResult<ShooterHoodCommand> SetShooterAndHood(float rpm, float hoodAngle, Turret side)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<ShooterHoodCommand>.Success(SetShooterAndHoodV1(rpm, hoodAngle, side));
        }

        private ShooterHoodCommand SetShooterAndHoodV1(float rpm, float hoodAngle, Turret side)
        {
            if (side != Turret.BOTH)
            {
                SetRpm(rpm, side);
                SetHoodAngle(hoodAngle, side);
                return null;
            }
            return new ShooterHoodCommand(this, rpm, hoodAngle);
        }

        public MoSimResult<bool> IsWrapping(Turret turret)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(IsWrappingV1(turret));
        }

        private bool IsWrappingV1(Turret turret)
        {
            var joint = (turret == Turret.LEFT ? LeftTurret : RightTurret);

            return joint.IsWrapping();
        }
        
        private Vector3 GetWallBiasedPassTarget(Vector3 target, GenericTurret turret)
        {
            if (LeftTurret == null || RightTurret == null)
            {
                return target;
            }

            Vector3 hubTarget = turret.GetDefaultMapPosition(GenericTurret.DefaultMap.Hub);
            Vector3 turretPosition = turret.transform.position;
            Vector2 hubDirection = new(hubTarget.x - turretPosition.x, hubTarget.z - turretPosition.z);
            float hubDistance = hubDirection.magnitude;

            if (hubDistance <= Mathf.Epsilon)
            {
                return target;
            }

            float hubAngle = Mathf.Atan2(hubDirection.y, hubDirection.x);
            float wallDirection = target.z >= 0f ? 1f : -1f;
            float requestedOffset = Mathf.Cos(hubAngle) * (passWallBiasScalar / hubDistance) * wallDirection;
            float minWallZ = Mathf.Min(passFieldWallZLimits.x, passFieldWallZLimits.y);
            float maxWallZ = Mathf.Max(passFieldWallZLimits.x, passFieldWallZLimits.y);
            float distanceToWall = wallDirection > 0f
                ? maxWallZ - target.z
                : target.z - minWallZ;

            float clampedMagnitude = Mathf.Min(Mathf.Abs(requestedOffset), Mathf.Max(0f, distanceToWall));
            target.z += Mathf.Sign(requestedOffset) * clampedMagnitude;
            return target;
        }
        
        private void handleLeftDampedVelocity()
        {
            Vector3 currentTurretPos = LeftTurret.transform.position;
            Vector3 turretVelocity = _rigidbody.GetPointVelocity(currentTurretPos);
            _leftSmoothedVel = Vector3.Lerp(_leftLastVelocity, turretVelocity, _velocityDampeningValue);
            _leftLastVelocity = turretVelocity;
        }

        private void handleRightDampedVelocity()
        {
            Vector3 currentTurretPos = RightTurret.transform.position;
            Vector3 turretVelocity = _rigidbody.GetPointVelocity(currentTurretPos);
            _rightSmoothedVel = Vector3.Lerp(_rightLastVelocity, turretVelocity, _velocityDampeningValue);
            _rightLastVelocity = turretVelocity;
        }

        public void HandleDampedVelocity()
        {
            var version = MoSim.ActiveVersion;
            HandleDampedVelocityV1();
        }

        private void HandleDampedVelocityV1()
        {
            handleLeftDampedVelocity();
            handleRightDampedVelocity();
        }

        public MoSimResult<bool> HoodAtTarget(Turret turret)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(HoodAtTargetV1(turret));
        }

        private bool HoodAtTargetV1(Turret turret)
        {
            return MoSimLib.Utils.InAngularRange((turret == Turret.LEFT ? LeftHood : RightHood).GetAngle(), (turret == Turret.LEFT ? _leftHoodTarget : _rightHoodTarget),
                _hoodTolerance);
        }

        public MoSimResult<bool> ShooterAtSpeed(Turret turret)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(ShooterAtSpeedV1(turret));
        }

        private bool ShooterAtSpeedV1(Turret turret)
        {
            return (turret == Turret.LEFT ? LeftShooterRollers : RightShooterRollers).AtSpeed();
        }

        public MoSimResult<bool> ShooterAndHoodAtTarget(Turret turret)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(ShooterAndHoodAtTargetV1(turret));
        }

        private bool ShooterAndHoodAtTargetV1(Turret turret)
        {
            return ShooterAtSpeed(turret) && HoodAtTarget(turret);
        }

    }

    public class ShooterHoodCommand
    {
        private readonly DualTurretWrapper _wrapper;
        private readonly float _rpm;
        private readonly float _hoodAngle;
        
        private float _inactiveRpm = -2000f;
        
        public ShooterHoodCommand(DualTurretWrapper wrapper, float rpm, float hoodAngle)
        {
            _wrapper = wrapper;
            _rpm = rpm;
            _hoodAngle = hoodAngle;
        }
        
        private bool UnderTrench(Transform point, float distance = 0f)
        {
            if (distance < 0f) distance = 0f;
            
            var underTrench1X = false;
            var underTrench2X = false;
            
            var underTrenchZ = false;
            
            var localPoint = _wrapper._robotBase.transform.parent.InverseTransformPoint(point.position);
                
            underTrench1X = localPoint.x < (3.83f + distance) && localPoint.x > (3.46f - distance);
            underTrench2X = localPoint.x > (-3.83f - distance) && localPoint.x < (-3.46f + distance);
                
            underTrenchZ = localPoint.z < -2.7f || localPoint.z > 2.7f;
            
            return (underTrench1X || underTrench2X) && underTrenchZ;
        }
    }
}
