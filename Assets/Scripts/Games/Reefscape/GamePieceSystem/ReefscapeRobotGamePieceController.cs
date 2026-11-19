using RobotFramework.Controllers.GamePieceSystem;

namespace Games.Reefscape.GamePieceSystem
{
    public class ReefscapeRobotGamePieceController : RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>
    {
        private new void OnEnable()
        {
            gamePieceNodes.Clear();

            var coralNode = new GamePieceControllerNode
            {
                pieceName = "Coral"
            };

            var algaeNode = new GamePieceControllerNode
            {
                pieceName = "Algae"
            };

            gamePieceNodes.Add(coralNode);
            gamePieceNodes.Add(algaeNode);
            
            base.OnEnable();
        }
    }
}