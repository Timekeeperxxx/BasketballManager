using System;
using System.Collections.Generic;
using BasketballManager.Core.Enums;
using BasketballManager.Core.Models;
using BasketballManager.Database;

namespace BasketballManager.Core.Services
{
    public sealed class PlayerDevelopmentService
    {
        private static readonly Random _rng = new Random();

        // 各位置上升期的属性权重（权重越高，本属性分得成长点越多）
        private static readonly Dictionary<Position, Dictionary<string, float>> _growthWeights =
            new Dictionary<Position, Dictionary<string, float>>
        {
            [Position.PG] = new Dictionary<string, float>
            {
                ["BallHandle"]=3f, ["Passing"]=3f, ["Drive"]=2.5f, ["Speed"]=2f,
                ["ThreePoint"]=2f, ["TwoPoint"]=1.5f, ["PerimeterDefense"]=1.5f,
                ["FreeThrow"]=1f, ["Stamina"]=1f, ["DefensiveConsistency"]=1f,
                ["Layup"]=1.5f, ["DrawFoul"]=1.5f, ["OffensiveConsistency"]=1f,
                ["CloseShot"]=0.5f, ["PostScoring"]=0.3f, ["Steal"]=1f, ["Block"]=0.3f,
                ["OffensiveRebound"]=0.3f, ["DefensiveRebound"]=0.5f, ["Strength"]=0.5f, ["InteriorDefense"]=0.3f,
            },
            [Position.SG] = new Dictionary<string, float>
            {
                ["ThreePoint"]=3f, ["TwoPoint"]=2.5f, ["Drive"]=2.5f, ["Speed"]=2f,
                ["BallHandle"]=2f, ["Layup"]=2f, ["PerimeterDefense"]=1.5f,
                ["FreeThrow"]=1.5f, ["DrawFoul"]=1.5f, ["Passing"]=1f, ["Stamina"]=1f,
                ["DefensiveConsistency"]=1f, ["OffensiveConsistency"]=1f, ["Steal"]=1f,
                ["CloseShot"]=1f, ["PostScoring"]=0.5f, ["Block"]=0.3f,
                ["OffensiveRebound"]=0.3f, ["DefensiveRebound"]=0.5f, ["Strength"]=0.5f, ["InteriorDefense"]=0.3f,
            },
            [Position.SF] = new Dictionary<string, float>
            {
                ["TwoPoint"]=2.5f, ["Drive"]=2.5f, ["PerimeterDefense"]=2f, ["Speed"]=2f,
                ["ThreePoint"]=2f, ["Layup"]=2f, ["Strength"]=1.5f,
                ["BallHandle"]=1.5f, ["FreeThrow"]=1f, ["Steal"]=1f, ["DefensiveRebound"]=1f,
                ["OffensiveConsistency"]=1f, ["DefensiveConsistency"]=1f, ["Stamina"]=1f,
                ["PostScoring"]=1f, ["CloseShot"]=1f, ["Block"]=0.8f,
                ["OffensiveRebound"]=0.8f, ["InteriorDefense"]=0.8f, ["DrawFoul"]=1f, ["Passing"]=0.5f,
            },
            [Position.PF] = new Dictionary<string, float>
            {
                ["PostScoring"]=3f, ["OffensiveRebound"]=2.5f, ["DefensiveRebound"]=2.5f, ["Strength"]=2.5f,
                ["InteriorDefense"]=2f, ["CloseShot"]=2f, ["Block"]=1.5f,
                ["Drive"]=1.5f, ["TwoPoint"]=1.5f, ["FreeThrow"]=1f, ["Stamina"]=1f,
                ["DefensiveConsistency"]=1f, ["OffensiveConsistency"]=1f,
                ["Speed"]=0.8f, ["PerimeterDefense"]=0.8f, ["ThreePoint"]=0.5f,
                ["Layup"]=1f, ["BallHandle"]=0.5f, ["DrawFoul"]=1f, ["Steal"]=0.5f, ["Passing"]=0.5f,
            },
            [Position.C] = new Dictionary<string, float>
            {
                ["PostScoring"]=3f, ["InteriorDefense"]=3f, ["Block"]=2.5f,
                ["OffensiveRebound"]=2.5f, ["DefensiveRebound"]=2.5f, ["Strength"]=2.5f,
                ["CloseShot"]=2f, ["FreeThrow"]=1f, ["Stamina"]=1f,
                ["DefensiveConsistency"]=1f, ["OffensiveConsistency"]=1f,
                ["Drive"]=0.8f, ["TwoPoint"]=0.8f, ["Speed"]=0.5f,
                ["Layup"]=1.5f, ["BallHandle"]=0.3f, ["DrawFoul"]=1f,
                ["Steal"]=0.5f, ["PerimeterDefense"]=0.5f, ["ThreePoint"]=0.3f,
                ["Passing"]=0.5f,
            },
        };

