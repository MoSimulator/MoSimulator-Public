using System;
using System.Collections.Generic;
using System.Linq;
using Games.Reefscape.Robots;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Games.Reefscape.FieldScripts
{
    public class CoralStation : MonoBehaviour
    {
        [field: SerializeField] public Alliance Alliance { get; private set; }
        [field: SerializeField] public List<ReefscapeRobotBase> Robots { get; set; }
        
        [field: SerializeField] public List<ReefscapeCommandRobotBase> CommandRobots { get; private set; }

        [SerializeField] private GameObject coralPrefab;

        /// <summary>Read by the game manager to build the shared pool, so it has nothing of its own to wire.</summary>
        public GameObject CoralPrefab => coralPrefab;

        [SerializeField] private Transform coralSpawnpoint;
        [SerializeField] private float maxXOffset = 0.8f;
        private Vector3 _initialSpawnpoint;
        private Quaternion _initialSpawnpointRotation;

        private BoxCollider _stationTrigger;

        [SerializeField] private float minCoralSpawnForce = 4f;
        [SerializeField] private float maxCoralSpawnForce = 6f;

        [SerializeField] private float coralSpawnTorque = 0.1f;

        [SerializeField] private bool spawnCoral;

        private float _coralSpawnTimer = 1.5f;

        [Tooltip("Seconds a robot waits between station drops. Coral now comes from a pre-built pool, so this no longer has to leave room for an instantiate.")]
        [SerializeField] private float stationDropInterval = 1f;

        [Tooltip("Seconds between coral dropped on the floor for robots that intake off the ground.")]
        [SerializeField] private float groundDropInterval = 1.5f;

        [Tooltip("How many loose coral the station keeps on its floor before it stops dropping more.")]
        [SerializeField] private int groundCoralCount = 3;

        [SerializeField] private Transform coralParent;

        // Per-robot cooldown tracker for station drops. Each robot can independently request station drops without being blocked.
        private readonly Dictionary<RobotBase, float> _lastStationSpawnTime = new();

        private void Awake()
        {
            _stationTrigger = GetComponent<BoxCollider>();

            coralParent = GameObject.FindGameObjectWithTag("GamePieceWorld").transform;

            _initialSpawnpoint = coralSpawnpoint.position;
            _initialSpawnpointRotation = coralSpawnpoint.rotation;
        }

        private void Update()
        {
            _coralSpawnTimer += Time.deltaTime;

            Transform closestGroundRobot = null;
            var closestGroundDistanceSquared = float.PositiveInfinity;

            void ConsiderGroundRobot(Transform robotTransform)
            {
                var distanceSquared = (robotTransform.position - _initialSpawnpoint).sqrMagnitude;
                if (distanceSquared < closestGroundDistanceSquared)
                {
                    closestGroundRobot = robotTransform;
                    closestGroundDistanceSquared = distanceSquared;
                }
            }

            foreach (var robot in Robots)
            {
                var mode = robot.CurrentCoralStationMode;
                if (mode == null)
                {
                    continue;
                }

                if (mode.DropType == DropType.Station)
                {
                    TryStationDrop(robot, mode, robot.IsIntaking);
                }
                else if (mode.DropType == DropType.Ground)
                {
                    ConsiderGroundRobot(robot.transform);
                }
            }

            foreach (var robot in CommandRobots)
            {
                var mode = robot.CurrentCoralStationMode;
                if (mode == null)
                {
                    continue;
                }

                if (mode.DropType == DropType.Station)
                {
                    TryStationDrop(robot, mode, robot.Intaking());
                }
                else if (mode.DropType == DropType.Ground)
                {
                    ConsiderGroundRobot(robot.transform);
                }
            }

            // Hoisted out of the loops above. The timer and the floor count are per station, not per
            // robot, so running this once instead of once per robot keeps the overlap query off the
            // part of the cost that grows with the number of teams on the field.
            if (closestGroundRobot != null || spawnCoral)
            {
                TryGroundDrop(closestGroundRobot);
            }
        }

        private void TryStationDrop(RobotBase robot, CoralStationMode mode, bool isIntaking)
        {
            var target = mode.TargetTransform != null ? mode.TargetTransform : robot.transform;
            AlignCoralSpawnPoint(target);

            if (GetDistanceToRobot(robot.transform) >= mode.DropDistance)
            {
                return;
            }

            if (mode.RequireIntaking && !isIntaking)
            {
                return;
            }

            _lastStationSpawnTime.TryGetValue(robot, out var lastSpawnTime);
            if (Time.time - lastSpawnTime < stationDropInterval)
            {
                return;
            }

            var offset = mode.DropOrientation == DropOrientation.Vertical
                ? Vector3.zero
                : new Vector3(0f, 90f, 0f);

            if (!TryTakeCoral(offset, out var rb))
            {
                return;
            }

            _lastStationSpawnTime[robot] = Time.time;

            rb.AddForce(
                coralSpawnpoint.forward *
                (mode.DropStrength +
                 Random.Range(
                     -mode.DropForceVariance,
                     mode.DropForceVariance)),
                ForceMode.Impulse);

            rb.AddRelativeTorque(
                new Vector3(0,
                    Random.Range(
                        -mode.RotationVariance,
                        mode.RotationVariance),
                    0),
                ForceMode.Impulse);
        }

        private void TryGroundDrop(Transform closestRobot)
        {
            coralSpawnpoint.position = _initialSpawnpoint;
            coralSpawnpoint.rotation = _initialSpawnpointRotation;
            if (closestRobot != null)
            {
                AlignCoralSpawnPoint(closestRobot);
            }

            // Bail out before the interval is spent. Further down the timer is cleared ahead of the
            // coral actually being taken, so a station that is holding off clears it once per
            // interval and the match then starts with a fresh one - which is the wait people were
            // seeing after restarting a match that ran to time. Leaving the timer alone while held
            // off means the first drop lands as soon as feeding is allowed, and skips the overlap
            // query for as long as it is not.
            if (!Feeding)
            {
                return;
            }

            if (!spawnCoral && _coralSpawnTimer < groundDropInterval)
            {
                return;
            }

            var trigger = _stationTrigger.transform;
            var results = Physics.OverlapBox(
                _stationTrigger.bounds.center,
                Vector3.Scale(_stationTrigger.size * 0.5f, trigger.lossyScale),
                trigger.rotation,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);

            var robotLayer = LayerMask.NameToLayer("Robot");
            RobotBase closestOpponent = null;
            var closestOpponentDistanceSquared = float.PositiveInfinity;
            foreach (var result in results)
            {
                if (result.gameObject.layer != robotLayer)
                {
                    continue;
                }

                // The cached lookup starts at the parent; also support colliders on the robot root.
                var robot = result.TryGetComponent<RobotBase>(out var rootRobot)
                    ? rootRobot
                    : Utils.FindParentObjectComponent<RobotBase>(result.gameObject);
                if (robot == null)
                {
                    continue;
                }

                // Leave the timer running so a ready drop resumes as soon as our robot clears the zone.
                if (robot.Alliance == Alliance)
                {
                    return;
                }

                var distanceSquared = (robot.transform.position - coralSpawnpoint.position).sqrMagnitude;
                if (distanceSquared < closestOpponentDistanceSquared)
                {
                    closestOpponent = robot;
                    closestOpponentDistanceSquared = distanceSquared;
                }
            }

            if (closestOpponent != null)
            {
                var slideAxis = _initialSpawnpointRotation * Vector3.right;
                var opponentOffset = Vector3.Dot(closestOpponent.transform.position - _initialSpawnpoint, slideAxis);
                // Move 30 inches toward the opposite side of the station, within the slider's travel.
                const float avoidanceOffset = 30f * 0.0254f;
                var dropOffset = opponentOffset + (opponentOffset >= 0f ? -avoidanceOffset : avoidanceOffset);
                coralSpawnpoint.position = _initialSpawnpoint +
                                           slideAxis * Mathf.Clamp(dropOffset, -maxXOffset, maxXOffset);
            }

            if (!spawnCoral && results.Count(result => result.CompareTag("Coral")) >= groundCoralCount)
            {
                return;
            }

            _coralSpawnTimer = 0f;
            spawnCoral = false;

            if (!TryTakeCoral(new Vector3(0f, 90f, 0f), out var rb))
            {
                return;
            }

            rb.AddForce(
                coralSpawnpoint.forward *
                Random.Range(minCoralSpawnForce, maxCoralSpawnForce), ForceMode.Impulse);
            rb.AddRelativeTorque(new Vector3(0, Random.Range(-coralSpawnTorque, coralSpawnTorque), 0),
                ForceMode.Impulse);
        }

        /// <summary>
        /// Takes a coral from the shared pool at this station's spawn point. Returns false once the
        /// alliance has all of its coral out on the field, which just means no drop this time.
        /// </summary>
        /// <summary>
        /// Whether the station should be handing coral out at all. It holds off for the few seconds
        /// between the match ending and robots coming back for free play, since coral arriving in
        /// that gap reads as the station firing at nothing. Keyed on the match being over as well as
        /// the robots being disabled, because they are also disabled between auto and teleop, where
        /// feeding should carry straight on.
        /// </summary>
        private static bool Feeding =>
            BaseGameManager.Instance == null ||
            BaseGameManager.Instance.GameState != GameState.End ||
            BaseGameManager.Instance.RobotState != RobotState.Disabled;

        private bool TryTakeCoral(Vector3 spawnRotationOffset, out Rigidbody rb)
        {
            rb = null;

            if (!Feeding)
            {
                return false;
            }

            if (!coralParent)
            {
                var world = GameObject.FindGameObjectWithTag("GamePieceWorld");
                if (!world)
                {
                    return false;
                }

                coralParent = world.transform;
            }

            if (!CoralPool.Instance)
            {
                return false;
            }

            return CoralPool.Instance.TryTake(
                Alliance,
                coralParent,
                coralSpawnpoint.position,
                coralSpawnpoint.rotation * Quaternion.Euler(spawnRotationOffset),
                out rb);
        }

        public void ResetCoralStation(Transform newCoralParent)
        {
            coralParent = newCoralParent;
        }

        private void AlignCoralSpawnPoint(Transform robotTransform)
        {
            var localXRotation = _initialSpawnpointRotation * Vector3.right;

            var toRobot = robotTransform.position - _initialSpawnpoint;

            var localXOffset = Vector3.Dot(toRobot, localXRotation);
            localXOffset = Mathf.Clamp(localXOffset, -maxXOffset, maxXOffset);

            var newLocalPosition = _initialSpawnpoint + localXRotation * localXOffset;

            coralSpawnpoint.position = newLocalPosition;
        }

        private float GetDistanceToRobot(Transform robotTransform)
        {
            var localZ = _initialSpawnpointRotation * Vector3.forward;
            var toRobot = robotTransform.position - _initialSpawnpoint;
            return Vector3.Dot(toRobot, localZ);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;

            foreach (var robot in Robots.Where(r => r.CurrentCoralStationMode.DrawDebugLines))
            {
                var mode = robot.CurrentCoralStationMode;
                var target = mode.TargetTransform != null ? mode.TargetTransform : robot.transform;

                Gizmos.DrawLine(target.position, _initialSpawnpoint);

#if UNITY_EDITOR
                UnityEditor.Handles.Label(
                    (target.position + _initialSpawnpoint) / 2 + Vector3.up * 0.2f,
                    $"Distance: {GetDistanceToRobot(target):F2}m");
#endif
            }
        }
    }

    [Serializable]
    public class CoralStationMode
    {
        [field: SerializeField] public DropType DropType { get; set; }

        [field: SerializeField] public float DropDistance { get; set; }

        [field: SerializeField] public float DropStrength { get; set; } = 5f;

        [field: SerializeField] public float DropForceVariance { get; set; } = 1f;

        [field: SerializeField] public float RotationVariance { get; set; } = 0.1f;

        [Header("Station Intake Settings")]
        [field: SerializeField] public DropOrientation DropOrientation { get; set; }

        [field: SerializeField] public bool RequireIntaking { get; set; } = true;

        [Header("Targeting")]
        [field: SerializeField] public Transform TargetTransform { get; set; }

        [Header("Debug Settings")]
        [field: SerializeField] public bool DrawDebugLines { get; private set; }
    }

    public enum DropType
    {
        Ground,
        Station
    }

    public enum DropOrientation
    {
        Horizontal,
        Vertical
    }
}
