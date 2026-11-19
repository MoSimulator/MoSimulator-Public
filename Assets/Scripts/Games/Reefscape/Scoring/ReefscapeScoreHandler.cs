using GameSystems.Scoring;
using MoSimCore.Enums;
using UnityEngine;
using Games.Reefscape.Scoring.Scorers;
using System.Collections;
using RobotFramework;

namespace Games.Reefscape.Scoring
{
    public class ReefscapeScoreHandler : ScoreHandler<ReefscapeScoreData, ReefscapeScoreUI>
    {
        private BoxCollider _blueStartLineCollider;
        private BoxCollider _redStartLineCollider;
        private BoxCollider _blueWideStartLineCollider;
        private BoxCollider _redWideStartLineCollider;

        protected override void UpdateUI()
        {
            base.UpdateUI();
        }

        //adding a delay so that the the scoreers are correctly added to the robots, before they werent
        protected override void Start()
        {
            base.Start();
            StartCoroutine(InitializeAfterSpawn());
        }

        private IEnumerator InitializeAfterSpawn()
        {
            yield return null;
            yield return null;
            EnsureScorersOnRobots();
            ReRegisterScorers();
        }

        public override void Reset()
        {
            base.Reset();

            // Clear cached colliders so they get re-discovered from the new field
            _blueStartLineCollider = null;
            _redStartLineCollider = null;
            _blueWideStartLineCollider = null;
            _redWideStartLineCollider = null;

            UpdateUI();
        }

        private void DiscoverStartLineColliders()
        {
            if (_blueStartLineCollider != null && _redStartLineCollider != null)
            {
                return;
            }

            // Find start line colliders by tag
            _blueStartLineCollider = FindStartLineByTag("BlueStartLine");
            _redStartLineCollider = FindStartLineByTag("RedStartLine");
            _blueWideStartLineCollider = FindStartLineByTag("BlueStartLineWide");
            _redWideStartLineCollider = FindStartLineByTag("RedStartLineWide");

            // Fallback: search by name if tags not found
            if (_blueStartLineCollider == null || _redStartLineCollider == null)
            {
                var allColliders = FindObjectsByType<BoxCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var bc in allColliders)
                {
                    var goName = bc.gameObject.name.ToLowerInvariant();
                    var isWide = goName.Contains("wide");
                    if (goName.Contains("blue") && goName.Contains("start"))
                    {
                        if (isWide) _blueWideStartLineCollider ??= bc;
                        else _blueStartLineCollider ??= bc;
                    }
                    else if (goName.Contains("red") && goName.Contains("start"))
                    {
                        if (isWide) _redWideStartLineCollider ??= bc;
                        else _redStartLineCollider ??= bc;
                    }
                }
            }
        }

        private BoxCollider FindStartLineByTag(string tag)
        {
            try
            {
                var go = GameObject.FindGameObjectWithTag(tag);
                if (go != null)
                {
                    var collider = go.GetComponent<BoxCollider>();
                    if (collider != null)
                    {
                        return collider;
                    }
                }
            }
            catch (UnityException)
            {
                // Tag doesn't exist
            }
            return null;
        }

        public void EnsureScorersOnRobots()
        {
            DiscoverStartLineColliders();

            var robots = FindObjectsByType<RobotBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var robot in robots)
            {
                if (robot == null) continue;
                var mb = robot as MonoBehaviour;
                if (mb == null) continue;

                var scorer = mb.GetComponent<AutoLineScorer>();
                if (scorer == null)
                {
                    scorer = mb.gameObject.AddComponent<AutoLineScorer>();
                }

                // Assign the correct start line collider based on the robot's alliance
                var startLineForRobot = robot.Alliance == Alliance.Blue ? _blueStartLineCollider : _redStartLineCollider;

                if (startLineForRobot != null)
                {
                    scorer.SetStartLineTrigger(startLineForRobot);
                }

                var wideStartLineForRobot = robot.Alliance == Alliance.Blue ? _blueWideStartLineCollider : _redWideStartLineCollider;
                scorer.SetWideStartLineTrigger(wideStartLineForRobot);

                // Reset scorer state to allow scoring again after game reset
                scorer.ResetScorerState();

                EnsureZoneFoulDetector(mb.gameObject);
            }
        }
        
        // Foul detector on Robot
        private static void EnsureZoneFoulDetector(GameObject robot)
        {
            var detector = robot.GetComponent<ZoneFoulDetector>();
            if (detector == null)
            {
                detector = robot.AddComponent<ZoneFoulDetector>();
            }

            detector.ResetDetectorState();
        }
    }
}