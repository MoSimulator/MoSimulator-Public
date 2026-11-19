using System.Collections.Generic;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.GamePieceSystem;
using RobotFramework.Interfaces;
using UnityEngine;

namespace RobotFramework.Controllers.GamePieceSystem
{
    public class RobotPiecePathController<TPiece, TData>
        where TPiece : GamePiece<TData> where TData : IGamePieceData
    {
        private GamePieceGraph _graph;
        private readonly RobotGamePieceController<TPiece, TData>.GamePieceControllerNode _inner;
        private GamePieceState _currentNode;
        private GamePieceState _destination;
        private GamePieceState _pendingDestination;

        private List<GamePieceState> _currentPath;
        private int _pathIndex;

        private enum TraversalState
        {
            Idle, 
            Moving, 
            Arrived,
        }

        private TraversalState _traversal = TraversalState.Idle;

        public RobotPiecePathController(
            GamePieceGraph graph, RobotGamePieceController<TPiece, TData>.GamePieceControllerNode inner)
        {
            _graph = graph;
            _currentNode = graph.GetPreload();
            _inner = inner;
        }
        
        public RobotPiecePathController(
            RobotGamePieceController<TPiece, TData>.GamePieceControllerNode inner)
        {
            _inner = inner;
        }

        public MoSimResult<MoSimLib.Void> SetGraph(GamePieceGraph graph)
        {
            var version = MoSim.ActiveVersion;
            SetGraphV1(graph);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetGraphV1(GamePieceGraph graph)
        {
            _graph = graph;
        }
        
        public MoSimResult<MoSimLib.Void> SetGraph(GamePieceGraph graph, GamePieceState node)
        {
            var version = MoSim.ActiveVersion;
            SetGraphV1(graph, node);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetGraphV1(GamePieceGraph graph, GamePieceState node)
        {
            _graph = graph;
            _currentNode = node;
            _destination = node;
            _traversal = TraversalState.Arrived;
        }

        /// <summary>
        /// Command the piece to move toward this destination.
        /// Safe to call every frame — only acts on change.
        /// If called mid-hop, the new destination takes effect at the
        /// next node arrival.
        /// </summary>
        public MoSimResult<MoSimLib.Void> SendTo(GamePieceState destination)
        {
            var version = MoSim.ActiveVersion;
            SendToV1(destination);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SendToV1(GamePieceState destination)
        {
            if (!HasPiece()) return;
            if (destination == _destination) return; // already headed there
            if (_traversal == TraversalState.Moving)
                _pendingDestination = destination;
            else
            {
                if (destination == _currentNode) return;
                SetDestination(destination);
            }
        }

        /// <summary>
        /// Returns true when the piece is sitting at the given node
        /// and is not in the middle of a hop.
        /// Use this to gate release conditions.
        /// </summary>
        public MoSimResult<bool> IsAt(GamePieceState node)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(IsAtV1(node));
        }

        private bool IsAtV1(GamePieceState node) =>
            _traversal != TraversalState.Moving && _currentNode == node;

        /// <summary>
        /// Returns true when the piece has arrived at the commanded destination.
        /// </summary>
        public MoSimResult<bool> IsAtDestination()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(IsAtDestinationV1());
        }

        private bool IsAtDestinationV1() =>
            _destination != null && IsAtV1(_destination);

        /// <summary>
        /// Passthrough — the underlying controller still owns intake logic.
        /// </summary>
        public MoSimResult<bool> HasPiece()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(HasPieceV1());
        }

        private bool HasPieceV1() => _inner.HasPiece();

        /// <summary>
        /// Release the piece with force in the holding target's local axes.
        /// Safe to call every frame — the underlying controller guards this.
        /// </summary>
        public MoSimResult<MoSimLib.Void> Release(Vector3 force)
        {
            var version = MoSim.ActiveVersion;
            ReleaseV1(force);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void ReleaseV1(Vector3 force)
        {
            _inner.ReleaseGamePieceWithForce(force);
            _traversal = TraversalState.Idle;
            _currentPath = null;
            _pendingDestination = null;
        }

        /// <summary>
        /// Request intake from a given intake source
        /// Safe to call every frame.
        /// </summary>
        public MoSimResult<MoSimLib.Void> RequestIntake(GamePieceIntake<TPiece, TData> intake, bool request)
        {
            var version = MoSim.ActiveVersion;
            RequestIntakeV1(intake, request);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void RequestIntakeV1(GamePieceIntake<TPiece, TData> intake, bool request)
        {
            _inner.RequestIntake(intake, request);

            // When a piece is newly acquired, reset position tracking to intake node
            if (_inner.HasPiece() && _inner.atTarget && _traversal == TraversalState.Idle)
            {
                _currentNode = _inner.GetCurrentState();
                _traversal   = TraversalState.Arrived;
            }
        }

        public MoSimResult<MoSimLib.Void> RequestIntakeFromSmoothHandoff(GamePieceIntake<TPiece, TData> intake, GamePieceState intakeTarget,
            bool request)
        {
            var version = MoSim.ActiveVersion;
            RequestIntakeFromSmoothHandoffV1(intake, intakeTarget, request);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void RequestIntakeFromSmoothHandoffV1(GamePieceIntake<TPiece, TData> intake, GamePieceState intakeTarget,
            bool request)
        {
            _inner.RequestIntake(intake, request);

            if (_inner.HasPiece() && _inner.atTarget && _traversal == TraversalState.Idle)
            {
                _currentNode = _inner.GetCurrentState();
                _traversal   = TraversalState.Arrived;
            }
            else if (!_inner.HasPiece() && request && _traversal == TraversalState.Idle)
            {
                _inner.SetTargetState(intakeTarget);
            }
        }

        public MoSimResult<MoSimLib.Void> Tick()
        {
            var version = MoSim.ActiveVersion;
            TickV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void TickV1()
        {
            if (!_inner.HasPiece())
            {
                _traversal = TraversalState.Idle;
                _pendingDestination = null;
                return;
            }
            
            switch (_traversal)
            {
                case TraversalState.Idle:

                    break;

                case TraversalState.Arrived:
                    if (_pendingDestination != null)
                    {
                        SetDestination(_pendingDestination);
                        _pendingDestination = null;
                    }

                    if (_currentPath != null && _pathIndex < _currentPath.Count)
                    {
                        GamePieceState nextNode = _currentPath[_pathIndex];
                        _inner.SetTargetState(nextNode);
                        _traversal = TraversalState.Moving;
                    } else if (_destination != _currentNode && _currentPath == null)
                    {
                        SetDestination(_destination);
                    }
                    break;

                case TraversalState.Moving:
                    // Check if the piece has reached the next node
                    GamePieceState target = _currentPath?[_pathIndex];
                    
                    if (target != null && _inner.currentStateNum == target.stateNum && _inner.atTarget)
                    {
                        _currentNode = target;
                        _pathIndex++;
                        _traversal = TraversalState.Arrived;


                        // Apply pending destination at this node boundary
                        if (_pendingDestination != null)
                        {
                            SetDestination(_pendingDestination);
                            _pendingDestination = null;
                        }
                    }

                    break;
            }
        }

        private void SetDestination(GamePieceState destination)
        {
            if (destination == _currentNode)
            {
                // Already here
                _destination = destination;
                _currentPath = null;
                _pathIndex = 0;
                _traversal = TraversalState.Arrived;
                return;
            }

            List<GamePieceState> path = _graph.FindPath(_currentNode, destination);
            if (path == null) return; // FindPath logs the error

            _destination = destination;
            _currentPath = path;
            _pathIndex = 0; // index 0 is _currentNode starting at 0 makes sure it finishes animating to the entry point
            _traversal = TraversalState.Moving; // trigger first hop next tick
        }

    }
}
