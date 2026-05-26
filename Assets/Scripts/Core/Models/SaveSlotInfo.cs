using System;

namespace BasketballManager.Core.Models
{
    public sealed class SaveSlotInfo
    {
        public int      SlotId;
        public bool     IsEmpty;
        public string   LastSeasonName   = string.Empty;
        public int      LastSeasonNumber;
        public string   UserTeamName     = string.Empty;
        public DateTime LastModified;
    }
}
