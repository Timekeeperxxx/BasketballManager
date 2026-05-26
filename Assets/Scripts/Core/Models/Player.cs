using System;
using System.Collections.Generic;
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
        public int PotentialMin;
        public int PotentialMax;
        public int PeakAgeStart = 25;
        public int PeakAgeEnd   = 30;
        public int ContractYears;
        public int ContractSalary;   // 单位：百万（3 = 3M）
        public int InjuryGamesRemaining;
        public bool IsFreeAgent   => TeamId == "__FA__";
        public bool IsInDraftPool => TeamId == "__DRAFT_POOL__";
        public bool IsInjured     => InjuryGamesRemaining > 0;
        public int Potential => (PotentialMin + PotentialMax) / 2;
        public PlayerAttributes Attributes = new PlayerAttributes();
        public PlayerTendencies Tendencies = new PlayerTendencies();
        public List<PlayerTrait> Traits = new List<PlayerTrait>();

        public static int GetMaxTraits(int overall)
        {
            if (overall >= 90) return 5;
            if (overall >= 80) return 4;
            if (overall >= 70) return 3;
            if (overall >= 60) return 2;
            if (overall >= 50) return 1;
            return 0;
        }

        public string GetDisplayName()
        {
            return NameFormatter.GetDisplayName(this);
        }
    }
}
