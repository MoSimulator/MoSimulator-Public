using Games.Reefscape.Enums;
using Games.Reefscape.GamePieceSystem;
using Games.Reefscape.Robots;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.Controllers.GamePieceSystem;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using RobotFramework.GamePieceSystem;
using UnityEngine;

namespace Prefabs.Reefscape.Robots.Mods.TestingMod._5985
{
    public class Volans : ReefscapeRobotBase
    {
        [SerializeField] private GenericElevator elevator;
        [SerializeField] private GenericJoint arm;

        [SerializeField] private PidConstants armPid;

        [SerializeField] private VolansSetpoint stow;
        [SerializeField] private VolansSetpoint intake;
        [SerializeField] private VolansSetpoint l1;
        [SerializeField] private VolansSetpoint l2;
        [SerializeField] private VolansSetpoint l3;
        [SerializeField] private VolansSetpoint l4;
        [SerializeField] private VolansSetpoint l4Place;
        [SerializeField] private VolansSetpoint lowAlgae;
        [SerializeField] private VolansSetpoint highAlgae;
        [SerializeField] private VolansSetpoint bargePrep;
        [SerializeField] private VolansSetpoint bargePlace;

        [SerializeField] private ReefscapeGamePieceIntake coralIntake;
        [SerializeField] private ReefscapeGamePieceIntake algaeIntake;

        [SerializeField] private GamePieceState coralStowState;
        [SerializeField] private GamePieceState algaeStowState;

        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _coralController;
        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _algaeController;

        private float _elevatorTargetHeight;
        private float _armTargetAngle;

        protected override void Start()
        {
            base.Start();

            arm.SetPid(armPid);

            _elevatorTargetHeight = 0;

            RobotGamePieceController.SetPreload(coralStowState);

            _coralController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Coral.ToString());
            _algaeController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Algae.ToString());

            _coralController.gamePieceStates = new[] { coralStowState };
            _coralController.intakes.Add(coralIntake);

            _algaeController.gamePieceStates = new[] { algaeStowState };
            _algaeController.intakes.Add(algaeIntake);
        }

        private void LateUpdate()
        {
            arm.UpdatePid(armPid);
        }

        private void FixedUpdate()
        {
            bool hasAlgae = _algaeController.HasPiece();
            bool hasCoral = _coralController.HasPiece();

            _algaeController.SetTargetState(algaeStowState);
            _coralController.SetTargetState(coralStowState);

            switch (CurrentSetpoint)
            {
                case ReefscapeSetpoints.Stow:
                    SetSetpoint(stow);
                    break;

                case ReefscapeSetpoints.Intake:
                    SetSetpoint(intake);
                    _algaeController.RequestIntake(algaeIntake, CurrentRobotMode == ReefscapeRobotMode.Algae && !hasAlgae);
                    _coralController.RequestIntake(coralIntake, CurrentRobotMode == ReefscapeRobotMode.Coral && !hasCoral);
                    break;

                case ReefscapeSetpoints.Place:
                    if (LastSetpoint == ReefscapeSetpoints.Barge)
                    {
                        SetSetpoint(bargePlace);
                    }
                    else if (LastSetpoint == ReefscapeSetpoints.L4)
                    {
                        SetSetpoint(l4Place);
                    }
                    PlacePiece();
                    break;

                case ReefscapeSetpoints.L1:
                    SetSetpoint(l1);
                    break;

                case ReefscapeSetpoints.Stack:
                    SetSetpoint(intake);
                    _algaeController.RequestIntake(algaeIntake, IntakeAction.IsPressed() && !hasAlgae);
                    _coralController.RequestIntake(coralIntake, false);
                    break;

                case ReefscapeSetpoints.L2:
                    SetSetpoint(l2);
                    break;

                case ReefscapeSetpoints.LowAlgae:
                    SetSetpoint(lowAlgae);
                    _algaeController.RequestIntake(algaeIntake, IntakeAction.IsPressed() && !hasAlgae);
                    _coralController.RequestIntake(coralIntake, false);
                    break;

                case ReefscapeSetpoints.L3:
                    SetSetpoint(l3);
                    break;

                case ReefscapeSetpoints.HighAlgae:
                    SetSetpoint(highAlgae);
                    _algaeController.RequestIntake(algaeIntake, IntakeAction.IsPressed() && !hasAlgae);
                    _coralController.RequestIntake(coralIntake, false);
                    break;

                case ReefscapeSetpoints.L4:
                    SetSetpoint(l4);
                    break;

                case ReefscapeSetpoints.Processor:
                    SetSetpoint(stow);
                    break;

                case ReefscapeSetpoints.Barge:
                    SetSetpoint(bargePrep);
                    break;

                case ReefscapeSetpoints.RobotSpecial:
                    SetState(ReefscapeSetpoints.Stow);
                    break;

                case ReefscapeSetpoints.Climb:
                    break;

                case ReefscapeSetpoints.Climbed:
                    break;
            }

            UpdateSetpoints();
        }

        private void SetSetpoint(VolansSetpoint setpoint)
        {
            _elevatorTargetHeight = setpoint.elevatorHeight;
            _armTargetAngle = setpoint.armAngle;
            Debug.Log(setpoint);
        }

        private void UpdateSetpoints()
        {
            elevator.SetTarget(_elevatorTargetHeight);
            arm.SetTargetAngle(_armTargetAngle).withAxis(JointAxis.Z);
            Debug.Log($"Elevator Target: {_elevatorTargetHeight}, Arm Target: {_armTargetAngle}");
        }

        private void PlacePiece()
        {
            if (CurrentRobotMode == ReefscapeRobotMode.Algae)
            {
                _algaeController.ReleaseGamePieceWithForce(new Vector3(0, 0, 0));
            }
            else
            {
                _coralController.ReleaseGamePieceWithForce(new Vector3(0, 0, 0));
            }
        }
    }
}
