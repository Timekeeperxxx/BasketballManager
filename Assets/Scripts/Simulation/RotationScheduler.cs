using System;
using System.Collections.Generic;
using System.Linq;
using BasketballManager.Core.Enums;
using BasketballManager.Core.Models;
using UnityEngine;

namespace BasketballManager.Simulation
{
    public sealed class RotationStint
    {
        public int QuarterIndex;
        public int StintIndexInQuarter;
        public float StartMinute;
        public float DurationMinutes;
        public List<Player> Lineup = new List<Player>();
    }

    public sealed class RotationSchedule
    {
        public List<RotationStint> Stints = new List<RotationStint>();
        public float StintDurationMinutes = 4f;
        public int StintsPerQuarter = 3;
    }

    /// <summary>
    /// 根据每个球员的 SourceMpg / MinuteFloor / MinuteCeiling（带高斯式扰动）
    /// 为一支球队生成 12 个 stint × 5 人的轮换名单，并在每个 stint 切换时
    /// 由教练 AI 动态调整（犯规麻烦、累计上场撞 ceiling、比分差距休主力等）。
    /// </summary>
    public sealed class RotationScheduler
    {
        private static readonly Position[] PositionOrder =
        {
            Position.PG, Position.SG, Position.SF, Position.PF, Position.C
        };

        // 按 mpg 角色档分的"想上场 stint 偏好"加成表。索引 = stint 0..11
        // （4 节 × 3 stint）。Starter 偏好每节首末段；六人首发承接 starter
        // 休息时段；深替补只在垃圾时间露面。
        private static readonly float[] StarterPattern = {
            +20, -10, +20,   // Q1
            -15, +10, +20,   // Q2
            +20, -10, +20,   // Q3
            -15, +10, +20,   // Q4
        };
        private static readonly float[] SixthManPattern = {
            -5, +18, +5,
            +18, +5, -5,
            -5, +18, +5,
            +18, +5, -5,
        };
        private static readonly float[] DeepBenchPattern = {
            -10, +12, -5,
            +12, -5, -10,
            -10, +12, -5,
            +12, -5, -10,
        };

        public RotationSchedule Build(
            MatchTeamSnapshot snapshot,
            IReadOnlyList<Player> players,
            IReadOnlyDictionary<int, SimulationPlayerProfile> profiles,
            SimulationRandom random,
            MatchConfig config)
        {
            int stintsPerQuarter = 3;
            int totalStints = config.QuarterCount * stintsPerQuarter;
            float stintMinutes = config.MinutesPerQuarter / (float)stintsPerQuarter;

            // 根据 mpg + 高斯扰动算每人本场目标 stint 数，受 floor/ceiling 限制。
            var targetStints = new Dictionary<int, int>();
            var mpgByPlayer = new Dictionary<int, float>();
            foreach (var p in players)
            {
                float mpg, floor, ceiling;
                if (profiles != null && profiles.TryGetValue(p.Id, out var prof))
                {
                    // 用两个均匀分布之和近似 N(0, ~1.4) 高斯扰动（中心极限定理简化版）。
                    float jitter = ((float)random.NextDouble() + (float)random.NextDouble() - 1f) * 2.5f;
                    mpg = Mathf.Clamp(prof.SourceMpg + jitter, prof.MinuteFloor, prof.MinuteCeiling);
                    floor = prof.MinuteFloor;
                    ceiling = prof.MinuteCeiling;
                }
                else
                {
                    mpg = 20f;
                    floor = 0f;
                    ceiling = 38f;
                }
                mpgByPlayer[p.Id] = mpg;
                int target = Mathf.Clamp(Mathf.RoundToInt(mpg / stintMinutes), 0, totalStints);
                targetStints[p.Id] = target;
            }

            // 把目标 stint 数归一到 5 人 × 总 stint 数（默认 60）。
            int requiredTotal = totalStints * 5;
            NormalizeTargets(targetStints, mpgByPlayer, requiredTotal, totalStints);

            // 贪心填充：每个 stint 按 PG/SG/SF/PF/C 顺序选当时最优的球员。
            var schedule = new RotationSchedule
            {
                StintDurationMinutes = stintMinutes,
                StintsPerQuarter = stintsPerQuarter,
            };

            for (int stintIdx = 0; stintIdx < totalStints; stintIdx++)
            {
                int q = stintIdx / stintsPerQuarter;
                int s = stintIdx % stintsPerQuarter;
                var stint = new RotationStint
                {
                    QuarterIndex = q,
                    StintIndexInQuarter = s,
                    StartMinute = stintIdx * stintMinutes,
                    DurationMinutes = stintMinutes,
                };

                var used = new HashSet<int>();
                foreach (var pos in PositionOrder)
                {
                    var pick = ChooseSlotPlayer(pos, stintIdx, players, targetStints, mpgByPlayer, used);
                    if (pick == null)
                    {
                        // 兜底：忽略位置 fit，挑还有剩余 stint 配额的人。
                        pick = players
                            .Where(p => !used.Contains(p.Id) && targetStints[p.Id] > 0)
                            .OrderByDescending(p => mpgByPlayer[p.Id])
                            .FirstOrDefault();
                        if (pick != null)
                        {
                            Debug.LogWarning($"[RotationScheduler] Emergency fill stint {stintIdx} pos {pos} on team {snapshot.Team.Id} → {pick.GetDisplayName()}");
                        }
                    }
                    if (pick != null)
                    {
                        stint.Lineup.Add(pick);
                        used.Add(pick.Id);
                        targetStints[pick.Id]--;
                    }
                }

                schedule.Stints.Add(stint);
            }

            return schedule;
        }

