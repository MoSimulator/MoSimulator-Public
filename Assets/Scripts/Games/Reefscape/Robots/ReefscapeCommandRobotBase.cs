using System;
using System.Collections.Generic;
using Games.Reefscape.Enums;
using Games.Reefscape.FieldScripts;
using Games.Reefscape.GamePieceSystem;
using Games.Reefscape.Scoring.Scorers;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework;
using UnityEngine;

namespace Games.Reefscape.Robots
{
    public class ReefscapeCommandRobotBase : StateCommandRobotBase
    {
        public MoSimInput L4Action { get; private set; }
        public MoSimInput L3Action { get; private set; }
        public MoSimInput L2Action { get; private set; }
        public MoSimInput L1Action { get; private set; }
        public MoSimInput ClimbAction { get; private set; }

        public MoSimInput AutoAlignLeftAction { get; private set; }
        public MoSimInput AutoAlignRightAction { get; private set; }

        public MoSimInput RobotModeToggleAction { get; private set; }
        public MoSimInput IntakeModeToggleAction { get; private set; }
        public MoSimInput RobotSpecialAction { get; private set; }

        /// <summary>Gets the stow/home input action.</summary>
        public MoSimInput StowAction { get; private set; }

        /// <summary>Gets the intake input action.</summary>
        public MoSimInput IntakeAction { get; private set; }

        /// <summary>Gets the outtake/place input action.</summary>
        public MoSimInput OuttakeAction { get; private set; }
        
        private MoSimActionScheduler _actions;
        
        private MoSimInput[] _cachedInputs;
        
        [field: SerializeField] public CoralStationMode CurrentCoralStationMode { get; private set; }

        [SerializeField] public bool superCycler;
        private GameObject _targetReef;
        private Reef _refReef;
        public bool FacingReef { get; private set; }
        public bool FacingBarge { get; private set; }
        
        private bool _hasAlgae;
        private bool _hasCoral = true;

        [HideInInspector] public Collider bumperCollider;
        
        public ReefscapeRobotMode CurrentRobotMode { get; private set; }
        public ReefscapeIntakeMode CurrentIntakeMode { get; private set; }

        /// <summary>Mode the driver has toggled to; applied to CurrentIntakeMode once no coral is held.</summary>
        public ReefscapeIntakeMode RequestedIntakeMode { get; private set; }
        
        protected ReefscapeRobotGamePieceController RobotGamePieceController { get; private set; }

        protected ReefscapeRobotPiecePathController CoralPathController { get; private set; }
        protected ReefscapeRobotPiecePathController AlgaePathController { get; private set; }
        
        protected int HasCoralTrigger { get; set; }

        protected override void Start()
        {
            RobotGamePieceController = GetComponent<ReefscapeRobotGamePieceController>();
            if (RobotGamePieceController == null)
            {
                Debug.LogError("ReefscapeRobotGamePieceController component not found on the robot!");
            }
            
            CoralPathController =
                new ReefscapeRobotPiecePathController(RobotGamePieceController.GetPieceByName(nameof(ReefscapeGamePieceType.Coral)));
            AlgaePathController = 
                new ReefscapeRobotPiecePathController(RobotGamePieceController.GetPieceByName(nameof(ReefscapeGamePieceType.Algae)));
            
            base.Start();
            
            
            _actions = new MoSimActionScheduler();
            _cachedInputs = new[]
            {
                L4Action, L3Action, L2Action, L1Action, ClimbAction, AutoAlignLeftAction, AutoAlignRightAction,
                RobotModeToggleAction, IntakeModeToggleAction, OuttakeAction, RobotSpecialAction, StowAction, IntakeAction, OuttakeAction
            };

            CurrentState = new StowState(this);
            
            var coralStations = FindObjectsByType(typeof(CoralStation), FindObjectsSortMode.None);
            if (coralStations.Length == 0)
            {
                Debug.LogError("No CoralStation found in the scene!");
            }

            foreach (var coralStation in coralStations)
            {
                var station = (CoralStation)coralStation;
                if (Alliance == Alliance.Blue && station.Alliance == Alliance.Blue ||
                    Alliance == Alliance.Red && station.Alliance == Alliance.Red)
                {
                    station.CommandRobots.Add(this);
                }
            }
            
            _targetReef = Alliance == Alliance.Blue ? GameObject.Find("BlueReef") : GameObject.Find("RedReef");
            _refReef = _targetReef.GetComponent<Reef>();
        }
        
