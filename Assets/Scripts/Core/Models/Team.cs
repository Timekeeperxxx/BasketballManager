using System;

namespace BasketballManager.Core.Models
{
    [Serializable]
    public class Team
    {
        public int Id;
        public string Name = string.Empty;
        public string City = string.Empty;
    }
}
