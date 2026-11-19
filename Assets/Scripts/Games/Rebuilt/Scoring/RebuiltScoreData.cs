using MoSimCore.Interfaces;

namespace Games.Rebuilt.Scoring
{
    public class RebuiltScoreData : IScoreData
    {
        
        
        public MoSimLib.SecureFloat FuelAuto { get; set; }
        
        public MoSimLib.SecureFloat FuelTele { get; set; }
        
        public MoSimLib.SecureFloat FuelPoints => FuelAuto + FuelTele;
            
        public MoSimLib.SecureFloat ClimbAuto { get; set; }
        public MoSimLib.SecureFloat ClimbTele { get; set; }
        public MoSimLib.SecureFloat ClimbPoints => ClimbAuto + ClimbTele;
        
        public MoSimLib.SecureFloat PenaltyEarned { get; set; }
        public MoSimLib.SecureFloat SelfPenaltyEarned { get; set; }

        public MoSimLib.SecureFloat TotalPoints => FuelPoints + ClimbPoints;
        public MoSimLib.SecureFloat PenaltyPoints => PenaltyEarned;
        public MoSimLib.SecureFloat SelfPenaltyPoints => SelfPenaltyEarned;
        
        public void Reset()
        {
            FuelAuto = 0;
            FuelTele = 0;
            ClimbAuto = 0;
            ClimbTele = 0;
            PenaltyEarned = 0;
            SelfPenaltyEarned = 0;
        }
    }
}