        protected override void Awake()
        {
            base.Awake();

            bumperCollider = GetComponent<BoxCollider>();
            if (GetComponent<ReefscapePushPenaltyScorer>() == null)
            {
                gameObject.AddComponent<ReefscapePushPenaltyScorer>();
            }

            SetupInputActions();
        }
        
        protected override void Update()
        {
            base.Update();
            HandleRumble();
            _actions.AddActionsToPool(
                _cachedInputs
            );
        }
        
        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!superCycler)
            {
                if (RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0)
                {
                    CurrentRobotMode = ReefscapeRobotMode.Algae;
                }
                else if (RobotGamePieceController.GetPieceByName("Coral").currentStateNum > 0)
                {
                    CurrentRobotMode = ReefscapeRobotMode.Coral;
                } else if (RobotModeToggleAction.WasTriggered())
                {
                    if (CurrentRobotMode == ReefscapeRobotMode.Algae)
                    {
                        CurrentRobotMode = ReefscapeRobotMode.Coral;
                    } else if (CurrentRobotMode == ReefscapeRobotMode.Coral)
                    {
                        CurrentRobotMode = ReefscapeRobotMode.Algae;
                    }
                }
            }
            else
            {
                if (RobotModeToggleAction.WasTriggered())
                {
                    if (CurrentRobotMode == ReefscapeRobotMode.Algae)
                    {
                        CurrentRobotMode = ReefscapeRobotMode.Coral;
                    } else if (CurrentRobotMode == ReefscapeRobotMode.Coral)
                    {
                        CurrentRobotMode = ReefscapeRobotMode.Algae;
                    }
                }
            }

            if (IntakeModeToggleAction.WasTriggered() && !RightStickModifierAction.IsPressed())
            {
                RequestedIntakeMode = RequestedIntakeMode == ReefscapeIntakeMode.L1
                    ? ReefscapeIntakeMode.Normal
                    : ReefscapeIntakeMode.L1;
            }

