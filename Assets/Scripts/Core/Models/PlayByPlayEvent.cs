namespace BasketballManager.Core.Models
{
    public class PlayByPlayEvent
    {
        public int Quarter;       // 1-based (5+ = OT)
        public int ClockSeconds;  // seconds remaining in period
        public int JerseyNumber;  // key player jersey #
        public int HomeScore;
        public int AwayScore;
        public string Description;
        public EventStatCredit[] Credits; // per-event stat deltas for partial-stat computation
    }

    /// <summary>
    /// One player's stat contribution from a single play-by-play event.
    /// Summing Credits[0.._pbpEventsShown] gives partial box scores.
    /// </summary>
    public class EventStatCredit
    {
        public int  PlayerId;
        public bool IsHome;
        public int  Pts, FgM, FgA, Fg3M, Fg3A, FtM, FtA;
        public int  OffReb, DefReb, Ast, Stl, Blk, Tov, PF;
    }
}
