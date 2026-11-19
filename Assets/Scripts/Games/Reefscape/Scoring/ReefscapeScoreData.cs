using MoSimCore.Interfaces;

namespace Games.Reefscape.Scoring
{
    public class ReefscapeScoreData : IScoreData
    {
        public MoSimLib.SecureFloat CoralPoints { get; set; }
        public MoSimLib.SecureFloat TroughPoints { get; set; }
        public MoSimLib.SecureFloat NetPoints { get; set; }
        public MoSimLib.SecureFloat ProcessorPoints { get; set; }
        public MoSimLib.SecureFloat ClimbPoints { get; set; }
        public MoSimLib.SecureFloat ParkPoints { get; set; }
        public MoSimLib.SecureFloat LeavePoints { get; set; }
        
        public MoSimLib.SecureFloat CoralScored { get; set; }
        public MoSimLib.SecureFloat AlgaeScored { get; set; }
        
        public MoSimLib.SecureFloat PenaltyEarned { get; set; }
        
        public MoSimLib.SecureFloat SelfPenaltyEarned { get; set; }
        
        public MoSimLib.SecureFloat TotalPoints => CoralPoints + TroughPoints + NetPoints + ProcessorPoints + ClimbPoints + ParkPoints + LeavePoints;
        
        public MoSimLib.SecureFloat PenaltyPoints => PenaltyEarned;
        
        public MoSimLib.SecureFloat SelfPenaltyPoints => SelfPenaltyEarned;
        
        public void Reset()
        {
            CoralPoints = 0;
            TroughPoints = 0;
            NetPoints = 0;
            ProcessorPoints = 0;
            ClimbPoints = 0;
            ParkPoints = 0;
            LeavePoints = 0;
            CoralScored = 0;
            AlgaeScored = 0;
            PenaltyEarned = 0;
            SelfPenaltyEarned = 0;
        }
    }
}