            // The requested mode only takes effect while empty, so a held piece
            // keeps the handling it was intaken with until it leaves the robot.
            if (RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0)
            {
                CurrentIntakeMode = RequestedIntakeMode;
            }
            CoralPathController.Tick();
            AlgaePathController.Tick();
            _actions.ResetActionsPool(
                _cachedInputs
            );
        }
        
        private void HandleRumble()
        {
            if (!DoControllerRumble)
            {
                Debug.LogWarning("wha");
                return;
            }

            switch (_hasCoral)
            {
                case false when (HasCoralTrigger > 0
                    ? RobotGamePieceController.GetPieceByName("Coral").currentStateNum == HasCoralTrigger
                    : RobotGamePieceController.GetPieceByName("Coral").currentStateNum > 0):
                    _hasCoral = true;
                    OnRumbleTrigger.Invoke();
                    break;
                case true when RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0:
                    _hasCoral = false;
                    break;
            }

            switch (_hasAlgae)
            {
                case false when RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0:
                    _hasAlgae = true;
                    OnRumbleTrigger.Invoke();
                    break;
                case true when RobotGamePieceController.GetPieceByName("Algae").currentStateNum == 0:
                    _hasAlgae = false;
                    break;
            }
        }

        private void SetupInputActions()
        {
            L4Action = new MoSimInput(InputActionMap.FindAction("L4"));
            L3Action = new MoSimInput(InputActionMap.FindAction("L3"));
            L2Action = new MoSimInput(InputActionMap.FindAction("L2"));
            L1Action = new MoSimInput(InputActionMap.FindAction("L1"));
            ClimbAction = new MoSimInput(InputActionMap.FindAction("Climb"));

            AutoAlignLeftAction = new MoSimInput(InputActionMap.FindAction("AutoAlignLeft"));
            AutoAlignRightAction = new MoSimInput( InputActionMap.FindAction("AutoAlignRight"));

            RobotModeToggleAction = new MoSimInput(InputActionMap.FindAction("RobotModeToggle"));
            IntakeModeToggleAction = new MoSimInput(InputActionMap.FindAction("IntakeModeToggle"));
            RobotSpecialAction = new MoSimInput(InputActionMap.FindAction("RobotSpecial"));

            StowAction = new MoSimInput(InputActionMap.FindAction("Stow"));
            OuttakeAction = new MoSimInput(InputActionMap.FindAction("Place"));
            IntakeAction = new MoSimInput(InputActionMap.FindAction("Intake"));
        }

        protected void SetRobotMode(ReefscapeRobotMode mode)
        {
            CurrentRobotMode = mode;
        }

        protected void SetIntakeMode(ReefscapeIntakeMode mode)
        {
            RequestedIntakeMode = mode;
            CurrentIntakeMode = mode;
        }

        public void CheckFacingReef()
        {
            var toReefVector = (_targetReef.transform.position - transform.position).normalized;
            var robotForwardVector = transform.forward.normalized;
            var angle = Vector3.Dot(robotForwardVector, toReefVector);
            FacingReef = angle > 0.0f;
        }

        public void CheckFacingBarge()
        {
            var toZAxisXY = new Vector3(-transform.position.x, -transform.position.y, 0f).normalized;
            var forwardXY = new Vector3(transform.forward.x, transform.forward.y, 0f).normalized;
            var dot = Vector3.Dot(forwardXY, toZAxisXY);
            FacingBarge = dot > 0.0f;
        }

        /// <summary>
        /// Returns the distance to the closest point on the barge in inches
        /// </summary>
        /// <returns></returns>
        public float GetDistanceToBarge()
        {
            var currentPose = transform.position;

            // Get the spawner's local axis in world space
            var axisDirection = transform.forward;

            // Project the vector from spawner to target onto the axis
            var projectionLength = Vector3.Dot(-currentPose, axisDirection);

            var closestPoint = currentPose + axisDirection * projectionLength;

            // Return the point along the spawner's axis within the bounds
            return Vector3.Distance(closestPoint, currentPose) * 39.37008f;
        }

        public Reef GetReef()
        {
            return _refReef;
        }

        public bool GetFacingReef()
        {
            return FacingReef;
        }

        public bool GetFacingBarge()
        {
            return FacingBarge;
        }

        public bool Stowed()
        {
            return CurrentState is StowState;
        }
        
        public bool Intaking()
        {
            return CurrentState is IntakeState;
        }

        public bool Climb()
        {
            return CurrentState is ClimbState;
        }

        public bool Climbed()
        {
            return  CurrentState is ClimbedState;
        }

        public bool Processor()
        {
            return CurrentState is ProcessorState;
        }
        
        public virtual bool AtSetpoint()
        {
            return false;
        }
        
       // ── Stow ────────────────────────────────────────────────────
        #nullable enable
        protected sealed class StowState : RobotState<ReefscapeCommandRobotBase>
        {
            public StowState(ReefscapeCommandRobotBase robot) : base(robot) { }
            
            public override IRobotState? CheckTransition()
            {
                return TransitionBehaviour(() =>
                {
                    // IntakeAction held → Intake
                    // (guard against algae setpoints is handled by those states not
                    //  transitioning back here while IntakeAction is still held)
                    if (Robot.IntakeAction.IsPressed())
                        return new IntakeState(Robot);

                    // Outtake → Place
                    if (Robot.OuttakeAction.WasTriggered())
                        return new PlaceState(Robot);

                    if (Robot.L1Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        var hasAlgae = Robot.RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0;
                        var hasCoral = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum > 0;

                        if (hasAlgae || hasCoral)
                            return hasAlgae ? new ProcessorState(Robot) : new L1State(Robot);

                        // Empty-handed in Algae mode → Stack else return null
                        return Robot.CurrentRobotMode == ReefscapeRobotMode.Algae ? new StackState(Robot) : null;
                    }

                    if (Robot.L2Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        var noCoral = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0;
                        var algaeMode = Robot.CurrentRobotMode == ReefscapeRobotMode.Algae && Robot.superCycler;
                        return (noCoral || algaeMode) ? new LowAlgaeState(Robot) : new L2State(Robot);
                    }

                    if (Robot.L3Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        var noCoral = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0;
                        var algaeMode = Robot.CurrentRobotMode == ReefscapeRobotMode.Algae && Robot.superCycler;
                        return (noCoral || algaeMode) ? new HighAlgaeState(Robot) : new L3State(Robot);
                    }

                    if (Robot.L4Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        Robot.CheckFacingBarge();
                        var hasAlgae = Robot.RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0;
                        var hasCoral = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum > 0;

                        if (!hasAlgae && !hasCoral) return null;

                        var noCoral = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0;
                        var algaeMode = Robot.CurrentRobotMode == ReefscapeRobotMode.Algae && Robot.superCycler;
                        if (noCoral || algaeMode)
                            return hasAlgae ? new BargeState(Robot) : new StowState(Robot);
                        return new L4State(Robot);
                    }

                    if (Robot.RobotSpecialAction.WasTriggered())
                        return new RobotSpecialState(Robot);

                    if (Robot.ClimbAction.WasTriggered() && !Robot.RightStickModifierAction.IsPressed())
                        return new ClimbState(Robot);

                    return null;
                }, 
                    this);

            }
        
            public override void OnEnter() =>
                Robot.Bindings.GetValueOrDefault(typeof(StowState))?.EnterAction?.Invoke();
        
            public override void OnMechanismUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(StowState))?.MechanismsAction?.Invoke();
        
            public override void OnGamePieceUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(StowState))?.GamePieceAction?.Invoke();
        }
        
        // ── Intake ───────────────────────────────────────────────────
        protected sealed class IntakeState : RobotState<ReefscapeCommandRobotBase>
        {
            public IntakeState(ReefscapeCommandRobotBase robot) : base(robot) { }
        
            public override IRobotState? CheckTransition()
            {
                return TransitionBehaviour(() =>
                {
                    if (!Robot.IntakeAction.IsPressed())
                        return new StowState(Robot);

                    return null;
                },
                    this);
            }
        
            public override void OnEnter() =>
                Robot.Bindings.GetValueOrDefault(typeof(IntakeState))?.EnterAction?.Invoke();
        
            public override void OnMechanismUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(IntakeState))?.MechanismsAction?.Invoke();
        
            public override void OnGamePieceUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(IntakeState))?.GamePieceAction?.Invoke();
        }
        
        // ── Place ────────────────────────────────────────────────────
        protected sealed class PlaceState : RobotState<ReefscapeCommandRobotBase>
        {
            public PlaceState(ReefscapeCommandRobotBase robot) : base(robot) { }
        
            public override IRobotState? CheckTransition()
            {
                return TransitionBehaviour(() =>
                {
                    if (Robot.L2Action.WasTriggered()) return new LowAlgaeState(Robot);
                    if (Robot.L3Action.WasTriggered()) return new HighAlgaeState(Robot);
                    if (Robot.L1Action.WasTriggered()) return new StowState(Robot);
                    if (Robot.WasLastState<L1State>() && Robot.L1Action.WasTriggered()) return new StowState(Robot);
                    if (Robot.WasLastState<L2State>() && Robot.L2Action.WasTriggered()) return new StowState(Robot);
                    if (Robot.WasLastState<L3State>() && Robot.L3Action.WasTriggered()) return new StowState(Robot);
                    if (Robot.WasLastState<L4State>() && Robot.L4Action.WasTriggered()) return new StowState(Robot);
                    if (Robot.WasLastState<ProcessorState>() && Robot.L1Action.WasTriggered()) return new StowState(Robot);
                    if (Robot.WasLastState<BargeState>() && Robot.L4Action.WasTriggered()) return new StowState(Robot);
                    if (Robot.WasLastState<LowAlgaeState>() && Robot.L2Action.WasTriggered()) return new StowState(Robot);
                    if (Robot.WasLastState<HighAlgaeState>() && Robot.L3Action.WasTriggered()) return new StowState(Robot);
                    if (Robot.WasLastState<StackState>() && Robot.L1Action.WasTriggered()) return new StowState(Robot);
                    if (Robot.StowAction.WasTriggered() || Robot.IntakeAction.WasTriggered()) return new StowState(Robot);

                    return null;
                }, 
                    this);
            }
        
            public override void OnEnter() =>
                Robot.Bindings.GetValueOrDefault(typeof(PlaceState))?.EnterAction?.Invoke();
        
            public override void OnMechanismUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(PlaceState))?.MechanismsAction?.Invoke();
        
            public override void OnGamePieceUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(PlaceState))?.GamePieceAction?.Invoke();
        }
        
        // ── L1 ──────────────────────────────────────────────────────
        protected sealed class L1State : RobotState<ReefscapeCommandRobotBase>
        {
            public L1State(ReefscapeCommandRobotBase robot) : base(robot) { }
        
            public override IRobotState? CheckTransition()
            {
                return TransitionBehaviour(() =>
                {
                    if (Robot.L1Action.WasTriggered() || Robot.StowAction.IsPressed())
                        return new StowState(Robot);
                    if (Robot.OuttakeAction.WasTriggered())
                        return new PlaceState(Robot);

                    if (Robot.L2Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        var noCoral   = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0;
                        var algaeMode = Robot.CurrentRobotMode == ReefscapeRobotMode.Algae && Robot.superCycler;
                        return (noCoral || algaeMode) ? new LowAlgaeState(Robot) : new L2State(Robot);
                    }

                    if (Robot.L3Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        var noCoral   = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0;
                        var algaeMode = Robot.CurrentRobotMode == ReefscapeRobotMode.Algae && Robot.superCycler;
                        return (noCoral || algaeMode) ? new HighAlgaeState(Robot) : new L3State(Robot);
                    }

                    if (Robot.L4Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        Robot.CheckFacingBarge();
                        var hasAlgae  = Robot.RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0;
                        var hasCoral  = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum > 0;

                        if (!hasAlgae && !hasCoral) return null;

                        var noCoral   = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0;
                        var algaeMode = Robot.CurrentRobotMode == ReefscapeRobotMode.Algae && Robot.superCycler;
                        if (noCoral || algaeMode)
                            return hasAlgae ? new BargeState(Robot) : new StowState(Robot);
                        return new L4State(Robot);
                    }

                    return null;
                },
                    this);
            }
        
            public override void OnEnter() =>
                Robot.Bindings.GetValueOrDefault(typeof(L1State))?.EnterAction?.Invoke();
        
            public override void OnMechanismUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(L1State))?.MechanismsAction?.Invoke();
        
            public override void OnGamePieceUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(L1State))?.GamePieceAction?.Invoke();
        }
        
        // ── L2 ──────────────────────────────────────────────────────
        protected sealed class L2State : RobotState<ReefscapeCommandRobotBase>
        {
            public L2State(ReefscapeCommandRobotBase robot) : base(robot) { }
        
            public override IRobotState? CheckTransition()
            {
                return TransitionBehaviour(() =>
                {
                    if (Robot.L2Action.WasTriggered() || Robot.StowAction.IsPressed() || Robot.L1Action.WasTriggered())
                        return new StowState(Robot);
                    if (Robot.OuttakeAction.WasTriggered())
                        return new PlaceState(Robot);
                    if (Robot.L1Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        var hasAlgae = Robot.RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0;
                        var hasCoral = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum > 0;

                        if (hasAlgae || hasCoral)
                            return hasAlgae ? new ProcessorState(Robot) : new L1State(Robot);

                        // Empty-handed in Algae mode → Stack
                        if (Robot.CurrentRobotMode == ReefscapeRobotMode.Algae)
                            return new StackState(Robot);

                        // Empty-handed otherwise → stay Stow (no-op)
                        return null;
                    }

                    if (Robot.L3Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        var noCoral   = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0;
                        var algaeMode = Robot.CurrentRobotMode == ReefscapeRobotMode.Algae && Robot.superCycler;
                        return (noCoral || algaeMode) ? new HighAlgaeState(Robot) : new L3State(Robot);
                    }

                    if (Robot.L4Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        Robot.CheckFacingBarge();
                        var hasAlgae  = Robot.RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0;
                        var hasCoral  = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum > 0;

                        if (!hasAlgae && !hasCoral) return null;

                        var noCoral   = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0;
                        var algaeMode = Robot.CurrentRobotMode == ReefscapeRobotMode.Algae && Robot.superCycler;
                        if (noCoral || algaeMode)
                            return hasAlgae ? new BargeState(Robot) : new StowState(Robot);
                        return new L4State(Robot);
                    }

                    return null;
                },
                    this);
            }
        
            public override void OnEnter() =>
                Robot.Bindings.GetValueOrDefault(typeof(L2State))?.EnterAction?.Invoke();
        
            public override void OnMechanismUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(L2State))?.MechanismsAction?.Invoke();
        
            public override void OnGamePieceUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(L2State))?.GamePieceAction?.Invoke();
        }
        
        // ── L3 ──────────────────────────────────────────────────────
        protected sealed class L3State : RobotState<ReefscapeCommandRobotBase>
        {
            public L3State(ReefscapeCommandRobotBase robot) : base(robot) { }
        
            public override IRobotState? CheckTransition()
            {
                return TransitionBehaviour(() =>
                {
                    if (Robot.L3Action.WasTriggered() || Robot.StowAction.IsPressed() || Robot.L1Action.WasTriggered())
                        return new StowState(Robot);
                    if (Robot.OuttakeAction.WasTriggered())
                        return new PlaceState(Robot);
                    if (Robot.L1Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        var hasAlgae = Robot.RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0;
                        var hasCoral = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum > 0;

                        if (hasAlgae || hasCoral)
                            return hasAlgae ? new ProcessorState(Robot) : new L1State(Robot);

                        // Empty-handed in Algae mode → Stack
                        if (Robot.CurrentRobotMode == ReefscapeRobotMode.Algae)
                            return new StackState(Robot);

                        // Empty-handed otherwise → stay Stow (no-op)
                        return null;
                    }

                    if (Robot.L2Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        var noCoral   = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0;
                        var algaeMode = Robot.CurrentRobotMode == ReefscapeRobotMode.Algae && Robot.superCycler;
                        return (noCoral || algaeMode) ? new LowAlgaeState(Robot) : new L2State(Robot);
                    }

                    if (Robot.L4Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        Robot.CheckFacingBarge();
                        var hasAlgae  = Robot.RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0;
                        var hasCoral  = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum > 0;

                        if (!hasAlgae && !hasCoral) return null;

                        var noCoral   = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0;
                        var algaeMode = Robot.CurrentRobotMode == ReefscapeRobotMode.Algae && Robot.superCycler;
                        if (noCoral || algaeMode)
                            return hasAlgae ? new BargeState(Robot) : new StowState(Robot);
                        return new L4State(Robot);
                    }

                    return null;
                },
                    this);
            }
        
            public override void OnEnter() =>
                Robot.Bindings.GetValueOrDefault(typeof(L3State))?.EnterAction?.Invoke();
        
            public override void OnMechanismUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(L3State))?.MechanismsAction?.Invoke();
        
            public override void OnGamePieceUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(L3State))?.GamePieceAction?.Invoke();
        }
        
        // ── L4 ──────────────────────────────────────────────────────
        protected sealed class L4State : RobotState<ReefscapeCommandRobotBase>
        {
            public L4State(ReefscapeCommandRobotBase robot) : base(robot) { }
        
            public override IRobotState? CheckTransition()
            {
                return TransitionBehaviour(() =>
                {
                    if (Robot.L4Action.WasTriggered() || Robot.StowAction.IsPressed() || Robot.L1Action.WasTriggered())
                        return new StowState(Robot);
                    if (Robot.OuttakeAction.WasTriggered())
                        return new PlaceState(Robot);
                    if (Robot.L1Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        var hasAlgae = Robot.RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0;
                        var hasCoral = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum > 0;

                        if (hasAlgae || hasCoral)
                            return hasAlgae ? new ProcessorState(Robot) : new L1State(Robot);

                        // Empty-handed in Algae mode → Stack
                        if (Robot.CurrentRobotMode == ReefscapeRobotMode.Algae)
                            return new StackState(Robot);

                        // Empty-handed otherwise → stay Stow (no-op)
                        return null;
                    }

                    if (Robot.L2Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        var noCoral   = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0;
                        var algaeMode = Robot.CurrentRobotMode == ReefscapeRobotMode.Algae && Robot.superCycler;
                        return (noCoral || algaeMode) ? new LowAlgaeState(Robot) : new L2State(Robot);
                    }

                    if (Robot.L3Action.WasTriggered())
                    {
                        Robot.CheckFacingReef();
                        var noCoral   = Robot.RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0;
                        var algaeMode = Robot.CurrentRobotMode == ReefscapeRobotMode.Algae && Robot.superCycler;
                        return (noCoral || algaeMode) ? new HighAlgaeState(Robot) : new L3State(Robot);
                    }

                    return null;
                },
                    this);
            }
        
            public override void OnEnter() =>
                Robot.Bindings.GetValueOrDefault(typeof(L4State))?.EnterAction?.Invoke();
        
            public override void OnMechanismUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(L4State))?.MechanismsAction?.Invoke();
        
            public override void OnGamePieceUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(L4State))?.GamePieceAction?.Invoke();
        }
        
        // ── LowAlgae ─────────────────────────────────────────────────
        protected sealed class LowAlgaeState : RobotState<ReefscapeCommandRobotBase>
        {
            public LowAlgaeState(ReefscapeCommandRobotBase robot) : base(robot) { }
        
            public override IRobotState? CheckTransition()
            {
                return TransitionBehaviour(() =>
                {
                    if (Robot.L2Action.WasTriggered() || Robot.StowAction.IsPressed() || Robot.L1Action.WasTriggered())
                        return new StowState(Robot);

                    var algaeAcquired = Robot.RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0;
                    if (algaeAcquired && !Robot.IntakeAction.IsPressed())
                        return new StowState(Robot);

                    if (Robot.OuttakeAction.WasTriggered())
                        return new PlaceState(Robot);

                    if (Robot.L3Action.WasTriggered()) return new HighAlgaeState(Robot);

                    return null;
                },
                    this);
            }
        
            public override void OnEnter() =>
                Robot.Bindings.GetValueOrDefault(typeof(LowAlgaeState))?.EnterAction?.Invoke();
        
            public override void OnMechanismUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(LowAlgaeState))?.MechanismsAction?.Invoke();
        
            public override void OnGamePieceUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(LowAlgaeState))?.GamePieceAction?.Invoke();
        }
        
        // ── HighAlgae ────────────────────────────────────────────────
        protected sealed class HighAlgaeState : RobotState<ReefscapeCommandRobotBase>
        {
            public HighAlgaeState(ReefscapeCommandRobotBase robot) : base(robot) { }
        
            public override IRobotState? CheckTransition()
            {
                return TransitionBehaviour(() =>
                {
                    if (Robot.L3Action.WasTriggered() || Robot.StowAction.IsPressed() || Robot.L1Action.WasTriggered())
                        return new StowState(Robot);

                    var algaeAcquired = Robot.RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0;
                    if (algaeAcquired && !Robot.IntakeAction.IsPressed())
                        return new StowState(Robot);

                    if (Robot.OuttakeAction.WasTriggered())
                        return new PlaceState(Robot);

                    if (Robot.L2Action.WasTriggered()) return new LowAlgaeState(Robot);

                    return null;
                },
                    this);
            }
        
            public override void OnEnter() =>
                Robot.Bindings.GetValueOrDefault(typeof(HighAlgaeState))?.EnterAction?.Invoke();
        
            public override void OnMechanismUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(HighAlgaeState))?.MechanismsAction?.Invoke();
        
            public override void OnGamePieceUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(HighAlgaeState))?.GamePieceAction?.Invoke();
        }
        
        // ── Processor ────────────────────────────────────────────────
        protected sealed class ProcessorState : RobotState<ReefscapeCommandRobotBase>
        {
            public ProcessorState(ReefscapeCommandRobotBase robot) : base(robot) { }
        
            public override IRobotState? CheckTransition()
            {
                return TransitionBehaviour(() =>
                {
                    if (Robot.L1Action.WasTriggered() || Robot.StowAction.IsPressed())
                        return new StowState(Robot);
                    if (Robot.OuttakeAction.WasTriggered())
                        return new PlaceState(Robot);

                    return null;
                },
                    this);
            }
        
            public override void OnEnter() =>
                Robot.Bindings.GetValueOrDefault(typeof(ProcessorState))?.EnterAction?.Invoke();
        
            public override void OnMechanismUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(ProcessorState))?.MechanismsAction?.Invoke();
        
            public override void OnGamePieceUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(ProcessorState))?.GamePieceAction?.Invoke();
        }
        
        // ── Barge ────────────────────────────────────────────────────
        protected sealed class BargeState : RobotState<ReefscapeCommandRobotBase>
        {
            public BargeState(ReefscapeCommandRobotBase robot) : base(robot) { }
        
            public override IRobotState? CheckTransition()
            {
                return TransitionBehaviour(() =>
                {
                    if (Robot.L4Action.WasTriggered() || Robot.StowAction.IsPressed() || Robot.L1Action.WasTriggered())
                        return new StowState(Robot);
                    if (Robot.OuttakeAction.WasTriggered())
                        return new PlaceState(Robot);

                    return null;
                },
                    this);
            }
        
            public override void OnEnter() =>
                Robot.Bindings.GetValueOrDefault(typeof(BargeState))?.EnterAction?.Invoke();
        
            public override void OnMechanismUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(BargeState))?.MechanismsAction?.Invoke();
        
            public override void OnGamePieceUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(BargeState))?.GamePieceAction?.Invoke();
        }
        
        // ── Stack ────────────────────────────────────────────────────
        protected sealed class StackState : RobotState<ReefscapeCommandRobotBase>
        {
            public StackState(ReefscapeCommandRobotBase robot) : base(robot) { }
        
            public override IRobotState? CheckTransition()
            {
                return TransitionBehaviour(() =>
                {
                    if (Robot.L1Action.WasTriggered() || Robot.StowAction.IsPressed())
                        return new StowState(Robot);

                    var algaeAcquired = Robot.RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0;
                    if (algaeAcquired && !Robot.IntakeAction.IsPressed())
                        return new StowState(Robot);

                    if (Robot.OuttakeAction.WasTriggered())
                        return new PlaceState(Robot);

                    return null;
                },
                    this);
            }
        
            public override void OnEnter() =>
                Robot.Bindings.GetValueOrDefault(typeof(StackState))?.EnterAction?.Invoke();
        
            public override void OnMechanismUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(StackState))?.MechanismsAction?.Invoke();
        
            public override void OnGamePieceUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(StackState))?.GamePieceAction?.Invoke();
        }
        
        // ── RobotSpecial ─────────────────────────────────────────────
        protected sealed class RobotSpecialState : RobotState<ReefscapeCommandRobotBase>
        {
            public RobotSpecialState(ReefscapeCommandRobotBase robot) : base(robot) { }
        
            public override IRobotState? CheckTransition()
            {
                return TransitionBehaviour(() =>
                {
                    if (Robot.RobotSpecialAction.WasTriggered() || Robot.L1Action.WasTriggered())
                        return new StowState(Robot);

                    return null;
                },
                    this);
            }
        
            public override void OnEnter() =>
                Robot.Bindings.GetValueOrDefault(typeof(RobotSpecialState))?.EnterAction?.Invoke();
        
            public override void OnMechanismUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(RobotSpecialState))?.MechanismsAction?.Invoke();
        
            public override void OnGamePieceUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(RobotSpecialState))?.GamePieceAction?.Invoke();
        }
        
        // ── Climb ────────────────────────────────────────────────────
        protected sealed class ClimbState : RobotState<ReefscapeCommandRobotBase>
        {
            public ClimbState(ReefscapeCommandRobotBase robot) : base(robot) { }
        
            public override IRobotState? CheckTransition()
            {
                return TransitionBehaviour(() =>
                {
                    if (Robot.ClimbAction.WasTriggered() && !Robot.RightStickModifierAction.IsPressed())
                        return new ClimbedState(Robot);

                    return null;
                },
                    this);
            }
        
            public override void OnEnter() =>
                Robot.Bindings.GetValueOrDefault(typeof(ClimbState))?.EnterAction?.Invoke();
        
            public override void OnMechanismUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(ClimbState))?.MechanismsAction?.Invoke();
        
            public override void OnGamePieceUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(ClimbState))?.GamePieceAction?.Invoke();
        }
        
        // ── Climbed ──────────────────────────────────────────────────
        protected sealed class ClimbedState : RobotState<ReefscapeCommandRobotBase>
        {
            public ClimbedState(ReefscapeCommandRobotBase robot) : base(robot) { }
        
            public override IRobotState? CheckTransition()
            {
                return TransitionBehaviour(() =>
                {
                    if (Robot.ClimbAction.WasTriggered() && !Robot.RightStickModifierAction.IsPressed())
                        return new StowState(Robot);

                    return null;
                },
                    this);
            }
        
            public override void OnEnter() =>
                Robot.Bindings.GetValueOrDefault(typeof(ClimbedState))?.EnterAction?.Invoke();
        
            public override void OnMechanismUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(ClimbedState))?.MechanismsAction?.Invoke();
        
            public override void OnGamePieceUpdate() =>
                Robot.Bindings.GetValueOrDefault(typeof(ClimbedState))?.GamePieceAction?.Invoke();
        }

    }
}
