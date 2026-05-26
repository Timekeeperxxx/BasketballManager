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

        // Three-point zone tendencies
        public int ZoneThreeLeftCorner  = 50;
        public int ZoneThreeRightCorner = 50;
        public int ZoneThreeLeftWing    = 60;
        public int ZoneThreeRightWing   = 60;
        public int ZoneThreeTopKey      = 70;

        // Mid-range zone tendencies
        public int ZoneMidLeftCorner  = 40;
        public int ZoneMidRightCorner = 40;
        public int ZoneMidLeftElbow   = 60;
        public int ZoneMidRightElbow  = 60;
        public int ZoneMidTopKey      = 50;

        // Close-range positional zone tendencies (surrounding basket; basket uses CloseShotTendency)
        public int ZoneCloseLeft   = 50;
        public int ZoneCloseCenter = 70;
        public int ZoneCloseRight  = 50;
    }
}
