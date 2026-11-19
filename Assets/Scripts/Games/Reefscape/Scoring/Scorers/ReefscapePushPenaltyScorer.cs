using System.Collections.Generic;
using Games.Reefscape.GamePieceSystem;
using Games.Reefscape.Robots;
using MoSimCore.BaseClasses;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using UnityEngine;
using RobotFramework;

namespace Games.Reefscape.Scoring.Scorers
{
    [DisallowMultipleComponent]
    public class ReefscapePushPenaltyScorer : MonoBehaviour, IScorer
    {


        private MoSimLib.SecureFloat numPenalties = 0;
        private MoSimLib.SecureFloat flatPenaltyPoints = 0;
        private List<GameObject> controlledCoral = new List<GameObject>();
        private List<GameObject> controlledAlgae = new List<GameObject>();
        private float maxPushPenaltyThreshold=2.5f;
        private float minPushPenaltyTreshold=0.8f;
        private float coralPenaltyCooldown = 1f;
        private float algaePenaltyCooldown = 1f;
        private float coralPenaltyThreshold=2.5f;
        private float algaePenaltyThreshold=2.5f;
        private float _coralPushDuration;
        private float _algaePushDuration;
        private bool _coralPenalizedThisPush;
        private bool _algaePenalizedThisPush;
        private const float TightPushDuration = 0.6f;
        private const float TightPushLimitMultiplier = 0.8f;
        private const float RegularPushLimitMultiplier = 0.7f;

        private Transform _blueBargeZone;
        private Transform _redBargeZone;
        private float _cagePenaltyCooldown;
        private Rigidbody robotRigidBody;
        private Vector2 customDetectionSize = new Vector2(2f, 2f);

        private LayerMask coralLayerMask;
        private LayerMask algaeLayerMask;

        private ReefscapeRobotReference _robot;
        private ReefscapeRobotGamePieceController _pieces;
        private RobotBase _robotBase;
        public Alliance Alliance { get; private set; }
        void Start()
        {
            numPenalties = 0;
            _robotBase = GetComponentInParent<RobotBase>();
            if (_robotBase == null)
            {
                Debug.LogError("ReefscapePushPenaltyScorer: RobotBase component not found in parent.");
                enabled = false;
                return;
            }

            Alliance = _robotBase.Alliance;
            _blueBargeZone = GameObject.FindGameObjectWithTag("BlueBargeZone")?.transform;
            _redBargeZone = GameObject.FindGameObjectWithTag("RedBargeZone")?.transform;
            algaePenaltyThreshold = maxPushPenaltyThreshold;
            coralPenaltyThreshold = maxPushPenaltyThreshold;
            var scoreHandler = FindFirstObjectByType<ReefscapeScoreHandler>();
            if (scoreHandler != null && (Alliance == Alliance.Blue
                    ? !scoreHandler.BlueScorers.Contains(this)
                    : !scoreHandler.RedScorers.Contains(this)))
            {
                scoreHandler.RegisterScorer(this);
            }

        }

        private void Awake()
        {
            numPenalties = 0;
            _robotBase = GetComponentInParent<RobotBase>();
            if (_robotBase == null)
            {
                Debug.LogError("ReefscapePushPenaltyScorer: RobotBase component not found in parent.");
                enabled = false;
                return;
            }

            Alliance = _robotBase.Alliance;
            robotRigidBody = _robotBase.GetComponent<Rigidbody>();
            _robot = new ReefscapeRobotReference(_robotBase);
            _pieces = _robotBase.GetComponent<ReefscapeRobotGamePieceController>();
            coralLayerMask = LayerMask.GetMask("Coral");
            algaeLayerMask = LayerMask.GetMask("Algae");



        }



        private void Update()
        {
            if (!_robotBase.isActiveAndEnabled || robotRigidBody == null || _pieces == null || MatchHasEnded)
            {
                return;
            }

            GetControlledCoral();
            GetControlledAlgae();
            checkForPushPenalty();
            if (_cagePenaltyCooldown > 0f)
            {
                _cagePenaltyCooldown -= Time.deltaTime;
            }
        }

        private bool HasPiece(string pieceName)
        {
            return _pieces.GetPieceByName(pieceName)?.HasPiece() == true;
        }

        private void OnCollisionEnter(Collision collision)
        {
            CheckCagePenalty(collision.collider);
        }

