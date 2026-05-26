namespace BasketballManager.Core.Models
{
    public class PlayerZoneStat
    {
        public ShotZone Zone;
        public int Fgm;
        public int Fga;
        public float FgPct => Fga > 0 ? Fgm / (float)Fga : 0f;
    }
}
