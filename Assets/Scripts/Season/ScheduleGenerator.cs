using System.Collections.Generic;
using BasketballManager.Core.Models;

namespace BasketballManager.Seasons
{
    /// <summary>
    /// 双循环赛程生成器。任意 N 支球队，每两支交手两次（一主一客）。
    /// 采用经典 round-robin 轮转表算法：N-1 轮完成第一循环（每轮所有队各打 1 场），
    /// 然后镜像主客方再跑一次。day 从 1 开始递增。N 为奇数时引入 bye 轮空。
    /// </summary>
    public static class ScheduleGenerator
    {
        public static IReadOnlyList<SeasonGame> Generate(IReadOnlyList<Team> teams)
        {
            var games = new List<SeasonGame>();
            if (teams == null || teams.Count < 2) return games;

            // 构造 working 列表，奇数队伍补一个 null（轮空标记）。
            var working = new List<Team>(teams);
            bool hasBye = working.Count % 2 != 0;
            if (hasBye) working.Add(null);

            int n = working.Count;
            int roundsPerCycle = n - 1;
            int halfSize = n / 2;
            int day = 1;

            // 第一循环。
            var rotation = new List<Team>(working);
            for (int r = 0; r < roundsPerCycle; r++)
            {
                EmitRound(rotation, halfSize, day, swapHomeAway: false, games);
                day++;
                Rotate(rotation);
            }

            // 第二循环：同样轮转，但主客互换。
            rotation = new List<Team>(working);
            for (int r = 0; r < roundsPerCycle; r++)
            {
                EmitRound(rotation, halfSize, day, swapHomeAway: true, games);
                day++;
                Rotate(rotation);
            }

            return games;
        }

        private static void EmitRound(List<Team> rotation, int halfSize, int day, bool swapHomeAway, List<SeasonGame> games)
        {
            for (int i = 0; i < halfSize; i++)
            {
                var a = rotation[i];
                var b = rotation[rotation.Count - 1 - i];
                if (a == null || b == null) continue;   // 轮空

                Team home = swapHomeAway ? b : a;
                Team away = swapHomeAway ? a : b;

                games.Add(new SeasonGame
                {
                    Day = day,
                    HomeTeamId = home.Id,
                    AwayTeamId = away.Id,
                    Status = "SCHEDULED",
                });
            }
        }

        // 经典 round-robin 轮转：固定 index 0，其余循环位移一格。
        private static void Rotate(List<Team> rotation)
        {
            if (rotation.Count <= 2) return;
            var last = rotation[rotation.Count - 1];
            for (int i = rotation.Count - 1; i > 1; i--)
            {
                rotation[i] = rotation[i - 1];
            }
            rotation[1] = last;
        }
    }
}
