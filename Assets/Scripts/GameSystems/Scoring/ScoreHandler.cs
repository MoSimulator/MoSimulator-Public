using System;
using System.Collections.Generic;
using System.Linq;
using MoSimCore.BaseClasses;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using UnityEngine;

namespace GameSystems.Scoring
{
    public abstract class ScoreHandler<T, TUI> : MonoBehaviour 
        where T : IScoreData, new()
        where TUI : BaseScoreUI<T>
    {
        protected T BlueScoreData = new();
        protected T RedScoreData = new();
        protected TUI ScoreUI;

        public T GetScoreData(Alliance alliance) =>
            alliance == Alliance.Blue ? BlueScoreData : RedScoreData;
        
        public readonly List<IScorer> BlueScorers = new();
        public readonly List<IScorer> RedScorers = new();

        /// <summary>Scorers already reported as inactive, so the report does not repeat every frame.</summary>
        private readonly HashSet<IScorer> _reportedSkips = new();

        protected virtual void Awake()
        {
            ScoreUI = FindFirstObjectByType<TUI>();
        }
        
        protected virtual void Start()
        {
            var allScorers = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).OfType<IScorer>();

            foreach (var scorer in allScorers)
            {
                RegisterScorer(scorer);
            }
            
            UpdateUI();
        }

        protected virtual void LateUpdate()
        {
            CalculateScore(Alliance.Blue);
            CalculateScore(Alliance.Red);
            UpdateUI();
        }

        protected virtual void UpdateUI()
        {
            ScoreUI?.UpdateUI(BlueScoreData, RedScoreData);
        }
        
        protected virtual void CalculateScore(Alliance alliance)
        {
            var scoreData = alliance == Alliance.Blue ? BlueScoreData : RedScoreData;
            var scorers = alliance == Alliance.Blue ? BlueScorers : RedScorers;
            
            scoreData.Reset();
            
            foreach (var scorer in scorers)
            {
                // Scorers are registered from a search that deliberately takes in inactive objects,
                // so one being switched off is not a fault - it just has nothing to contribute this
                // frame. Reported once, since the alternative was this line every frame of a match.
                var mb = scorer as MonoBehaviour;
                if (mb == null || !mb.isActiveAndEnabled)
                {
                    if (_reportedSkips.Add(scorer))
                    {
                        Debug.LogWarning($"ScoreHandler: {alliance} is skipping {scorer.GetType().Name}" +
                                         $"{(mb != null ? $" on {mb.name}" : " (destroyed)")}, which is not active.");
                    }
                    continue;
                }
                scorer.AddScore(scoreData, BaseGameManager.Instance.GameState);
            }
        }
        
        protected virtual MoSimLib.SecureFloat GetTotalScore(Alliance alliance)
        {
            return alliance == Alliance.Blue ? BlueScoreData.TotalPoints + RedScoreData.PenaltyPoints: RedScoreData.TotalPoints + BlueScoreData.PenaltyPoints;
        }

        public void RegisterScorer(IScorer scorer)
        {
        
            BlueScorers.Remove(scorer);
            RedScorers.Remove(scorer);

            switch (scorer.Alliance)
            {
                case Alliance.Blue:
                    BlueScorers.Add(scorer);
                    break;
                case Alliance.Red:
                    RedScorers.Add(scorer);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scorer), $"ScoreHandler: invalid alliance {scorer.Alliance}.");
            }
        }

        public void UnregisterScorer(IScorer scorer)
        {
            switch (scorer.Alliance)
            {
                case Alliance.Blue when BlueScorers.Contains(scorer):
                    BlueScorers.Remove(scorer);
                    break;
                case Alliance.Red when RedScorers.Contains(scorer):
                    RedScorers.Remove(scorer);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void ReRegisterScorers()
        {
            UnregisterAllScorers();
            
            var allScorers = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).OfType<IScorer>();

            foreach (var scorer in allScorers)
            {
                RegisterScorer(scorer);
            }
        }
        
        private void UnregisterAllScorers()
        {
            BlueScorers.Clear();
            RedScorers.Clear();
        }

        public virtual void Reset()
        {
            UnregisterAllScorers();
            _reportedSkips.Clear();
            BlueScoreData = new T();
            RedScoreData = new T();
            UpdateUI();
        }
    }
}
