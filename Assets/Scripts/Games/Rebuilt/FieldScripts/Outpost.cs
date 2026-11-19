using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Games.Rebuilt.Robots;
using GameSystems.Management;
using GameSystems.Settings;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using RobotFramework.Controllers.PidSystems;
using UnityEngine;

namespace Games.Rebuilt.FieldScripts
{
    public class Outpost : MonoBehaviour
    {

        [Header("Settings")]
        [field: SerializeField]
        public Alliance Alliance { get; private set; }

        [field: SerializeField] public List<RebuiltRobotBase> Robots { get; set; }

        [SerializeField] float closedAngle;
        [SerializeField] float openAngle;
        
        [SerializeField] private Rigidbody chuteDoor;

        [SerializeField] private PidConstants chuteDoorPid;
        
        [SerializeField] bool log;

        private float _chuteDoorTarget;
        private float _integral;
        private float _previousError;

        private float _angleOffset;

        private float kP, kI, kD, iSaturation;

        private int numAutoOpen = 0;

        private float _teleportTimer = 0f;
        
        [SerializeField] private BoxCollider corralCollider;
        [SerializeField] private BoxCollider chuteCollider;
        [SerializeField] private GameObject fuelSpawn;

        [SerializeField] private float cooldown = 0.45f;

        private bool _doorSequenceRunning = false;
        private bool _proximityDoorOpen = false;
        [SerializeField] private float doorOpenTime = 2f;

        [SerializeField] private bool forceClose = false;
        [SerializeField] private float maxOpenDistance = 4.5f;

        private float _doorLogTimer;

        private int _chuteCount;
        private int _corralCount;
        private float _fuelCountTimer;

        private int _gamepieceLayer;

        private bool autoOpened = false;

        [Header("Human Player")]
        [SerializeField] private Transform humanPlayerLaunchPoint;
        [SerializeField] private float humanPlayerShotInterval = 1f;
        [SerializeField] private float humanPlayerFlightTime = 1.2f;
        [SerializeField] private float humanPlayerSideSpread = 0.2f;
        [SerializeField] private float humanPlayerDepthSpread = 0.2f;

        private Transform _hubTarget;
        private bool _humanPlayerActive;
        private float _humanPlayerTimer;

        [Header("Fuel Carry")]
        [SerializeField] private Transform[] corralToChutePath;
        [SerializeField] private Transform[] corralToLaunchPath;
        [SerializeField] private Transform[] chuteToLaunchPath;
        [SerializeField] private Transform chuteLaneEdgeA;
        [SerializeField] private Transform chuteLaneEdgeB;
        [SerializeField] private float carrySpeed = 1.5f;
        [SerializeField] private float carryTimeout = 0.5f;

        private const float CarryArriveDistance = 0.01f;
        private const float CarryMaxSpeedRatio = 2f;
        private const int CurveSamplesPerSegment = 16;
        private const float FuelRadius = 0.075f;
        private const float ChuteLaneStep = 0.01f;

        private class FuelCarry
        {
            public Transform Fuel;
            public Rigidbody Body;
            public List<Vector3> Path;
            public float Length;
            public float Elapsed;
            public bool Launch;
            public bool ThroughMouth;
            public float LaneZ;
        }

        private readonly List<FuelCarry> _carries = new List<FuelCarry>();

        private void Start()
        {
            _chuteDoorTarget = closedAngle;
            _angleOffset = chuteDoor.transform.localRotation.eulerAngles.x;

            kP = chuteDoorPid.kP;
            kI = chuteDoorPid.kI;
            kD = chuteDoorPid.kD;
            iSaturation = chuteDoorPid.Isaturation;
            
            _gamepieceLayer = 1 << LayerMask.NameToLayer("GamePiece1");

            var allianceName = Alliance == Alliance.Blue ? "blue" : "red";
            var nodeLayer = LayerMask.NameToLayer("AutoAngleNodes");
            _hubTarget = transform.root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(node => node.gameObject.layer == nodeLayer
                                        && node.name.ToLower().Contains(allianceName)
                                        && node.name.ToLower().Contains("hub"));
        }

