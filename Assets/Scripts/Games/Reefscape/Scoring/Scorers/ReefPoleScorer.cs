using Games.Reefscape.Enums;
using Games.Reefscape.FieldScripts;
using Games.Reefscape.GamePieceSystem;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using MoSimLib;
using RobotFramework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Games.Reefscape.Scoring.Scorers
{
    public class ReefPoleScorer : MonoBehaviour, IScorer
    {
        public Alliance Alliance { get; private set; }

        private Reef _reef;
        private GameObject _triggersCache;
        private Branch[] _branches;
        private bool _initialized;
        private int _supportExclusionMask;

        // Update queues every branch as one batch. FixedUpdate performs that batch
        // once, then Update applies the saved results on its following frame.
        private readonly List<Branch> _branchCheckQueue = new();
        private readonly List<BranchCheckResult> _completedBranchChecks = new();
        private bool _branchCheckResultsReady;

        [SerializeField] private AudioClip scoreClip;
        [SerializeField] private AudioSource poleAudioSource;

        private void Awake()
        {
            if (SceneManager.GetActiveScene().name == "MainMenuReefscape")
            {
                enabled = false;
                return;
            }
            
            _reef = GetComponentInParent<Reef>();
            if (_reef == null)
            {
                Debug.LogError("ReefPoleScorer could not find a Reef component in parent objects.");
                enabled = false;
                return;
            }

            Alliance = _reef.Alliance;
            _supportExclusionMask = LayerMask.GetMask("Algae", "Robot");

            _triggersCache = Utils.FindChild("Triggers", gameObject);
            if (_triggersCache == null)
            {
                Debug.LogError("ReefPoleScorer could not find 'Triggers' child object.");
                enabled = false;
                return;
            }
            
            var l4 = Utils.FindChild("L4", _triggersCache)?.GetComponent<BoxCollider>();
            var l3 = Utils.FindChild("L3", _triggersCache)?.GetComponent<BoxCollider>();
            var l2 = Utils.FindChild("L2", _triggersCache)?.GetComponent<BoxCollider>();
            if (l4 == null || l3 == null || l2 == null)
            {
                Debug.LogError("ReefPoleScorer could not find one or more branch colliders.");
                enabled = false;
                return;
            }

            _branches = new[]
            {
                new Branch(ReefscapeBranchHeight.L4, l4),
                new Branch(ReefscapeBranchHeight.L3, l3),
                new Branch(ReefscapeBranchHeight.L2, l2)
            };
            
            _initialized = true;

            foreach (var branch in _branches)
            {
                branch.bounds = Utils.MultiplyVectors(branch.ScoringTrigger.size, branch.ScoringTrigger.transform.lossyScale) / 2;
                branch.center = branch.ScoringTrigger.bounds.center;
                branch.rotation = branch.ScoringTrigger.transform.rotation;
            }
        }

        private void Update()
        {
            if (!_initialized || !isActiveAndEnabled)
            {
                Debug.LogError("ReefPoleScorer is initialized: " + _initialized + " and active/enabled: " + isActiveAndEnabled);
                return;
            }
            
            if (BaseGameManager.Instance.GameState == GameState.End)
            {
                return;
            }
            
            if (_branchCheckResultsReady)
            {
                foreach (var checkResult in _completedBranchChecks)
                {
                    ApplyCoralCheck(checkResult);
                }

                _completedBranchChecks.Clear();
                _branchCheckResultsReady = false;
            }

            QueueBranchChecks();
        }

        private void FixedUpdate()
        {
            if (_branchCheckQueue.Count == 0)
            {
                return;
            }

            _completedBranchChecks.Clear();
            foreach (var branch in _branchCheckQueue)
            {
                if (branch?.ScoringTrigger == null)
                {
                    continue;
                }

                _completedBranchChecks.Add(EvaluateCoral(branch));
            }

            _branchCheckQueue.Clear();
            _branchCheckResultsReady = true;
        }

        private void QueueBranchChecks()
        {
            _branchCheckQueue.Clear();
            foreach (var branch in _branches)
            {
                if (branch?.ScoringTrigger == null)
                {
                    Debug.LogError("Branch or ScoringTrigger is null in ReefPoleScorer on " + gameObject.name);
                    continue;
                }

                _branchCheckQueue.Add(branch);
            }
        }

        /// <summary>
        /// returns what branches are scored in an array. (l4:0, l3:1, l2:2)
        /// </summary>
        /// <returns></returns>
        public bool[] GetScoredHeights()
        {
            bool[] result = new bool[3];
            result[0] = _branches[0].Scored;
            result[1] = _branches[1].Scored;
            result[2] = _branches[2].Scored;
            
            return result;
        }

        public void AddScore(IScoreData scoreData, GameState gameState)
        {
            if (!_initialized || !isActiveAndEnabled)
            {
                Debug.LogError("ReefPoleScorer is initialized: " + _initialized + " and active/enabled: " + isActiveAndEnabled);
                return;
            }
            if (scoreData is not ReefscapeScoreData reefscapeData) return;

            foreach (var branch in _branches)
            {
                if (branch == null)
                {
                    Debug.LogError("Branch is null in ReefPoleScorer on " + gameObject.name);
                    continue;
                }

                var val = branch.TargetHeight switch
                {
                    ReefscapeBranchHeight.L4 => branch.ScoredInAuto ? 7 : 5,
                    ReefscapeBranchHeight.L3 => branch.ScoredInAuto ? 6 : 4,
                    ReefscapeBranchHeight.L2 => branch.ScoredInAuto ? 4 : 3,
                    _ => 0
                };

                if (branch.Scored)
                {
                    reefscapeData.CoralPoints += val;
                    reefscapeData.CoralScored += 1;
                }
            }
        }

        private BranchCheckResult EvaluateCoral(Branch branch)
        {
            var hasCoral = false;
            ReefscapeGamePieceController scoredPiece = null;
            foreach (var result in Physics.OverlapBox(branch.center,
                         branch.bounds,
                         branch.rotation))
            {
                if (!result.CompareTag("Coral")) continue;

                // A coral held by a robot has its colliders reparented under the
                // robot, so an ancestor RobotBase means it isn't on the branch.
                if (result.GetComponentInParent<RobotBase>() != null) continue;

                // A coral propped up by an algae or pressed in by a robot isn't
                // legitimately scored either — reject anything touching them.
                if (Physics.CheckBox(result.bounds.center, result.bounds.extents * 1.05f,
                        Quaternion.identity, _supportExclusionMask)) continue;

                hasCoral = true;
                scoredPiece = result.GetComponentInParent<ReefscapeGamePieceController>();
                break;
            }

            return new BranchCheckResult(branch, hasCoral, scoredPiece);
        }

        private void ApplyCoralCheck(BranchCheckResult checkResult)
        {
            var branch = checkResult.Branch;
            switch (checkResult.HasCoral)
            {
                case true when !branch.Scored:
                    branch.Scored = true;
                    branch.ScoredPiece = checkResult.ScoredPiece;
                    if (checkResult.ScoredPiece != null)
                    {
                        checkResult.ScoredPiece.IsScored = true;
                    }

                    if (!branch.ScoredInAuto && BaseGameManager.Instance.GameState == GameState.Auto)
                    {
                        branch.ScoredInAuto = true;
                    }

                    if (poleAudioSource != null && scoreClip != null)
                    {
                        poleAudioSource.PlayOneShot(scoreClip);
                    }
                    break;
                case false when branch.Scored:
                    branch.Scored = false;
                    if (branch.ScoredPiece != null)
                    {
                        branch.ScoredPiece.IsScored = false;
                    }

                    branch.ScoredPiece = null;
                    break;
            }
        }

        private readonly struct BranchCheckResult
        {
            public Branch Branch { get; }
            public bool HasCoral { get; }
            public ReefscapeGamePieceController ScoredPiece { get; }

            public BranchCheckResult(Branch branch, bool hasCoral, ReefscapeGamePieceController scoredPiece)
            {
                Branch = branch;
                HasCoral = hasCoral;
                ScoredPiece = scoredPiece;
            }
        }

        private class Branch
        {
            public ReefscapeBranchHeight TargetHeight { get; }
            public BoxCollider ScoringTrigger { get; }
            public bool Scored { get; set; }
            public bool ScoredInAuto { get; set; }
            public ReefscapeGamePieceController ScoredPiece { get; set; }

            public Vector3 bounds;

            public Vector3 center;
            
            public Quaternion rotation;

            public Branch(ReefscapeBranchHeight targetHeight, BoxCollider scoringTrigger)
            {
                TargetHeight = targetHeight;
                ScoringTrigger = scoringTrigger;
                Scored = false;
            }
        }
    }
}
