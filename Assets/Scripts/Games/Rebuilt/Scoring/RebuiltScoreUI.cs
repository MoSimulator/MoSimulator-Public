using Games.Rebuilt.FieldScripts;
using GameSystems.Management;
using MoSimCore.BaseClasses;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using MoSimLib;
using RobotFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Games.Rebuilt.Scoring
{
    public class RebuiltScoreUI : BaseScoreUI<RebuiltScoreData>, IScoreUI
    {
        [Header("Total Scores")] [SerializeField]
        private TextMeshProUGUI blueScoreText;

        [SerializeField] private TextMeshProUGUI redScoreText;
        [SerializeField] private TextMeshProUGUI redFuelCount;
        [SerializeField] private TextMeshProUGUI blueFuelCount;
        [SerializeField] private TextMeshProUGUI shiftCounter;
        [SerializeField] private TextMeshProUGUI shiftTimer;
        [SerializeField] private GameObject blueShiftIndicator;
        [SerializeField] private GameObject redShiftIndicator;
        [SerializeField] private GameObject ShiftIndicator;

        [Header("Detailed Breakdown")]
        [SerializeField] private GameObject detailedScoreOverlay;
        [SerializeField] private float scoreOverlayFadeTime = 0.25f;

        private CanvasGroup _scoreOverlayCanvasGroup;
        [SerializeField] private Button scoreUIOverlayButton;
        [SerializeField] private Button detailedScoreOverlayButton;
        [SerializeField] private TextMeshProUGUI redTotalScore;
        [SerializeField] private TextMeshProUGUI blueTotalScore;
        [SerializeField] private TextMeshProUGUI redAutoFuelCount;
        [SerializeField] private TextMeshProUGUI blueAutoFuelCount;
        [SerializeField] private TextMeshProUGUI redTeleopFuelCount;
        [SerializeField] private TextMeshProUGUI blueTeleopFuelCount;
        [SerializeField] private TextMeshProUGUI redAutoTowerCount;
        [SerializeField] private TextMeshProUGUI blueAutoTowerCount;
        [SerializeField] private TextMeshProUGUI redTeleopTowerCount;
        [SerializeField] private TextMeshProUGUI blueTeleopTowerCount;
        [SerializeField] private TextMeshProUGUI bluePenaltyCount;
        [SerializeField] private TextMeshProUGUI redPenaltyCount;

        [Header("Version Text")]
        [SerializeField] private TextMeshProUGUI versionText;
        [SerializeField] private TextMeshProUGUI versionText2;

        private RobotSpawnController _robotSpawnController;
        private bool _leaderboardDone;

        [Header("Robot Labels")] [SerializeField]
        private GameObject[] blueRobots;

        [SerializeField] private GameObject[] redRobots;

        [Header("Modding Disclaimer Images")] [SerializeField]
        private GameObject[] blueDisclaimerImages;

        [SerializeField] private GameObject[] redDisclaimerImages;

        [Header("Winner Labels")] [SerializeField]
        private GameObject blueWinnerLabel;

        [SerializeField] private GameObject redWinnerLabel;

        [Header("Leaderboards")]
        [SerializeField] private GameObject RedRankParent;
        [SerializeField] private TextMeshProUGUI RedRankLabel;
        [SerializeField] private GameObject BlueRankParent;
        [SerializeField] private TextMeshProUGUI BlueRankLabel;


        public static SecureFloat TotalBlueScore;
        public static SecureFloat TotalRedScore;

        public static SecureFloat redAutoFuelScore;
        public static SecureFloat blueAutoFuelScore;

        private void Start()
        {
            _robotSpawnController = FindFirstObjectByType<RobotSpawnController>();

            _scoreOverlayCanvasGroup = Utils.TryGetAddComponent<CanvasGroup>(detailedScoreOverlay);

            scoreUIOverlayButton.interactable = false;
            scoreUIOverlayButton.onClick.AddListener(() =>
                {
                    if (!detailedScoreOverlay.activeSelf)
                        ShowScoreSummary();
                }
            );

            detailedScoreOverlayButton.onClick.AddListener(() => StartCoroutine(FadeCanvasGroup(_scoreOverlayCanvasGroup, 1f, 0f, scoreOverlayFadeTime, true)));

            if (versionText && versionText.text != "v" + Application.version)
            {
                versionText.text = $"v{Application.version}";
            }

            if (versionText2 && versionText2.text != "v" + Application.version)
            {
                versionText2.text = $"v{Application.version}";
            }
        }

        public void OnLeaderboardSubmissionComplete()
        {
            if (BaseGameManager.Instance.GameState != GameState.End) return;
            
            _leaderboardDone = true;
            scoreUIOverlayButton.interactable = true;
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
                StartCoroutine(FadeCanvasGroup(_scoreOverlayCanvasGroup, 1f, 0f, scoreOverlayFadeTime, true));
                if (_leaderboardDone)
                {
                    _leaderboardDone = false;
                    scoreUIOverlayButton.interactable = false;
                }
            }
        }

        protected override void UpdateTotalScores(RebuiltScoreData blueScore, RebuiltScoreData redScore)
        {
            TotalBlueScore = blueScore.TotalPoints + redScore.PenaltyPoints + blueScore.SelfPenaltyPoints;
            TotalRedScore = redScore.TotalPoints + blueScore.PenaltyPoints + redScore.SelfPenaltyPoints;

            blueScoreText.text = TotalBlueScore.ToString();
            redScoreText.text = TotalRedScore.ToString();

            redFuelCount.text = redScore.FuelPoints.ToString();
            blueFuelCount.text = blueScore.FuelPoints.ToString();

            var blueHub = RebuiltShifts.ActiveHub is ActiveHub.Blue or ActiveHub.Both;
            var redHub = RebuiltShifts.ActiveHub is ActiveHub.Red or ActiveHub.Both;

            blueShiftIndicator.SetActive(blueHub);
            redShiftIndicator.SetActive(redHub);

            int shiftNumber = RebuiltShifts.ShiftNumber;

            ShiftIndicator.SetActive(shiftNumber != 0);

            int displayedShiftSeconds = Mathf.FloorToInt(RebuiltShifts.SecondsUntilNextShift);
            shiftTimer.text = $"{Mathf.Max(0, displayedShiftSeconds):00}";

            shiftCounter.text = $"{shiftNumber} / 6";
        }

        protected override void UpdateDetailedScores(RebuiltScoreData blueScore, RebuiltScoreData redScore)
        {
            redAutoFuelScore = redScore.FuelAuto;
            blueAutoFuelScore = blueScore.FuelAuto;

            redAutoFuelCount.text = redScore.FuelAuto.ToString();
            blueAutoFuelCount.text = blueScore.FuelAuto.ToString();
            redAutoTowerCount.text = redScore.ClimbAuto.ToString();
            blueAutoTowerCount.text = blueScore.ClimbAuto.ToString();

            redTeleopFuelCount.text = redScore.FuelTele.ToString();
            blueTeleopFuelCount.text = blueScore.FuelTele.ToString();
            redTeleopTowerCount.text = redScore.ClimbTele.ToString();
            blueTeleopTowerCount.text = blueScore.ClimbTele.ToString();
            redPenaltyCount.text = blueScore.PenaltyEarned.ToString();
            bluePenaltyCount.text = redScore.PenaltyEarned.ToString();

            redTotalScore.text = (redScore.TotalPoints + blueScore.PenaltyEarned).ToString();
            blueTotalScore.text = (blueScore.TotalPoints + redScore.PenaltyEarned).ToString();

        }

        protected override void UpdateGamePieceCounters(RebuiltScoreData blueScore, RebuiltScoreData redScore)
        {

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
