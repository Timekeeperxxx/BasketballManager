namespace BasketballManager.Core.Models
{
    /// <summary>
    /// 赛季内一场比赛。生成赛程时 status=SCHEDULED；模拟后置为 PLAYED 并填入比分。
    /// </summary>
    public sealed class SeasonGame
    {
        public int Id { get; set; }
        public int SeasonId { get; set; }
        public int Day { get; set; }
        public string HomeTeamId { get; set; } = string.Empty;
        public string AwayTeamId { get; set; } = string.Empty;
        public int HomeScore { get; set; }
        public int AwayScore { get; set; }
        public string Status { get; set; } = "SCHEDULED";      // SCHEDULED / PLAYED / CANCELLED
        public string WinnerTeamId { get; set; } = string.Empty;
        public string PlayedAt { get; set; } = string.Empty;
        public string Phase { get; set; } = "REGULAR";        // REGULAR | PLAYOFF
        public int SeriesId { get; set; } = 0;
    }
}
