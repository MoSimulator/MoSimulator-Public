using GameSystems.Management;
using MoSimCore.GameSelection;
using UnityEngine;

namespace Games.Reefscape.GameManagement
{
    /// <summary>
    /// Reefscape modpack metadata. Extend BaseModpackSO for Reefscape-specific packs.
    /// Create via Assets > Create > Games > Reefscape > ReefscapeModpackMetadata.
    /// </summary>
    [CreateAssetMenu(fileName = "ReefscapeModpack", menuName = "Games/Reefscape/ReefscapeModpackMetadata", order = 101)]
    public class ReefscapeBaseModpackSo : BaseModpackSO
    {
        public override GameOption GameOption => Resources.Load<GameOption>("GameOptions/Reefscape");
    }
}