        private void FixedUpdate()
        {
            UpdateChuteDoor();
            UpdateCarries();
            if (forceClose)
            {
                _chuteDoorTarget = closedAngle;
                return;
            }
            
            _fuelCountTimer += Time.fixedDeltaTime;
            if (_fuelCountTimer >= 0.1f)
            {
                _chuteCount = CountFuelInZone(chuteCollider);
                _corralCount = CountFuelInZone(corralCollider);
                _fuelCountTimer = 0f;
            }

            if (_humanPlayerActive && Robots.Count > 0 && _hubTarget != null && BaseGameManager.Instance != null)
            {
                _humanPlayerTimer += Time.fixedDeltaTime;
                if (_humanPlayerTimer > humanPlayerShotInterval)
                {
                    _humanPlayerTimer -= humanPlayerShotInterval;
                    HumanPlayerShoot();
                }
            }

            if (!_doorSequenceRunning)
            {
                _chuteDoorTarget = closedAngle;

                foreach (var robot in Robots)
                {
                    var distanceToTarget = GetDistanceToRobot(robot.transform.position);

                    var openDistance = GetOpenDistance(robot);

                    if (distanceToTarget < openDistance)
                    {
                        _chuteDoorTarget = openAngle;
                        if (!_proximityDoorOpen)
                        {
                            _proximityDoorOpen = true;
                            WakeUpChuteBalls();
                        }
                        break;
                    }

                    if (robot.OpenChuteOnStart && openDistance > 0f) numAutoOpen++;
                }

                if (Robots.Count > 0 && numAutoOpen == Robots.Count && !autoOpened && !_doorSequenceRunning)
                {
                    StartCoroutine(OpenDoorSequence());
                    autoOpened = true;
                }

                numAutoOpen = 0;

                if (Robots.Count == 0)
                {
                    _chuteDoorTarget = closedAngle;
                }

                if (_chuteDoorTarget != openAngle)
                    _proximityDoorOpen = false;
            }


            _teleportTimer += Time.fixedDeltaTime;
                
            float currentAngle = chuteDoor.transform.localRotation.eulerAngles.x - _angleOffset;
            if (currentAngle >= 180f) currentAngle -= 360f;
            bool doorIsClosed = Mathf.Abs(Mathf.DeltaAngle(currentAngle, closedAngle)) < 5f;

            _doorLogTimer += Time.fixedDeltaTime;
            if (_doorLogTimer >= 0.5f && (_chuteDoorTarget == openAngle || !doorIsClosed))
            {
                _doorLogTimer = 0f;
                var nearest = Robots.Count > 0 ? Robots.Min(r => GetDistanceToRobot(r.transform.position)) : -1f;
            }


            if (_chuteCount + _carries.Count(c => !c.Launch) < 24 && _corralCount > 0 && _teleportTimer >= cooldown && doorIsClosed)
            {
                FeedChute();
                _teleportTimer = 0f;
            }

            if (_chuteCount >= 24 && _corralCount >= 12 && !_doorSequenceRunning && Robots.Any(r => GetOpenDistance(r) > 0f))
            {
                StartCoroutine(OpenDoorSequence());
            }

            if(log) 
            {
                print("Chute: " + _chuteCount + ". Corral: " + _corralCount);
            }
            
            
        }

        private float GetOpenDistance(RebuiltRobotBase robot)
        {
            var match = MatchManager.Instance;
            if (match == null || robot.PlayerIndex < 0) return robot.OpenDistance;

            var player = match.GetOrCreatePlayerSettings(robot.PlayerIndex);
            if (player.outpostDistanceMax) return maxOpenDistance;
            return player.outpostDistance >= 0f ? player.outpostDistance : robot.OpenDistance;
        }

