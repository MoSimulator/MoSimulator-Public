using GameSystems.Management;
using MoSimCore.GameSelection;
using UnityEngine;

namespace Games.Rebuilt.GameManagement
{
    /// <summary>
    /// Rebuilt modpack metadata. Extend BaseModpackSO for Rebuilt-specific packs.
    /// Create via Assets > Create > Games > Rebuilt > RebuiltModpackMetadata.
    /// </summary>
    [CreateAssetMenu(fileName = "RebuiltModpack", menuName = "Games/Rebuilt/RebuiltModpackMetadata", order = 11)]
    public class RebuiltModpackSO : BaseModpackSO
    {
        public override GameOption GameOption => Resources.Load<GameOption>("GameOptions/Rebuilt");
    }
}