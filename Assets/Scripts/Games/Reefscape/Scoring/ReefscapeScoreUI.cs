using GameSystems.Management;
using MoSimCore.BaseClasses;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using MoSimLib;
using RobotFramework;
using TMPro;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Games.Reefscape.Scoring
{
    public class ReefscapeScoreUI : BaseScoreUI<ReefscapeScoreData>, IScoreUI
    {
        [Header("Total Scores")]
        [SerializeField]
        private TextMeshProUGUI blueScoreText;

        [SerializeField] private TextMeshProUGUI redScoreText;

        public static SecureFloat TotalBlueScore;
        public static SecureFloat TotalRedScore;

        [Header("Game Pieces Scored")]
        [SerializeField]
        private TextMeshProUGUI blueCoralScoredText;

        [SerializeField] private TextMeshProUGUI redCoralScoredText;
        [SerializeField] private TextMeshProUGUI blueAlgaeScoredText;
        [SerializeField] private TextMeshProUGUI redAlgaeScoredText;

        [Header("PenatlyCard")]
        [SerializeField] private GameObject bluePenatlyCard;
        [SerializeField] private GameObject redPenatlyCard;

        [Header("Detailed Score Overlay")]
        [SerializeField]
        private GameObject detailedScoreOverlay;
        [SerializeField] private float scoreOverlayFadeTime = 0.25f;

        private CanvasGroup _scoreOverlayCanvasGroup;

        [SerializeField] private Button detailedScoreOverlayButton;
        private Button _detailedScoreButton;

        [Header("Blue Detailed Scores")]
        [SerializeField]
        private TextMeshProUGUI detailedBlueScoreText;

        [SerializeField] private TextMeshProUGUI blueLeaveText;
        [SerializeField] private TextMeshProUGUI blueCoralText;
        [SerializeField] private TextMeshProUGUI blueAlgaeText;
        [SerializeField] private TextMeshProUGUI blueBargeText;
        [SerializeField] private TextMeshProUGUI bluePenaltyText;

        [Header("Red Detailed Scores")]
        [SerializeField]
        private TextMeshProUGUI detailedRedScoreText;

        [SerializeField] private TextMeshProUGUI redLeaveText;
        [SerializeField] private TextMeshProUGUI redCoralText;
        [SerializeField] private TextMeshProUGUI redAlgaeText;
        [SerializeField] private TextMeshProUGUI redBargeText;
        [SerializeField] private TextMeshProUGUI redPenaltyText;

        [Header("Version Text")]
        [SerializeField] private TextMeshProUGUI versionText;
        [SerializeField] private TextMeshProUGUI versionText2;

        private RobotSpawnController _robotSpawnController;
        private bool _leaderboardDone;

        [Header("Robot Labels")]
        [SerializeField]
        private GameObject[] blueRobots;

        [SerializeField] private GameObject[] redRobots;

        [Header("Modding Disclaimer Images")]
        [SerializeField]
        private GameObject[] blueDisclaimerImages;

        [SerializeField] private GameObject[] redDisclaimerImages;

        [Header("Winner Labels")]
        [SerializeField]
        private GameObject blueWinnerLabel;

        [SerializeField] private GameObject redWinnerLabel;

        [Header("Leaderboards")]
        [SerializeField] private GameObject RedRankParent;
        [SerializeField] private TextMeshProUGUI RedRankLabel;
        [SerializeField] private GameObject BlueRankParent;
        [SerializeField] private TextMeshProUGUI BlueRankLabel;

        private void Start()
        {
            _robotSpawnController = FindFirstObjectByType<RobotSpawnController>();
            _scoreOverlayCanvasGroup = Utils.TryGetAddComponent<CanvasGroup>(detailedScoreOverlay);

            _detailedScoreButton = GetComponent<Button>();
            _detailedScoreButton.interactable = false;
            _detailedScoreButton.onClick.AddListener(() =>
                {
                    if (!detailedScoreOverlay.activeSelf)
                        ShowScoreSummary();
                }
            );

            if (versionText && versionText.text != "v" + Application.version)
            {
                versionText.text = $"v{Application.version}";
            }

            if (versionText2 && versionText2.text != "v" + Application.version)
            {
                versionText2.text = $"v{Application.version}";
            }

            detailedScoreOverlayButton.onClick.AddListener(() =>
                StartCoroutine(FadeCanvasGroup(_scoreOverlayCanvasGroup, 1f, 0f, scoreOverlayFadeTime, true)));
            redPenatlyCard.SetActive(false);
            bluePenatlyCard.SetActive(false);
        }

        public void OnLeaderboardSubmissionComplete()
        {
            if (BaseGameManager.Instance.GameState != GameState.End) return;
            
            _leaderboardDone = true;
            _detailedScoreButton.interactable = true;
            ShowScoreSummary();
        }


        /// <summary>
        /// Whether the breakdown is over the match. Closing it switches the overlay off once it has
        /// faded, so the overlay's own state is the answer.
        /// </summary>
        public bool IsBreakdownOpen => detailedScoreOverlay != null && detailedScoreOverlay.activeSelf;

        /// <summary>
        /// Closes the breakdown. Faded from wherever it currently is rather than from fully shown, so
        /// a second press while it is already going reads as the same dismissal instead of snapping it
        /// back to full and starting again.
        /// </summary>
        public void CloseBreakdown()
        {
            if (!IsBreakdownOpen || _scoreOverlayCanvasGroup == null)
            {
                return;
            }

            StartCoroutine(FadeCanvasGroup(_scoreOverlayCanvasGroup, _scoreOverlayCanvasGroup.alpha, 0f,
                scoreOverlayFadeTime, true));
        }

        SecureFloat IScoreUI.TotalBlueScore => TotalBlueScore;
        SecureFloat IScoreUI.TotalRedScore => TotalRedScore;

        private void ShowScoreSummary()
        {
            if (_robotSpawnController.BlueSpawnedRobots != null)
            {
                for (var i = 0; i < _robotSpawnController.BlueSpawnedRobots.Length; i++)
                {
                    var robot = _robotSpawnController.BlueSpawnedRobots[i];
                    if (robot == null) continue;
                    blueRobots[i].GetComponentInChildren<TextMeshProUGUI>().text =
                        robot.GetComponent<RobotBase>().TeamNumber.ToString();
                    blueDisclaimerImages[i].SetActive(_robotSpawnController.BlueRobotsModded[i]);
                }
            }

            if (_robotSpawnController.RedSpawnedRobots != null)
            {
                for (var i = 0; i < _robotSpawnController.RedSpawnedRobots.Length; i++)
                {
                    var robot = _robotSpawnController.RedSpawnedRobots[i];
                    if (robot == null) continue;
                    redRobots[i].GetComponentInChildren<TextMeshProUGUI>().text =
                        robot.GetComponent<RobotBase>().TeamNumber.ToString();
                    redDisclaimerImages[i].SetActive(_robotSpawnController.RedRobotsModded[i]);
                }
            }

            blueWinnerLabel.SetActive(TotalBlueScore > TotalRedScore);
            redWinnerLabel.SetActive(TotalRedScore > TotalBlueScore);
            StartCoroutine(FadeCanvasGroup(_scoreOverlayCanvasGroup, 0f, 1f, scoreOverlayFadeTime, false));
        }

        private void Update()
        {
            if (BaseGameManager.Instance != null && BaseGameManager.Instance.IsResetting)
            {
                redPenatlyCard.SetActive(false);
                bluePenatlyCard.SetActive(false);
                StartCoroutine(FadeCanvasGroup(_scoreOverlayCanvasGroup, 1f, 0f, scoreOverlayFadeTime, true));
                if (_leaderboardDone)
                {
                    _leaderboardDone = false;
                    _detailedScoreButton.interactable = false;
                }
            }
        }

        protected override void UpdateTotalScores(ReefscapeScoreData blueScore, ReefscapeScoreData redScore)
        {
            TotalBlueScore = blueScore.TotalPoints + redScore.PenaltyPoints + blueScore.SelfPenaltyPoints;
            TotalRedScore = redScore.TotalPoints + blueScore.PenaltyPoints + redScore.SelfPenaltyPoints;
            if (TotalBlueScore != blueScore.TotalPoints + redScore.PenaltyPoints + blueScore.SelfPenaltyPoints || TotalRedScore != redScore.TotalPoints + blueScore.PenaltyPoints + redScore.SelfPenaltyPoints)
            {
                Debug.Log("Score mismatch detected");
                BaseGameManager.Instance.wasCheated = true;
            }
            // if (blueScore.PenaltyPoints + redScore.SelfPenaltyPoints > 0)
            // {
            //     bluePenatlyCard.SetActive(true);
            // } else if (redScore.PenaltyPoints + blueScore.SelfPenaltyPoints > 0)
            // {
            //     redPenatlyCard.SetActive(true);
            // }

            blueScoreText.text = TotalBlueScore.ToString();
            redScoreText.text = TotalRedScore.ToString();
        }

        protected override void UpdateDetailedScores(ReefscapeScoreData blueScore, ReefscapeScoreData redScore)
        {
            TotalBlueScore = blueScore.TotalPoints + redScore.PenaltyPoints + blueScore.SelfPenaltyPoints;
            TotalRedScore = redScore.TotalPoints + blueScore.PenaltyPoints + redScore.SelfPenaltyPoints;
            detailedBlueScoreText.text = TotalBlueScore.ToString();

            detailedRedScoreText.text = TotalRedScore.ToString();

            blueLeaveText.text = blueScore.LeavePoints.ToString();
            blueCoralText.text = blueScore.CoralPoints.ToString();
            blueAlgaeText.text = (blueScore.NetPoints + blueScore.ProcessorPoints).ToString();
            blueBargeText.text = (blueScore.ClimbPoints + blueScore.ParkPoints).ToString();

            redLeaveText.text = redScore.LeavePoints.ToString();
            redCoralText.text = redScore.CoralPoints.ToString();
            redAlgaeText.text = (redScore.NetPoints + redScore.ProcessorPoints).ToString();
            redBargeText.text = (redScore.ClimbPoints + redScore.ParkPoints).ToString();
        }

        protected override void UpdateGamePieceCounters(ReefscapeScoreData blueScore, ReefscapeScoreData redScore)
        {
            blueCoralScoredText.text = blueScore.CoralScored.ToString();
            redCoralScoredText.text = redScore.CoralScored.ToString();
            blueAlgaeScoredText.text = blueScore.AlgaeScored.ToString();
            redAlgaeScoredText.text = redScore.AlgaeScored.ToString();
            bluePenaltyText.text = redScore.PenaltyPoints.ToString();
            redPenaltyText.text = blueScore.PenaltyPoints.ToString();

        }

        public void UpdateRankLabels(int rank, bool isBlue)
        {
            if (rank > 0)
            {
                switch (isBlue)
                {
                    case true:
                        BlueRankParent.SetActive(true);
                        BlueRankLabel.text = rank.ToString();
                        break;
                    case false:
                        RedRankParent.SetActive(true);
                        RedRankLabel.text = rank.ToString();
                        break;
                }
            }
            else
            {
                BlueRankParent.SetActive(false);
                RedRankParent.SetActive(false);
            }
        }
    }
}
