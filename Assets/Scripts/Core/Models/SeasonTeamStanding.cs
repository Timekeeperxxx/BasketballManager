namespace BasketballManager.Core.Models
{
    /// <summary>
    /// 球队在某赛季内的累计战绩 + 得失分。用于积分榜展示。
    /// </summary>
    public sealed class SeasonTeamStanding
    {
        public int SeasonId { get; set; }
        public string TeamId { get; set; } = string.Empty;
        public string TeamName { get; set; } = string.Empty;
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int PointsFor { get; set; }
        public int PointsAgainst { get; set; }

        public int GamesPlayed => Wins + Losses;
        public float WinPercentage => GamesPlayed == 0 ? 0f : (float)Wins / GamesPlayed;
        public int PointDifferential => PointsFor - PointsAgainst;
    }
}