        // 衰退期：体能属性先行
        private static readonly string[] _physicalAttrs = { "Speed", "Stamina", "Strength" };

        public List<PlayerDevelopmentResult> ApplySeasonEndDevelopment(
            IReadOnlyList<Player> allPlayers,
            PlayerRepository playerRepository)
        {
            var results = new List<PlayerDevelopmentResult>();

            foreach (var player in allPlayers)
            {
                int oldOverall = player.Overall;
                int oldAge     = player.Age;

                var deltas = ComputeAttributeDeltas(player);

                // 应用变化
                ApplyDeltas(player, deltas);

                // 增龄
                player.Age++;

                // 写库（内部自动重算 Overall）
                playerRepository.UpdatePlayer(player);

                // 记录结果（仅有变化时收录）
                var result = new PlayerDevelopmentResult
                {
                    PlayerId    = player.Id,
                    PlayerName  = player.GetDisplayName(),
                    TeamId      = player.TeamId,
                    OldAge      = oldAge,
                    NewAge      = player.Age,
                    OldOverall  = oldOverall,
                    NewOverall  = player.Overall,
                    AttributeDeltas = BuildDisplayDeltas(deltas),
                };

                if (result.OverallDelta != 0 || result.AttributeDeltas.Count > 0)
                    results.Add(result);
            }

            // 按 Overall 变化降序（成长靠前，衰退靠后）
            results.Sort((a, b) => b.OverallDelta.CompareTo(a.OverallDelta));
            return results;
        }

        // ── 核心：计算各属性的变化量 ──────────────────────────────────────

        private static Dictionary<string, int> ComputeAttributeDeltas(Player p)
        {
            var deltas = new Dictionary<string, int>();
            int age           = p.Age;
            int overall       = p.Overall;
            int potential     = p.Potential;
            int peakStart     = p.PeakAgeStart;
            int peakEnd       = p.PeakAgeEnd;

            // 阶段判断
            bool isGrowing   = age < peakStart;
            bool isPeak      = age >= peakStart && age <= peakEnd;
            bool isDeclining = age > peakEnd;

            if (isGrowing)
            {
                int yearsToStart  = peakStart - age;
                // 距巅峰越远预算越大，基础值提升使新秀每赛季有明显进步
                int baseMin = yearsToStart >= 4 ? 15 : (yearsToStart >= 2 ? 7 : 2);
                int baseMax = yearsToStart >= 4 ? 28 : (yearsToStart >= 2 ? 16 : 6);
                int budget  = RandomRange(baseMin, baseMax);

                // 潜力加成：离上限越远加速越快，加强乘数使高潜力球员成长更显著
                if (overall < potential)
                {
                    int bonus = (int)Math.Min(12f, (potential - overall) * 0.4f);
                    budget += bonus;
                }
                else
                {
                    // 已达潜力上限，成长停止
                    budget = 0;
                }

                if (budget > 0)
                    DistributeGrowth(p, budget, deltas);
            }
            else if (isPeak)
            {
                // 巅峰期：每属性 ±1 随机抖动
                PeakFluctuation(p, deltas);
            }
            else // isDeclining
            {
                int yearsOver = age - peakEnd;
                int baseMin   = yearsOver >= 7 ? 7 : (yearsOver >= 4 ? 4 : 2);
                int baseMax   = yearsOver >= 7 ? 9 : (yearsOver >= 4 ? 6 : 3);
                int budget    = -RandomRange(baseMin, baseMax);  // 负值

                DistributeDecline(p, budget, deltas);
            }

            return deltas;
        }