        private void UpdateChuteDoor()
        {
            float currentAngle = chuteDoor.transform.localRotation.eulerAngles.x - _angleOffset;
            if (currentAngle >= 180f) currentAngle -= 360f;
            
            float target = _chuteDoorTarget;
            if (target >= 180f) target -= 360f;

            float error = Mathf.DeltaAngle(currentAngle, target);
            
            _integral = Mathf.Clamp(_integral + error * Time.fixedDeltaTime, -iSaturation, iSaturation);
            float derivative = (error - _previousError) / Time.fixedDeltaTime;
            _previousError = error;
            
            float pidOutput = (kP * error) + (kI * _integral) + (kD * derivative);
            
            Vector3 worldRotationAxis = Alliance == Alliance.Blue ? Vector3.right : Vector3.left;

            chuteDoor.angularVelocity = worldRotationAxis * pidOutput * Mathf.Deg2Rad;
            
            if (Mathf.Abs(error) < 1f)
            {
                chuteDoor.angularVelocity = Vector3.zero;
                _integral = 0f;
                _previousError = 0f;
            }
            
            if (log)
            {
                print("Target: " + _chuteDoorTarget);
                print("Real Angle: " + chuteDoor.transform.localRotation.eulerAngles.x);
                print("Current Angle: " + currentAngle);
            }
        }

        public void SetHumanPlayerActive(bool active)
        {
            if (_humanPlayerActive == active) return;
            _humanPlayerActive = active;
            _humanPlayerTimer = 0f;
        }

        private void HumanPlayerShoot()
        {
            var fuel = FindFuelInZone(chuteCollider, FirstPathPoint(chuteToLaunchPath, humanPlayerLaunchPoint));
            if (fuel != null && !IsMouthLaneBusy(fuel.position.z))
            {
                StartCarry(fuel, chuteToLaunchPath, humanPlayerLaunchPoint, true, fuel.position.z, true);
                return;
            }

            fuel = FindFuelInZone(corralCollider, FirstPathPoint(corralToLaunchPath, humanPlayerLaunchPoint));
            if (fuel != null) StartCarry(fuel, corralToLaunchPath, humanPlayerLaunchPoint, true, fuel.position.z, false);
        }

        private Vector3 GetLaunchVelocity(Vector3 start)
        {
            var forward = _hubTarget.position - start;
            forward.y = 0f;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward);

            var target = _hubTarget.position
                         + right * Random.Range(-humanPlayerSideSpread, humanPlayerSideSpread)
                         + forward * Random.Range(-humanPlayerDepthSpread, humanPlayerDepthSpread);

            var t = humanPlayerFlightTime;
            return (target - start) / t - 0.5f * t * Physics.gravity;
        }

        private void FeedChute()
        {
            var fuel = FindFuelInZone(corralCollider, FirstPathPoint(corralToChutePath, fuelSpawn.transform));
            if (fuel == null) return;
            if (!TryFindChuteLane(fuel.position.z, out var laneZ)) return;

            StartCarry(fuel, corralToChutePath, fuelSpawn.transform, false, laneZ, true);
        }

        private bool TryFindChuteLane(float preferredZ, out float laneZ)
        {
            var laneMin = Mathf.Min(chuteLaneEdgeA.position.z, chuteLaneEdgeB.position.z);
            var laneMax = Mathf.Max(chuteLaneEdgeA.position.z, chuteLaneEdgeB.position.z);
            var preferred = Mathf.Clamp(preferredZ, laneMin, laneMax);
            var mouth = LastPathPoint(corralToChutePath, fuelSpawn.transform);
            var drop = fuelSpawn.transform.position;

            for (var offset = 0f; offset <= laneMax - laneMin; offset += ChuteLaneStep)
            {
                for (var side = -1; side <= 1; side += 2)
                {
                    var z = preferred + side * offset;
                    if (z < laneMin || z > laneMax || IsMouthLaneBusy(z)) continue;
                    if (Physics.CheckCapsule(new Vector3(mouth.x, mouth.y, z), new Vector3(drop.x, drop.y, z),
                            FuelRadius, _gamepieceLayer, QueryTriggerInteraction.Collide)) continue;

                    laneZ = z;
                    return true;
                }
            }

            laneZ = 0f;
            return false;
        }

