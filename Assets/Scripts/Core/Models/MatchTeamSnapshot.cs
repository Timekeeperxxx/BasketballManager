using System.Collections.Generic;
using BasketballManager.Simulation;

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

    public class TeamStyleProfile
    {
        public float PaceModifier { get; set; } = 1.0f;
        public float SpacingModifier { get; set; } = 1.0f;
        public float ThreeGravity { get; set; } = 1.0f;
        public float RimPressure { get; set; } = 1.0f;
        public float AssistModifier { get; set; } = 1.0f;
        public float TurnoverControl { get; set; } = 1.0f;
        public float OffensiveReboundModifier { get; set; } = 1.0f;
        public float DefensiveReboundModifier { get; set; } = 1.0f;
        public float SwitchDefense { get; set; } = 1.0f;
        public float RimProtection { get; set; } = 1.0f;
        public float FoulPressure { get; set; } = 1.0f;
        public float TransitionModifier { get; set; } = 1.0f;
    }

    public class MatchTeamSnapshot
    {
        public Team Team { get; set; }
        public IReadOnlyList<Player> Players { get; set; }
        public List<Player> RotationPlayers { get; set; } = new List<Player>();
        public Dictionary<BasketballManager.Core.Enums.Position, Player> Starters { get; set; } = new Dictionary<BasketballManager.Core.Enums.Position, Player>();
        public TeamBoxScore TeamStats { get; set; } = new TeamBoxScore();
        public Dictionary<int, PlayerBoxScore> PlayerStatsById { get; set; } = new Dictionary<int, PlayerBoxScore>();
        public Dictionary<int, int> OffensiveRoleRankByPlayerId { get; set; } = new Dictionary<int, int>();
        public Dictionary<int, int> PlaymakerRoleRankByPlayerId { get; set; } = new Dictionary<int, int>();
        public Dictionary<int, PlayerGameState> PlayerGameStates { get; set; } = new Dictionary<int, PlayerGameState>();
        public int ConsecutiveTeamThreeMisses { get; set; }
        public bool HasTransitionOpportunity { get; set; }
        public TeamStyleProfile StyleProfile { get; set; } = new TeamStyleProfile();

        public RotationSchedule Rotation { get; set; }
        public List<Player> CurrentLineup { get; set; } = new List<Player>();
        public IReadOnlyDictionary<int, SimulationPlayerProfile> Profiles { get; set; }

        public float GetSourceMpg(int playerId, float fallback = 20f)
        {
            return Profiles != null && Profiles.TryGetValue(playerId, out var p) ? p.SourceMpg : fallback;
        }
    }
}
