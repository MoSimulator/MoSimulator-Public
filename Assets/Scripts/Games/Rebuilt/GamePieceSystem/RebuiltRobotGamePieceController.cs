using RobotFramework.Controllers.GamePieceSystem;

namespace Games.Rebuilt.GamePieceSystem
{
    public class RebuiltRobotGamePieceController : RobotGamePieceController<RebuiltGamePiece, RebuiltGamePieceData>
    {
        private new void OnEnable()
        {
            gamePieceNodes.Clear();

            var fuelNode = new GamePieceControllerNode
            {
                pieceName = "Fuel"
            };

            gamePieceNodes.Add(fuelNode);

            base.OnEnable();
        }
    }
}