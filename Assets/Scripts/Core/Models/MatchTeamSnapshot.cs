using System.Collections.Generic;

namespace BasketballManager.Core.Models
{
    public class PlayerGameState
    {
        public int Fga { get; set; }
        public int Fgm { get; set; }
        public int ThreePa { get; set; }
        public int ThreePm { get; set; }
        public int ConsecutiveMisses { get; set; }
        public int ConsecutiveThreeMisses { get; set; }
        public int Points { get; set; }
    }

    public class MatchTeamSnapshot
    {
        public Team Team { get; set; }
        public IReadOnlyList<Player> Players { get; set; }
        public List<Player> RotationPlayers { get; set; } = new List<Player>();
        public TeamBoxScore TeamStats { get; set; } = new TeamBoxScore();
        public Dictionary<int, PlayerBoxScore> PlayerStatsById { get; set; } = new Dictionary<int, PlayerBoxScore>();
        public Dictionary<int, int> OffensiveRoleRankByPlayerId { get; set; } = new Dictionary<int, int>();
        public Dictionary<int, int> PlaymakerRoleRankByPlayerId { get; set; } = new Dictionary<int, int>();
        public Dictionary<int, PlayerGameState> PlayerGameStates { get; set; } = new Dictionary<int, PlayerGameState>();
        public int ConsecutiveTeamThreeMisses { get; set; }
        public bool HasTransitionOpportunity { get; set; }
    }
}
