using System.Collections.Generic;
using System.Linq;
using MoSimLib;
using RobotFramework.GamePieceSystem;
using UnityEngine;

namespace RobotFramework.Controllers.GamePieceSystem
{
    public class GamePieceGraph
    {
        /// Defines the physical waypoint graph a game piece travels through on a robot.
        /// 
        /// USAGE — call these in your robot's Init() to describe how pieces move:
        /// 
        ///     graph.AddNode(intake,       isIntake: true)
        ///          .AddNode(splinePoint)
        ///          .AddNode(shooterStow)
        ///          .AddNode(ampStow)
        ///          .AddEdge(intake,       splinePoint)
        ///          .AddEdge(splinePoint,  shooterStow)
        ///          .AddEdge(splinePoint,  ampStow);
        /// 
        /// All edges are bidirectional — pieces can move forward and backward.
        /// Each node is exclusive; only one piece may occupy a node at a time.
        /// </summary>
        /// 
        private readonly Dictionary<GamePieceState, Node> _nodes = new();

        public GamePieceGraph AddNode(GamePieceState state, bool isPreload = false)
        {
            var version = MoSim.ActiveVersion;
            return AddNodeV1(state, isPreload);
        }

        private GamePieceGraph AddNodeV1(GamePieceState state, bool isPreload = false)
        {
            if (_nodes.ContainsKey(state))
            {
                return this;
            }

            _nodes[state] = new Node { State = state, isPreload = isPreload };
            return this;
        }

        public GamePieceGraph AddNodes(params GamePieceState[] nodes)
        {
            var version = MoSim.ActiveVersion;
            return AddNodesV1(nodes);
        }

        private GamePieceGraph AddNodesV1(params GamePieceState[] nodes)
        {
            foreach (var node in nodes)
            {
                _nodes[node] = new Node { State = node, isPreload = false };
            }

            return this;
        }

        public GamePieceGraph AddNodes(params (GamePieceState, bool isPreload)[] nodes)
        {
            var version = MoSim.ActiveVersion;
            return AddNodesV1(nodes);
        }

        private GamePieceGraph AddNodesV1(params (GamePieceState, bool isPreload)[] nodes)
        {
            foreach (var node in nodes)
            {
                var piece = node.Item1;
                var isPreload = node.Item2;

                _nodes[piece] = new Node { State = piece, isPreload = isPreload };
            }

            return this;
        }

        public GamePieceGraph AddEdge(GamePieceState a, GamePieceState b)
        {
            var version = MoSim.ActiveVersion;
            return AddEdgeV1(a, b);
        }

        private GamePieceGraph AddEdgeV1(GamePieceState a, GamePieceState b)
        {
            if (!Validate(a, nameof(a)) || !Validate(b, nameof(b))) return this;

            Node nodeA = _nodes[a];
            Node nodeB = _nodes[b];

            if (!nodeA.Neighbors.Contains(nodeB)) nodeA.Neighbors.Add(nodeB);
            if (!nodeB.Neighbors.Contains(nodeA)) nodeB.Neighbors.Add(nodeA);

            return this;
        }

        public MoSimResult<GamePieceState[]> GetStates()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<GamePieceState[]>.Success(GetStatesV1());
        }

        private GamePieceState[] GetStatesV1()
        {
            return _nodes.Keys.ToArray();
        }

        public MoSimResult<List<GamePieceState>> FindPath(GamePieceState from, GamePieceState to)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<List<GamePieceState>>.Success(FindPathV1(from, to));
        }

        private List<GamePieceState> FindPathV1(GamePieceState from, GamePieceState to)
        {
            if (!Validate(from, nameof(from)) || !Validate(to, nameof(to))) return null;
            if (from == to) return new List<GamePieceState> { from };

            // Standard BFS
            var visited = new HashSet<Node>();
            var queue = new Queue<Node>();
            var cameFrom = new Dictionary<Node, Node>();

            Node start = _nodes[from];
            Node goal = _nodes[to];

            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                Node current = queue.Dequeue();

                if (current == goal)
                    return ReconstructPath(cameFrom, start, goal);

                foreach (Node neighbor in current.Neighbors)
                {
                    if (visited.Contains(neighbor)) continue;
                    visited.Add(neighbor);
                    cameFrom[neighbor] = current;
                    queue.Enqueue(neighbor);
                }
            }

            Debug.LogError($"[NotePathGraph] No path found from '{from.name}' to '{to.name}'. " +
                           $"Check that AddEdge() connects these nodes.");
            return null;
        }

        public MoSimResult<GamePieceState> GetPreload()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<GamePieceState>.Success(GetPreloadV1());
        }

        private GamePieceState GetPreloadV1()
        {
            foreach (var pair in _nodes)
                if (pair.Value.isPreload)
                    return pair.Key;

            Debug.LogError("[NotePathGraph] No intake node registered. " +
                           "Call AddNode(state, isIntake: true) for your intake state.");
            return null;
        }

        private List<GamePieceState> ReconstructPath(Dictionary<Node, Node> cameFrom, Node start, Node goal)
        {
            var path = new List<GamePieceState>();
            Node current = goal;

            while (current != start)
            {
                path.Add(current.State);
                current = cameFrom[current];
            }

            path.Add(start.State);
            path.Reverse();
            return path;
        }

        private bool Validate(GamePieceState state, string paramName)
        {
            if (_nodes.ContainsKey(state)) return true;
            Debug.LogError($"[NotePathGraph] '{paramName}' ('{state?.name}') is not registered. " +
                           $"Call AddNode() before AddEdge().");
            return false;
        }
    }

    public class Node
    {
        public GamePieceState State;
        public bool isPreload;
        public List<Node> Neighbors = new();
    }
}
