using System.Collections.Generic;

namespace BasketballManager.Simulation
{
    public class PlayerAverageStatLine
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; }
        public float Points { get; set; }
        public float Rebounds { get; set; }
        public float Assists { get; set; }
        public float FieldGoalsMade { get; set; }
        public float FieldGoalAttempts { get; set; }
        public float ThreePointersMade { get; set; }
        public float ThreePointAttempts { get; set; }
        public float FreeThrowsMade { get; set; }
        public float FreeThrowAttempts { get; set; }
    }

    public class MatchSimulationReport
    {
        public string HomeTeamId { get; set; }
        public string AwayTeamId { get; set; }
        public string HomeTeamName { get; set; }
        public string AwayTeamName { get; set; }
        public int Games { get; set; }
        
        public int HomeWins { get; set; }
        public int AwayWins { get; set; }
        
        public float AverageHomeScore { get; set; }
        public float AverageAwayScore { get; set; }
        public float AverageTotalScore { get; set; }
        
        public float HomeFieldGoalPercent { get; set; }
        public float AwayFieldGoalPercent { get; set; }
        public float HomeThreePointPercent { get; set; }
        public float AwayThreePointPercent { get; set; }
        public float HomeFreeThrowPercent { get; set; }
        public float AwayFreeThrowPercent { get; set; }
        
        public float HomeThreePointAttempts { get; set; }
        public float AwayThreePointAttempts { get; set; }
        public float HomeFreeThrowAttempts { get; set; }
        public float AwayFreeThrowAttempts { get; set; }
        
        public float HomeRebounds { get; set; }
        public float AwayRebounds { get; set; }
        public float HomeAssists { get; set; }
        public float AwayAssists { get; set; }
        public float HomeAssistRate { get; set; }
        public float AwayAssistRate { get; set; }
        public float HomeTurnovers { get; set; }
        public float AwayTurnovers { get; set; }
        public float HomeFouls { get; set; }
        public float AwayFouls { get; set; }
        
        public List<PlayerAverageStatLine> TopHomeScorers { get; set; } = new List<PlayerAverageStatLine>();
        public List<PlayerAverageStatLine> TopAwayScorers { get; set; } = new List<PlayerAverageStatLine>();
    }
}