        private bool IsMouthLaneBusy(float z)
        {
            return _carries.Any(c => c.ThroughMouth && Mathf.Abs(c.LaneZ - z) < FuelRadius * 2f);
        }

        private void StartCarry(Transform fuel, Transform[] waypoints, Transform end, bool launch, float laneZ, bool throughMouth)
        {
            var rb = fuel.GetComponent<Rigidbody>();

            var points = new List<Vector3> { rb.position };
            foreach (var waypoint in waypoints)
            {
                if (waypoint == null) continue;
                points.Add(new Vector3(waypoint.position.x, waypoint.position.y, laneZ));
            }
            points.Add(new Vector3(end.position.x, end.position.y, laneZ));

            var path = BuildCurve(points);

            var length = 0f;
            for (var i = 1; i < path.Count; i++) length += Vector3.Distance(path[i - 1], path[i]);

            rb.useGravity = false;
            rb.WakeUp();
            _carries.Add(new FuelCarry
            {
                Fuel = fuel, Body = rb, Path = path, Length = length, Launch = launch,
                ThroughMouth = throughMouth, LaneZ = laneZ
            });
        }

        private void UpdateCarries()
        {
            for (var i = _carries.Count - 1; i >= 0; i--)
            {
                var carry = _carries[i];
                if (carry.Body == null)
                {
                    _carries.RemoveAt(i);
                    continue;
                }

                carry.Elapsed += Time.fixedDeltaTime;
                var duration = carry.Length / carrySpeed;
                var progress = duration > 0f ? Mathf.Clamp01(carry.Elapsed / duration) : 1f;
                var target = PointAlongPath(carry.Path, Mathf.SmoothStep(0f, carry.Length, progress));
                var offset = target - carry.Body.position;

                if (progress >= 1f && offset.magnitude < CarryArriveDistance)
                {
                    FinishCarry(carry, true);
                    _carries.RemoveAt(i);
                    continue;
                }

                if (carry.Elapsed > duration + carryTimeout)
                {
                    FinishCarry(carry, false);
                    _carries.RemoveAt(i);
                    continue;
                }

                carry.Body.velocity = Vector3.ClampMagnitude(offset / Time.fixedDeltaTime, carrySpeed * CarryMaxSpeedRatio);
                carry.Body.angularVelocity = Vector3.zero;
            }
        }

        private void FinishCarry(FuelCarry carry, bool arrived)
        {
            carry.Body.useGravity = true;
            carry.Body.velocity = arrived && carry.Launch ? GetLaunchVelocity(carry.Body.position) : Vector3.zero;
        }

        private static List<Vector3> BuildCurve(List<Vector3> points)
        {
            var curve = new List<Vector3> { points[0] };
            for (var i = 0; i < points.Count - 1; i++)
            {
                var p0 = i > 0 ? points[i - 1] : 2f * points[i] - points[i + 1];
                var p3 = i + 2 < points.Count ? points[i + 2] : 2f * points[i + 1] - points[i];
                for (var s = 1; s <= CurveSamplesPerSegment; s++)
                {
                    curve.Add(CatmullRom(p0, points[i], points[i + 1], p3, (float)s / CurveSamplesPerSegment));
                }
            }

            return curve;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            var t1 = Knot(p0, p1);
            var t2 = t1 + Knot(p1, p2);
            var t3 = t2 + Knot(p2, p3);
            var t = Mathf.Lerp(t1, t2, u);

            var a1 = Vector3.LerpUnclamped(p0, p1, t / t1);
            var a2 = Vector3.LerpUnclamped(p1, p2, (t - t1) / (t2 - t1));
            var a3 = Vector3.LerpUnclamped(p2, p3, (t - t2) / (t3 - t2));
            var b1 = Vector3.LerpUnclamped(a1, a2, t / t2);
            var b2 = Vector3.LerpUnclamped(a2, a3, (t - t1) / (t3 - t1));
            return Vector3.LerpUnclamped(b1, b2, (t - t1) / (t2 - t1));
        }

