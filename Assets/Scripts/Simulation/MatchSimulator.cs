using System;
using System.Collections.Generic;
using System.Linq;
using BasketballManager.Core.Models;
using UnityEngine;

namespace BasketballManager.Simulation
{
    public enum ShotType
    {
        ThreePoint,
        TwoPoint,
        Layup,
        CloseShot,
        Post
    }

    public sealed class MatchSimulator
    {
        private SimulationRandom _random;
        private readonly RotationScheduler _scheduler = new RotationScheduler();

        // Play-by-play state (only populated when EnablePlayByPlay is true)
        private List<PlayByPlayEvent> _pbp;
        private int _curQuarter;
        private int _clockSec;
        private MatchTeamSnapshot _homeSnap;
        private MatchTeamSnapshot _awaySnap;
        // Scoring run & milestone tracking
        private int _homeRunScore;
        private int _awayRunScore;
        private HashSet<string> _milestonesFired;

        public MatchResult Simulate(
            Team homeTeam,
            IReadOnlyList<Player> homePlayers,
            IReadOnlyDictionary<int, SimulationPlayerProfile> homeProfiles,
            Team awayTeam,
            IReadOnlyList<Player> awayPlayers,
            IReadOnlyDictionary<int, SimulationPlayerProfile> awayProfiles,
            MatchConfig config)
        {
            _random = config.UseFixedSeed ? new SimulationRandom(config.Seed) : new SimulationRandom();
            _pbp = config.EnablePlayByPlay ? new List<PlayByPlayEvent>() : null;
            _homeRunScore = 0;
            _awayRunScore = 0;
            _milestonesFired = config.EnablePlayByPlay ? new HashSet<string>() : null;

            var homeSnapshot = CreateSnapshot(homeTeam, homePlayers, homeProfiles);
            var awaySnapshot = CreateSnapshot(awayTeam, awayPlayers, awayProfiles);
            _homeSnap = homeSnapshot;
            _awaySnap = awaySnapshot;

            var result = new MatchResult
            {
                HomeTeamId = homeTeam.Id,
                AwayTeamId = awayTeam.Id,
                HomeTeamName = homeTeam.Name,
                AwayTeamName = awayTeam.Name
            };

            // Pre-game: choose starters, derive style, build rotation schedule.
            PrepareForSimulation(homeSnapshot, homePlayers, homeProfiles, config);
            PrepareForSimulation(awaySnapshot, awayPlayers, awayProfiles, config);

            int stintsPerQuarter = homeSnapshot.Rotation.StintsPerQuarter;
            int totalStints = config.QuarterCount * stintsPerQuarter;
            float gamePace = (homeSnapshot.StyleProfile.PaceModifier + awaySnapshot.StyleProfile.PaceModifier) * 0.5f;
            float avgPossPerStint = (config.MinPossessionsPerTeam + config.MaxPossessionsPerTeam) * 0.5f * gamePace / totalStints;

            for (int q = 0; q < config.QuarterCount; q++)
            {
                _curQuarter = q + 1;
                _clockSec = 720;

                int quarterStartHome = homeSnapshot.TeamStats.Points;
                int quarterStartAway = awaySnapshot.TeamStats.Points;

                LogQuarterStart(q, config.QuarterCount);

                for (int s = 0; s < stintsPerQuarter; s++)
                {
                    int stintIdx = q * stintsPerQuarter + s;
                    var homeStint = homeSnapshot.Rotation.Stints[stintIdx];
                    var awayStint = awaySnapshot.Rotation.Stints[stintIdx];

                    if (stintIdx > 0)
                    {
                        var prevHomeLineup = new System.Collections.Generic.List<Player>(homeSnapshot.CurrentLineup);
                        var prevAwayLineup = new System.Collections.Generic.List<Player>(awaySnapshot.CurrentLineup);

                        int diff = homeSnapshot.TeamStats.Points - awaySnapshot.TeamStats.Points;
                        _scheduler.AdjustLineup(homeStint, homeSnapshot, diff, homePlayers, homeProfiles, _random);
                        _scheduler.AdjustLineup(awayStint, awaySnapshot, -diff, awayPlayers, awayProfiles, _random);

                        LogSubstitutions(prevHomeLineup, homeStint.Lineup, homeSnapshot.Team.Name);
                        LogSubstitutions(prevAwayLineup, awayStint.Lineup, awaySnapshot.Team.Name);
                    }

                    homeSnapshot.CurrentLineup = homeStint.Lineup;
                    awaySnapshot.CurrentLineup = awayStint.Lineup;

                    int stintMinutes = Mathf.RoundToInt(homeSnapshot.Rotation.StintDurationMinutes);
                    foreach (var p in homeStint.Lineup)
                        homeSnapshot.PlayerStatsById[p.Id].Minutes += stintMinutes;
                    foreach (var p in awayStint.Lineup)
                        awaySnapshot.PlayerStatsById[p.Id].Minutes += stintMinutes;

                    int stintPossessions = Mathf.Max(1, Mathf.RoundToInt(_random.Range(avgPossPerStint * 0.85f, avgPossPerStint * 1.15f)));
                    for (int i = 0; i < stintPossessions && _clockSec > 0; i++)
                    {
                        SimulatePossession(homeSnapshot, awaySnapshot, q);
                        if (_clockSec > 0)
                            SimulatePossession(awaySnapshot, homeSnapshot, q);
                    }
                }

                // Mop-up：用末节 lineup 继续推进，直到时钟归零
                while (_clockSec > 0)
                {
                    SimulatePossession(homeSnapshot, awaySnapshot, q);
                    if (_clockSec > 0)
                        SimulatePossession(awaySnapshot, homeSnapshot, q);
                }

                int qH = homeSnapshot.TeamStats.Points - quarterStartHome;
                int qA = awaySnapshot.TeamStats.Points - quarterStartAway;
                result.HomeQuarterScores.Add(qH);
                result.AwayQuarterScores.Add(qA);
                result.HomeScore = homeSnapshot.TeamStats.Points;
                result.AwayScore = awaySnapshot.TeamStats.Points;

                _clockSec = 0;
                LogQuarterEnd(q, config.QuarterCount, qH, qA);
            }

            // 加时赛：4 节结束若平分，每节 5 分钟 OT，直到分出胜负。
            // 安全护栏 maxOvertimes 防止极端配对持续平局造成的无限循环。
            const int maxOvertimes = 5;
            int overtimes = 0;
            while (homeSnapshot.TeamStats.Points == awaySnapshot.TeamStats.Points && overtimes < maxOvertimes)
            {
                overtimes++;
                _curQuarter = config.QuarterCount + overtimes;
                _clockSec = 300;
                string otLabel = overtimes == 1 ? "双方激战平局，" : $"第 {overtimes} 次加时，";
                LogEvent(-1, Pick(
                    $"平局！进入第 {overtimes} 次加时赛，双方势均力敌！",
                    $"加时赛！{otLabel}5 分钟决出胜负！",
                    $"平局收场！进入加时！谁能承受压力笑到最后？"));

                int otStartHome = homeSnapshot.TeamStats.Points;
                int otStartAway = awaySnapshot.TeamStats.Points;

                SimulateOvertimePeriod(homeSnapshot, awaySnapshot, homePlayers, awayPlayers, homeProfiles, awayProfiles, config, gamePace);

                result.HomeQuarterScores.Add(homeSnapshot.TeamStats.Points - otStartHome);
                result.AwayQuarterScores.Add(awaySnapshot.TeamStats.Points - otStartAway);
                result.HomeScore = homeSnapshot.TeamStats.Points;
                result.AwayScore = awaySnapshot.TeamStats.Points;

                _clockSec = 0;
                if (homeSnapshot.TeamStats.Points != awaySnapshot.TeamStats.Points)
                    LogEvent(-1,$"加时赛 {overtimes} 结束！{_homeSnap.Team.Name} {homeSnapshot.TeamStats.Points} - {awaySnapshot.TeamStats.Points} {_awaySnap.Team.Name}");
            }

            result.HomeTeamStats = homeSnapshot.TeamStats;
            result.AwayTeamStats = awaySnapshot.TeamStats;
            result.HomePlayerStats = homeSnapshot.PlayerStatsById.Values.ToList();
            result.AwayPlayerStats = awaySnapshot.PlayerStatsById.Values.ToList();

            foreach (var kvp in homeSnapshot.Starters) result.HomeStarters[kvp.Key] = kvp.Value.GetDisplayName();
            foreach (var kvp in awaySnapshot.Starters) result.AwayStarters[kvp.Key] = kvp.Value.GetDisplayName();

            result.HomeStyleProfile = homeSnapshot.StyleProfile;
            result.AwayStyleProfile = awaySnapshot.StyleProfile;

            // 加时跑完仍平（极小概率撞 maxOvertimes 上限），按主队胜处理避免空 WinnerTeamId。
            result.WinnerTeamId = result.HomeScore >= result.AwayScore ? result.HomeTeamId : result.AwayTeamId;

            _clockSec = 0;
            LogGameEnd();

            result.PlayByPlay = _pbp ?? new List<PlayByPlayEvent>();

            ValidateResult(result);

            return result;
        }

        private MatchTeamSnapshot CreateSnapshot(Team team, IReadOnlyList<Player> players, IReadOnlyDictionary<int, SimulationPlayerProfile> profiles)
        {
            var snapshot = new MatchTeamSnapshot
            {
                Team = team,
                Players = players,
                Profiles = profiles,
                TeamStats = new TeamBoxScore
                {
                    TeamId = team.Id,
                    TeamName = team.Name
                }
            };

            var sortedPlayers = players.OrderByDescending(p => p.Overall).ToList();
            snapshot.RotationPlayers = sortedPlayers.Take(Math.Min(10, sortedPlayers.Count)).ToList();

            var roleSorted = snapshot.RotationPlayers.OrderByDescending(p => 
                p.Tendencies.ShotTendency * 0.42f + 
                p.Attributes.OffensiveConsistency * 0.30f + 
                p.Attributes.BallHandle * 0.10f + 
                p.Attributes.Drive * 0.08f + 
                p.Attributes.DrawFoul * 0.07f + 
                p.Attributes.Passing * 0.03f).ToList();

            for (int i = 0; i < roleSorted.Count; i++)
            {
                snapshot.OffensiveRoleRankByPlayerId[roleSorted[i].Id] = i + 1;
            }

            foreach (var player in players)
            {
                snapshot.PlayerStatsById[player.Id] = new PlayerBoxScore
                {
                    PlayerId = player.Id,
                    TeamId = player.TeamId,
                    PlayerName = player.GetDisplayName(),
                    Position = player.Position.ToString()
                };
                
                snapshot.PlayerGameStates[player.Id] = new PlayerGameState();
            }

            return snapshot;
        }

        // 跑一节 5 分钟加时赛：复用第 4 节末段 lineup，让 Coach AI 调度一次再上场。
        private void SimulateOvertimePeriod(
            MatchTeamSnapshot home, MatchTeamSnapshot away,
            IReadOnlyList<Player> homePlayers, IReadOnlyList<Player> awayPlayers,
            IReadOnlyDictionary<int, SimulationPlayerProfile> homeProfiles,
            IReadOnlyDictionary<int, SimulationPlayerProfile> awayProfiles,
            MatchConfig config, float gamePace)
        {
            const int overtimeMinutes = 5;

            // 常规一节 stint 数与 stint 时长，用来推算加时段的 possession 数（按 5/4 比例放大）。
            int stintsPerQuarter = home.Rotation.StintsPerQuarter;
            int totalStintsRegular = config.QuarterCount * stintsPerQuarter;
            float regularStintMinutes = home.Rotation.StintDurationMinutes;
            if (regularStintMinutes <= 0f) regularStintMinutes = 4f;

            float avgPossPerStint = (config.MinPossessionsPerTeam + config.MaxPossessionsPerTeam) * 0.5f * gamePace / totalStintsRegular;
            float otAvgPoss = avgPossPerStint * (overtimeMinutes / regularStintMinutes);

            // 用最后一个常规 stint 的 lineup 作为加时初始名单（最后一段通常是主力收尾），
            // 然后过一次教练 AI 处理犯规 ≥5 / 撞 ceiling 的强制下场。
            var homeLastStint = home.Rotation.Stints[home.Rotation.Stints.Count - 1];
            var awayLastStint = away.Rotation.Stints[away.Rotation.Stints.Count - 1];

            var homeOtStint = new RotationStint
            {
                QuarterIndex = config.QuarterCount,    // 标记为"第 5 节"（仅供日志/调试）
                StintIndexInQuarter = 0,
                StartMinute = 0f,
                DurationMinutes = overtimeMinutes,
                Lineup = new List<Player>(homeLastStint.Lineup),
            };
            var awayOtStint = new RotationStint
            {
                QuarterIndex = config.QuarterCount,
                StintIndexInQuarter = 0,
                StartMinute = 0f,
                DurationMinutes = overtimeMinutes,
                Lineup = new List<Player>(awayLastStint.Lineup),
            };

            int diff = home.TeamStats.Points - away.TeamStats.Points;
            _scheduler.AdjustLineup(homeOtStint, home, diff, homePlayers, homeProfiles, _random);
            _scheduler.AdjustLineup(awayOtStint, away, -diff, awayPlayers, awayProfiles, _random);

            home.CurrentLineup = homeOtStint.Lineup;
            away.CurrentLineup = awayOtStint.Lineup;

            foreach (var p in homeOtStint.Lineup)
                home.PlayerStatsById[p.Id].Minutes += overtimeMinutes;
            foreach (var p in awayOtStint.Lineup)
                away.PlayerStatsById[p.Id].Minutes += overtimeMinutes;

            // possession 数沿用 ±15% 随机扰动。
            int possessions = Mathf.Max(1, Mathf.RoundToInt(_random.Range(otAvgPoss * 0.85f, otAvgPoss * 1.15f)));
            // quarterIndex 传 config.QuarterCount - 1 让加时套用第 4 节末段的进攻强度调整逻辑。
            int qIdx = Mathf.Max(0, config.QuarterCount - 1);
            for (int i = 0; i < possessions && _clockSec > 0; i++)
            {
                SimulatePossession(home, away, qIdx);
                if (_clockSec > 0)
                    SimulatePossession(away, home, qIdx);
            }
            // Mop-up：确保加时赛时钟也跑满 300 秒
            while (_clockSec > 0)
            {
                SimulatePossession(home, away, qIdx);
                if (_clockSec > 0)
                    SimulatePossession(away, home, qIdx);
            }
        }

