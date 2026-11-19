using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RobotFramework.GamePieceSystem
{
    // Returns Game Pieces that have left the Field
    public class GamePieceFieldReturn : MonoBehaviour
    {
        private const float SlideStep = 0.5f;
        private const int SlideSteps = 20;

        // Fix exclusion zone not working
        private const float ReturnClearance = 0.6f;

        [Serializable]
        public class ExclusionZone
        {
            public Transform anchor;
            public Vector3 center = new(0f, 2f, 0f);
            public Vector3 size = new(1.1f, 4f, 2.29f);
        }

        [Serializable]
        public class PieceReturn
        {
            public string tag;

            [Tooltip("Height above field the game piece is release from")]
            [Min(0f)] public float dropHeight = 0.71f;
            
            [Tooltip("Horizontal Offset from the wall")]
            public float offsetFromWall = -0.35f;

            [Tooltip("Force applied to Game Piece for Field Return from Wall")]
            [Min(0f)] public float nudgeSpeed = 0.7f;

            [Tooltip("Places this specific Game Piece is allowed to sit outside the Field")]
            public ExclusionZone[] exclusionZones = Array.Empty<ExclusionZone>();
        }

        private class TrackedPiece
        {
            public PieceReturn Piece;
            public Vector3 ExitPoint;
            public float OutTimer;
            public float Delay = -1f;
        }

        [Header("Pieces")]
        [SerializeField] private PieceReturn[] pieces = Array.Empty<PieceReturn>();

        [Header("Field Bounds")]
        [SerializeField] private Vector3 fieldCenter = new(0f, 4f, 0f);
        [SerializeField] private Vector3 fieldSize = new(17.53f, 8f, 8.06f);

        // Any out of field zones a box does not cover
        [Tooltip("Areas inside the bounds that are actually out of the Field")]
        [SerializeField] private ExclusionZone[] outOfFieldZones = Array.Empty<ExclusionZone>();

        [Header("Return")]
        [Min(0f)]
        [SerializeField] private float returnDelayMin = 2f;
        [Min(0f)]
        [SerializeField] private float returnDelayMax = 6f;

        [Tooltip("How close a Robot can be before the Game Piece waits to be put back")]
        [Min(0f)]
        [SerializeField] private float robotClearance = 1f;

        [Header("Tracking")]
        [Min(0.1f)]
        [SerializeField] private float rescanInterval = 1f;

        private readonly Dictionary<Rigidbody, TrackedPiece> _tracked = new();
        private readonly List<Rigidbody> _stale = new();
        private readonly List<RobotBase> _robots = new();

        private float _rescanTimer;

        private void Start()
        {
            if (pieces.Length == 0)
            {
                Debug.LogError("GamePieceFieldReturn: no pieces configured on " + gameObject.name);
                enabled = false;
                return;
            }

            foreach (var piece in pieces)
            {
                if (string.IsNullOrEmpty(piece.tag))
                {
                    Debug.LogError("GamePieceFieldReturn: a piece entry has no tag.");
                    enabled = false;
                    return;
                }

                try
                {
                    GameObject.FindGameObjectsWithTag(piece.tag);
                }
                catch (UnityException)
                {
                    Debug.LogError("GamePieceFieldReturn: tag \"" + piece.tag + "\" is not defined.");
                    enabled = false;
                    return;
                }
            }

            Rescan();
        }

        private void FixedUpdate()
        {
            _rescanTimer += Time.fixedDeltaTime;
            if (_rescanTimer >= rescanInterval)
            {
                Rescan();
            }

            foreach (var pair in _tracked)
            {
                var body = pair.Key;
                var state = pair.Value;

                if (body == null)
                {
                    _stale.Add(body);
                    continue;
                }

                if (!body.useGravity)
                {
                    ResetTimer(state);
                    continue;
                }

                var position = body.position;

                if (IsInsideField(position))
                {
                    state.ExitPoint = position;
                    ResetTimer(state);
                    continue;
                }

                if (IsInExclusionZone(state.Piece, position))
                {
                    ResetTimer(state);
                    continue;
                }

                if (state.Delay < 0f)
                {
                    state.Delay = Random.Range(returnDelayMin, returnDelayMax);
                }

                state.OutTimer += Time.fixedDeltaTime;
                if (state.OutTimer < state.Delay)
                {
                    continue;
                }

                ComputeReturn(state.Piece, state.ExitPoint, out var point, out var velocity);

                if (!IsClear(point))
                {
                    continue;
                }

                body.position = point;
                body.angularVelocity = Vector3.zero;
                body.velocity = velocity;
                ResetTimer(state);
            }

            if (_stale.Count == 0)
            {
                return;
            }

            foreach (var body in _stale)
            {
                _tracked.Remove(body);
            }

            _stale.Clear();
        }

        private static void ResetTimer(TrackedPiece state)
        {
            state.OutTimer = 0f;
            state.Delay = -1f;
        }

        private bool IsInsideField(Vector3 worldPosition)
        {
            if (!IsInBox(transform, worldPosition, fieldCenter, fieldSize))
            {
                return false;
            }

            return !IsInAnyZone(outOfFieldZones, worldPosition);
        }

        private static bool IsInAnyZone(ExclusionZone[] zones, Vector3 worldPosition)
        {
            foreach (var zone in zones)
            {
                if (zone?.anchor != null && IsInBox(zone.anchor, worldPosition, zone.center, zone.size))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInExclusionZone(PieceReturn piece, Vector3 worldPosition)
        {
            return IsInAnyZone(piece.exclusionZones, worldPosition);
        }

        private static bool IsInBox(Transform anchor, Vector3 worldPosition, Vector3 center, Vector3 size)
        {
            var local = anchor.InverseTransformPoint(worldPosition) - center;

            return Mathf.Abs(local.x) <= size.x * 0.5f &&
                   Mathf.Abs(local.y) <= size.y * 0.5f &&
                   Mathf.Abs(local.z) <= size.z * 0.5f;
        }

        private bool IsClear(Vector3 point)
        {
            if (robotClearance <= 0f)
            {
                return true;
            }

            foreach (var robot in _robots)
            {
                if (robot == null)
                {
                    continue;
                }

                var offset = robot.transform.position - point;
                offset.y = 0f;

                if (offset.sqrMagnitude < robotClearance * robotClearance)
                {
                    return false;
                }
            }

            return true;
        }

        private void ComputeReturn(PieceReturn piece, Vector3 exitPoint, out Vector3 point, out Vector3 velocity)
        {
            var local = transform.InverseTransformPoint(exitPoint);
            var extents = fieldSize * 0.5f;
            var offset = Mathf.Min(piece.offsetFromWall, extents.z);

            // Game piece over side wall
            var side = local.z >= fieldCenter.z ? 1f : -1f;

            var minX = fieldCenter.x - extents.x;
            var maxX = fieldCenter.x + extents.x;
            var baseX = Mathf.Clamp(local.x, minX, maxX);

            local.z = fieldCenter.z + side * (extents.z - offset);
            local.y = fieldCenter.y - extents.y + piece.dropHeight;
            local.x = FindClearX(piece, local, baseX, minX, maxX);

            var inward = new Vector3(fieldCenter.x - local.x, 0f, fieldCenter.z - local.z);
            inward = inward.sqrMagnitude > 0.0001f ? inward.normalized : Vector3.zero;

            point = transform.TransformPoint(local);
            velocity = transform.TransformDirection(inward) * piece.nudgeSpeed;
        }

        private float FindClearX(PieceReturn piece, Vector3 local, float baseX, float minX, float maxX)
        {
            if (IsSpotClear(piece, local, baseX))
            {
                return baseX;
            }

            for (var step = 1; step <= SlideSteps; step++)
            {
                var shift = step * SlideStep;

                var forward = Mathf.Clamp(baseX + shift, minX, maxX);
                if (IsSpotClear(piece, local, forward))
                {
                    return forward;
                }

                var back = Mathf.Clamp(baseX - shift, minX, maxX);
                if (IsSpotClear(piece, local, back))
                {
                    return back;
                }
            }

            return baseX;
        }

        private bool IsSpotClear(PieceReturn piece, Vector3 local, float x)
        {
            var world = transform.TransformPoint(new Vector3(x, local.y, local.z));

            return !IsNearAnyZone(piece.exclusionZones, world) && !IsNearAnyZone(outOfFieldZones, world);
        }

        private static bool IsNearAnyZone(ExclusionZone[] zones, Vector3 worldPosition)
        {
            var grown = Vector3.one * (ReturnClearance * 2f);

            foreach (var zone in zones)
            {
                if (zone?.anchor != null &&
                    IsInBox(zone.anchor, worldPosition, zone.center, zone.size + grown))
                {
                    return true;
                }
            }

            return false;
        }

        private void Rescan()
        {
            _rescanTimer = 0f;

            RescanRobots();

            foreach (var piece in pieces)
            {
                foreach (var found in GameObject.FindGameObjectsWithTag(piece.tag))
                {
                    if (!found.TryGetComponent(out Rigidbody body) || _tracked.ContainsKey(body))
                    {
                        continue;
                    }

                    _tracked.Add(body, new TrackedPiece { Piece = piece, ExitPoint = body.position });
                }
            }
        }

        private void RescanRobots()
        {
            if (!RobotsAreStale())
            {
                return;
            }

            _robots.Clear();
            _robots.AddRange(FindObjectsByType<RobotBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        }

        private bool RobotsAreStale()
        {
            if (_robots.Count == 0)
            {
                return true;
            }

            foreach (var robot in _robots)
            {
                if (robot == null)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            DrawBox(transform, fieldCenter, fieldSize);

            Gizmos.color = Color.red;
            DrawZones(outOfFieldZones);

            Gizmos.color = Color.yellow;
            foreach (var piece in pieces)
            {
                DrawZones(piece.exclusionZones);
            }
        }

        private static void DrawZones(ExclusionZone[] zones)
        {
            foreach (var zone in zones)
            {
                if (zone?.anchor != null)
                {
                    DrawBox(zone.anchor, zone.center, zone.size);
                }
            }
        }

        private static void DrawBox(Transform anchor, Vector3 center, Vector3 size)
        {
            Gizmos.matrix = anchor.localToWorldMatrix;
            Gizmos.DrawWireCube(center, size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
