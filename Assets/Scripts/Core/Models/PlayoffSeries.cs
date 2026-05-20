namespace BasketballManager.Core.Models
{
    public sealed class PlayoffSeries
    {
        public int Id { get; set; }
        public int SeasonId { get; set; }
        public int Round { get; set; }
        public int Seed1 { get; set; }
        public int Seed2 { get; set; }
        public string Team1Id { get; set; } = string.Empty;
        public string Team2Id { get; set; } = string.Empty;
        public string Team1Name { get; set; } = string.Empty;
        public string Team2Name { get; set; } = string.Empty;
        public int Team1Wins { get; set; }
        public int Team2Wins { get; set; }
        public string Status { get; set; } = "IN_PROGRESS";   // IN_PROGRESS / COMPLETE
        public string WinnerTeamId { get; set; } = string.Empty;
    }
}