        private void PrepareForSimulation(
            MatchTeamSnapshot snapshot,
            IReadOnlyList<Player> players,
            IReadOnlyDictionary<int, SimulationPlayerProfile> profiles,
            MatchConfig config)
        {
            SelectStartersByPosition(snapshot, players, profiles);

            // Playmaker role rank — based on mpg now that minutes are no longer pre-allocated.
            var playmakerSorted = snapshot.RotationPlayers.OrderByDescending(p =>
            {
                float mpg = snapshot.GetSourceMpg(p.Id);
                return p.Attributes.Passing * 0.38f +
                       p.Tendencies.PassTendency * 0.30f +
                       p.Attributes.BallHandle * 0.14f +
                       p.Attributes.OffensiveConsistency * 0.08f +
                       mpg * 0.07f +
                       p.Tendencies.ShotTendency * 0.03f;
            }).ToList();
            snapshot.PlaymakerRoleRankByPlayerId.Clear();
            for (int i = 0; i < playmakerSorted.Count; i++)
                snapshot.PlaymakerRoleRankByPlayerId[playmakerSorted[i].Id] = i + 1;

            snapshot.StyleProfile = DeriveTeamStyleProfile(snapshot);
            snapshot.Rotation = _scheduler.Build(snapshot, players, profiles, _random, config);
        }

        private BasketballManager.Core.Enums.Position NormalizePosition(string pos)
        {
            if (string.IsNullOrEmpty(pos)) return BasketballManager.Core.Enums.Position.PG;
            if (Enum.TryParse<BasketballManager.Core.Enums.Position>(pos, out var parsed)) return parsed;
            return BasketballManager.Core.Enums.Position.PG;
        }

        private float GetPositionFit(Player player, BasketballManager.Core.Enums.Position slot)
        {
            if (player.Position == slot) return 1.0f;
            if (player.SecondaryPosition.HasValue && player.SecondaryPosition.Value == slot) return 0.85f;
            if (IsAdjacentPosition(player.Position, slot)) return 0.60f;
            return 0.0f;
        }

        private bool IsAdjacentPosition(BasketballManager.Core.Enums.Position p1, BasketballManager.Core.Enums.Position p2)
        {
            int diff = Math.Abs((int)p1 - (int)p2);
            return diff == 1;
        }

        private void SelectStartersByPosition(MatchTeamSnapshot snapshot, IReadOnlyList<Player> players, IReadOnlyDictionary<int, SimulationPlayerProfile> profiles)
        {
            var positions = new[] { 
                BasketballManager.Core.Enums.Position.PG, 
                BasketballManager.Core.Enums.Position.SG, 
                BasketballManager.Core.Enums.Position.SF, 
                BasketballManager.Core.Enums.Position.PF, 
                BasketballManager.Core.Enums.Position.C 
            };
            var availablePlayers = new HashSet<Player>(players);

            foreach (var pos in positions)
            {
                var candidates = availablePlayers
                    .Select(p => new
                    {
                        Player = p,
                        Fit = GetPositionFit(p, pos),
                        SourceMpg = profiles != null && profiles.TryGetValue(p.Id, out var profile) ? profile.SourceMpg : 20f
                    })
                    .Select(c => new
                    {
                        c.Player,
                        c.Fit,
                        c.SourceMpg,
                        Score = c.Player.Overall * 1.00f + c.SourceMpg * 0.35f + (c.Fit > 0 ? c.Fit : 0.35f) * 15.0f
                    })
                    .OrderByDescending(c => c.Fit > 0 ? c.Fit : 0.35f) // Prefer those who have a fit first
                    .ThenByDescending(c => c.Score)
                    .ThenBy(c => c.Player.Id)
                    .ToList();

                var best = candidates.FirstOrDefault();
                if (best != null)
                {
                    if (best.Fit == 0f)
                    {
                        Debug.LogWarning($"[MatchSimulator] Emergency fallback for starting {pos} on team {snapshot.Team.Id}: {best.Player.GetDisplayName()}");
                    }
                    snapshot.Starters[pos] = best.Player;
                    availablePlayers.Remove(best.Player);
                }
            }
            
            // Build rotation list prioritizing starters
            snapshot.RotationPlayers.Clear();
            snapshot.RotationPlayers.AddRange(snapshot.Starters.Values);
            
            var benchPlayers = availablePlayers.OrderByDescending(p => p.Overall).ToList();
            snapshot.RotationPlayers.AddRange(benchPlayers.Take(Math.Max(0, 10 - snapshot.RotationPlayers.Count)));
        }

        private TeamStyleProfile DeriveTeamStyleProfile(MatchTeamSnapshot snapshot)
        {
            var profile = new TeamStyleProfile();
            var rotation = snapshot.RotationPlayers;
            if (rotation.Count == 0) return profile;

            // Weight by SourceMpg so the profile reflects each player's expected role
            // before any in-game minutes are accumulated (Build is called pre-game).
            float totalMpg = 0f;
            foreach (var player in rotation)
            {
                totalMpg += snapshot.GetSourceMpg(player.Id);
            }
            if (totalMpg <= 0) totalMpg = 1f;

            float WeightedAvg(Func<Player, float> metricSelector)
            {
                float sum = 0f;
                foreach (var p in rotation)
                {
                    sum += metricSelector(p) * snapshot.GetSourceMpg(p.Id);
                }
                return sum / totalMpg;
            }

            float spacingScore = WeightedAvg(p => p.Attributes.ThreePoint * 0.6f + p.Tendencies.ThreeTendency * 0.4f);
            profile.SpacingModifier = MapToModifier(spacingScore, 74f, 30f, 0.94f, 1.08f);

            var top5 = rotation.OrderByDescending(p => snapshot.GetSourceMpg(p.Id)).Take(5).ToList();
            float top5Mpg = top5.Sum(p => snapshot.GetSourceMpg(p.Id));
            if (top5Mpg <= 0) top5Mpg = 1f;
            float gravityScore = top5.Sum(p => (p.Attributes.ThreePoint * 0.5f + p.Tendencies.ThreeTendency * 0.5f) * snapshot.GetSourceMpg(p.Id)) / top5Mpg;
            profile.ThreeGravity = MapToModifier(gravityScore, 76f, 28f, 0.94f, 1.10f);

            float rimScore = WeightedAvg(p => p.Attributes.Drive * 0.3f + p.Attributes.Layup * 0.25f + p.Attributes.DrawFoul * 0.25f + p.Tendencies.DrawFoulTendency * 0.2f);
            profile.RimPressure = MapToModifier(rimScore, 74f, 30f, 0.94f, 1.10f);

            float assistScore = WeightedAvg(p => p.Attributes.Passing * 0.55f + p.Tendencies.PassTendency * 0.45f);
            profile.AssistModifier = MapToModifier(assistScore, 72f, 30f, 0.94f, 1.10f);

            float controlScore = WeightedAvg(p => p.Attributes.BallHandle * 0.4f + p.Attributes.Passing * 0.3f + p.Attributes.OffensiveConsistency * 0.3f);
            profile.TurnoverControl = MapToModifier(controlScore, 74f, 35f, 0.94f, 1.08f);

            float orbScore = WeightedAvg(p => p.Attributes.OffensiveRebound * 0.55f + p.Tendencies.OffensiveReboundTendency * 0.45f);
            profile.OffensiveReboundModifier = MapToModifier(orbScore, 70f, 35f, 0.94f, 1.08f);

            float drbScore = WeightedAvg(p => p.Attributes.DefensiveRebound * 0.55f + p.Tendencies.DefensiveReboundTendency * 0.45f);
            profile.DefensiveReboundModifier = MapToModifier(drbScore, 72f, 35f, 0.94f, 1.08f);

            float switchScore = WeightedAvg(p => p.Attributes.PerimeterDefense * 0.45f + p.Attributes.DefensiveConsistency * 0.35f + p.Attributes.Steal * 0.2f);
            profile.SwitchDefense = MapToModifier(switchScore, 74f, 35f, 0.94f, 1.08f);

            float rimProtectionScore = WeightedAvg(p => p.Attributes.InteriorDefense * 0.45f + p.Attributes.Block * 0.35f + p.Attributes.Strength * 0.2f);
            profile.RimProtection = MapToModifier(rimProtectionScore, 74f, 35f, 0.94f, 1.10f);

            float foulScore = WeightedAvg(p => p.Attributes.DrawFoul * 0.4f + p.Attributes.Drive * 0.25f + p.Attributes.CloseShot * 0.2f + p.Attributes.PostScoring * 0.15f);
            profile.FoulPressure = MapToModifier(foulScore, 72f, 35f, 0.94f, 1.10f);

            float transitionScore = WeightedAvg(p => p.Attributes.Drive * 0.35f + p.Attributes.BallHandle * 0.25f + p.Attributes.Steal * 0.2f + p.Attributes.PerimeterDefense * 0.2f);
            profile.TransitionModifier = MapToModifier(transitionScore, 72f, 35f, 0.94f, 1.08f);

            float paceScore = WeightedAvg(p =>
                p.Attributes.Speed * 0.30f +
                p.Tendencies.DriveTendency * 0.25f +
                p.Tendencies.ThreeTendency * 0.18f +
                p.Attributes.Steal * 0.15f +
                p.Attributes.BallHandle * 0.12f);
            profile.PaceModifier = MapToModifier(paceScore, 72f, 35f, 0.92f, 1.10f);

            return profile;
        }

        private float MapToModifier(float score, float center, float scale, float min, float max)
        {
            float modifier = 1f + (score - center) / scale * 0.05f;
            return Mathf.Clamp(modifier, min, max);
        }

        private struct ShotAttemptContext
        {
            public Player AssistAttributionPlayer;
            public bool AllowSecondChanceOnMiss;
            public float MadeChanceBoost;
            public bool IsTransition;
        }

        private void SimulatePossession(MatchTeamSnapshot offense, MatchTeamSnapshot defense, int quarterIndex)
        {
            int elapsed = Mathf.RoundToInt(_random.Range(8f, 22f));
            _clockSec = Mathf.Max(0, _clockSec - elapsed);

            bool isTransition = offense.HasTransitionOpportunity;
            offense.HasTransitionOpportunity = false;

            var attacker = SelectInitiator(offense);
            if (attacker == null) return;
            var defender = SelectMatchedDefender(defense, attacker, null);

            var offStats = offense.PlayerStatsById[attacker.Id];
            var defStats = defense.PlayerStatsById[defender.Id];

            float turnoverChance = CalculateTurnoverChance(offense, defense, attacker, defender, isTransition);
            if (_random.Chance(turnoverChance))
            {
                offStats.Turnovers++;
                offense.TeamStats.Turnovers++;

                bool gotSteal = _random.Chance(0.5f);
                if (gotSteal)
                {
                    defStats.Steals++;
                    defense.TeamStats.Steals++;
                    defense.HasTransitionOpportunity = true;
                }
                bool offIsHomeTov = IsHome(offense);
                if (gotSteal)
                {
                    string stealDesc = Pick(
                        $"{defender.GetDisplayName()} 断球！{attacker.GetDisplayName()} 失误，快攻来了！",
                        $"{attacker.GetDisplayName()} 传球失误，{defender.GetDisplayName()} 抢断！",
                        $"抢断！{defender.GetDisplayName()} 拦截成功，{attacker.GetDisplayName()} 丢球！",
                        $"闪电抢断！{defender.GetDisplayName()} 预判到传球路线，将球截下！",
                        $"{defender.GetDisplayName()} 如影随形，干净利落地从 {attacker.GetDisplayName()} 手中拨球！",
                        $"球脱手！{defender.GetDisplayName()} 趁虚而入，{attacker.GetDisplayName()} 失去控球！");
                    LogEvent(defender.JerseyNumber, stealDesc,
                        Credit(attacker.Id, offIsHomeTov, tov: 1),
                        Credit(defender.Id, !offIsHomeTov, stl: 1));
                }
                else
                {
                    string tovDesc = Pick(
                        $"{attacker.GetDisplayName()} 失误出界",
                        $"{attacker.GetDisplayName()} 传球失误，球权易手",
                        $"{attacker.GetDisplayName()} 走步违例",
                        $"{attacker.GetDisplayName()} 带球撞人，进攻犯规",
                        $"{attacker.GetDisplayName()} 控球失误，球权转换",
                        $"{attacker.GetDisplayName()} 翻腕违例，裁判响哨");
                    LogEvent(attacker.JerseyNumber, tovDesc,
                        Credit(attacker.Id, offIsHomeTov, tov: 1));
                }
                return;
            }

            var finisher = SelectFinisher(offense, isTransition);
            if (finisher == null) return;
            var shotType = SelectShotType(finisher, offense, quarterIndex, isTransition);

            ResolveShotAttempt(offense, defense, finisher, shotType, quarterIndex, new ShotAttemptContext
            {
                AssistAttributionPlayer = attacker,
                AllowSecondChanceOnMiss = true,
                MadeChanceBoost = 0f,
                IsTransition = isTransition,
            });
        }

