using System.Collections.Generic;

namespace BasketballManager.Core.Models
{
    public class MatchTeamSnapshot
    {
        public Team Team { get; set; }
        public IReadOnlyList<Player> Players { get; set; }
        public List<Player> RotationPlayers { get; set; } = new List<Player>();
        public TeamBoxScore TeamStats { get; set; } = new TeamBoxScore();
        public Dictionary<int, PlayerBoxScore> PlayerStatsById { get; set; } = new Dictionary<int, PlayerBoxScore>();
        public Dictionary<int, int> OffensiveRoleRankByPlayerId { get; set; } = new Dictionary<int, int>();
    }
}
