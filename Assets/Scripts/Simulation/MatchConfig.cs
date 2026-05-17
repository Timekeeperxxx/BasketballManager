namespace BasketballManager.Simulation
{
    public class MatchConfig
    {
        public int Seed { get; set; }
        public int QuarterCount { get; set; } = 4;
        public int MinutesPerQuarter { get; set; } = 12;
        public int MinPossessionsPerTeam { get; set; } = 90;
        public int MaxPossessionsPerTeam { get; set; } = 105;
        public bool UseFixedSeed { get; set; }
    }
}