        // 上升期：按位置权重分配成长点
        private static void DistributeGrowth(Player p, int budget, Dictionary<string, int> deltas)
        {
            var weights = _growthWeights.TryGetValue(p.Position, out var w) ? w : _growthWeights[Position.SF];
            var attrNames = GetAllAttrNames();

            // 计算归一化权重
            float totalWeight = 0;
            foreach (var name in attrNames) totalWeight += weights.TryGetValue(name, out float ww) ? ww : 0.3f;

            // 按权重随机分配
            int remaining = budget;
            var shuffled  = ShuffledCopy(attrNames);
            for (int i = 0; i < shuffled.Count && remaining > 0; i++)
            {
                string attr  = shuffled[i];
                float  wgt   = weights.TryGetValue(attr, out float ww) ? ww : 0.3f;
                int    share = (int)Math.Round(budget * (wgt / totalWeight));
                share = Math.Min(share, remaining);
                share = Math.Min(share, 6);  // 单属性每季最多 +6
                if (share <= 0) continue;

                int cur  = GetAttr(p, attr);
                int gain = Math.Min(share, 99 - cur);
                if (gain > 0)
                {
                    deltas[attr] = (deltas.TryGetValue(attr, out int ex) ? ex : 0) + gain;
                    remaining   -= gain;
                }
            }
        }

        // 巅峰期：随机抖动
        private static void PeakFluctuation(Player p, Dictionary<string, int> deltas)
        {
            var attrNames = GetAllAttrNames();
            foreach (var name in attrNames)
            {
                int roll = _rng.Next(-1, 2);  // -1, 0, +1
                if (roll == 0) continue;
                int cur   = GetAttr(p, name);
                int after = Math.Clamp(cur + roll, 40, 99);
                int delta = after - cur;
                if (delta != 0) deltas[name] = delta;
            }
        }

        // 衰退期：体能属性先衰，然后均匀分配
        private static void DistributeDecline(Player p, int budget, Dictionary<string, int> deltas)
        {
            // budget 是负数，表示总衰退量
            int remaining = budget;  // e.g. -5

            // 体能先行（Speed/Stamina/Strength 拿走约 60% 衰退量）
            int physBudget = (int)(remaining * 0.6f);  // e.g. -3
            int eachPhys   = physBudget / _physicalAttrs.Length;  // e.g. -1

            foreach (var attr in _physicalAttrs)
            {
                if (remaining >= 0) break;
                int cur   = GetAttr(p, attr);
                int delta = Math.Max(eachPhys, 40 - cur);  // 不低于 40
                if (delta < 0)
                {
                    deltas[attr] = delta;
                    remaining   -= delta;
                }
            }

            // 剩余均匀衰减技能属性
            if (remaining < 0)
            {
                var skillAttrs = new List<string>(GetAllAttrNames());
                foreach (var pa in _physicalAttrs) skillAttrs.Remove(pa);
                var shuffled = ShuffledCopy(skillAttrs);

                foreach (var attr in shuffled)
                {
                    if (remaining >= 0) break;
                    int cur   = GetAttr(p, attr);
                    int delta = Math.Max(-2, 40 - cur);  // 单属性每季最多 -2
                    if (delta < 0)
                    {
                        deltas[attr] = (deltas.TryGetValue(attr, out int ex) ? ex : 0) + delta;
                        remaining   -= delta;
                    }
                }
            }
        }

        // ── 属性读写 ──────────────────────────────────────────────────────

        private static void ApplyDeltas(Player p, Dictionary<string, int> deltas)
        {
            var a = p.Attributes;
            foreach (var kv in deltas)
            {
                int cur = GetAttr(p, kv.Key);
                SetAttr(p, kv.Key, Math.Clamp(cur + kv.Value, 40, 99));
            }
        }

        private static int GetAttr(Player p, string name)
        {
            var a = p.Attributes;
            return name switch
            {
                "TwoPoint"             => a.TwoPoint,
                "ThreePoint"           => a.ThreePoint,
                "Layup"                => a.Layup,
                "CloseShot"            => a.CloseShot,
                "PostScoring"          => a.PostScoring,
                "FreeThrow"            => a.FreeThrow,
                "Passing"              => a.Passing,
                "BallHandle"           => a.BallHandle,
                "Drive"                => a.Drive,
                "DrawFoul"             => a.DrawFoul,
                "OffensiveConsistency" => a.OffensiveConsistency,
                "PerimeterDefense"     => a.PerimeterDefense,
                "InteriorDefense"      => a.InteriorDefense,
                "Steal"                => a.Steal,
                "Block"                => a.Block,
                "OffensiveRebound"     => a.OffensiveRebound,
                "DefensiveRebound"     => a.DefensiveRebound,
                "DefensiveConsistency" => a.DefensiveConsistency,
                "Speed"                => a.Speed,
                "Strength"             => a.Strength,
                "Stamina"              => a.Stamina,
                _                      => 60,
            };
        }