        /// <summary>
        /// G428: contacting an opponent's cage awards 6 points to the other alliance.
        /// All physics cages share the "Cage" tag, so a cage's alliance is inferred
        /// from which alliance's barge zone it hangs over.
        /// </summary>
        private void CheckCagePenalty(Collider other)
        {
            if (_cagePenaltyCooldown > 0f) return;
            if (other == null || !other.CompareTag("Cage")) return;
            if (_blueBargeZone == null || _redBargeZone == null) return;

            var cagePosition = other.transform.position;
            var cageAlliance =
                (cagePosition - _blueBargeZone.position).sqrMagnitude <=
                (cagePosition - _redBargeZone.position).sqrMagnitude
                    ? Alliance.Blue
                    : Alliance.Red;
            if (cageAlliance == Alliance) return;

            AddPenaltyPoints(6);
            _cagePenaltyCooldown = 3f;
        }

        private void GetControlledCoral()
        {
            // 1. Define the center point of your check (usually the robot's center)
            Vector3 center = robotRigidBody.position;

            // 2. Create custom extents. Extents are half-dimensions, so we divide by 2f.
            // We set the Y extent to a massive number to act as "infinity".
            Vector3 customExtents = new Vector3(
                customDetectionSize.x / 2f,
                100000f,
                customDetectionSize.y / 2f
            );

            // 3. Use the custom extents and the robot's rotation (so the box rotates with the robot)
            // Passing a LayerMask here significantly improves performance for large overlap areas.
            Collider[] results = Physics.OverlapBox(center, customExtents, robotRigidBody.rotation, coralLayerMask);

            controlledCoral.Clear();

            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].CompareTag("Coral"))
                {
                    GameObject parentGO = results[i].transform.gameObject;

                    if (!controlledCoral.Contains(parentGO))
                    {
                        controlledCoral.Add(parentGO);
                    }
                }
            }
        }
        private void GetControlledAlgae()
        {
            // 1. Define the center point of your check (usually the robot's center)
            Vector3 center = robotRigidBody.position;

            // 2. Create custom extents. Extents are half-dimensions, so we divide by 2f.
            // We set the Y extent to a massive number to act as "infinity".
            Vector3 customExtents = new Vector3(
                customDetectionSize.x / 2f,
                100000f,
                customDetectionSize.y / 2f
            );

            // 3. Use the custom extents and the robot's rotation (so the box rotates with the robot)
            // Passing a LayerMask here significantly improves performance for large overlap areas.
            Collider[] results = Physics.OverlapBox(center, customExtents, robotRigidBody.rotation, algaeLayerMask);

            controlledAlgae.Clear();

            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].CompareTag("Algae"))
                {
                    GameObject parentGO = results[i].transform.gameObject;

                    if (!controlledAlgae.Contains(parentGO))
                    {
                        controlledAlgae.Add(parentGO);
                    }
                }
            }
        }
        private void checkForPushPenalty()
        {
            maxPushPenaltyThreshold = Mathf.Max(maxPushPenaltyThreshold, minPushPenaltyTreshold);
            int coralPushCount = 0;
            if (coralPenaltyCooldown > 0f)
            {
                coralPenaltyCooldown -= Time.deltaTime;
            }
            for (int i = 0; i < controlledCoral.Count; i++)
            {
                Rigidbody coralRB = controlledCoral[i].GetComponent<Rigidbody>();
                if (IsPushing(coralRB))
                {
                    coralPushCount++;
                }
            }
            UpdatePushThreshold(coralPushCount > 0, ref coralPenaltyThreshold, ref _coralPushDuration,
                ref _coralPenalizedThisPush);

            if (coralPushCount > 0 && coralPenaltyCooldown <= 0f && coralPenaltyThreshold <= 0f)
            {
                coralPenaltyCooldown = 1f;
                AddPenalty(HasPiece("Coral") ? coralPushCount : coralPushCount - 1);
                TightenPushLimit(RegularPushLimitMultiplier);
                _coralPenalizedThisPush = true;
            }


            int algaePushCount = 0;
            if (algaePenaltyCooldown > 0f)
            {
                algaePenaltyCooldown -= Time.deltaTime;
            }
            for (int i = 0; i < controlledAlgae.Count; i++)
            {
                Rigidbody algaeRB = controlledAlgae[i].GetComponent<Rigidbody>();
                if (IsPushing(algaeRB))
                {
                    algaePushCount++;
                }
            }
            UpdatePushThreshold(algaePushCount > 0, ref algaePenaltyThreshold, ref _algaePushDuration,
                ref _algaePenalizedThisPush);

            if (algaePushCount > 0 && algaePenaltyCooldown <= 0f && algaePenaltyThreshold <= 0f)
            {
                algaePenaltyCooldown = 1f;
                AddPenalty(HasPiece("Algae") ? algaePushCount : algaePushCount - 1);
                TightenPushLimit(RegularPushLimitMultiplier);
                _algaePenalizedThisPush = true;
            }

        }

        private bool IsPushing(Rigidbody piece)
        {
            Vector3 pieceVelocity = piece.velocity;
            if (pieceVelocity.magnitude <= 0.5f) return false;

            Vector3 piecePosition = piece.worldCenterOfMass;
            var bumperCollider = _robot.BumperCollider;
            Vector3 bumperPoint = bumperCollider != null ? bumperCollider.ClosestPoint(piecePosition) : piecePosition;
            // GetPointVelocity includes the bumper's tangential velocity when the robot spins.
            Vector3 bumperVelocity = robotRigidBody.GetPointVelocity(bumperPoint);
            return bumperVelocity.magnitude > 0.5f &&
                   Vector3.Dot(bumperVelocity.normalized, pieceVelocity.normalized) > 0.7f;
        }

        private void TightenPushLimit(float multiplier)
        {
            maxPushPenaltyThreshold = Mathf.Max(minPushPenaltyTreshold, maxPushPenaltyThreshold * multiplier);
        }

        private void UpdatePushThreshold(bool pushing, ref float penaltyThreshold, ref float pushDuration,
            ref bool penalizedThisPush)
        {
            if (pushing)
            {
                if (pushDuration <= 0f)
                {
                    penaltyThreshold = maxPushPenaltyThreshold;
                }
                pushDuration += Time.deltaTime;
                penaltyThreshold -= Time.deltaTime;
                return;
            }

            // A shorter push tightens the allowance for the next episode; only the
            // longer penaltyThreshold can award points.
            if (!penalizedThisPush && pushDuration >= TightPushDuration)
            {
                TightenPushLimit(TightPushLimitMultiplier);
            }
            pushDuration = 0f;
            penalizedThisPush = false;
            penaltyThreshold = maxPushPenaltyThreshold;
        }
        public void AddScore(IScoreData scoreData, GameState gameState)
        {
            if (scoreData is ReefscapeScoreData reefscapeData)
            {
                reefscapeData.PenaltyEarned += numPenalties * 2 + flatPenaltyPoints;
            }
        }

        /// <summary>
        /// Whether the match is over and penalties should no longer count.
        ///
        /// Robots become controllable again a few seconds after the buzzer - BaseTimerManager's
        /// WaitEndgame re-enables them - so contact after time expires was still being scored. Cage
        /// penalties showed this most because they come from a trigger on the robot rather than a
        /// scoring volume that stops polling at the end of the match.
        /// </summary>
        private static bool MatchHasEnded =>
            BaseGameManager.Instance != null && BaseGameManager.Instance.GameState == GameState.End;

        public void AddPenalty(int penaltyAmount = 1)
        {
            if (MatchHasEnded)
            {
                return;
            }

            // Callers subtract from the amount when the push did not actually earn one - pushing a coral
            // while not holding one comes through as zero. Nothing is awarded, so nothing should flash.
            if (penaltyAmount <= 0)
            {
                return;
            }

            numPenalties += penaltyAmount;
            FlashOffendingAlliance();
        }

        /// <summary>Adds a flat point value (e.g. a 6-point cage foul) rather than 2-point units.</summary>
        public void AddPenaltyPoints(int points)
        {
            if (MatchHasEnded || points <= 0)
            {
                return;
            }

            flatPenaltyPoints += points;
            FlashOffendingAlliance();
        }

        /// <summary>
        /// Flashes the marker for whoever committed the foul - this alliance.
        ///
        /// Despite the name, PenaltyEarned is the penalty this alliance *caused*: ReefscapeScoreUI adds
        /// redScore.PenaltyPoints to blue's total and vice versa. So this scorer lives on the offending
        /// robot, and the marker to flash is its own side, not the opponent's.
        /// </summary>
        private void FlashOffendingAlliance()
        {
            PenaltyFlasher.Flash(Alliance);
        }
    }
}
