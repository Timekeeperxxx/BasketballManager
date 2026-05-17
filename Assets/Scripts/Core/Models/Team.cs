using System;

namespace BasketballManager.Core.Models
{
    [Serializable]
    public class Team
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string City = string.Empty;
        public int Era;
        public bool IsCurrent;
    }
}
