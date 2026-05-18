namespace BasketballManager.Core.Models
{
    public sealed class SimulationPlayerProfile
    {
        public int PlayerId { get; set; }
        public string TeamId { get; set; }
        public float SourceMpg { get; set; }
        public string RotationRole { get; set; }
        public float MinuteFloor { get; set; }
        public float MinuteCeiling { get; set; }
    }
}
