using System;
using System.Collections.Generic;
using BasketballManager.Core.Models;

namespace BasketballManager.Core.Services
{
    public static class InjuryService
    {
        private static readonly Random _rng = new Random();

        // 赛后为上场球员掷伤病骰子。返回 playerId → 受伤场数的映射（仅包含新受伤球员）。
        public static Dictionary<int, int> RollInjuries(IEnumerable<PlayerBoxScore> playedStats)
        {
            var result = new Dictionary<int, int>();
            foreach (var stat in playedStats)
            {
                float prob = CalcInjuryProbability(stat.Minutes);
                if (_rng.NextDouble() < prob)
                    result[stat.PlayerId] = DrawInjuryDuration();
            }
            return result;
        }

        public static string GetInjuryLabel(int gamesRemaining)
        {
            if (gamesRemaining <= 0)  return "健康";
            if (gamesRemaining <= 3)  return $"日常伤 ({gamesRemaining}场)";
            if (gamesRemaining <= 10) return $"短期伤 ({gamesRemaining}场)";
            if (gamesRemaining <= 30) return $"中期伤 ({gamesRemaining}场)";
            return $"赛季报销 ({gamesRemaining}场)";
        }

        private static float CalcInjuryProbability(int minutes)
        {
            float rate = 0.025f;
            if (minutes > 30) rate += (minutes - 30) * 0.002f;
            return Math.Min(rate, 0.10f);
        }

        private static int DrawInjuryDuration()
        {
            int roll = _rng.Next(100);
            if (roll < 65) return _rng.Next(1, 4);    // 日常伤 1-3 场
            if (roll < 90) return _rng.Next(4, 11);   // 短期伤 4-10 场
            if (roll < 98) return _rng.Next(11, 31);  // 中期伤 11-30 场
            return _rng.Next(40, 61);                  // 赛季报销 40-60 场
        }
    }
}
