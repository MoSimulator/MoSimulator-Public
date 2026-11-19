using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.GamePieceSystem;
using RobotFramework.Interfaces;
using UnityEngine;

namespace RobotFramework.Controllers.GamePieceSystem
{
    public class RobotGamePieceController<TPiece, TData> : RobotGamePieceControllerBase
        where TPiece : GamePiece<TData> where TData : IGamePieceData
    {
        [Header("Game Piece Nodes")] [SerializeField]
        protected List<GamePieceControllerNode> gamePieceNodes;
        
        private GamePieceState _preloadState;

        private RobotBase _robotBase;
        
        private Dictionary<string, GamePieceControllerNode> _nodeQuickLookup;

        // Add a new implementation before raising this boundary. Public entry points
        // deliberately remain stable and select the implementation for the caller

        private static void LogUnsupportedVersion(string apiName, MoSimVersion version)
        {
            Debug.LogError($"{apiName} does not support MoSim API version {version}.");
        }

        protected void OnEnable()
        {
            _nodeQuickLookup = new Dictionary<string, GamePieceControllerNode>();
        }

        public MoSimResult<MoSimLib.Void> SetPreload(GamePieceState state)
        {
            var version = MoSim.ActiveVersion;
            SetPreloadV1(state);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetPreloadV1(GamePieceState state)
        {
            _preloadState = state;
        }

        private void Start()
        {
            InitializeNodes();

            if (_preloadState != null)
            {
                InitializePreload();
            }
        }

        private void InitializeNodes()
        {
            foreach (var node in gamePieceNodes)
            {
                node.StateLookup.Clear();

                if (node.gamePieceStates != null)
                {
                    foreach (var state in node.gamePieceStates)
                    {
                        if (state == null || string.IsNullOrWhiteSpace(state.name)) continue;
                        var key = state.name.Trim().ToLower();
                        if (!node.StateLookup.ContainsKey(key))
                        {
                            node.StateLookup[key] = new GamePieceData(state.stateNum, state.stateTarget,
                                state.stateMoveSpeed,
                                state.angularSpeed, state.smoothHandoff);
                        }
                    }
                }

                node.currentStateNum = 0;
                node.atTarget = false;
                node.movingTo = string.Empty;
                node.wasMovingTo = string.Empty;
                node.requestIntake.Clear();
                node.gamePieceController = this;
            }
        }

        private void InitializePreload()
        {
            if (!TryGetComponent(out _robotBase)) return;
            if (_robotBase.PreloadGamePiece == null) return;

            var preload = Instantiate(_robotBase.PreloadGamePiece, _preloadState.stateTarget.position,
                _preloadState.stateTarget.rotation, _preloadState.stateTarget);

            var controller = preload.GetComponent<GamePieceController<TPiece, TData>>();
            if (!controller) return;

            var node = gamePieceNodes.FirstOrDefault(node => !node.controller);
            if (node == null) return;

            node.controller = controller;
            node.currentStateNum = _preloadState.stateNum;
            node.wasMovingTo = _preloadState.name?.Trim().ToLower() ?? string.Empty;
            node.atTarget = true;
        }

        private void FixedUpdate()
        {
            foreach (var node in gamePieceNodes)
            {
                ProcessNode(node);
            }
        }

        private void ProcessNode(GamePieceControllerNode controllerNode)
        {
            if (controllerNode?.intakes == null || controllerNode.intakes.Count == 0) return;

            foreach (var intake in controllerNode.requestIntake)
            {
                if (controllerNode.currentStateNum == 0 && intake.Item2)
                {
                    var val = intake;
                    val.Item2.requestIntake = val.Item1;
                    val.Item2.pieceName = controllerNode.pieceName;
                }
            }
            
            controllerNode.requestIntake.Clear();
            

            var securingIntake = controllerNode.intakes.FirstOrDefault(intake => intake && intake.securedGamePiece);

            if (securingIntake && controllerNode.currentStateNum == 0)
            {
                if (string.IsNullOrWhiteSpace(controllerNode.movingTo))
                {
                    if (controllerNode.gamePieceStates is { Length: > 0 } &&
                        !string.IsNullOrWhiteSpace(controllerNode.gamePieceStates[0].name))
                    {
                        controllerNode.movingTo = controllerNode.gamePieceStates[0].name.Trim().ToLower();
                    }
                }

                var state = GetNodeState(controllerNode, controllerNode.movingTo);

                controllerNode.controller = securingIntake.GamePieceController;
                securingIntake.RemovePiece();

                controllerNode.requestIntake.Clear();
                controllerNode.wasMovingTo = controllerNode.movingTo;
                controllerNode.atTarget = !securingIntake.smoothHandoff;
                controllerNode.currentStateNum = state.stateNum;
            }

            if (controllerNode.controller)
            {
                UpdateNodeMovement(controllerNode);
            }
        }

        private void UpdateNodeMovement(GamePieceControllerNode controllerNode)
        {
            controllerNode.requestIntake.Clear();

            if (string.IsNullOrWhiteSpace(controllerNode.movingTo))
            {
                return;
            }

            if (!controllerNode.atTarget && controllerNode.currentStateNum != 0)
            {
                MoveNodeTowards(controllerNode, controllerNode.wasMovingTo);
            }
            else if (!string.Equals(controllerNode.wasMovingTo, controllerNode.movingTo, StringComparison.OrdinalIgnoreCase) &&
                     controllerNode.atTarget)
            {
                MoveNodeTowards(controllerNode, controllerNode.movingTo);
            }
        }

        private void MoveNodeTowards(GamePieceControllerNode controllerNode, string targetStateName)
        {
            var state = GetNodeState(controllerNode, targetStateName);

            if (!state.stateTarget)
            {
                controllerNode.atTarget = true;
                return;
            }

            controllerNode.currentStateNum = state.stateNum;

            var result = controllerNode.controller.Move(
                state.stateTarget,
                state.stateMoveSpeed,
                state.stateAngularSpeed,
                state.smoothHandoff);

            controllerNode.atTarget = result == 1;
            controllerNode.wasMovingTo = targetStateName;
        }

        private GamePieceData GetNodeState(GamePieceControllerNode controllerNode, string targetStateName)
        {
            if (string.IsNullOrWhiteSpace(targetStateName))
            {
                return new GamePieceData(0, null, 0, 0, false);
            }

            targetStateName = targetStateName.Trim().ToLower();
            if (!controllerNode.StateLookup.TryGetValue(targetStateName, out var gamePieceData))
            {
                Debug.LogError($"State '{targetStateName}' not found in node '{controllerNode.pieceName}'.");
                return new GamePieceData(0, null, 0, 0, false);
            }

            return gamePieceData;
        }

        public override MoSimResult<MoSimLib.Void> RunCoroutine(IEnumerator coroutine)
        {
            var version = MoSim.ActiveVersion;

            RunCoroutineV1(coroutine);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        public MoSimResult<MoSimLib.Void> RunCoroutine(MoSimResult<IEnumerator> coroutine)
        {
            var version = MoSim.ActiveVersion;

            RunCoroutineV1(coroutine.Value);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void RunCoroutineV1(IEnumerator coroutine)
        {
            StartCoroutine(coroutine);
        }
        
        /// <summary>
        /// Returns the Node to interact with a specific Piece
        /// </summary>
        /// <param name="nodeName"></param>
        /// <returns></returns>
        public GamePieceControllerNode GetPieceByName(string nodeName)
        {
            var version = MoSim.ActiveVersion;

            return GetPieceByNameV1(nodeName);
        }

        private GamePieceControllerNode GetPieceByNameV1(string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName))
            {
                return null;
            }
            
            var found = _nodeQuickLookup.TryGetValue(nodeName, out var node);
            
            if (found) return node;
            
            node = gamePieceNodes.FirstOrDefault(checkNode => string.Equals(checkNode.pieceName?.Trim().ToLower(), nodeName.Trim().ToLower(), StringComparison.OrdinalIgnoreCase));
            if (node == null) return null;
            _nodeQuickLookup.Add(nodeName, node);

            return node;
        }

        [Serializable]
        public class GamePieceControllerNode
        {
            [Tooltip("Name of the game piece node (e.g. Coral, Algae, etc.)")]
            public string pieceName;

            [Tooltip("List of intakes associated with this node")]
            public List<GamePieceIntake<TPiece, TData>> intakes = new();

            [Tooltip("Ordered list of states for this game piece node")]
            public GamePieceState[] gamePieceStates;

            [Header("Runtime Variables (Read-Only)")]
            public GamePieceController<TPiece, TData> controller;

            public int currentStateNum;
            public bool atTarget;
            public string movingTo = "";
            public string wasMovingTo = "";

            [NonSerialized] public Dictionary<string, GamePieceData> StateLookup = new();

            [HideInInspector] public RobotGamePieceController<TPiece, TData> gamePieceController;

            public List<(bool, GamePieceIntake<TPiece, TData>)> requestIntake = new();

            /// <summary>Releases with force relative to the holding target, independent of the piece's orientation.</summary>
            public MoSimResult<bool> ReleaseGamePieceWithForce(Vector3 force,
                ForceMode forceMode = ForceMode.Impulse,
                bool whenAtTarget = true)
            {
                var version = MoSim.ActiveVersion;
                return MoSimResult<bool>.Success(ReleaseGamePieceWithForceV1(force, forceMode, whenAtTarget));
            }

            private bool ReleaseGamePieceWithForceV1(Vector3 force,
                ForceMode forceMode = ForceMode.Impulse,
                bool whenAtTarget = true)
            {
                var node = this;
                if (node == null)
                {
                    throw new NullReferenceException($"GamePieceNode '{pieceName}' not found.");
                }

                if (whenAtTarget && !node.atTarget)
                {
                    return false;
                }

                if (node.controller)
                {
                    node.controller.Release(force, forceMode);
                    node.currentStateNum = 0;
                    node.controller = null;
                    node.atTarget = false;
                    return true;
                }

                return false;
            }
            
            /// <summary>Starts force in the holding target's axes, then follows the piece's rotation throughout release.</summary>
            public MoSimResult<bool> ReleaseGamePieceWithContinuedForce(Vector3 force, float time, float maxSpeed,
                bool whenAtTarget = true)
            {
                var version = MoSim.ActiveVersion;
                return MoSimResult<bool>.Success(ReleaseGamePieceWithContinuedForceV1(force, time, maxSpeed, whenAtTarget));
            }
            
            private bool ReleaseGamePieceWithContinuedForceV1(Vector3 force, float time, float maxSpeed,
                bool whenAtTarget = true)
            {
                var node = this;
                if (node == null)
                {
                    throw new NullReferenceException($"GamePieceNode '{pieceName}' not found.");
                }

                if (whenAtTarget && !node.atTarget)
                {
                    return false;
                }

                if (node.controller != null)
                {
                    gamePieceController.RunCoroutine(node.controller.ContinuedRelease(force, time, maxSpeed));
                    node.currentStateNum = 0;
                    node.controller = null;
                    node.atTarget = false;
                    return true;
                }

                return false;
            }

            public MoSimResult<bool> HasPiece()
            {
                var version = MoSim.ActiveVersion;
                return MoSimResult<bool>.Success(HasPieceV1());
            }

            private bool HasPieceV1()
            {
                return currentStateNum > 0;
            }
            
            public GamePieceState GetCurrentState()
            {
                var version = MoSim.ActiveVersion;
                return GetCurrentStateV1();
            }

            private GamePieceState GetCurrentStateV1()
            {
                var node = this;
                if (node == null)
                {
                    throw new NullReferenceException($"GamePieceNode '{pieceName}' not found.");
                }

                return node.gamePieceStates.FirstOrDefault(state =>
                    state.name.Equals(node.wasMovingTo, StringComparison.OrdinalIgnoreCase));
            }
            
            public MoSimResult<MoSimLib.Void> SetTargetState(string stateName)
            {
                var version = MoSim.ActiveVersion;
                SetTargetStateV1(stateName);
                return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
            }

            private void SetTargetStateV1(string stateName)
            {
                var node = this;
                if (node == null)
                {
                    throw new NullReferenceException($"GamePieceNode '{pieceName}' not found.");
                }

                if (string.IsNullOrWhiteSpace(stateName))
                {
                    Debug.LogError($"SetNodeTargetState called with null or empty stateName for node '{stateName}'.");
                    return;
                }

                var key = stateName.Trim().ToLower();
                if (!node.StateLookup.ContainsKey(key))
                {
                    Debug.LogError($"State '{stateName}' not found in node '{pieceName}'.");
                    return;
                }

                node.movingTo = key;
            }

            public MoSimResult<MoSimLib.Void> SetTargetState(GamePieceState state)
            {
                var version = MoSim.ActiveVersion;
                SetTargetStateV1(state);
                return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
            }

            private void SetTargetStateV1(GamePieceState state)
            {
                if (state == null)
                {
                    Debug.LogError($"SetNodeTargetState called with null state for node '{pieceName}'.");
                    return;
                }
                SetTargetStateV1(state.name);
            }
            
            public MoSimResult<MoSimLib.Void> RequestIntake(GamePieceIntake<TPiece, TData> intakePoint, bool requestIntake = true)
            {
                var version = MoSim.ActiveVersion;
                RequestIntakeV1(intakePoint, requestIntake);
                return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
            }

            private void RequestIntakeV1(GamePieceIntake<TPiece, TData> intakePoint, bool requestIntake)
            {
                var node = this;
                if (!intakePoint)
                {
                    throw new NullReferenceException($"GamePieceNode '{pieceName}' not found.");
                }

                if (!node.requestIntake.Contains((requestIntake, intakePoint)) || !node.requestIntake.Contains((!requestIntake, intakePoint)))
                {
                    node.requestIntake.Add((requestIntake, intakePoint));
                }
            }
            
            public MoSimResult<MoSimLib.Void> MoveIntake(GamePieceIntake<TPiece, TData> intake, Transform newTarget)
            {
                var version = MoSim.ActiveVersion;
                MoveIntakeV1(intake, newTarget);
                return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
            }

            private void MoveIntakeV1(GamePieceIntake<TPiece, TData> intake, Transform newTarget)
            {
                if (!intake) return;
                
                intake.ChangeTarget(newTarget);
            }
            
            public MoSimResult<bool> IntakeHasPieces(GamePieceIntake<TPiece, TData> intake)
            {
                var version = MoSim.ActiveVersion;
                return MoSimResult<bool>.Success(IntakeHasPiecesV1(intake));
            }

            private bool IntakeHasPiecesV1(GamePieceIntake<TPiece, TData> intake)
            {
                var pieceNode = this;
                if (pieceNode == null)
                {
                    throw new NullReferenceException($"GamePieceNode '{pieceName}' not found.");
                }

                return intake && intake.hasGamePiece;
            }

            #region LegacyAPI


            #endregion
        }
    }
}
