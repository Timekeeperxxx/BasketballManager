using System;

namespace BasketballManager.Core.Models
{
    public class PlayerBoxScore
    {
        public int PlayerId { get; set; }
        public string TeamId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public int Minutes { get; set; }
        public int Points { get; set; }
        public int FieldGoalsMade { get; set; }
        public int FieldGoalsAttempted { get; set; }
        public int ThreePointersMade { get; set; }
        public int ThreePointersAttempted { get; set; }
        public int FreeThrowsMade { get; set; }
        public int FreeThrowsAttempted { get; set; }
        public int OffensiveRebounds { get; set; }
        public int DefensiveRebounds { get; set; }
        public int Rebounds => OffensiveRebounds + DefensiveRebounds;
        public int Assists { get; set; }
        public int Steals { get; set; }
        public int Blocks { get; set; }
        public int Turnovers { get; set; }
        public int Fouls { get; set; }
        public int PlusMinus { get; set; }

        // Zone FG tracking — indexed by (int)ShotZone, length 14
        public int[] ZoneFga = new int[14];
        public int[] ZoneFgm = new int[14];
    }
}