        private static void SetAttr(Player p, string name, int val)
        {
            var a = p.Attributes;
            switch (name)
            {
                case "TwoPoint":             a.TwoPoint             = val; break;
                case "ThreePoint":           a.ThreePoint           = val; break;
                case "Layup":                a.Layup                = val; break;
                case "CloseShot":            a.CloseShot            = val; break;
                case "PostScoring":          a.PostScoring          = val; break;
                case "FreeThrow":            a.FreeThrow            = val; break;
                case "Passing":              a.Passing              = val; break;
                case "BallHandle":           a.BallHandle           = val; break;
                case "Drive":                a.Drive                = val; break;
                case "DrawFoul":             a.DrawFoul             = val; break;
                case "OffensiveConsistency": a.OffensiveConsistency = val; break;
                case "PerimeterDefense":     a.PerimeterDefense     = val; break;
                case "InteriorDefense":      a.InteriorDefense      = val; break;
                case "Steal":                a.Steal                = val; break;
                case "Block":                a.Block                = val; break;
                case "OffensiveRebound":     a.OffensiveRebound     = val; break;
                case "DefensiveRebound":     a.DefensiveRebound     = val; break;
                case "DefensiveConsistency": a.DefensiveConsistency = val; break;
                case "Speed":                a.Speed                = val; break;
                case "Strength":             a.Strength             = val; break;
                case "Stamina":              a.Stamina              = val; break;
            }
        }

        // ── 中文显示名映射 ────────────────────────────────────────────────

        private static readonly Dictionary<string, string> _attrDisplayNames = new Dictionary<string, string>
        {
            ["TwoPoint"]             = "中距离",
            ["ThreePoint"]           = "三分",
            ["Layup"]                = "上篮",
            ["CloseShot"]            = "近距离",
            ["PostScoring"]          = "低位",
            ["FreeThrow"]            = "罚球",
            ["Passing"]              = "传球",
            ["BallHandle"]           = "运球",
            ["Drive"]                = "突破",
            ["DrawFoul"]             = "造犯规",
            ["OffensiveConsistency"] = "进攻稳定",
            ["PerimeterDefense"]     = "外线防守",
            ["InteriorDefense"]      = "内线防守",
            ["Steal"]                = "抢断",
            ["Block"]                = "盖帽",
            ["OffensiveRebound"]     = "前场篮板",
            ["DefensiveRebound"]     = "后场篮板",
            ["DefensiveConsistency"] = "防守稳定",
            ["Speed"]                = "速度",
            ["Strength"]             = "力量",
            ["Stamina"]              = "体能",
        };

        private static Dictionary<string, int> BuildDisplayDeltas(Dictionary<string, int> raw)
        {
            var result = new Dictionary<string, int>();
            foreach (var kv in raw)
            {
                if (kv.Value == 0) continue;
                var displayName = _attrDisplayNames.TryGetValue(kv.Key, out var dn) ? dn : kv.Key;
                result[displayName] = kv.Value;
            }
            return result;
        }

        // ── 工具 ─────────────────────────────────────────────────────────

        private static List<string> GetAllAttrNames() => new List<string>
        {
            "TwoPoint","ThreePoint","Layup","CloseShot","PostScoring","FreeThrow",
            "Passing","BallHandle","Drive","DrawFoul","OffensiveConsistency",
            "PerimeterDefense","InteriorDefense","Steal","Block",
            "OffensiveRebound","DefensiveRebound","DefensiveConsistency",
            "Speed","Strength","Stamina",
        };

        private static int RandomRange(int min, int maxInclusive)
            => _rng.Next(min, maxInclusive + 1);

        private static List<T> ShuffledCopy<T>(IEnumerable<T> source)
        {
            var list = new List<T>(source);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }
    }
}
