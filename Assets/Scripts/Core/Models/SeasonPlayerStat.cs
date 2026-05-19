namespace BasketballManager.Core.Models
{
    /// <summary>
    /// 球员在某赛季的累计数据（场次 + 各项 box-score 总数 + 正负值）。
    /// 场均通过 GamesPlayed 在 UI 层做除法换算。
    /// </summary>
    public sealed class SeasonPlayerStat
    {
        public int SeasonId { get; set; }
        public int PlayerId { get; set; }
        public string TeamId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;

        public int GamesPlayed { get; set; }
        public int Minutes { get; set; }
        public int Points { get; set; }
        public int Rebounds { get; set; }
        public int OffensiveRebounds { get; set; }
        public int Assists { get; set; }
        public int Steals { get; set; }
        public int Blocks { get; set; }
        public int Turnovers { get; set; }
        public int Fouls { get; set; }
        public int FieldGoalsMade { get; set; }
        public int FieldGoalsAttempted { get; set; }
        public int ThreePointersMade { get; set; }
        public int ThreePointersAttempted { get; set; }
        public int FreeThrowsMade { get; set; }
        public int FreeThrowsAttempted { get; set; }
        public int PlusMinus { get; set; }

        public float MinutesPerGame => GamesPlayed == 0 ? 0f : (float)Minutes / GamesPlayed;
        public float PointsPerGame => GamesPlayed == 0 ? 0f : (float)Points / GamesPlayed;
        public float ReboundsPerGame => GamesPlayed == 0 ? 0f : (float)Rebounds / GamesPlayed;
        public float AssistsPerGame => GamesPlayed == 0 ? 0f : (float)Assists / GamesPlayed;
    }
}