        private void SimulateSecondChanceAttempt(MatchTeamSnapshot offense, MatchTeamSnapshot defense, Player rebounder, int quarterIndex)
        {
            bool isBig = rebounder.Position == BasketballManager.Core.Enums.Position.C || rebounder.Position == BasketballManager.Core.Enums.Position.PF;
            bool selfFinish = _random.Chance(isBig ? 0.55f : 0.25f);

            Player finisher = selfFinish ? rebounder : SelectFinisher(offense, false);
            if (finisher == null) return;

            ShotType shotType;
            if (selfFinish)
            {
                float r = _random.Range(0f, 1f);
                if (isBig)
                {
                    if (r < 0.45f) shotType = ShotType.CloseShot;
                    else if (r < 0.70f) shotType = ShotType.Layup;
                    else if (r < 0.85f) shotType = ShotType.Post;
                    else shotType = _random.Chance(0.5f) ? ShotType.TwoPoint : ShotType.ThreePoint;
                }
                else
                {
                    if (r < 0.35f) shotType = ShotType.ThreePoint;
                    else if (r < 0.65f) shotType = ShotType.TwoPoint;
                    else if (r < 0.85f) shotType = ShotType.Layup;
                    else shotType = ShotType.CloseShot;
                }
            }
            else
            {
                shotType = SelectShotType(finisher, offense, quarterIndex, false);
            }

            float boost = shotType switch
            {
                ShotType.CloseShot => 0.04f,
                ShotType.Layup => 0.04f,
                ShotType.Post => 0.02f,
                ShotType.ThreePoint => 0.02f,
                ShotType.TwoPoint => 0.02f,
                _ => 0f
            };

            ResolveShotAttempt(offense, defense, finisher, shotType, quarterIndex, new ShotAttemptContext
            {
                AssistAttributionPlayer = selfFinish ? null : rebounder,
                AllowSecondChanceOnMiss = false,
                MadeChanceBoost = boost,
                IsTransition = false,
            });
        }

        private void ResolveShotAttempt(
            MatchTeamSnapshot offense, MatchTeamSnapshot defense,
            Player finisher, ShotType shotType, int quarterIndex,
            ShotAttemptContext ctx)
        {
            var finDefender = SelectMatchedDefender(defense, finisher, shotType);
            var finDefStats = defense.PlayerStatsById[finDefender.Id];

            bool offIsHome = IsHome(offense);
            bool isThreePt = shotType == ShotType.ThreePoint;

            // Determine shot zone once, used for zone stats and PBP throughout this possession
            ShotZone shotZone = SelectShotZone(finisher, shotType);

            // Block (priority over Foul/And-1)
            float blockChance = CalculateBlockChance(shotType, finisher, finDefender);
            if (_random.Chance(blockChance))
            {
                finDefStats.Blocks++;
                defense.TeamStats.Blocks++;

                RecordShotResult(offense, finisher, shotType, false, true, shotZone);

                var (rebounder, isOffensive) = ResolveRebound(offense, defense, shotType);
                string rebDesc = rebounder != null
                    ? (isOffensive ? $"，{rebounder.GetDisplayName()} 进攻篮板" : $"，{rebounder.GetDisplayName()} 防守篮板")
                    : "";

                var blkCredits = new System.Collections.Generic.List<EventStatCredit>();
                blkCredits.Add(Credit(finisher.Id, offIsHome, fga: 1, fg3a: isThreePt ? 1 : 0));
                blkCredits.Add(Credit(finDefender.Id, !offIsHome, blk: 1));
                if (rebounder != null)
                {
                    bool rebIsHome = isOffensive ? offIsHome : !offIsHome;
                    blkCredits.Add(Credit(rebounder.Id, rebIsHome, offReb: isOffensive ? 1 : 0, defReb: isOffensive ? 0 : 1));
                }
                string blkDesc = Pick(
                    $"封盖！{finDefender.GetDisplayName()} 盖掉了 {finisher.GetDisplayName()} 的投篮{rebDesc}",
                    $"{finDefender.GetDisplayName()} 送出大帽！{finisher.GetDisplayName()} 被封{rebDesc}",
                    $"{finisher.GetDisplayName()} 的出手被 {finDefender.GetDisplayName()} 拦截{rebDesc}",
                    $"大帽！{finDefender.GetDisplayName()} 卡位绝妙，{finisher.GetDisplayName()} 的投篮被挡出{rebDesc}",
                    $"拒绝入内！{finDefender.GetDisplayName()} 护框成功，将 {finisher.GetDisplayName()} 的出手弹飞{rebDesc}");
                LogEvent(finDefender.JerseyNumber, blkDesc, blkCredits.ToArray());
                if (isOffensive && rebounder != null && ctx.AllowSecondChanceOnMiss)
                    SimulateSecondChanceAttempt(offense, defense, rebounder, quarterIndex);
                return;
            }

            // Foul + And-1
            bool isFoul = ResolveFoul(finisher, finDefender, shotType, offense, quarterIndex, out int freeThrows);
            if (isFoul)
            {
                var (foulDesc, foulCredits) = ResolveFoulOutcome(offense, defense, finisher, finDefender, shotType, freeThrows, ctx.AssistAttributionPlayer);
                LogEvent(finisher.JerseyNumber, foulDesc, foulCredits);
                int foulPts = 0;
                foreach (var c in foulCredits) if (c.IsHome == offIsHome) foulPts += c.Pts;
                UpdateScoringRun(offIsHome, foulPts);
                if (foulPts > 0) CheckPlayerMilestone(finisher.Id, offIsHome, offense);
                return;
            }

            // FG
            float fgChance = CalculateFgChance(offense, defense, finisher, finDefender, shotType, ctx.IsTransition)
                             + ctx.MadeChanceBoost
                             + GetClutchTraitBonus(finisher, offense)
                             + GetCatchAndShootBonus(finisher, ctx)
                             + GetVolumeShooterBonus(finisher, offense)
                             + GetComebackKidBonus(finisher, offense);
            bool isMade = _random.Chance(fgChance);

            int fgPts = RecordShotResult(offense, finisher, shotType, isMade, false, shotZone);
            ApplyPlusMinus(offense, defense, fgPts);

            if (isMade)
            {
                Player assister = null;
                if (ctx.AssistAttributionPlayer != null)
                    assister = ResolveAssist(offense, ctx.AssistAttributionPlayer, finisher, shotType, true);
                string assisterName = assister?.GetDisplayName();

                int finPts = offense.PlayerStatsById[finisher.Id].Points;
                string zoneName = shotZone.ToChinese();
                string madeVerb;
                if (shotType == ShotType.ThreePoint)
                    madeVerb = shotZone switch
                    {
                        ShotZone.ThreeLeftCorner  => Pick($"{zoneName}，精准命中！", $"{zoneName}出手，打进！", $"{zoneName}三分，空心入网！"),
                        ShotZone.ThreeRightCorner => Pick($"{zoneName}，精准命中！", $"{zoneName}出手，打进！", $"{zoneName}三分，空心入网！"),
                        ShotZone.ThreeLeftWing    => Pick($"{zoneName}，三分命中！", $"{zoneName}拔起，远投打进！", $"{zoneName}斜线三分，落袋！"),
                        ShotZone.ThreeRightWing   => Pick($"{zoneName}，三分命中！", $"{zoneName}拔起，远投打进！", $"{zoneName}斜线三分，落袋！"),
                        _                         => Pick($"{zoneName}，空心入网！", $"{zoneName}出手，三分命中！", "弧顶三分，从容打进！", "梦幻弧线，三分落袋！"),
                    };
                else if (shotType == ShotType.TwoPoint)
                    madeVerb = shotZone switch
                    {
                        ShotZone.MidLeftCorner  => Pick($"{zoneName}，急停跳投命中！", $"{zoneName}出手，中投打进！"),
                        ShotZone.MidRightCorner => Pick($"{zoneName}，急停跳投命中！", $"{zoneName}出手，中投打进！"),
                        ShotZone.MidLeftElbow   => Pick($"{zoneName}，干净命中！", $"{zoneName}急停拔起，打进！", "左肘区后撤步，命中！"),
                        ShotZone.MidRightElbow  => Pick($"{zoneName}，干净命中！", $"{zoneName}急停拔起，打进！", "右肘区后撤步，命中！"),
                        _                       => Pick($"{zoneName}，中投得分！", "急停跳投，打进！", "高难度出手，竟然打进！"),
                    };
                else if (shotType == ShotType.Layup && ctx.IsTransition)
                    madeVerb = Pick(
                        "快攻上篮，打进！",
                        "反击成功！上篮得手！",
                        "快攻推进，轻松上篮！",
                        "无人防守，一条龙上篮！");
                else if (shotType == ShotType.Layup)
                    madeVerb = shotZone switch
                    {
                        ShotZone.CloseLeft  => Pick("左侧切入，上篮打进！", "左路突破，上篮命中！", "左侧上篮，打板得分！"),
                        ShotZone.CloseRight => Pick("右侧切入，上篮打进！", "右路突破，上篮命中！", "右侧上篮，打板得分！"),
                        _                   => Pick("中路切入，上篮打进！", "强行杀入禁区，上篮命中！", "假动作骗过防守，上篮得分！", "高抛手腕，上篮打板进！"),
                    };
                else if (shotType == ShotType.Post)
                    madeVerb = shotZone switch
                    {
                        ShotZone.CloseLeft  => Pick("左侧低位，背身命中！", "左侧背身，打板得分！", "左低位强打，转身命中！"),
                        ShotZone.CloseRight => Pick("右侧低位，背身命中！", "右侧背身，打板得分！", "右低位强打，转身命中！"),
                        _                   => Pick("中路低位，强打命中！", "背身造空间，转身得手！", "低位要位，强力得分！"),
                    };
                else
                    madeVerb = Pick(
                        "篮下命中！",
                        "近距离轻松打进！",
                        "禁区内强力完成！",
                        "近投得手！");

                // Clutch time: Q4 final 2 minutes, margin ≤ 5
                bool isClutch = _curQuarter >= 4 && _clockSec < 120 &&
                                Math.Abs(_homeSnap.TeamStats.Points - _awaySnap.TeamStats.Points) <= 5;
                string clutchPrefix = isClutch ? Pick("关键一球！", "紧要关头！", "压迫性得分！") + " " : "";

                string desc = !string.IsNullOrEmpty(assisterName)
                    ? $"{clutchPrefix}{assisterName} 传球，{finisher.GetDisplayName()} {madeVerb}（{finPts}分）比分 {ScoreStr()}"
                    : $"{clutchPrefix}{finisher.GetDisplayName()} {madeVerb}（{finPts}分）比分 {ScoreStr()}";

                var madeCredits = new System.Collections.Generic.List<EventStatCredit>();
                madeCredits.Add(Credit(finisher.Id, offIsHome, pts: fgPts, fgM: 1, fga: 1, fg3M: isThreePt ? 1 : 0, fg3a: isThreePt ? 1 : 0));
                if (assister != null)
                    madeCredits.Add(Credit(assister.Id, offIsHome, ast: 1));
                LogEvent(finisher.JerseyNumber, desc, madeCredits.ToArray());
                UpdateScoringRun(offIsHome, fgPts);
                CheckPlayerMilestone(finisher.Id, offIsHome, offense);
            }
            else
            {
                var (rebounder, isOffensive) = ResolveRebound(offense, defense, shotType);

                var missCredits = new System.Collections.Generic.List<EventStatCredit>();
                missCredits.Add(Credit(finisher.Id, offIsHome, fga: 1, fg3a: isThreePt ? 1 : 0));
                if (rebounder != null)
                {
                    bool rebIsHome = isOffensive ? offIsHome : !offIsHome;
                    missCredits.Add(Credit(rebounder.Id, rebIsHome, offReb: isOffensive ? 1 : 0, defReb: isOffensive ? 0 : 1));
                }
                // 有篮板时 missVerb 用逗号收尾，避免「！，」并排
                bool hasReb = rebounder != null;
                string missVerb = hasReb
                    ? Pick("不中，", "打铁，", "出手偏了，", "擦框而出，", "打到后板，")
                    : Pick("不中！", "打铁！", "出手偏了！", "擦框而出！", "篮筐不买账！");
                string rebDesc = hasReb
                    ? (isOffensive ? $"{rebounder.GetDisplayName()} 进攻篮板" : $"{rebounder.GetDisplayName()} 防守篮板")
                    : "";
                string shotNameM = shotZone.ToChinese();
                LogEvent(finisher.JerseyNumber, $"{finisher.GetDisplayName()} {shotNameM}{missVerb}{rebDesc}", missCredits.ToArray());
                if (isOffensive && rebounder != null && ctx.AllowSecondChanceOnMiss)
                    SimulateSecondChanceAttempt(offense, defense, rebounder, quarterIndex);
            }
        }