        /// <summary>
        /// 每个 stint 切换时（首 stint 跳过）调用，由教练 AI 处理强制下场情形：
        /// 犯规麻烦、累计上场撞 ceiling，并按位置 fit 选最佳替补补位。
        /// </summary>
        public void AdjustLineup(
            RotationStint stint, MatchTeamSnapshot snapshot, int scoreDiff,
            IReadOnlyList<Player> players,
            IReadOnlyDictionary<int, SimulationPlayerProfile> profiles,
            SimulationRandom random)
        {
            var forcedOut = new List<Player>();
            foreach (var p in stint.Lineup)
            {
                int fouls = snapshot.PlayerStatsById.TryGetValue(p.Id, out var ps) ? ps.Fouls : 0;
                int mins = snapshot.PlayerStatsById[p.Id].Minutes;
                float ceiling = profiles != null && profiles.TryGetValue(p.Id, out var prof) ? prof.MinuteCeiling : 40f;
                if (fouls >= 5 || mins >= ceiling)
                {
                    forcedOut.Add(p);
                }
            }

            // 比分差距规则：大胜方提前休主力，落后方咬牙坚持。
            // 简化实现：第 4 节比分差 ≥ 18 时，让 mpg 最高的还在场主力下场休息。
            bool blowoutGarbage = stint.QuarterIndex == 3 && Math.Abs(scoreDiff) >= 18;
            if (blowoutGarbage && scoreDiff > 0)
            {
                // 大胜方：让仍在场的 mpg 最高主力下场。
                var resting = stint.Lineup
                    .OrderByDescending(p => profiles != null && profiles.TryGetValue(p.Id, out var pr) ? pr.SourceMpg : 20f)
                    .FirstOrDefault(p => !forcedOut.Contains(p));
                if (resting != null) forcedOut.Add(resting);
            }

            foreach (var outPlayer in forcedOut)
            {
                Position slotPos = outPlayer.Position;
                var replacement = players
                    .Where(p => p.Id != outPlayer.Id && !stint.Lineup.Any(l => l.Id == p.Id))
                    .Where(p =>
                    {
                        int fouls = snapshot.PlayerStatsById.TryGetValue(p.Id, out var ps) ? ps.Fouls : 0;
                        int mins = snapshot.PlayerStatsById[p.Id].Minutes;
                        float ceiling = profiles != null && profiles.TryGetValue(p.Id, out var prof) ? prof.MinuteCeiling : 40f;
                        return fouls < 5 && mins < ceiling && GetPositionFit(p, slotPos) > 0f;
                    })
                    .OrderByDescending(p =>
                    {
                        float fit = GetPositionFit(p, slotPos);
                        float mpg = profiles != null && profiles.TryGetValue(p.Id, out var prof) ? prof.SourceMpg : 20f;
                        return fit * 100f + mpg;
                    })
                    .FirstOrDefault();

                if (replacement == null)
                {
                    // 最后兜底：忽略位置 fit。
                    replacement = players
                        .Where(p => p.Id != outPlayer.Id && !stint.Lineup.Any(l => l.Id == p.Id))
                        .Where(p =>
                        {
                            int fouls = snapshot.PlayerStatsById.TryGetValue(p.Id, out var ps) ? ps.Fouls : 0;
                            return fouls < 5;
                        })
                        .OrderByDescending(p => profiles != null && profiles.TryGetValue(p.Id, out var prof) ? prof.SourceMpg : 20f)
                        .FirstOrDefault();
                }

                if (replacement != null)
                {
                    stint.Lineup.Remove(outPlayer);
                    stint.Lineup.Add(replacement);
                }
            }
        }

