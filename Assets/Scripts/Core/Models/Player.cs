using System;
using BasketballManager.Core.Enums;

namespace BasketballManager.Core.Models
{
    [Serializable]
    public class Player
    {
        public int Id;
        public string TeamId = string.Empty;
        public string FirstName = string.Empty;
        public string LastName = string.Empty;
        public NameOrder NameOrder = NameOrder.WESTERN;
        public Position Position = Position.PG;
        public Position? SecondaryPosition = null;
        public int HeightCm;
        public int WeightKg;
        public int Age;
        public int JerseyNumber;
        public int Overall;
        public bool IsCurrent;
        public PlayerAttributes Attributes = new PlayerAttributes();
        public PlayerTendencies Tendencies = new PlayerTendencies();

        public string GetDisplayName()
        {
            return NameFormatter.GetDisplayName(this);
        }
    }
}
