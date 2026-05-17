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
        public string DisplayName = string.Empty;
        public NameOrder NameOrder = NameOrder.WESTERN;
        public string Nationality = string.Empty;
        public string RegionType = string.Empty;
        public Position Position = Position.PG;
        public int HeightCm;
        public int WeightKg;
        public int Age;
        public int JerseyNumber;
        public int Overall;
        public PlayerAttributes Attributes = new PlayerAttributes();
        public PlayerTendencies Tendencies = new PlayerTendencies();

        public string GetDisplayName()
        {
            return NameFormatter.GetDisplayName(this);
        }
    }
}
