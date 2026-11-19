﻿using System.Collections;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Interfaces;
using UnityEngine;

namespace MoSimCore.BaseClasses
{
    /// <summary>
    /// Abstract base class for score UI components in game modes.
    /// Provides a template for updating alliance scores in the UI.
    /// </summary>
    /// <typeparam name="T">The type of score data, must implement IScoreData.</typeparam>
    public abstract class BaseScoreUI<T> : MonoBehaviour where T : IScoreData
    {
        [SerializeField]
        [Tooltip("Shown when the match is flagged as cheated - typically the ScoringUI and ScoringSummary " +
                 "flags. Scores are left alone; the flag marks the match rather than hiding what happened " +
                 "in it.")]
        private GameObject[] anticheatFlags;

        /// <summary>
        /// Shows or hides the cheated flags. Lives here so every game gets it from one place, and takes
        /// a list because the same state drives the flag on both the live UI and the summary.
        /// </summary>
        protected void UpdateAnticheatFlags()
        {
            if (anticheatFlags == null)
            {
                return;
            }

            var cheated = BaseGameManager.Instance != null && BaseGameManager.Instance.wasCheated;

            foreach (var flag in anticheatFlags)
            {
                if (flag != null && flag.activeSelf != cheated)
                {
                    flag.SetActive(cheated);
                }
            }
        }

        /// <summary>
        /// Updates the total score displays for both alliances.
        /// Implement this to show the overall point totals.
        /// </summary>
        /// <param name="blueScore">Score data for the blue alliance.</param>
        /// <param name="redScore">Score data for the red alliance.</param>
        protected abstract void UpdateTotalScores(T blueScore, T redScore);
        
        /// <summary>
        /// Updates the detailed score breakdowns for both alliances.
        /// Implement this to show individual scoring components.
        /// </summary>
        /// <param name="blueScore">Score data for the blue alliance.</param>
        /// <param name="redScore">Score data for the red alliance.</param>
        protected abstract void UpdateDetailedScores(T blueScore, T redScore);
        
        /// <summary>
        /// Updates the game piece counter displays for both alliances.
        /// Implement this to show quantities of game pieces scored or held.
        /// </summary>
        /// <param name="blueScore">Score data for the blue alliance.</param>
        /// <param name="redScore">Score data for the red alliance.</param>
        protected abstract void UpdateGamePieceCounters(T blueScore, T redScore);

        /// <summary>
        /// Main update method that refreshes all UI elements with current score data.
        /// Override to customize the update sequence.
        /// </summary>
        /// <param name="blueScore">Score data for the blue alliance.</param>
        /// <param name="redScore">Score data for the red alliance.</param>
        public virtual void UpdateUI(T blueScore, T redScore)
        {
            UpdateTotalScores(blueScore, redScore);
            UpdateDetailedScores(blueScore, redScore);
            UpdateGamePieceCounters(blueScore, redScore);
            UpdateAnticheatFlags();
        }
        
        protected static IEnumerator FadeCanvasGroup(CanvasGroup canvasGroup, float initialAlpha, float targetAlpha,
            float duration, bool disableOnComplete)
        {
            if (disableOnComplete && !canvasGroup.gameObject.activeSelf)
            {
                yield break;
            }
            
            var elapsedTime = 0f;
            canvasGroup.alpha = initialAlpha;
            canvasGroup.gameObject.SetActive(true);

            while (elapsedTime < duration)
            {
                elapsedTime += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(initialAlpha, targetAlpha, elapsedTime / duration);
                yield return null;
            }

            canvasGroup.alpha = targetAlpha;

            if (disableOnComplete && Mathf.Approximately(targetAlpha, 0f))
            {
                canvasGroup.gameObject.SetActive(false);
            }
        }
    }
}