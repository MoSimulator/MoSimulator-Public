namespace MoSimCore.Interfaces
{
    /// <summary>
    /// Interface for score UI components that support leaderboard rank display.
    /// </summary>
    public interface IScoreUI
    {
        /// <summary>
        /// Updates the rank labels displayed in the UI.
        /// </summary>
        /// <param name="rank">The global rank from the leaderboard.</param>
        /// <param name="isBlue">Whether to display the rank for the blue alliance (true) or red alliance (false).</param>
        void UpdateRankLabels(int rank, bool isBlue);

        /// <summary>
        /// Called when the leaderboard submission process has fully completed (or was skipped).
        /// Implementations should auto-show the score summary and enable its toggle button.
        /// </summary>
        void OnLeaderboardSubmissionComplete();

        /// <summary>
        /// Gets whether the detailed score breakdown is currently over the match. The breakdown covers
        /// the whole screen, so anything that would open behind it has to wait for it to be closed.
        /// </summary>
        bool IsBreakdownOpen { get; }

        /// <summary>
        /// Closes the detailed score breakdown, the same way its own close button does. Does nothing
        /// when it is not open.
        /// </summary>
        void CloseBreakdown();

        /// <summary>
        /// Gets the total score for the blue alliance to be used in leaderboard submissions.
        /// </summary> <returns>The total blue score.</returns>
        MoSimLib.SecureFloat TotalBlueScore { get; }
        /// <summary> Gets the total score for the red alliance to be used in leaderboard submissions.
        /// </summary> <returns>The total red score.</returns>
        MoSimLib.SecureFloat TotalRedScore { get; }
    }
}