        private Player ChooseSlotPlayer(
            Position pos, int stintIdx,
            IReadOnlyList<Player> players,
            Dictionary<int, int> targetStints,
            Dictionary<int, float> mpgByPlayer,
            HashSet<int> used)
        {
            Player best = null;
            float bestScore = float.MinValue;

            foreach (var p in players)
            {
                if (used.Contains(p.Id)) continue;
                if (targetStints[p.Id] <= 0) continue;

                float fit = GetPositionFit(p, pos);
                if (fit <= 0f) continue;

                float mpg = mpgByPlayer[p.Id];
                float patternBonus = GetPatternBonus(mpg, stintIdx);
                int remaining = targetStints[p.Id];

                float score = fit * 100f + mpg * 1.5f + patternBonus + remaining * 3f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }

            return best;
        }

        private float GetPatternBonus(float mpg, int stintIdx)
        {
            if (mpg >= 28f) return StarterPattern[stintIdx % StarterPattern.Length];
            if (mpg >= 18f) return SixthManPattern[stintIdx % SixthManPattern.Length];
            return DeepBenchPattern[stintIdx % DeepBenchPattern.Length];
        }

        private float GetPositionFit(Player p, Position slot)
        {
            if (p.Position == slot) return 1.0f;
            if (p.SecondaryPosition.HasValue && p.SecondaryPosition.Value == slot) return 0.85f;
            if (Math.Abs((int)p.Position - (int)slot) == 1) return 0.60f;
            return 0f;
        }

        private void NormalizeTargets(
            Dictionary<int, int> targetStints,
            Dictionary<int, float> mpgByPlayer,
            int requiredTotal, int totalStints)
        {
            int sum = targetStints.Values.Sum();
            if (sum == requiredTotal) return;

            int diff = requiredTotal - sum;
            var ordered = targetStints.Keys
                .OrderByDescending(pid => mpgByPlayer[pid])
                .ToList();

            int idx = 0;
            int safety = 0;
            while (diff != 0 && safety < 1000 && ordered.Count > 0)
            {
                int pid = ordered[idx % ordered.Count];
                int cur = targetStints[pid];
                if (diff > 0 && cur < totalStints)
                {
                    targetStints[pid]++;
                    diff--;
                }
                else if (diff < 0 && cur > 0)
                {
                    targetStints[pid]--;
                    diff++;
                }
                idx++;
                safety++;
            }
        }
    }
}