        private (string desc, EventStatCredit[] credits) ResolveFoulOutcome(
            MatchTeamSnapshot offense, MatchTeamSnapshot defense,
            Player finisher, Player finDefender, ShotType shotType, int freeThrows,
            Player assistAttribution)
        {
            bool offIsHome = IsHome(offense);
            var finDefStats = defense.PlayerStatsById[finDefender.Id];

            float finishQuality = shotType switch
            {
                ShotType.Layup => finisher.Attributes.Layup,
                ShotType.CloseShot => finisher.Attributes.CloseShot,
                ShotType.Post => finisher.Attributes.PostScoring,
                ShotType.TwoPoint => finisher.Attributes.TwoPoint,
                ShotType.ThreePoint => finisher.Attributes.ThreePoint,
                _ => 50f
            };

            float andOneChance = shotType switch
            {
                ShotType.Layup => 0.16f,
                ShotType.CloseShot => 0.13f,
                ShotType.Post => 0.11f,
                ShotType.TwoPoint => 0.07f,
                ShotType.ThreePoint => 0.025f,
                _ => 0.05f
            };

            if (finishQuality >= 88) andOneChance += 0.04f;
            else if (finishQuality >= 80) andOneChance += 0.025f;

            if (finDefender.Attributes.InteriorDefense >= 85 || finDefender.Attributes.PerimeterDefense >= 85)
                andOneChance -= 0.02f;

            andOneChance = Mathf.Clamp(andOneChance, 0.01f, 0.22f);

            if (_random.Chance(andOneChance))
            {
                // And-1 Success
                finDefStats.Fouls++;
                defense.TeamStats.Fouls++;

                bool isThree = shotType == ShotType.ThreePoint;
                int fgPts = RecordShotResult(offense, finisher, shotType, true, false);
                ApplyPlusMinus(offense, defense, fgPts);

                Player andOneAssister = null;
                if (assistAttribution != null)
                    andOneAssister = ResolveAssist(offense, assistAttribution, finisher, shotType, true);

                float ftChance = (finisher.Attributes.FreeThrow / 100f) * 0.85f + 0.05f;
                bool ftHit = _random.Chance(ftChance);
                int ftPts = RecordFreeThrow(offense, finisher, ftHit);
                ApplyPlusMinus(offense, defense, ftPts);

                int finPts = offense.PlayerStatsById[finisher.Id].Points;
                string andOneVerb = Pick(
                    "AND ONE！带着犯规打进！",
                    "犯规加命中！AND ONE！",
                    "硬打！带犯规得分！",
                    "And One！犯规也阻止不了他！",
                    "强势！带着防守者完成得分，AND ONE！");
                string ftResult = ftHit
                    ? Pick("加罚命中！漂亮的三分打击！", "罚球落入，完美的AND ONE！", "加罚稳稳命中！")
                    : Pick("加罚不中，但已完成主要得分。", "可惜加罚偏出。");
                string desc = $"{andOneVerb} {finisher.GetDisplayName()}（{finPts}分）{ftResult} 防守者 {finDefender.GetDisplayName()}，比分 {ScoreStr()}";

                var credits = new System.Collections.Generic.List<EventStatCredit>();
                credits.Add(Credit(finisher.Id, offIsHome,
                    pts: fgPts + ftPts, fgM: 1, fga: 1,
                    fg3M: isThree ? 1 : 0, fg3a: isThree ? 1 : 0,
                    ftM: ftHit ? 1 : 0, fta: 1));
                if (andOneAssister != null)
                    credits.Add(Credit(andOneAssister.Id, offIsHome, ast: 1));
                credits.Add(Credit(finDefender.Id, !offIsHome, pf: 1));

                return (desc, credits.ToArray());
            }
            else
            {
                // Regular shooting foul
                finDefStats.Fouls++;
                defense.TeamStats.Fouls++;

                int ftMadeCount = 0;
                for (int i = 0; i < freeThrows; i++)
                {
                    float ftChance = (finisher.Attributes.FreeThrow / 100f) * 0.85f + 0.05f;
                    bool made = _random.Chance(ftChance);
                    int ftPts = RecordFreeThrow(offense, finisher, made);
                    ApplyPlusMinus(offense, defense, ftPts);
                    if (made) ftMadeCount++;
                }

                int finPts = offense.PlayerStatsById[finisher.Id].Points;
                string foulVerb = Pick(
                    "裁判吹哨！犯规！",
                    "哨声响起！",
                    "吹犯规！",
                    "吹停！防守犯规！",
                    "哨声！犯规成立，走上罚球线！");
                string ftResultDesc = (ftMadeCount, freeThrows) switch
                {
                    (2, 2) => "两罚全中！",
                    (1, 2) => "两罚一中",
                    (0, 2) => "两罚全失！",
                    (3, 3) => "三罚全中！",
                    (2, 3) => "三罚两中",
                    (1, 3) => "三罚仅一中",
                    (0, 3) => "三罚全失！",
                    (1, 1) => "罚球命中！",
                    (0, 1) => "罚球不中。",
                    _ => $"罚球 {ftMadeCount}/{freeThrows}"
                };
                string desc = $"{foulVerb} {finDefender.GetDisplayName()} 对 {finisher.GetDisplayName()} 犯规，{ftResultDesc}（{finPts}分），比分 {ScoreStr()}";

                var credits = new EventStatCredit[]
                {
                    Credit(finisher.Id, offIsHome, pts: ftMadeCount, ftM: ftMadeCount, fta: freeThrows),
                    Credit(finDefender.Id, !offIsHome, pf: 1)
                };

                return (desc, credits);
            }
        }

        private float GetStarUsageBoost(Player player)
        {
            float scoringCore =
                player.Tendencies.ShotTendency * 0.45f
              + player.Attributes.OffensiveConsistency * 0.25f
              + player.Attributes.BallHandle * 0.10f
              + player.Attributes.Drive * 0.10f
              + player.Attributes.DrawFoul * 0.10f;

            if (scoringCore >= 90) return 1.75f;
            if (scoringCore >= 85) return 1.45f;
            if (scoringCore >= 80) return 1.25f;
            if (scoringCore >= 75) return 1.10f;
            return 1.0f;
        }

        private float GetOffensiveRoleBoost(Player player, MatchTeamSnapshot team)
        {
            int roleRank = team.OffensiveRoleRankByPlayerId.TryGetValue(player.Id, out var rank) ? rank : 99;
            float mpg = team.GetSourceMpg(player.Id);

            if (roleRank == 1)
            {
                return 1.05f;
            }
            if (roleRank == 2)
            {
                if (player.Tendencies.ShotTendency >= 70 && mpg >= 28) return 1.15f;
                return 1.08f;
            }
            if (roleRank == 3)
            {
                if (player.Tendencies.ShotTendency >= 65 && mpg >= 26) return 1.08f;
                return 1.03f;
            }
            return 1.0f;
        }

        private float GetOffBallShooterBoost(Player player)
        {
            if (player.Attributes.ThreePoint >= 82 && player.Tendencies.ThreeTendency >= 80 && player.Tendencies.ShotTendency >= 55)
            {
                return 1.25f;
            }
            if (player.Attributes.ThreePoint >= 76 && player.Tendencies.ThreeTendency >= 75 && player.Tendencies.ShotTendency >= 50)
            {
                return 1.12f;
            }
            return 1.0f;
        }

