using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoSimCore.GameSelection
{
    public class SelectedGameManager : MonoBehaviour
    {
        public static SelectedGameManager Instance { get; private set; }

        public static string PendingSelectedGameGuid { get; set; } = "";
        
        [field: SerializeField] public GameOption SelectedGame { get; set; }

        public List<GameOption> GameOptions { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadGame();
        }

        private void Start()
        {
            InitializeSelectedGame();
        }
        
        private void LoadGame()
        {
            GameOptions = Resources.LoadAll<GameOption>("GameOptions").ToList();

            GameOptions.Sort((a, b) => b.GameYear.CompareTo(a.GameYear));
        }

        public void SetSelectedGameFromGuid(string selectedGameGuid)
        {
            if (string.IsNullOrEmpty(selectedGameGuid))
            {
                return;
            }

            var match = GameOptions.FirstOrDefault(option => option.GetGuid() == selectedGameGuid);
            if (match != null)
            {
                SelectedGame = match;
            }
        }

        public bool TrySetSelectedGameFallback(string selectedGameGuid)
        {
            SetSelectedGameFromGuid(selectedGameGuid);
            if (SelectedGame != null)
            {
                return true;
            }

            if (GameOptions.Count > 0)
            {
                SelectedGame = GameOptions[0];
                return true;
            }

            return false;
        }

        private void InitializeSelectedGame()
        {
            if (SelectedGame != null)
            {
                return;
            }

            SetSelectedGameFromGuid(PendingSelectedGameGuid);
            if (SelectedGame != null)
            {
                return;
            }

            var activeSceneName = SceneManager.GetActiveScene().name;
            foreach (var gameOption in GameOptions)
            {
                if (gameOption.GameScene.Name == activeSceneName ||
                    gameOption.MainMenuScene.Name == activeSceneName)
                {
                    SelectedGame = gameOption;
                }
            }

            if (SelectedGame == null && GameOptions.Count > 0)
            {
                SelectedGame = GameOptions[0];
            }
        }
    }
}