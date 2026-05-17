using System;

namespace BasketballManager.Core.Models
{
    [Serializable]
    public class PlayerTendencies
    {
        public int ShotTendency;
        public int ThreeTendency;
        public int TwoPointTendency;
        public int DriveTendency;
        public int PostTendency;
        public int CloseShotTendency;
        public int PassTendency;
        public int DrawFoulTendency;
        public int StealTendency;
        public int BlockTendency;
        public int FoulTendency;
        public int HelpDefenseTendency;
        public int OffensiveReboundTendency;
        public int DefensiveReboundTendency;
    }
}