        private Player SelectInitiator(MatchTeamSnapshot offense)
        {
            var candidates = offense.CurrentLineup;
            if (candidates.Count == 0) return null;
            float totalWeight = 0;
            var weights = new float[candidates.Count];

            for (int i = 0; i < candidates.Count; i++)
            {
                var p = candidates[i];
                float mpg = offense.GetSourceMpg(p.Id);

                float w = mpg * 1.5f +
                          p.Attributes.Passing * 1.2f +
                          p.Attributes.BallHandle * 1.0f +
                          p.Tendencies.PassTendency * 0.9f +
                          p.Attributes.OffensiveConsistency * 0.4f;

                if (p.Position == BasketballManager.Core.Enums.Position.PG)
                {
                    w *= 1.2f;
                }

                if (mpg < 12)
                {
                    w *= 0.65f;
                }

                weights[i] = w;
                totalWeight += w;
            }

            float roll = _random.Range(0f, totalWeight);
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0) return candidates[i];
            }
            return candidates.Last();
        }

        private Player SelectFinisher(MatchTeamSnapshot offense, bool isTransition = false)
        {
            var candidates = offense.CurrentLineup;
            if (candidates.Count == 0) return null;
            float totalWeight = 0;
            var weights = new float[candidates.Count];

            for (int i = 0; i < candidates.Count; i++)
            {
                var p = candidates[i];
                float mpg = offense.GetSourceMpg(p.Id);

                float w = mpg * 1.8f +
                          p.Tendencies.ShotTendency * 2.2f +
                          p.Tendencies.ThreeTendency * 0.55f +
                          p.Tendencies.DriveTendency * 0.65f +
                          p.Tendencies.PostTendency * 0.45f +
                          p.Tendencies.CloseShotTendency * 0.35f +
                          p.Attributes.OffensiveConsistency * 0.65f +
                          p.Attributes.BallHandle * 0.35f +
                          p.Attributes.DrawFoul * 0.35f;

                w *= GetStarUsageBoost(p);
                w *= GetOffensiveRoleBoost(p, offense);
                w *= GetOffBallShooterBoost(p);
                w *= GetInGameFinisherAdjustment(offense, p, null);

                if (isTransition)
                {
                    if (p.Position != BasketballManager.Core.Enums.Position.C && p.Position != BasketballManager.Core.Enums.Position.PF)
                    {
                        w *= 1.15f * (p.Attributes.Drive / 80f);
                        w *= 1.08f * (p.Tendencies.ThreeTendency / 80f);
                    }
                }

                if (p.Position == BasketballManager.Core.Enums.Position.C &&
                    p.Attributes.Passing >= 85 &&
                    p.Attributes.PostScoring >= 78 &&
                    p.Attributes.OffensiveConsistency >= 82 &&
                    mpg >= 32)
                {
                    w *= 1.12f;
                }

                if (mpg < 12)
                {
                    w *= 0.55f;
                }
                else if (mpg < 18)
                {
                    w *= 0.75f;
                }

                weights[i] = w;
                totalWeight += w;
            }

            float roll = _random.Range(0f, totalWeight);
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0) return candidates[i];
            }
            return candidates.Last();
        }

        private Player SelectMatchedDefender(MatchTeamSnapshot defense, Player attacker, ShotType? shotType = null)
        {
            var rotation = defense.CurrentLineup;
            
            var primary = new List<BasketballManager.Core.Enums.Position>();
            var secondary = new List<BasketballManager.Core.Enums.Position>();

            switch (attacker.Position)
            {
                case BasketballManager.Core.Enums.Position.PG:
                    primary.Add(BasketballManager.Core.Enums.Position.PG); primary.Add(BasketballManager.Core.Enums.Position.SG);
                    secondary.Add(BasketballManager.Core.Enums.Position.SF);
                    break;
                case BasketballManager.Core.Enums.Position.SG:
                    primary.Add(BasketballManager.Core.Enums.Position.SG); primary.Add(BasketballManager.Core.Enums.Position.PG); primary.Add(BasketballManager.Core.Enums.Position.SF);
                    secondary.Add(BasketballManager.Core.Enums.Position.SF);
                    break;
                case BasketballManager.Core.Enums.Position.SF:
                    primary.Add(BasketballManager.Core.Enums.Position.SF); primary.Add(BasketballManager.Core.Enums.Position.SG); primary.Add(BasketballManager.Core.Enums.Position.PF);
                    secondary.Add(BasketballManager.Core.Enums.Position.PG); secondary.Add(BasketballManager.Core.Enums.Position.C);
                    break;
                case BasketballManager.Core.Enums.Position.PF:
                    primary.Add(BasketballManager.Core.Enums.Position.PF); primary.Add(BasketballManager.Core.Enums.Position.C); primary.Add(BasketballManager.Core.Enums.Position.SF);
                    secondary.Add(BasketballManager.Core.Enums.Position.SG);
                    break;
                case BasketballManager.Core.Enums.Position.C:
                    primary.Add(BasketballManager.Core.Enums.Position.C); primary.Add(BasketballManager.Core.Enums.Position.PF);
                    secondary.Add(BasketballManager.Core.Enums.Position.SF);
                    break;
            }

            var candidates = rotation.Where(p => primary.Contains(p.Position)).ToList();
            if (candidates.Count == 0) candidates = rotation.Where(p => secondary.Contains(p.Position)).ToList();
            if (candidates.Count == 0) candidates = rotation;

            var weights = new float[candidates.Count];
            float totalWeight = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                var p = candidates[i];
                float mpg = defense.GetSourceMpg(p.Id);
                float w = 0;

                if (shotType == ShotType.ThreePoint || shotType == ShotType.TwoPoint)
                {
                    w = mpg * 0.5f + p.Attributes.PerimeterDefense * 1.2f + p.Attributes.DefensiveConsistency * 0.6f + p.Attributes.Steal * 0.3f;
                }
                else if (shotType == ShotType.Layup || shotType == ShotType.CloseShot || shotType == ShotType.Post)
                {
                    w = mpg * 0.5f + p.Attributes.InteriorDefense * 1.1f + p.Attributes.Block * 0.8f + p.Attributes.Strength * 0.4f + p.Attributes.DefensiveConsistency * 0.6f;
                }
                else
                {
                    w = mpg * 0.5f + p.Attributes.PerimeterDefense * 0.8f + p.Attributes.Steal * 0.8f + p.Attributes.DefensiveConsistency * 0.5f;
                }

                weights[i] = w;
                totalWeight += w;
            }

            if (totalWeight <= 0) return candidates.First();

            float roll = _random.Range(0f, totalWeight);
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0) return candidates[i];
            }
            return candidates.Last();
        }

        private ShotZone SelectShotZone(Player player, ShotType shotType)
        {
            var t = player.Tendencies;
            switch (shotType)
            {
                case ShotType.ThreePoint:
                {
                    float total = t.ZoneThreeLeftCorner + t.ZoneThreeRightCorner
                                + t.ZoneThreeLeftWing   + t.ZoneThreeRightWing + t.ZoneThreeTopKey;
                    if (total <= 0) return ShotZone.ThreeTopKey;
                    float roll = _random.Range(0f, total);
                    roll -= t.ZoneThreeLeftCorner;  if (roll <= 0) return ShotZone.ThreeLeftCorner;
                    roll -= t.ZoneThreeRightCorner; if (roll <= 0) return ShotZone.ThreeRightCorner;
                    roll -= t.ZoneThreeLeftWing;    if (roll <= 0) return ShotZone.ThreeLeftWing;
                    roll -= t.ZoneThreeRightWing;   if (roll <= 0) return ShotZone.ThreeRightWing;
                    return ShotZone.ThreeTopKey;
                }
                case ShotType.TwoPoint:
                {
                    float total = t.ZoneMidLeftCorner + t.ZoneMidRightCorner
                                + t.ZoneMidLeftElbow  + t.ZoneMidRightElbow + t.ZoneMidTopKey;
                    if (total <= 0) return ShotZone.MidTopKey;
                    float roll = _random.Range(0f, total);
                    roll -= t.ZoneMidLeftCorner;  if (roll <= 0) return ShotZone.MidLeftCorner;
                    roll -= t.ZoneMidRightCorner; if (roll <= 0) return ShotZone.MidRightCorner;
                    roll -= t.ZoneMidLeftElbow;   if (roll <= 0) return ShotZone.MidLeftElbow;
                    roll -= t.ZoneMidRightElbow;  if (roll <= 0) return ShotZone.MidRightElbow;
                    return ShotZone.MidTopKey;
                }
                case ShotType.Layup:
                case ShotType.Post:
                {
                    float total = t.ZoneCloseLeft + t.ZoneCloseCenter + t.ZoneCloseRight;
                    if (total <= 0) return ShotZone.CloseCenter;
                    float roll = _random.Range(0f, total);
                    roll -= t.ZoneCloseLeft;   if (roll <= 0) return ShotZone.CloseLeft;
                    roll -= t.ZoneCloseCenter; if (roll <= 0) return ShotZone.CloseCenter;
                    return ShotZone.CloseRight;
                }
                default:
                    return ShotZone.Basket;
            }
        }

        private ShotType SelectShotType(Player player, MatchTeamSnapshot offense, int quarterIndex, bool isTransition = false)
        {
            float threeWeight = player.Tendencies.ThreeTendency * 1.65f + player.Attributes.ThreePoint * 0.65f;
            float twoPointWeight = player.Tendencies.TwoPointTendency * 0.95f + player.Attributes.TwoPoint * 0.35f;
            float driveWeight = player.Tendencies.DriveTendency * 1.00f + player.Attributes.Drive * 0.45f + player.Attributes.Layup * 0.25f;
            float closeWeight = player.Tendencies.CloseShotTendency * 0.85f + player.Attributes.CloseShot * 0.35f;
            float postWeight = player.Tendencies.PostTendency * 0.95f + player.Attributes.PostScoring * 0.45f + player.Attributes.Strength * 0.25f;

            if (player.Tendencies.ThreeTendency >= 85)
            {
                threeWeight *= 1.35f;
            }
            else if (player.Tendencies.ThreeTendency >= 75)
            {
                threeWeight *= 1.20f;
            }

            threeWeight *= GetOffBallShooterBoost(player);

            if (player.Attributes.ThreePoint < 60)
            {
                threeWeight *= 0.65f;
            }

            if (player.Position == BasketballManager.Core.Enums.Position.C && player.Attributes.ThreePoint < 70)
            {
                threeWeight *= 0.45f;
            }

            if (player.Position == BasketballManager.Core.Enums.Position.PF && player.Attributes.ThreePoint < 65)
            {
                threeWeight *= 0.70f;
            }

            threeWeight *= GetTeamThreePointAdjustment(offense);

            float lowScoreAdjustment = GetLowScoreAggressionAdjustment(offense, quarterIndex);
            driveWeight *= lowScoreAdjustment;
            closeWeight *= lowScoreAdjustment * 1.04f;
            threeWeight *= Mathf.Lerp(1.0f, 0.92f, lowScoreAdjustment - 1.0f);

            if (offense.ConsecutiveTeamThreeMisses >= 5)
            {
                threeWeight *= 0.65f;
                driveWeight *= 1.25f;
                closeWeight *= 1.18f;
                twoPointWeight *= 1.08f;
            }

            if (isTransition)
            {
                driveWeight *= 1.35f;
                closeWeight *= 1.15f;
                threeWeight *= 1.18f;
                twoPointWeight *= 0.70f;
                postWeight *= 0.20f;
            }

            float total = threeWeight + twoPointWeight + driveWeight + closeWeight + postWeight;
            if (total == 0) return ShotType.TwoPoint;

            float roll = _random.Range(0f, total);
            
            roll -= threeWeight;
            if (roll <= 0) 
            {
                return ShotType.ThreePoint;
            }
            
            offense.ConsecutiveTeamThreeMisses = Mathf.Max(0, offense.ConsecutiveTeamThreeMisses - 2);

            roll -= twoPointWeight;
            if (roll <= 0) return ShotType.TwoPoint;
            
            roll -= driveWeight;
            if (roll <= 0) return ShotType.Layup;
            
            roll -= postWeight;
            if (roll <= 0) return ShotType.Post;
            
            return ShotType.CloseShot;
        }

        private float CalculateTurnoverChance(MatchTeamSnapshot offense, MatchTeamSnapshot defense, Player attacker, Player defender, bool isTransition = false)
        {
            float off = (attacker.Attributes.BallHandle + attacker.Attributes.Passing + attacker.Attributes.OffensiveConsistency) / 3f;
            float def = (defender.Attributes.Steal + defender.Tendencies.StealTendency + defender.Attributes.PerimeterDefense) / 3f;
            
            float baseChance = 0.12f;
            float delta = (def - off) / 100f * 0.1f; 
            float result = Mathf.Clamp(baseChance + delta, 0.05f, 0.25f);
            
            result /= offense.StyleProfile.TurnoverControl;
            // 传切利器：在任何防守压力下都能精准送出传球，降低失误率
            result -= GetNeedleThreaderBonus(attacker);
            result = Mathf.Clamp(result, 0.05f, 0.25f);

            if (isTransition)
            {
                result *= 0.90f;
            }
            return result;
        }

        private float CalculateBlockChance(ShotType shotType, Player attacker, Player defender)
        {
            if (shotType == ShotType.ThreePoint) return 0.01f;
            if (shotType == ShotType.TwoPoint) return 0.03f;

            float defAbility = (defender.Attributes.Block + defender.Tendencies.BlockTendency + defender.Attributes.InteriorDefense) / 3f;
            float attAbility = shotType switch
            {
                ShotType.Layup => (attacker.Attributes.Layup + attacker.Attributes.Strength) * 0.5f,
                ShotType.CloseShot => (attacker.Attributes.CloseShot + attacker.Attributes.Strength) * 0.5f,
                ShotType.Post => (attacker.Attributes.PostScoring + attacker.Attributes.Strength) * 0.5f,
                _ => 60f
            };
            float delta = (defAbility - attAbility) / 100f;
            float baseChance = defAbility / 100f * 0.15f;
            return Mathf.Clamp(baseChance + delta * 0.06f, 0.01f, 0.15f);
        }

        private bool ResolveFoul(Player attacker, Player defender, ShotType shotType, MatchTeamSnapshot offense, int quarterIndex, out int freeThrows)
        {
            freeThrows = 0;
            float baseChance = shotType switch
            {
                ShotType.ThreePoint => 0.05f,
                ShotType.TwoPoint => 0.12f,
                ShotType.Layup => 0.25f,
                ShotType.CloseShot => 0.20f,
                ShotType.Post => 0.18f,
                _ => 0.10f
            };

            float chance = baseChance + 
                           (attacker.Attributes.DrawFoul + attacker.Tendencies.DrawFoulTendency) / 200f * 0.1f +
                           defender.Tendencies.FoulTendency / 100f * 0.1f -
                           defender.Attributes.DefensiveConsistency / 100f * 0.05f;

            chance = Mathf.Clamp(chance, 0.02f, 0.40f);

            chance *= offense.StyleProfile.FoulPressure;

            chance *= 0.72f;

            if (shotType == ShotType.ThreePoint) chance *= 0.35f;
            else if (shotType == ShotType.TwoPoint) chance *= 0.65f;
            else if (shotType == ShotType.Layup) chance *= 0.90f;
            else if (shotType == ShotType.CloseShot) chance *= 0.95f;
            else if (shotType == ShotType.Post) chance *= 0.85f;

            float drawFoulScore = attacker.Attributes.DrawFoul * 0.55f + attacker.Tendencies.DrawFoulTendency * 0.45f;
            if (drawFoulScore < 70) chance *= 0.75f;
            if (drawFoulScore >= 88) chance *= 1.10f;

            chance *= GetLowScoreAggressionAdjustment(offense, quarterIndex);

            if (_random.Chance(chance))
            {
                freeThrows = shotType == ShotType.ThreePoint ? 3 : 2;
                return true;
            }
            return false;
        }

        private int RecordShotResult(MatchTeamSnapshot offense, Player shooter, ShotType shotType, bool isMade, bool isBlocked = false, ShotZone shotZone = ShotZone.Basket)
        {
            var pStats = offense.PlayerStatsById[shooter.Id];
            var pState = offense.PlayerGameStates[shooter.Id];
            var tStats = offense.TeamStats;

            pStats.FieldGoalsAttempted++;
            pState.Fga++;
            tStats.FieldGoalsAttempted++;

            // Zone FGA always counts (blocked shots are still FGA)
            pStats.ZoneFga[(int)shotZone]++;

            bool isThree = shotType == ShotType.ThreePoint;
            if (isThree)
            {
                pStats.ThreePointersAttempted++;
                pState.ThreePa++;
                tStats.ThreePointersAttempted++;
            }

            if (isMade)
            {
                pStats.FieldGoalsMade++;
                pState.Fgm++;
                tStats.FieldGoalsMade++;

                pStats.ZoneFgm[(int)shotZone]++;

                int pts = isThree ? 3 : 2;
                pStats.Points += pts;
                pState.Points += pts;
                tStats.Points += pts;

                pState.ConsecutiveMisses = 0;

                if (isThree)
                {
                    pStats.ThreePointersMade++;
                    pState.ThreePm++;
                    tStats.ThreePointersMade++;

                    pState.ConsecutiveThreeMisses = 0;
                    offense.ConsecutiveTeamThreeMisses = 0;
                }

                return pts;
            }
            else
            {
                pState.ConsecutiveMisses++;
                if (isThree)
                {
                    pState.ConsecutiveThreeMisses++;
                    offense.ConsecutiveTeamThreeMisses++;
                }
                return 0;
            }
        }

        private int RecordFreeThrow(MatchTeamSnapshot offense, Player shooter, bool isMade)
        {
            var pStats = offense.PlayerStatsById[shooter.Id];
            var pState = offense.PlayerGameStates[shooter.Id];
            var tStats = offense.TeamStats;

            pStats.FreeThrowsAttempted++;
            tStats.FreeThrowsAttempted++;

            if (isMade)
            {
                pStats.FreeThrowsMade++;
                tStats.FreeThrowsMade++;

                pStats.Points++;
                pState.Points++;
                tStats.Points++;
                return 1;
            }
            return 0;
        }

        private void ApplyPlusMinus(MatchTeamSnapshot offense, MatchTeamSnapshot defense, int points)
        {
            if (points <= 0) return;
            foreach (var p in offense.CurrentLineup)
                offense.PlayerStatsById[p.Id].PlusMinus += points;
            foreach (var p in defense.CurrentLineup)
                defense.PlayerStatsById[p.Id].PlusMinus -= points;
        }

        private float CalculateFgChance(MatchTeamSnapshot offense, MatchTeamSnapshot defense, Player attacker, Player defender, ShotType shotType, bool isTransition = false)
        {
            float offAttr = 60f;
            float defAttr = 60f;
            float transitionBoost = 0f;

            float transMod = offense.StyleProfile.TransitionModifier;
            switch (shotType)
            {
                case ShotType.ThreePoint:
                    offAttr = attacker.Attributes.ThreePoint;
                    defAttr = defender.Attributes.PerimeterDefense;
                    if (isTransition) transitionBoost = 0.025f * transMod;
                    break;
                case ShotType.TwoPoint:
                    offAttr = attacker.Attributes.TwoPoint;
                    defAttr = defender.Attributes.PerimeterDefense;
                    if (isTransition) transitionBoost = 0.02f * transMod;
                    break;
                case ShotType.Layup:
                    offAttr = (attacker.Attributes.Layup + attacker.Attributes.Drive) / 2f;
                    defAttr = (defender.Attributes.InteriorDefense + defender.Attributes.Block + defender.Attributes.Strength) / 3f;
                    if (isTransition) transitionBoost = 0.05f * transMod;
                    break;
                case ShotType.CloseShot:
                    offAttr = attacker.Attributes.CloseShot;
                    defAttr = (defender.Attributes.InteriorDefense + defender.Attributes.Block + defender.Attributes.Strength) / 3f;
                    if (isTransition) transitionBoost = 0.03f * transMod;
                    break;
                case ShotType.Post:
                    offAttr = (attacker.Attributes.PostScoring + attacker.Attributes.Strength) / 2f;
                    defAttr = (defender.Attributes.InteriorDefense + defender.Attributes.Block + defender.Attributes.Strength) / 3f;
                    break;
            }

            float effectiveOff = offAttr * _random.Range(attacker.Attributes.OffensiveConsistency / 100f, 1.0f);
            float effectiveDef = defAttr * _random.Range(defender.Attributes.DefensiveConsistency / 100f, 1.0f);
            // 封锁专家：放大防守方有效属性，压低攻方命中率
            effectiveDef *= GetClampsMultiplier(defender);

            float delta = (effectiveOff - effectiveDef) / 100f * 0.2f;

            float chance = shotType switch
            {
                ShotType.ThreePoint => (0.35f + delta + transitionBoost) * offense.StyleProfile.SpacingModifier * offense.StyleProfile.ThreeGravity / defense.StyleProfile.SwitchDefense,
                ShotType.TwoPoint => (0.45f + delta + transitionBoost) * offense.StyleProfile.SpacingModifier / defense.StyleProfile.SwitchDefense,
                ShotType.Layup => (0.55f + delta + transitionBoost) * offense.StyleProfile.RimPressure / defense.StyleProfile.RimProtection,
                ShotType.CloseShot => (0.58f + delta + transitionBoost) * offense.StyleProfile.RimPressure / defense.StyleProfile.RimProtection,
                ShotType.Post => (0.50f + delta) / defense.StyleProfile.RimProtection,
                _ => 0.40f
            };

            // 硬汉防守：压低篮下/低位/中距离出手的命中率上限
            float intimidate = GetIntimidatorClampReduction(defender, shotType);
            return shotType switch
            {
                ShotType.ThreePoint => Mathf.Clamp(chance, 0.22f, 0.55f),
                ShotType.TwoPoint   => Mathf.Clamp(chance, 0.32f, 0.62f),
                ShotType.Layup      => Mathf.Clamp(chance, 0.38f, 0.72f - intimidate),
                ShotType.CloseShot  => Mathf.Clamp(chance, 0.40f, 0.75f - intimidate),
                ShotType.Post       => Mathf.Clamp(chance, 0.35f, 0.68f - intimidate),
                _ => 0.40f
            };
        }

        private float GetClutchTraitBonus(Player attacker, MatchTeamSnapshot offenseSnap)
        {
            if (_curQuarter < 4 || _clockSec > 300) return 0f;
            int homeScore = _homeSnap.TeamStats.Points;
            int awayScore = _awaySnap.TeamStats.Points;
            int scoreDiff = IsHome(offenseSnap) ? homeScore - awayScore : awayScore - homeScore;
            if (Math.Abs(scoreDiff) >= 5) return 0f;

            var trait = attacker.Traits?.Find(t => t.NameKey == "clutch_performer");
            if (trait == null) return 0f;

            return trait.StarLevel switch
            {
                1 => 0.015f,
                2 => 0.025f,
                3 => 0.04f,
                _ => 0f
            };
        }

        // 接球即投：接到传球直接出手，命中率小幅提升（直接改结果，保守取值）
        private float GetCatchAndShootBonus(Player shooter, ShotAttemptContext ctx)
        {
            if (ctx.AssistAttributionPlayer == null) return 0f;
            var trait = shooter.Traits?.Find(t => t.NameKey == "catch_and_shoot");
            if (trait == null) return 0f;
            return trait.StarLevel switch { 1 => 0.01f, 2 => 0.02f, 3 => 0.03f, _ => 0f };
        }

        // 量产型：出手越多越进入状态，命中率随出手数缓慢积累（直接改结果，保守取值）
        private float GetVolumeShooterBonus(Player attacker, MatchTeamSnapshot offense)
        {
            var trait = attacker.Traits?.Find(t => t.NameKey == "volume_shooter");
            if (trait == null) return 0f;
            int fga       = offense.PlayerStatsById[attacker.Id].FieldGoalsAttempted;
            int threshold = trait.StarLevel switch { 1 => 10, 2 => 8, 3 => 6, _ => 999 };
            if (fga < threshold) return 0f;
            float perFga   = trait.StarLevel switch { 1 => 0.001f, 2 => 0.002f, 3 => 0.003f, _ => 0f };
            float maxBonus = trait.StarLevel switch { 1 => 0.01f,  2 => 0.02f,  3 => 0.03f,  _ => 0f };
            return Mathf.Min((fga - threshold + 1) * perFga, maxBonus);
        }

        // 慢热型：球队落后时激发斗志，落后越大加成越高（直接改结果，保守取值）
        private float GetComebackKidBonus(Player attacker, MatchTeamSnapshot offenseSnap)
        {
            var trait = attacker.Traits?.Find(t => t.NameKey == "comeback_kid");
            if (trait == null) return 0f;
            int scoreDiff = IsHome(offenseSnap)
                ? _homeSnap.TeamStats.Points - _awaySnap.TeamStats.Points
                : _awaySnap.TeamStats.Points - _homeSnap.TeamStats.Points;
            if (scoreDiff >= -4) return 0f;
            int deficit = -scoreDiff;
            return trait.StarLevel switch
            {
                1 => 0.01f,
                2 => deficit >= 15 ? 0.025f : 0.015f,
                3 => deficit >= 15 ? 0.035f : 0.02f,
                _ => 0f
            };
        }

        // 封锁专家：放大防守方有效属性，通过 delta 公式间接压低对手命中率（改属性，适度慷慨）
        private float GetClampsMultiplier(Player defender)
        {
            var trait = defender.Traits?.Find(t => t.NameKey == "clamps");
            if (trait == null) return 1f;
            return trait.StarLevel switch { 1 => 1.12f, 2 => 1.22f, 3 => 1.32f, _ => 1f };
        }

        // 硬汉防守：气场压制，降低靠近篮筐时的命中率上限（直接改上限，保守取值）
        private float GetIntimidatorClampReduction(Player defender, ShotType shotType)
        {
            if (shotType is ShotType.ThreePoint or ShotType.TwoPoint) return 0f;
            var trait = defender.Traits?.Find(t => t.NameKey == "intimidator");
            if (trait == null) return 0f;
            return trait.StarLevel switch { 1 => 0.02f, 2 => 0.03f, 3 => 0.04f, _ => 0f };
        }

        // 传切利器：减少持球传球时的失误率（直接改失误率，保守取值）
        private float GetNeedleThreaderBonus(Player attacker)
        {
            var trait = attacker.Traits?.Find(t => t.NameKey == "needle_threader");
            if (trait == null) return 0f;
            return trait.StarLevel switch { 1 => 0.008f, 2 => 0.015f, 3 => 0.02f, _ => 0f };
        }

        private float GetBaseAssistChance(ShotType shotType)
        {
            return shotType switch
            {
                ShotType.ThreePoint => 0.78f,
                ShotType.TwoPoint => 0.52f,
                ShotType.Layup => 0.55f,
                ShotType.CloseShot => 0.48f,
                ShotType.Post => 0.38f,
                _ => 0.50f
            };
        }

        private float GetAssistedFinisherModifier(Player finisher, ShotType shotType)
        {
            float modifier = 0f;

            if (shotType == ShotType.ThreePoint)
            {
                modifier += 0.06f;
            }

            if (finisher.Attributes.ThreePoint >= 80 && 
                finisher.Tendencies.ThreeTendency >= 78 && 
                finisher.Attributes.BallHandle < 78)
            {
                modifier += 0.08f;
            }

            if (finisher.Tendencies.ThreeTendency >= 85)
            {
                modifier += 0.04f;
            }

            if (shotType == ShotType.Layup && finisher.Tendencies.CloseShotTendency >= 70)
            {
                modifier += 0.04f;
            }

            return modifier;
        }

        private float GetSelfCreationPenalty(Player finisher, ShotType shotType, Player initiator)
        {
            float penalty = 0f;

            float selfCreation =
                finisher.Attributes.BallHandle * 0.35f +
                finisher.Attributes.Drive * 0.30f +
                finisher.Tendencies.DriveTendency * 0.20f +
                finisher.Tendencies.ShotTendency * 0.15f;

            if (selfCreation >= 85 && (shotType == ShotType.Layup || shotType == ShotType.TwoPoint))
            {
                penalty += 0.12f;
            }

            if (selfCreation >= 80 && shotType == ShotType.Post)
            {
                penalty += 0.08f;
            }

            if (initiator.Id == finisher.Id)
            {
                penalty += 0.18f;
            }

            return penalty;
        }

        private float GetPlaymakerAssistBoost(Player candidate, MatchTeamSnapshot offense)
        {
            int rank = offense.PlaymakerRoleRankByPlayerId.TryGetValue(candidate.Id, out var r) ? r : 99;

            if (rank == 1) return 2.00f;
            if (rank == 2) return 1.45f;
            if (rank == 3) return 1.18f;

            return 0.82f;
        }

        private float GetSecondaryBallHandlerAssistBoost(Player candidate, MatchTeamSnapshot offense)
        {
            float handlerScore =
                candidate.Attributes.Passing * 0.40f
              + candidate.Tendencies.PassTendency * 0.35f
              + candidate.Attributes.BallHandle * 0.25f;

            int rank = offense.PlaymakerRoleRankByPlayerId.TryGetValue(candidate.Id, out var r) ? r : 99;

            if (rank == 2 && handlerScore >= 72f) return 1.12f;
            if (rank == 3 && handlerScore >= 70f) return 1.10f;
            if (rank >= 4 && handlerScore >= 76f) return 1.08f;
            if (rank >= 4 && handlerScore >= 70f) return 1.04f;

            return 1.0f;
        }

        private Player SelectAssisterCandidate(MatchTeamSnapshot offense, Player initiator, Player finisher, ShotType shotType)
        {
            var candidates = offense.CurrentLineup.Where(p => p.Id != finisher.Id).ToList();
            if (candidates.Count == 0) return null;

            var weights = new float[candidates.Count];
            float totalWeight = 0;

            // stint 模型下，5 人在场都参与助攻竞争。mpg-based 项（旧版 mpg×0.45 +
             // mpg<12/18 衰减）已移除：mpg 通过决定球员"上多少 stint"已经影响总助攻数，
             // 单回合权重再乘 mpg 因子会让主控被双重 boost、第二/第三球员被吞助攻。
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];

                float w = candidate.Attributes.Passing * 2.10f +
                          candidate.Tendencies.PassTendency * 1.70f +
                          candidate.Attributes.BallHandle * 0.30f +
                          candidate.Attributes.OffensiveConsistency * 0.20f;

                w *= GetPlaymakerAssistBoost(candidate, offense);
                w *= GetSecondaryBallHandlerAssistBoost(candidate, offense);

                if (candidate.Id == initiator.Id)
                {
                    w *= 1.75f;
                    int rank = offense.PlaymakerRoleRankByPlayerId.TryGetValue(candidate.Id, out var r) ? r : 99;
                    if (rank <= 2) w *= 1.20f;
                }

                if (candidate.Position == BasketballManager.Core.Enums.Position.PG) w *= 1.10f;

                if (candidate.Position == BasketballManager.Core.Enums.Position.C &&
                    candidate.Attributes.Passing >= 85 &&
                    candidate.Tendencies.PassTendency >= 80 &&
                    candidate.Attributes.OffensiveConsistency >= 82)
                {
                    w *= 1.45f;
                }

                if (candidate.Position == BasketballManager.Core.Enums.Position.PF &&
                    candidate.Attributes.Passing >= 78 &&
                    candidate.Tendencies.PassTendency >= 75)
                {
                    w *= 1.25f;
                }

                if (candidate.Id != initiator.Id)
                {
                    if (candidate.Attributes.Passing < 62 && candidate.Tendencies.PassTendency < 62 && candidate.Attributes.BallHandle < 68)
                    {
                        w *= 0.85f;
                    }
                    else if (candidate.Attributes.ThreePoint >= 80 && candidate.Tendencies.ThreeTendency >= 80 && candidate.Tendencies.PassTendency < 62)
                    {
                        w *= 0.88f;
                    }
                }

                weights[i] = w;
                totalWeight += w;
            }

            if (totalWeight <= 0) return candidates.First();

            float roll = _random.Range(0f, totalWeight);
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0) return candidates[i];
            }

            return candidates.Last();
        }

        private Player ResolveAssist(MatchTeamSnapshot offense, Player initiator, Player finisher, ShotType shotType, bool made)
        {
            if (!made || finisher == null) return null;

            var assister = SelectAssisterCandidate(offense, initiator, finisher, shotType);
            if (assister == null || assister.Id == finisher.Id) return null;

            float baseAssistChance = GetBaseAssistChance(shotType);
            float modifier = GetAssistedFinisherModifier(finisher, shotType);
            float penalty = GetSelfCreationPenalty(finisher, shotType, initiator);

            float passQuality = assister.Attributes.Passing * 0.60f + assister.Tendencies.PassTendency * 0.40f;
            float passBonus = 0f;

            if (passQuality >= 90) passBonus = 0.12f;
            else if (passQuality >= 84) passBonus = 0.09f;
            else if (passQuality >= 78) passBonus = 0.06f;
            else if (passQuality >= 70) passBonus = 0.03f;

            if (assister.Position == BasketballManager.Core.Enums.Position.C && assister.Attributes.Passing >= 85)
            {
                passBonus += 0.03f;
            }

            float assistChance = (baseAssistChance + modifier - penalty + passBonus) * offense.StyleProfile.AssistModifier;
            assistChance = Mathf.Clamp(assistChance, 0.25f, 0.90f);

            if (_random.Chance(assistChance))
            {
                offense.PlayerStatsById[assister.Id].Assists++;
                offense.TeamStats.Assists++;
                return assister;
            }
            return null;
        }

        private float GetEliteRebounderBoost(Player player, bool defensive)
        {
            float reboundAttribute = defensive ? player.Attributes.DefensiveRebound : player.Attributes.OffensiveRebound;
            float reboundTendency = defensive ? player.Tendencies.DefensiveReboundTendency : player.Tendencies.OffensiveReboundTendency;
            float heightScore = Mathf.Clamp((player.HeightCm - 180f) / 50f * 100f, 0f, 100f);

            float reboundCore = reboundAttribute * 0.55f + reboundTendency * 0.30f + player.Attributes.Strength * 0.10f + heightScore * 0.05f;

            if (reboundCore >= 90f) return 1.22f;
            if (reboundCore >= 84f) return 1.13f;
            if (reboundCore >= 78f) return 1.06f;
            return 1.0f;
        }

        private float GetReboundMinuteParticipationFactor(float minutes)
        {
            float factor = Mathf.Clamp(minutes / 30f, 0.22f, 1.12f);

            if (minutes < 8)
            {
                factor *= 0.62f;
            }
            else if (minutes < 12)
            {
                factor *= 0.72f;
            }
            else if (minutes < 18)
            {
                factor *= 0.86f;
            }
            else if (minutes < 24)
            {
                factor *= 0.95f;
            }

            return Mathf.Clamp(factor, 0.14f, 1.12f);
        }

        private (Player rebounder, bool isOffensive) ResolveRebound(MatchTeamSnapshot offense, MatchTeamSnapshot defense, ShotType? shotType)
        {
            float teamOffWeight = 0;
            float teamDefWeight = 0;

            float offStyleMod = offense.StyleProfile.OffensiveReboundModifier;
            float defStyleMod = defense.StyleProfile.DefensiveReboundModifier;

            // stint 模型下，每个 possession 在场 5 人平等竞争篮板。
            // 不再用 mpg 衰减权重（会重复计算——mpg 已经决定球员"上多少 stint"，
            // 这才是真正影响总篮板数的杠杆，单回合内权重再乘 mpg 因子会把主力的
            // 单回合篮板拉高、挤掉真正的内线）。
            var offWeights = new float[offense.CurrentLineup.Count];
            for (int i = 0; i < offense.CurrentLineup.Count; i++)
            {
                var p = offense.CurrentLineup[i];
                float baseWeight = p.Attributes.OffensiveRebound * 1.65f + p.Tendencies.OffensiveReboundTendency * 1.25f + p.Attributes.Strength * 0.35f + p.HeightCm * 0.16f;
                float w = baseWeight;

                float posMultiplier = 1.0f;
                if (p.Position == BasketballManager.Core.Enums.Position.PG) posMultiplier = 0.78f;
                else if (p.Position == BasketballManager.Core.Enums.Position.SG) posMultiplier = 0.82f;
                else if (p.Position == BasketballManager.Core.Enums.Position.SF) posMultiplier = 0.95f;
                else if (p.Position == BasketballManager.Core.Enums.Position.PF) posMultiplier = 1.12f;
                else if (p.Position == BasketballManager.Core.Enums.Position.C) posMultiplier = 1.25f;

                if (shotType == ShotType.ThreePoint)
                {
                    // 三分球长篮板对外线略有利，但内线仍然主导。
                    if (p.Position == BasketballManager.Core.Enums.Position.PG) posMultiplier *= 1.20f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SG) posMultiplier *= 1.15f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SF) posMultiplier *= 1.08f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.PF) posMultiplier *= 0.95f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.C) posMultiplier *= 0.90f;
                }
                else if (shotType == ShotType.TwoPoint)
                {
                    if (p.Position == BasketballManager.Core.Enums.Position.PG) posMultiplier *= 1.10f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SG) posMultiplier *= 1.08f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SF) posMultiplier *= 1.05f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.PF) posMultiplier *= 0.98f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.C) posMultiplier *= 0.94f;
                }

                w *= posMultiplier;
                w *= GetEliteRebounderBoost(p, false);

                offWeights[i] = w;
                teamOffWeight += w;
            }

            var defWeights = new float[defense.CurrentLineup.Count];
            for (int i = 0; i < defense.CurrentLineup.Count; i++)
            {
                var p = defense.CurrentLineup[i];
                float baseWeight = p.Attributes.DefensiveRebound * 1.65f + p.Tendencies.DefensiveReboundTendency * 1.25f + p.Attributes.Strength * 0.35f + p.HeightCm * 0.16f;
                float w = baseWeight;

                float posMultiplier = 1.0f;
                if (p.Position == BasketballManager.Core.Enums.Position.PG) posMultiplier = 0.78f;
                else if (p.Position == BasketballManager.Core.Enums.Position.SG) posMultiplier = 0.82f;
                else if (p.Position == BasketballManager.Core.Enums.Position.SF) posMultiplier = 0.95f;
                else if (p.Position == BasketballManager.Core.Enums.Position.PF) posMultiplier = 1.12f;
                else if (p.Position == BasketballManager.Core.Enums.Position.C) posMultiplier = 1.25f;

                if (shotType == ShotType.ThreePoint)
                {
                    if (p.Position == BasketballManager.Core.Enums.Position.PG) posMultiplier *= 1.20f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SG) posMultiplier *= 1.15f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SF) posMultiplier *= 1.08f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.PF) posMultiplier *= 0.95f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.C) posMultiplier *= 0.90f;
                }
                else if (shotType == ShotType.TwoPoint)
                {
                    if (p.Position == BasketballManager.Core.Enums.Position.PG) posMultiplier *= 1.10f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SG) posMultiplier *= 1.08f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SF) posMultiplier *= 1.05f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.PF) posMultiplier *= 0.98f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.C) posMultiplier *= 0.94f;
                }

                // 抢防守篮板能力强的后卫仍打不过真正的内线，但保证基础地位。
                if (p.Position == BasketballManager.Core.Enums.Position.PG || p.Position == BasketballManager.Core.Enums.Position.SG)
                {
                    if (p.Attributes.DefensiveRebound >= 65 || p.Tendencies.DefensiveReboundTendency >= 65) posMultiplier = Mathf.Max(posMultiplier, 1.00f);
                    else if (p.Attributes.DefensiveRebound >= 60 || p.Tendencies.DefensiveReboundTendency >= 60) posMultiplier = Mathf.Max(posMultiplier, 0.92f);
                }
                else if (p.Position == BasketballManager.Core.Enums.Position.SF)
                {
                    if (p.Attributes.DefensiveRebound >= 70 || p.Tendencies.DefensiveReboundTendency >= 70) posMultiplier = Mathf.Max(posMultiplier, 1.05f);
                    else if (p.Attributes.DefensiveRebound >= 62 || p.Tendencies.DefensiveReboundTendency >= 62) posMultiplier = Mathf.Max(posMultiplier, 1.00f);
                }

                w *= posMultiplier;
                w *= GetEliteRebounderBoost(p, true);

                defWeights[i] = w;
                teamDefWeight += w;
            }

            teamOffWeight *= offStyleMod;
            teamDefWeight *= defStyleMod;

            if (teamOffWeight + teamDefWeight == 0) return (null, false);

            float offChance = teamOffWeight / (teamOffWeight + teamDefWeight);
            offChance *= 0.58f;
            offChance = Mathf.Clamp(offChance, 0.18f, 0.38f);

            bool isOffensive = _random.Chance(offChance);

            if (isOffensive)
            {
                float roll = _random.Range(0f, teamOffWeight);
                Player rebounder = offense.CurrentLineup.Last();
                for (int i = 0; i < offense.CurrentLineup.Count; i++)
                {
                    roll -= offWeights[i];
                    if (roll <= 0)
                    {
                        rebounder = offense.CurrentLineup[i];
                        break;
                    }
                }
                offense.PlayerStatsById[rebounder.Id].OffensiveRebounds++;
                offense.TeamStats.OffensiveRebounds++;
                return (rebounder, true);
            }
            else
            {
                float roll = _random.Range(0f, teamDefWeight);
                Player rebounder = defense.CurrentLineup.Last();
                for (int i = 0; i < defense.CurrentLineup.Count; i++)
                {
                    roll -= defWeights[i];
                    if (roll <= 0)
                    {
                        rebounder = defense.CurrentLineup[i];
                        break;
                    }
                }
                defense.PlayerStatsById[rebounder.Id].DefensiveRebounds++;
                defense.TeamStats.DefensiveRebounds++;
                return (rebounder, false);
            }
        }
        private float GetInGameFinisherAdjustment(MatchTeamSnapshot offense, Player player, ShotType? shotType = null)
        {
            if (!offense.PlayerGameStates.TryGetValue(player.Id, out var state))
                return 1.0f;

            float multiplier = 1.0f;

            float fgPct = state.Fga > 0 ? (float)state.Fgm / state.Fga : 0f;
            if (state.Fga >= 8 && fgPct < 0.35f) multiplier *= 0.88f;
            if (state.Fga >= 12 && fgPct < 0.32f) multiplier *= 0.78f;

            if (state.ConsecutiveMisses >= 4) multiplier *= 0.85f;
            if (state.ConsecutiveMisses >= 6) multiplier *= 0.72f;

            if (shotType == null || shotType == ShotType.ThreePoint)
            {
                float threePct = state.ThreePa > 0 ? (float)state.ThreePm / state.ThreePa : 0f;
                if (state.ThreePa >= 6 && threePct < 0.28f) multiplier *= 0.82f;
                if (state.ConsecutiveThreeMisses >= 4) multiplier *= 0.78f;
            }

            float offensiveCore = player.Tendencies.ShotTendency * 0.35f +
                                  player.Attributes.OffensiveConsistency * 0.25f +
                                  player.Attributes.BallHandle * 0.20f +
                                  player.Overall * 0.20f;

            if (offensiveCore >= 88f) multiplier = Mathf.Max(multiplier, 0.78f);
            else if (offensiveCore >= 82f) multiplier = Mathf.Max(multiplier, 0.72f);
            else multiplier = Mathf.Max(multiplier, 0.55f);

            return Mathf.Max(multiplier, 0.10f);
        }

        private float GetTeamThreePointAdjustment(MatchTeamSnapshot offense)
        {
            var tStats = offense.TeamStats;
            if (tStats.ThreePointersAttempted < 16) return 1.0f;

            float team3p = (float)tStats.ThreePointersMade / tStats.ThreePointersAttempted;

            if (tStats.ThreePointersAttempted >= 30 && team3p < 0.26f) return 0.65f;
            if (tStats.ThreePointersAttempted >= 24 && team3p < 0.28f) return 0.74f;
            if (tStats.ThreePointersAttempted >= 16 && team3p < 0.30f) return 0.86f;

            if (tStats.ThreePointersAttempted >= 20 && team3p > 0.40f) return 1.08f;

            return 1.0f;
        }

        private float GetLowScoreAggressionAdjustment(MatchTeamSnapshot offense, int quarterIndex)
        {
            if (quarterIndex == 0) return 1.0f;

            int pts = offense.TeamStats.Points;
            if (quarterIndex == 1 && pts < 42) return 1.08f;
            if (quarterIndex == 2 && pts < 68) return 1.12f;
            if (quarterIndex == 3 && pts < 92) return 1.16f;

            return 1.0f;
        }

        private bool IsHome(MatchTeamSnapshot snap) => snap == _homeSnap;

        private static EventStatCredit Credit(int playerId, bool isHome,
            int pts = 0, int fgM = 0, int fga = 0, int fg3M = 0, int fg3a = 0,
            int ftM = 0, int fta = 0, int offReb = 0, int defReb = 0,
            int ast = 0, int stl = 0, int blk = 0, int tov = 0, int pf = 0)
        {
            return new EventStatCredit
            {
                PlayerId = playerId, IsHome = isHome,
                Pts = pts, FgM = fgM, FgA = fga, Fg3M = fg3M, Fg3A = fg3a,
                FtM = ftM, FtA = fta, OffReb = offReb, DefReb = defReb,
                Ast = ast, Stl = stl, Blk = blk, Tov = tov, PF = pf
            };
        }

        private void LogEvent(int keyJersey, string description, params EventStatCredit[] credits)
        {
            if (_pbp == null) return;
            _pbp.Add(new PlayByPlayEvent
            {
                Quarter = _curQuarter,
                ClockSeconds = _clockSec,
                JerseyNumber = keyJersey,
                HomeScore = _homeSnap?.TeamStats.Points ?? 0,
                AwayScore = _awaySnap?.TeamStats.Points ?? 0,
                Description = description,
                Credits = credits.Length > 0 ? credits : null
            });
        }

        // 从给定选项里随机选一条
        private string Pick(params string[] options) => options[_random.Range(0, options.Length)];

        private void LogSubstitutions(
            System.Collections.Generic.IReadOnlyList<Player> oldLineup,
            System.Collections.Generic.IReadOnlyList<Player> newLineup,
            string teamName)
        {
            if (_pbp == null) return;

            var oldIds = new System.Collections.Generic.HashSet<int>(oldLineup.Select(p => p.Id));
            var newIds = new System.Collections.Generic.HashSet<int>(newLineup.Select(p => p.Id));

            var outPlayers = oldLineup.Where(p => !newIds.Contains(p.Id)).ToList();
            var inPlayers  = newLineup.Where(p => !oldIds.Contains(p.Id)).ToList();

            if (outPlayers.Count == 0 && inPlayers.Count == 0) return;

            var parts = new System.Collections.Generic.List<string>();
            int pairs = Mathf.Min(outPlayers.Count, inPlayers.Count);
            for (int i = 0; i < pairs; i++)
                parts.Add($"{inPlayers[i].GetDisplayName()} 替换 {outPlayers[i].GetDisplayName()}");
            for (int i = pairs; i < inPlayers.Count; i++)
                parts.Add($"{inPlayers[i].GetDisplayName()} 上场");
            for (int i = pairs; i < outPlayers.Count; i++)
                parts.Add($"{outPlayers[i].GetDisplayName()} 下场");

            LogEvent(-1, $"换人 — {teamName}：{string.Join("，", parts)}");
        }

        private void LogQuarterStart(int q, int totalQuarters)
        {
            // Reset scoring run at each period boundary
            _homeRunScore = 0;
            _awayRunScore = 0;

            if (_pbp == null) return;
            string homeName = _homeSnap.Team.Name;
            string awayName = _awaySnap.Team.Name;
            string msg = q switch
            {
                0 => Pick(
                    $"跳球！比赛正式开始！{homeName} 主场迎战 {awayName}！",
                    $"比赛开始！{homeName} 主场坐阵，{awayName} 发起冲击！"),
                1 => Pick(
                    "第二节 开始！",
                    "第二节登场！替补球员将要接过担子！",
                    "进入第二节，双方调整轮换！"),
                2 => Pick(
                    "下半场！第三节 开球！",
                    "下半场开始！双方重整旗鼓，第三节！",
                    "中场休息结束，第三节开始！双方调整策略，重回赛场！"),
                3 => Pick(
                    "最后一节！第四节 开始，决战时刻！",
                    "第四节！最后 12 分钟，全力以赴！",
                    "进入第四节！胜负将在此决出！",
                    "最后一节！双方主力全部出动，比赛进入白热化！"),
                _ => Pick(
                    $"第 {_curQuarter} 节 开始！",
                    $"加时赛继续！进入第 {_curQuarter} 节！")
            };
            LogEvent(-1, msg);
        }

        private void LogQuarterEnd(int q, int totalQuarters, int qH, int qA)
        {
            if (_pbp == null) return;
            int h = _homeSnap.TeamStats.Points;
            int a = _awaySnap.TeamStats.Points;
            int diff = Math.Abs(h - a);
            string homeName = _homeSnap.Team.Name;
            string awayName = _awaySnap.Team.Name;

            string msg;
            if (q == 1) // halftime
            {
                string leader = h > a ? $"{homeName} 领先 {diff} 分" : h < a ? $"{awayName} 领先 {diff} 分" : "双方平分";
                string tone = diff == 0 ? "势均力敌" : diff <= 5 ? "分差极小" : diff <= 12 ? "尚在追赶范围" : "优势明显";
                msg = Pick(
                    $"上半场 结束！{leader}，{homeName} {h} - {a} {awayName}。{tone}，中场休息。",
                    $"半场哨响！比分 {h}-{a}，{leader}，双方球员回到休息室调整部署。");
            }
            else if (q == totalQuarters - 1) // last regular quarter, about to go to OT or end
            {
                msg = h == a
                    ? $"第 {q + 1} 节 结束！比分 {h}-{a} 平！将进入加时赛！"
                    : $"第 {q + 1} 节 结束 — {homeName} {h} - {a} {awayName}（本节 {qH}-{qA}）";
            }
            else
            {
                string qLeader = qH > qA ? $"本节 {homeName} 多得 {qH - qA} 分"
                               : qH < qA ? $"本节 {awayName} 多得 {qA - qH} 分"
                               : "本节平分";
                msg = Pick(
                    $"第 {q + 1} 节 结束 — 比分 {homeName} {h} - {a} {awayName}（本节 {qH}-{qA}）",
                    $"第 {q + 1} 节结束！{qLeader}，总比分 {h}-{a}。");
            }
            LogEvent(-1, msg);
        }

        private void LogGameEnd()
        {
            if (_pbp == null) return;
            int h = _homeSnap.TeamStats.Points;
            int a = _awaySnap.TeamStats.Points;
            string winner = h >= a ? _homeSnap.Team.Name : _awaySnap.Team.Name;
            string loser  = h >= a ? _awaySnap.Team.Name : _homeSnap.Team.Name;
            int wPts = Mathf.Max(h, a), lPts = Mathf.Min(h, a);
            int margin = wPts - lPts;
            string msg = margin <= 3
                ? Pick(
                    $"终场！险胜！{winner} 以 {wPts}-{lPts} 力克 {loser}，惊险 {margin} 分完成逆袭！",
                    $"比赛结束！{winner} {wPts}-{lPts} 艰难击败 {loser}，仅差 {margin} 分的惊天大战！")
                : margin <= 10
                ? Pick(
                    $"比赛结束！{winner} {wPts} - {lPts} {loser}，{winner} 获胜！",
                    $"终场哨响！{winner} 以 {wPts}-{lPts} 送走 {loser}，稳定发挥赢得胜利！")
                : margin >= 25
                ? Pick(
                    $"比赛结束！{winner} 大比分 {wPts}-{lPts} 横扫 {loser}，统治全场！",
                    $"终场！{winner} 完胜！{wPts}-{lPts} 击败 {loser}，领先优势超过 {margin} 分！")
                : $"比赛结束！{winner} {wPts} - {lPts} {loser}，{winner} 获胜！";
            LogEvent(-1, msg);
        }

        private string ScoreStr()
        {
            int h = _homeSnap?.TeamStats.Points ?? 0;
            int a = _awaySnap?.TeamStats.Points ?? 0;
            return $"{h}-{a}";
        }

        private static string ShotTypeChinese(ShotType shotType) => shotType switch
        {
            ShotType.ThreePoint => "三分",
            ShotType.TwoPoint   => "中投",
            ShotType.Layup      => "上篮",
            ShotType.CloseShot  => "近投",
            ShotType.Post       => "背身",
            _                   => "投篮"
        };

        // -------- Scoring run & milestone helpers --------

        private void UpdateScoringRun(bool offIsHome, int pts)
        {
            if (_pbp == null || pts <= 0) return;
            if (offIsHome) { _homeRunScore += pts; _awayRunScore = 0; }
            else           { _awayRunScore += pts; _homeRunScore = 0; }
            int run = offIsHome ? _homeRunScore : _awayRunScore;
            string runTeam = offIsHome ? _homeSnap.Team.Name : _awaySnap.Team.Name;
            string runMsg = run switch
            {
                6  => Pick($"连续攻势！{runTeam} 打出 6-0！", $"{runTeam} 连得 6 分，势头渐起！"),
                8  => Pick($"势不可挡！{runTeam} 已连得 8 分！", $"8-0！{runTeam} 把对手打得无法得分！"),
                10 => Pick($"10-0！{runTeam} 的进攻势头锐不可当！", $"惊人攻势！{runTeam} 连得 10 分，对手急需叫停！"),
                12 => Pick($"12-0！{runTeam} 统治这段时间！", $"停止出血！{runTeam} 打出恐怖 12-0 攻势！"),
                15 => Pick($"15-0！{runTeam} 完全控制比赛节奏！", $"这段时间 {runTeam} 横扫一切，连得 15 分！"),
                20 => $"不可思议！{runTeam} 竟然连得 20 分！对手急需喘息！",
                _  => null
            };
            if (runMsg != null) LogEvent(-1, runMsg);
        }

        private void CheckPlayerMilestone(int playerId, bool isHome, MatchTeamSnapshot snap)
        {
            if (_pbp == null || _milestonesFired == null) return;
            if (!snap.PlayerStatsById.TryGetValue(playerId, out var stats)) return;
            var player = snap.Players?.FirstOrDefault(p => p.Id == playerId);
            string name = player?.GetDisplayName() ?? playerId.ToString();
            foreach (int m in new[] { 20, 30, 40 })
            {
                string key = $"{playerId}:{m}";
                if (stats.Points >= m && !_milestonesFired.Contains(key))
                {
                    _milestonesFired.Add(key);
                    string msg = m switch
                    {
                        20 => Pick($"里程碑！{name} 本场已得 20 分！", $"{name} 砍下 20 分，成为比赛最亮眼的球星！"),
                        30 => Pick($"爆发！{name} 本场已拿下 30 分！", $"统治级演出！{name} 30 分登顶全场得分榜！"),
                        40 => $"传奇演出！{name} 本场已得 40 分！全场震撼！",
                        _  => null
                    };
                    if (msg != null) LogEvent(-1, msg);
                    break; // only one milestone per possession
                }
            }
        }

        private void ValidateResult(MatchResult result)
        {
            ValidateTeamStats(result.HomeTeamName, result.HomeTeamStats, result.HomePlayerStats);
            ValidateTeamStats(result.AwayTeamName, result.AwayTeamStats, result.AwayPlayerStats);
            
            int homeQuarterSum = result.HomeQuarterScores.Sum();
            if (homeQuarterSum != result.HomeScore)
            {
                Debug.LogWarning($"[Validation] {result.HomeTeamName} quarter sum {homeQuarterSum} != total score {result.HomeScore}");
            }
            
            int awayQuarterSum = result.AwayQuarterScores.Sum();
            if (awayQuarterSum != result.AwayScore)
            {
                Debug.LogWarning($"[Validation] {result.AwayTeamName} quarter sum {awayQuarterSum} != total score {result.AwayScore}");
            }
        }

        private void ValidateTeamStats(string teamName, TeamBoxScore teamStats, List<PlayerBoxScore> players)
        {
            int pPoints = players.Sum(p => p.Points);
            if (pPoints != teamStats.Points) Debug.LogWarning($"[Validation] {teamName}: Player Points {pPoints} != Team Points {teamStats.Points}");

            int pFgm = players.Sum(p => p.FieldGoalsMade);
            if (pFgm != teamStats.FieldGoalsMade) Debug.LogWarning($"[Validation] {teamName}: Player FGM {pFgm} != Team FGM {teamStats.FieldGoalsMade}");

            int pFga = players.Sum(p => p.FieldGoalsAttempted);
            if (pFga != teamStats.FieldGoalsAttempted) Debug.LogWarning($"[Validation] {teamName}: Player FGA {pFga} != Team FGA {teamStats.FieldGoalsAttempted}");
            
            if (teamStats.FieldGoalsMade > teamStats.FieldGoalsAttempted) Debug.LogWarning($"[Validation] {teamName}: FGM > FGA");

            int p3pm = players.Sum(p => p.ThreePointersMade);
            if (p3pm != teamStats.ThreePointersMade) Debug.LogWarning($"[Validation] {teamName}: Player 3PM {p3pm} != Team 3PM {teamStats.ThreePointersMade}");

            int p3pa = players.Sum(p => p.ThreePointersAttempted);
            if (p3pa != teamStats.ThreePointersAttempted) Debug.LogWarning($"[Validation] {teamName}: Player 3PA {p3pa} != Team 3PA {teamStats.ThreePointersAttempted}");
            
            if (teamStats.ThreePointersMade > teamStats.ThreePointersAttempted) Debug.LogWarning($"[Validation] {teamName}: 3PM > 3PA");

            int pFtm = players.Sum(p => p.FreeThrowsMade);
            if (pFtm != teamStats.FreeThrowsMade) Debug.LogWarning($"[Validation] {teamName}: Player FTM {pFtm} != Team FTM {teamStats.FreeThrowsMade}");

            int pFta = players.Sum(p => p.FreeThrowsAttempted);
            if (pFta != teamStats.FreeThrowsAttempted) Debug.LogWarning($"[Validation] {teamName}: Player FTA {pFta} != Team FTA {teamStats.FreeThrowsAttempted}");
            
            if (teamStats.FreeThrowsMade > teamStats.FreeThrowsAttempted) Debug.LogWarning($"[Validation] {teamName}: FTM > FTA");

            int pOreb = players.Sum(p => p.OffensiveRebounds);
            if (pOreb != teamStats.OffensiveRebounds) Debug.LogWarning($"[Validation] {teamName}: Player OREB {pOreb} != Team OREB {teamStats.OffensiveRebounds}");

            int pDreb = players.Sum(p => p.DefensiveRebounds);
            if (pDreb != teamStats.DefensiveRebounds) Debug.LogWarning($"[Validation] {teamName}: Player DREB {pDreb} != Team DREB {teamStats.DefensiveRebounds}");

            foreach (var p in players)
            {
                if (p.Rebounds != p.OffensiveRebounds + p.DefensiveRebounds)
                    Debug.LogWarning($"[Validation] {teamName} Player {p.PlayerName}: Rebounds {p.Rebounds} != ORB + DRB");
            }

            int pReb = players.Sum(p => p.Rebounds);
            if (pReb != teamStats.Rebounds) Debug.LogWarning($"[Validation] {teamName}: Player REB {pReb} != Team REB {teamStats.Rebounds}");
        }
    }
}
