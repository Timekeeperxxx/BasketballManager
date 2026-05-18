using System.Collections.Generic;

namespace BasketballManager.Core.Models
{
    public class MatchResult
    {
        public string HomeTeamId { get; set; } = string.Empty;
        public string AwayTeamId { get; set; } = string.Empty;
        public string HomeTeamName { get; set; } = string.Empty;
        public string AwayTeamName { get; set; } = string.Empty;
        
        public int HomeScore { get; set; }
        public int AwayScore { get; set; }
        
        public List<int> HomeQuarterScores { get; set; } = new List<int>();
        public List<int> AwayQuarterScores { get; set; } = new List<int>();
        
        public TeamBoxScore HomeTeamStats { get; set; } = new TeamBoxScore();
        public TeamBoxScore AwayTeamStats { get; set; } = new TeamBoxScore();
        
        public List<PlayerBoxScore> HomePlayerStats { get; set; } = new List<PlayerBoxScore>();
        public List<PlayerBoxScore> AwayPlayerStats { get; set; } = new List<PlayerBoxScore>();

        public TeamStyleProfile HomeStyleProfile { get; set; }
        public TeamStyleProfile AwayStyleProfile { get; set; }
        
        public string WinnerTeamId { get; set; } = string.Empty;
    }
}