        private static float Knot(Vector3 a, Vector3 b)
        {
            return Mathf.Max(Mathf.Sqrt(Vector3.Distance(a, b)), 0.0001f);
        }

        private static Vector3 PointAlongPath(List<Vector3> path, float distance)
        {
            for (var i = 1; i < path.Count; i++)
            {
                var segment = Vector3.Distance(path[i - 1], path[i]);
                if (distance <= segment) return Vector3.Lerp(path[i - 1], path[i], segment > 0f ? distance / segment : 1f);
                distance -= segment;
            }

            return path[path.Count - 1];
        }

        private static Vector3 LastPathPoint(Transform[] waypoints, Transform end)
        {
            var last = waypoints.LastOrDefault(waypoint => waypoint != null);
            return last != null ? last.position : end.position;
        }

        private static Vector3 FirstPathPoint(Transform[] waypoints, Transform end)
        {
            var first = waypoints.FirstOrDefault(waypoint => waypoint != null);
            return first != null ? first.position : end.position;
        }

        private Transform FindFuelInZone(BoxCollider zone, Vector3 exit)
        {
            var results = Physics.OverlapBox(
                zone.bounds.center,
                zone.bounds.extents,
                Quaternion.identity,
                _gamepieceLayer,
                QueryTriggerInteraction.Collide
            );

            Transform closest = null;
            var closestDistance = float.MaxValue;
            foreach (var result in results)
            {
                if (result.transform.parent?.parent?.CompareTag("Fuel") != true) continue;
                var fuel = result.transform.parent.parent;
                if (_carries.Any(c => c.Fuel == fuel)) continue;

                var offset = fuel.position - exit;
                var distance = offset.x * offset.x + offset.y * offset.y;
                if (distance >= closestDistance) continue;

                closestDistance = distance;
                closest = fuel;
            }

            return closest;
        }

        private float GetDistanceToRobot(Vector3 robot)
        {
            return Mathf.Sqrt(Mathf.Pow(chuteDoor.transform.position.x - robot.x, 2) +
                              Mathf.Pow(chuteDoor.transform.position.z - robot.z, 2));
        }

        private int CountFuelInZone(BoxCollider zone)
        {
            var results = Physics.OverlapBox(
                zone.bounds.center,
                zone.bounds.extents,
                Quaternion.identity,
                _gamepieceLayer,
                QueryTriggerInteraction.Collide
            );

            int cnt = 0;
            foreach (var result in results)
            {
                if (result.transform.parent?.parent?.CompareTag("Fuel") == true) cnt++;
            }

            return cnt;
        }
        
        private IEnumerator OpenDoorSequence()
        {
            _doorSequenceRunning = true;
            _chuteDoorTarget = openAngle;
            WakeUpChuteBalls();
            yield return new WaitForSeconds(doorOpenTime);
            _chuteDoorTarget = closedAngle;
            _doorSequenceRunning = false;
        }

        private void WakeUpChuteBalls()
        {
            var bounds = chuteCollider.bounds;
            var hits = Physics.OverlapBox(bounds.center, bounds.extents, Quaternion.identity, _gamepieceLayer,
                QueryTriggerInteraction.Collide);
            foreach (var col in hits)
            {
                if (col.transform.parent?.parent?.CompareTag("Fuel") != true) continue;
                var rb = col.transform.parent.parent.GetComponent<Rigidbody>();
                rb?.WakeUp();
            }
        }
    }
}