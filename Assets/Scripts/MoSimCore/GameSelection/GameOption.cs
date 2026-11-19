using System;
using System.Security.Cryptography;
using System.Text;
using Eflatun.SceneReference;
using MoSimLib;
using UnityEngine;

namespace MoSimCore.GameSelection
{
    [CreateAssetMenu(fileName = "Game Option", menuName = "Games/Game Option", order = -1)]
    public class GameOption : ScriptableObject
    {
        [field: SerializeField] public int GameYear { get; set; }
        [field: SerializeField] public GameType GameType { get; set; } = GameType.FRC;
        [field: SerializeField] public SceneReference MainMenuScene { get; set; }
        [field: SerializeField] public SceneReference GameScene { get; set; }
        [field: SerializeField] public Sprite GameBackground { get; set; }

        public override string ToString()
        {
            return $"{name}|{GameYear}|{GameType}";
        }

        public string GetGuid()
        {
            var seed = $"{name}|{GameYear}|{GameType}";
            return Utils.CreateDeterministicGuid(seed).ToString();
        }
    }

    public enum GameType
    {
        FRC,
        FTC
    }
}