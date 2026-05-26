using System.Collections.Generic;

namespace BasketballManager.Core.Models
{
    public sealed class PlayerDevelopmentResult
    {
        public int    PlayerId;
        public string PlayerName = string.Empty;
        public string TeamId     = string.Empty;
        public int    OldAge;
        public int    NewAge;
        public int    OldOverall;
        public int    NewOverall;
        public int    OverallDelta => NewOverall - OldOverall;
        // key = 属性显示名，value = 变化量（仅非零项）
        public Dictionary<string, int> AttributeDeltas = new Dictionary<string, int>();
    }
}
