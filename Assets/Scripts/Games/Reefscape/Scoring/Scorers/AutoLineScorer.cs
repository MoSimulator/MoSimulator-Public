using MoSimCore.Enums;
using MoSimCore.Interfaces;
using RobotFramework;
using UnityEngine;

namespace Games.Reefscape.Scoring.Scorers
{
    public class AutoLineScorer : MonoBehaviour, IScorer
    {
        private RobotBase _robotBase;
        [SerializeField] private BoxCollider startLineTrigger;
        [SerializeField] private BoxCollider wideStartLineTrigger;
        
        // State tracking for scoring
        private bool _hasBeenInInnerZone;
        private bool _hasBeenInWideOnlyZone;
        private bool _hasAlreadyScored;
        private bool _initialized;
        
        public Alliance Alliance { get; private set; }
        
        private void Awake()
        {
            InitializeRobotBase();
        }
        
        private void Start()
        {
            InitializeRobotBase();
            
            if (_robotBase == null)
            {
                enabled = false;
                return;
            }
            
            _hasBeenInInnerZone = false;
            _hasBeenInWideOnlyZone = false;
            _hasAlreadyScored = false;

            // Register with score handler if not already registered
            var scoreHandler = FindFirstObjectByType<ReefscapeScoreHandler>();
            if (scoreHandler != null && !scoreHandler.BlueScorers.Contains(this) && !scoreHandler.RedScorers.Contains(this))
            {
                scoreHandler.RegisterScorer(this);
            }
        }
        
        private void InitializeRobotBase()
        {
            if (_initialized) return;
            
            _robotBase = GetComponent<RobotBase>();
            if (_robotBase != null)
            {
                Alliance = _robotBase.Alliance;
                _initialized = true;
            }
        }

        public void SetStartLineTrigger(BoxCollider collider)
        {
            if (collider == null) return;

            InitializeRobotBase();

            startLineTrigger = collider;
            enabled = true;
        }

        public void SetWideStartLineTrigger(BoxCollider collider)
        {
            wideStartLineTrigger = collider;
        }

        public void ResetScorerState()
        {
            InitializeRobotBase();
            
            _hasBeenInInnerZone = false;
            _hasBeenInWideOnlyZone = false;
            _hasAlreadyScored = false;
        }

        public void AddScore(IScoreData scoreData, GameState gameState)
        {
            if (scoreData is not ReefscapeScoreData reefscapeData)
                return;

            // Only process scoring during Auto period
            if (gameState != GameState.Auto)
            {
                // During non-Auto periods, only persist if already scored
                if (_hasAlreadyScored)
                {
                    reefscapeData.LeavePoints += 3;
                }
                return;
            }

            if (!_initialized)
            {
                InitializeRobotBase();
            }

            if (startLineTrigger == null || _robotBase == null)
            {
                return;
            }

            bool robotInInnerZone = IsInsideCollider(startLineTrigger);
            bool robotInWideZone = wideStartLineTrigger != null && IsInsideCollider(wideStartLineTrigger);

            if (robotInInnerZone)
                _hasBeenInInnerZone = true;
            else if (robotInWideZone && !_hasBeenInInnerZone)
                _hasBeenInWideOnlyZone = true;

            // Robots that spawned in the inner zone must exit the inner zone.
            // Robots that spawned only in the wide zone must exit the wide zone.
            bool hasLeft = _hasBeenInInnerZone ? !robotInInnerZone : !robotInWideZone;

            if (!_hasAlreadyScored && (_hasBeenInInnerZone || _hasBeenInWideOnlyZone) && hasLeft)
            {
                _hasAlreadyScored = true;
            }

            // Persist points during Auto once awarded
            if (_hasAlreadyScored)
            {
                reefscapeData.LeavePoints += 3;
            }
        }

        private bool IsInsideCollider(BoxCollider col)
        {
            Vector3 closestPoint = col.ClosestPoint(_robotBase.transform.position);
            return Vector3.Distance(closestPoint, _robotBase.transform.position) < 0.5f;
        }
    }
}
