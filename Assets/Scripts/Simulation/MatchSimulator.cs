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

            var homeSnapshot = CreateSnapshot(homeTeam, homePlayers);
            var awaySnapshot = CreateSnapshot(awayTeam, awayPlayers);

            var result = new MatchResult
            {
                HomeTeamId = homeTeam.Id,
                AwayTeamId = awayTeam.Id,
                HomeTeamName = homeTeam.Name,
                AwayTeamName = awayTeam.Name
            };

            // Distribute minutes based on rotation constraints before simulation
            DistributeMinutesByPosition(homeSnapshot, homePlayers, homeProfiles, config);
            DistributeMinutesByPosition(awaySnapshot, awayPlayers, awayProfiles, config);

            for (int q = 0; q < config.QuarterCount; q++)
            {
                SimulateQuarter(homeSnapshot, awaySnapshot, config, q);
                result.HomeQuarterScores.Add(homeSnapshot.TeamStats.Points - result.HomeScore);
                result.AwayQuarterScores.Add(awaySnapshot.TeamStats.Points - result.AwayScore);
                result.HomeScore = homeSnapshot.TeamStats.Points;
                result.AwayScore = awaySnapshot.TeamStats.Points;
            }

            result.HomeTeamStats = homeSnapshot.TeamStats;
            result.AwayTeamStats = awaySnapshot.TeamStats;
            result.HomePlayerStats = homeSnapshot.PlayerStatsById.Values.ToList();
            result.AwayPlayerStats = awaySnapshot.PlayerStatsById.Values.ToList();

            foreach (var kvp in homeSnapshot.Starters) result.HomeStarters[kvp.Key] = kvp.Value.GetDisplayName();
            foreach (var kvp in awaySnapshot.Starters) result.AwayStarters[kvp.Key] = kvp.Value.GetDisplayName();

            result.HomeStyleProfile = homeSnapshot.StyleProfile;
            result.AwayStyleProfile = awaySnapshot.StyleProfile;
            
            result.WinnerTeamId = result.HomeScore > result.AwayScore ? result.HomeTeamId : result.AwayTeamId;

            ValidateResult(result);

            return result;
        }

        private MatchTeamSnapshot CreateSnapshot(Team team, IReadOnlyList<Player> players)
        {
            var snapshot = new MatchTeamSnapshot
            {
                Team = team,
                Players = players,
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

        private void SimulateQuarter(MatchTeamSnapshot home, MatchTeamSnapshot away, MatchConfig config, int quarterIndex)
        {
            float gamePace = (home.StyleProfile.PaceModifier + away.StyleProfile.PaceModifier) * 0.5f;
            float minPerQuarter = config.MinPossessionsPerTeam / (float)config.QuarterCount;
            float maxPerQuarter = config.MaxPossessionsPerTeam / (float)config.QuarterCount;
            int quarterPossessions = Mathf.Max(1, Mathf.RoundToInt(_random.Range(minPerQuarter, maxPerQuarter) * gamePace));

            for (int i = 0; i < quarterPossessions; i++)
            {
                SimulatePossession(home, away, quarterIndex);
                SimulatePossession(away, home, quarterIndex);
            }
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

        private void DistributeMinutesByPosition(MatchTeamSnapshot snapshot, IReadOnlyList<Player> players, IReadOnlyDictionary<int, SimulationPlayerProfile> profiles, MatchConfig config)
        {
            SelectStartersByPosition(snapshot, players, profiles);
            
            int totalRequiredMinutes = config.QuarterCount * config.MinutesPerQuarter * 5; // Usually 240
            int requiredSlotMinutes = config.QuarterCount * config.MinutesPerQuarter; // Usually 48
            
            var playerMinutes = new Dictionary<int, float>();
            foreach (var p in players) playerMinutes[p.Id] = 0f;

            var positions = new[] { 
                BasketballManager.Core.Enums.Position.PG, 
                BasketballManager.Core.Enums.Position.SG, 
                BasketballManager.Core.Enums.Position.SF, 
                BasketballManager.Core.Enums.Position.PF, 
                BasketballManager.Core.Enums.Position.C 
            };

            foreach (var slot in positions)
            {
                float slotMinutesRemaining = requiredSlotMinutes;
                
                // 1. Assign to starter
                if (snapshot.Starters.TryGetValue(slot, out var starter))
                {
                    float starterMpg = profiles != null && profiles.TryGetValue(starter.Id, out var starterProfile) ? starterProfile.SourceMpg : 30f;
                    float ceiling = profiles != null && profiles.TryGetValue(starter.Id, out var p1) ? p1.MinuteCeiling : 40f;

                    // Base assignment logic for starter
                    float assign = Mathf.Clamp(starterMpg * (starter.Overall >= 90 ? 1.05f : 1.0f), 0, ceiling);
                    assign = Mathf.Min(assign, slotMinutesRemaining);

                    // Respect ceiling cap across multiple positions
                    float currentTotal = playerMinutes[starter.Id];
                    if (currentTotal + assign > ceiling) assign = ceiling - currentTotal;

                    playerMinutes[starter.Id] += assign;
                    slotMinutesRemaining -= assign;
                }

                // 2. Distribute remaining slot minutes to eligible bench players
                if (slotMinutesRemaining > 0.1f)
                {
                    var eligibleBench = players
                        .Where(p => (!snapshot.Starters.TryGetValue(slot, out var s) || p.Id != s.Id))
                        .Select(p => new
                        {
                            Player = p,
                            Fit = GetPositionFit(p, slot),
                            SourceMpg = profiles != null && profiles.TryGetValue(p.Id, out var profile) ? profile.SourceMpg : 15f,
                            Ceiling = profiles != null && profiles.TryGetValue(p.Id, out var prof) ? prof.MinuteCeiling : 35f
                        })
                        .Where(c => playerMinutes[c.Player.Id] < c.Ceiling) // Only players who haven't reached ceiling
                        .ToList();

                    var fitBench = eligibleBench.Where(c => c.Fit > 0).ToList();
                    
                    if (fitBench.Count == 0)
                    {
                        Debug.LogWarning($"[MatchSimulator] Emergency fallback for bench {slot} on team {snapshot.Team.Id}");
                        // Apply emergency penalty
                        fitBench = eligibleBench.Select(c => new { c.Player, Fit = 0.35f, c.SourceMpg, c.Ceiling }).ToList();
                    }

                    if (fitBench.Count > 0)
                    {
                        // Calculate weights
                        float maxMpg = fitBench.Max(c => c.SourceMpg) > 0 ? fitBench.Max(c => c.SourceMpg) : 1f;
                        float maxOvr = fitBench.Max(c => c.Player.Overall);
                        
                        var benchWeights = fitBench.Select(c => new
                        {
                            c.Player,
                            c.Ceiling,
                            Weight = (c.SourceMpg / maxMpg) * 0.45f + (c.Player.Overall / maxOvr) * 0.30f + c.Fit * 0.25f
                        }).ToList();

                        float totalWeight = benchWeights.Sum(w => w.Weight);
                        
                        foreach (var bench in benchWeights)
                        {
                            if (slotMinutesRemaining <= 0.1f) break;
                            float assign = (bench.Weight / totalWeight) * slotMinutesRemaining;
                            
                            float currentTotal = playerMinutes[bench.Player.Id];
                            if (currentTotal + assign > bench.Ceiling) assign = bench.Ceiling - currentTotal;
                            
                            playerMinutes[bench.Player.Id] += assign;
                        }
                    }
                }
            }

            // Normalization to ensure exactly 240 mins (or required total)
            float sum = playerMinutes.Values.Sum();
            if (Mathf.Abs(sum - totalRequiredMinutes) > 0.5f)
            {
                float diff = totalRequiredMinutes - sum;
                var availableForNorm = playerMinutes.Keys.Where(pid => 
                {
                    float ceil = profiles != null && profiles.TryGetValue(pid, out var p) ? p.MinuteCeiling : 40f;
                    return diff > 0 ? playerMinutes[pid] < ceil : playerMinutes[pid] > 0;
                }).ToList();

                if (availableForNorm.Count > 0)
                {
                    float adj = diff / availableForNorm.Count;
                    foreach (var pid in availableForNorm) playerMinutes[pid] += adj;
                }
            }

            // Largest-remainder integerization: floor everyone, then hand the leftover
            // out one minute at a time to the players with the biggest fractional parts.
            // This keeps the residual bounded by orderedPids.Count instead of dumping
            // hundreds of minutes onto a single player when ceilings are too tight.
            var orderedPids = playerMinutes.Keys.OrderByDescending(k => playerMinutes[k]).ToList();
            var remainders = new Dictionary<int, float>();
            int assignedInt = 0;
            foreach (var pid in orderedPids)
            {
                int min = Mathf.Max(0, Mathf.FloorToInt(playerMinutes[pid]));
                snapshot.PlayerStatsById[pid].Minutes = min;
                remainders[pid] = playerMinutes[pid] - min;
                assignedInt += min;
            }

            int residual = totalRequiredMinutes - assignedInt;
            if (Mathf.Abs(residual) > orderedPids.Count + 1)
            {
                Debug.LogWarning($"[MatchSimulator] Large minute distribution residual ({residual}) on team {snapshot.Team.Id} — profile data (mpg/ceiling) may be invalid.");
            }

            if (residual > 0)
            {
                foreach (var pid in orderedPids.OrderByDescending(pid => remainders[pid]))
                {
                    if (residual <= 0) break;
                    float ceil = profiles != null && profiles.TryGetValue(pid, out var p) ? p.MinuteCeiling : 40f;
                    if (snapshot.PlayerStatsById[pid].Minutes < ceil)
                    {
                        snapshot.PlayerStatsById[pid].Minutes++;
                        residual--;
                    }
                }
            }
            else if (residual < 0)
            {
                foreach (var pid in orderedPids.OrderBy(pid => remainders[pid]))
                {
                    if (residual >= 0) break;
                    if (snapshot.PlayerStatsById[pid].Minutes > 0)
                    {
                        snapshot.PlayerStatsById[pid].Minutes--;
                        residual++;
                    }
                }
            }

            var playmakerSorted = snapshot.RotationPlayers.OrderByDescending(p =>
            {
                int mins = snapshot.PlayerStatsById[p.Id].Minutes;
                return p.Attributes.Passing * 0.38f +
                       p.Tendencies.PassTendency * 0.30f +
                       p.Attributes.BallHandle * 0.14f +
                       p.Attributes.OffensiveConsistency * 0.08f +
                       mins * 0.07f +
                       p.Tendencies.ShotTendency * 0.03f;
            }).ToList();

            for (int i = 0; i < playmakerSorted.Count; i++)
            {
                snapshot.PlaymakerRoleRankByPlayerId[playmakerSorted[i].Id] = i + 1;
            }

            snapshot.StyleProfile = DeriveTeamStyleProfile(snapshot);
        }

        private TeamStyleProfile DeriveTeamStyleProfile(MatchTeamSnapshot snapshot)
        {
            var profile = new TeamStyleProfile();
            var rotation = snapshot.RotationPlayers;
            if (rotation.Count == 0) return profile;

            float totalMinutes = 0f;
            foreach (var player in rotation)
            {
                totalMinutes += snapshot.PlayerStatsById[player.Id].Minutes;
            }
            if (totalMinutes <= 0) totalMinutes = 1f;

            float WeightedAvg(Func<Player, float> metricSelector)
            {
                float sum = 0f;
                foreach (var p in rotation)
                {
                    sum += metricSelector(p) * snapshot.PlayerStatsById[p.Id].Minutes;
                }
                return sum / totalMinutes;
            }

            float spacingScore = WeightedAvg(p => p.Attributes.ThreePoint * 0.6f + p.Tendencies.ThreeTendency * 0.4f);
            profile.SpacingModifier = MapToModifier(spacingScore, 74f, 30f, 0.94f, 1.08f);

            var top5 = rotation.OrderByDescending(p => snapshot.PlayerStatsById[p.Id].Minutes).Take(5).ToList();
            float top5Mins = top5.Sum(p => snapshot.PlayerStatsById[p.Id].Minutes);
            if (top5Mins <= 0) top5Mins = 1f;
            float gravityScore = top5.Sum(p => (p.Attributes.ThreePoint * 0.5f + p.Tendencies.ThreeTendency * 0.5f) * snapshot.PlayerStatsById[p.Id].Minutes) / top5Mins;
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
            bool isTransition = offense.HasTransitionOpportunity;
            offense.HasTransitionOpportunity = false;

            var attacker = SelectInitiator(offense);
            var defender = SelectMatchedDefender(defense, attacker, null);

            var offStats = offense.PlayerStatsById[attacker.Id];
            var defStats = defense.PlayerStatsById[defender.Id];

            float turnoverChance = CalculateTurnoverChance(offense, defense, attacker, defender, isTransition);
            if (_random.Chance(turnoverChance))
            {
                offStats.Turnovers++;
                offense.TeamStats.Turnovers++;

                if (_random.Chance(0.5f))
                {
                    defStats.Steals++;
                    defense.TeamStats.Steals++;
                    defense.HasTransitionOpportunity = true;
                }
                return;
            }

            var finisher = SelectFinisher(offense, isTransition);
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

            // Block (priority over Foul/And-1)
            float blockChance = CalculateBlockChance(shotType, finisher, finDefender);
            if (_random.Chance(blockChance))
            {
                finDefStats.Blocks++;
                defense.TeamStats.Blocks++;

                RecordShotResult(offense, finisher, shotType, false, true);

                var (rebounder, isOffensive) = ResolveRebound(offense, defense, shotType);
                if (isOffensive && rebounder != null && ctx.AllowSecondChanceOnMiss)
                    SimulateSecondChanceAttempt(offense, defense, rebounder, quarterIndex);
                return;
            }

            // Foul + And-1
            bool isFoul = ResolveFoul(finisher, finDefender, shotType, offense, quarterIndex, out int freeThrows);
            if (isFoul)
            {
                ResolveFoulOutcome(offense, defense, finisher, finDefender, shotType, freeThrows, ctx.AssistAttributionPlayer);
                return;
            }

            // FG
            float fgChance = CalculateFgChance(offense, defense, finisher, finDefender, shotType, ctx.IsTransition) + ctx.MadeChanceBoost;
            bool isMade = _random.Chance(fgChance);

            RecordShotResult(offense, finisher, shotType, isMade, false);

            if (isMade)
            {
                if (ctx.AssistAttributionPlayer != null)
                    ResolveAssist(offense, ctx.AssistAttributionPlayer, finisher, shotType, true);
            }
            else
            {
                var (rebounder, isOffensive) = ResolveRebound(offense, defense, shotType);
                if (isOffensive && rebounder != null && ctx.AllowSecondChanceOnMiss)
                    SimulateSecondChanceAttempt(offense, defense, rebounder, quarterIndex);
            }
        }

        private void ResolveFoulOutcome(
            MatchTeamSnapshot offense, MatchTeamSnapshot defense,
            Player finisher, Player finDefender, ShotType shotType, int freeThrows,
            Player assistAttribution)
        {
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

                RecordShotResult(offense, finisher, shotType, true, false);

                if (assistAttribution != null)
                    ResolveAssist(offense, assistAttribution, finisher, shotType, true);

                float ftChance = (finisher.Attributes.FreeThrow / 100f) * 0.85f + 0.05f;
                RecordFreeThrow(offense, finisher, _random.Chance(ftChance));
            }
            else
            {
                // Regular shooting foul
                finDefStats.Fouls++;
                defense.TeamStats.Fouls++;

                for (int i = 0; i < freeThrows; i++)
                {
                    float ftChance = (finisher.Attributes.FreeThrow / 100f) * 0.85f + 0.05f;
                    RecordFreeThrow(offense, finisher, _random.Chance(ftChance));
                }
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
            int minutes = team.PlayerStatsById[player.Id].Minutes;

            if (roleRank == 1)
            {
                return 1.05f;
            }
            if (roleRank == 2)
            {
                if (player.Tendencies.ShotTendency >= 70 && minutes >= 28) return 1.15f;
                return 1.08f;
            }
            if (roleRank == 3)
            {
                if (player.Tendencies.ShotTendency >= 65 && minutes >= 26) return 1.08f;
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
            var candidates = offense.RotationPlayers;
            float totalWeight = 0;
            var weights = new float[candidates.Count];

            for (int i = 0; i < candidates.Count; i++)
            {
                var p = candidates[i];
                int expectedMins = offense.PlayerStatsById[p.Id].Minutes;
                
                float w = expectedMins * 1.5f + 
                          p.Attributes.Passing * 1.2f + 
                          p.Attributes.BallHandle * 1.0f + 
                          p.Tendencies.PassTendency * 0.9f + 
                          p.Attributes.OffensiveConsistency * 0.4f;

                if (p.Position == BasketballManager.Core.Enums.Position.PG)
                {
                    w *= 1.2f;
                }

                if (expectedMins < 12)
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
            var candidates = offense.RotationPlayers;
            float totalWeight = 0;
            var weights = new float[candidates.Count];

            for (int i = 0; i < candidates.Count; i++)
            {
                var p = candidates[i];
                int expectedMins = offense.PlayerStatsById[p.Id].Minutes;
                
                float w = expectedMins * 1.8f +
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
                    expectedMins >= 32)
                {
                    w *= 1.12f;
                }

                if (expectedMins < 12)
                {
                    w *= 0.55f;
                }
                else if (expectedMins < 18)
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
            var rotation = defense.RotationPlayers;
            
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
                int mins = defense.PlayerStatsById[p.Id].Minutes;
                float w = 0;

                if (shotType == ShotType.ThreePoint || shotType == ShotType.TwoPoint)
                {
                    w = mins * 0.5f + p.Attributes.PerimeterDefense * 1.2f + p.Attributes.DefensiveConsistency * 0.6f + p.Attributes.Steal * 0.3f;
                }
                else if (shotType == ShotType.Layup || shotType == ShotType.CloseShot || shotType == ShotType.Post)
                {
                    w = mins * 0.5f + p.Attributes.InteriorDefense * 1.1f + p.Attributes.Block * 0.8f + p.Attributes.Strength * 0.4f + p.Attributes.DefensiveConsistency * 0.6f;
                }
                else
                {
                    w = mins * 0.5f + p.Attributes.PerimeterDefense * 0.8f + p.Attributes.Steal * 0.8f + p.Attributes.DefensiveConsistency * 0.5f;
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

        private void RecordShotResult(MatchTeamSnapshot offense, Player shooter, ShotType shotType, bool isMade, bool isBlocked = false)
        {
            var pStats = offense.PlayerStatsById[shooter.Id];
            var pState = offense.PlayerGameStates[shooter.Id];
            var tStats = offense.TeamStats;

            pStats.FieldGoalsAttempted++;
            pState.Fga++;
            tStats.FieldGoalsAttempted++;

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
            }
            else
            {
                pState.ConsecutiveMisses++;
                if (isThree)
                {
                    pState.ConsecutiveThreeMisses++;
                    offense.ConsecutiveTeamThreeMisses++;
                }
            }
        }

        private void RecordFreeThrow(MatchTeamSnapshot offense, Player shooter, bool isMade)
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
            }
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

            return shotType switch
            {
                ShotType.ThreePoint => Mathf.Clamp(chance, 0.22f, 0.55f),
                ShotType.TwoPoint => Mathf.Clamp(chance, 0.32f, 0.62f),
                ShotType.Layup => Mathf.Clamp(chance, 0.38f, 0.72f),
                ShotType.CloseShot => Mathf.Clamp(chance, 0.40f, 0.75f),
                ShotType.Post => Mathf.Clamp(chance, 0.35f, 0.68f),
                _ => 0.40f
            };
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
            var candidates = offense.RotationPlayers.Where(p => p.Id != finisher.Id).ToList();
            if (candidates.Count == 0) return null;

            var weights = new float[candidates.Count];
            float totalWeight = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                int mins = offense.PlayerStatsById[candidate.Id].Minutes;

                float w = mins * 0.45f +
                          candidate.Attributes.Passing * 2.10f +
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

                if (mins < 12) w *= 0.35f;
                else if (mins < 18) w *= 0.65f;

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

        private void ResolveAssist(MatchTeamSnapshot offense, Player initiator, Player finisher, ShotType shotType, bool made)
        {
            if (!made || finisher == null) return;

            var assister = SelectAssisterCandidate(offense, initiator, finisher, shotType);
            if (assister == null || assister.Id == finisher.Id) return;

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

            int mins = offense.PlayerStatsById[assister.Id].Minutes;
            if (mins < 18) passBonus -= 0.04f;

            float assistChance = (baseAssistChance + modifier - penalty + passBonus) * offense.StyleProfile.AssistModifier;
            assistChance = Mathf.Clamp(assistChance, 0.25f, 0.90f);

            if (_random.Chance(assistChance))
            {
                offense.PlayerStatsById[assister.Id].Assists++;
                offense.TeamStats.Assists++;
            }
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

        private float GetReboundMinuteParticipationFactor(int minutes)
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

            var offWeights = new float[offense.RotationPlayers.Count];
            for (int i = 0; i < offense.RotationPlayers.Count; i++)
            {
                var p = offense.RotationPlayers[i];
                int mins = offense.PlayerStatsById[p.Id].Minutes;
                float baseWeight = p.Attributes.OffensiveRebound * 1.65f + p.Tendencies.OffensiveReboundTendency * 1.25f + p.Attributes.Strength * 0.35f + p.HeightCm * 0.16f;
                float w = baseWeight * GetReboundMinuteParticipationFactor(mins);

                float posMultiplier = 1.0f;
                if (p.Position == BasketballManager.Core.Enums.Position.PG) posMultiplier = 0.78f;
                else if (p.Position == BasketballManager.Core.Enums.Position.SG) posMultiplier = 0.82f;
                else if (p.Position == BasketballManager.Core.Enums.Position.SF) posMultiplier = 0.95f;
                else if (p.Position == BasketballManager.Core.Enums.Position.PF) posMultiplier = 1.12f;
                else if (p.Position == BasketballManager.Core.Enums.Position.C) posMultiplier = 1.25f;

                if (shotType == ShotType.ThreePoint)
                {
                    if (p.Position == BasketballManager.Core.Enums.Position.PG) posMultiplier *= 1.45f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SG) posMultiplier *= 1.35f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SF) posMultiplier *= 1.20f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.PF) posMultiplier *= 0.90f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.C) posMultiplier *= 0.82f;
                }
                else if (shotType == ShotType.TwoPoint)
                {
                    if (p.Position == BasketballManager.Core.Enums.Position.PG) posMultiplier *= 1.15f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SG) posMultiplier *= 1.12f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SF) posMultiplier *= 1.08f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.PF) posMultiplier *= 0.97f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.C) posMultiplier *= 0.92f;
                }

                w *= posMultiplier;

                float eliteBoost = GetEliteRebounderBoost(p, false);
                if (mins < 12)
                    eliteBoost = Mathf.Min(eliteBoost, 1.05f);
                else if (mins < 18)
                    eliteBoost = Mathf.Min(eliteBoost, 1.12f);
                w *= eliteBoost;

                if (mins <= 6)
                    w *= 0.75f;
                else if (mins <= 10)
                    w *= 0.85f;
                else if (mins <= 16)
                    w *= 0.94f;

                float reboundDominanceScore = p.Attributes.OffensiveRebound * 0.55f + p.Tendencies.OffensiveReboundTendency * 0.30f + p.Attributes.Strength * 0.10f + Mathf.Clamp((p.HeightCm - 180f) / 50f * 100f, 0f, 100f) * 0.05f;
                if (mins >= 32 && reboundDominanceScore >= 88f)
                {
                    w *= 0.92f;
                }
                else if (mins >= 32 && reboundDominanceScore >= 82f)
                {
                    w *= 0.96f;
                }

                if ((p.Position == BasketballManager.Core.Enums.Position.PF || p.Position == BasketballManager.Core.Enums.Position.C) && mins >= 12 && mins <= 24)
                {
                    w *= 1.06f;
                }

                offWeights[i] = w;
                teamOffWeight += w;
            }

            var defWeights = new float[defense.RotationPlayers.Count];
            for (int i = 0; i < defense.RotationPlayers.Count; i++)
            {
                var p = defense.RotationPlayers[i];
                int mins = defense.PlayerStatsById[p.Id].Minutes;
                float baseWeight = p.Attributes.DefensiveRebound * 1.65f + p.Tendencies.DefensiveReboundTendency * 1.25f + p.Attributes.Strength * 0.35f + p.HeightCm * 0.16f;
                float w = baseWeight * GetReboundMinuteParticipationFactor(mins);

                float posMultiplier = 1.0f;
                if (p.Position == BasketballManager.Core.Enums.Position.PG) posMultiplier = 0.78f;
                else if (p.Position == BasketballManager.Core.Enums.Position.SG) posMultiplier = 0.82f;
                else if (p.Position == BasketballManager.Core.Enums.Position.SF) posMultiplier = 0.95f;
                else if (p.Position == BasketballManager.Core.Enums.Position.PF) posMultiplier = 1.12f;
                else if (p.Position == BasketballManager.Core.Enums.Position.C) posMultiplier = 1.25f;

                if (shotType == ShotType.ThreePoint)
                {
                    if (p.Position == BasketballManager.Core.Enums.Position.PG) posMultiplier *= 1.45f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SG) posMultiplier *= 1.35f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SF) posMultiplier *= 1.20f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.PF) posMultiplier *= 0.90f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.C) posMultiplier *= 0.82f;
                }
                else if (shotType == ShotType.TwoPoint)
                {
                    if (p.Position == BasketballManager.Core.Enums.Position.PG) posMultiplier *= 1.15f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SG) posMultiplier *= 1.12f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.SF) posMultiplier *= 1.08f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.PF) posMultiplier *= 0.97f;
                    else if (p.Position == BasketballManager.Core.Enums.Position.C) posMultiplier *= 0.92f;
                }

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
                
                float eliteBoost = GetEliteRebounderBoost(p, true);
                if (mins < 12)
                    eliteBoost = Mathf.Min(eliteBoost, 1.05f);
                else if (mins < 18)
                    eliteBoost = Mathf.Min(eliteBoost, 1.12f);
                w *= eliteBoost;

                if (mins <= 6)
                    w *= 0.75f;
                else if (mins <= 10)
                    w *= 0.85f;
                else if (mins <= 16)
                    w *= 0.94f;

                float reboundDominanceScore = p.Attributes.DefensiveRebound * 0.55f + p.Tendencies.DefensiveReboundTendency * 0.30f + p.Attributes.Strength * 0.10f + Mathf.Clamp((p.HeightCm - 180f) / 50f * 100f, 0f, 100f) * 0.05f;
                if (mins >= 32 && reboundDominanceScore >= 88f)
                {
                    w *= 0.92f;
                }
                else if (mins >= 32 && reboundDominanceScore >= 82f)
                {
                    w *= 0.96f;
                }

                if ((p.Position == BasketballManager.Core.Enums.Position.PF || p.Position == BasketballManager.Core.Enums.Position.C) && mins >= 12 && mins <= 24)
                {
                    if (p.Attributes.DefensiveRebound >= 70 || p.Tendencies.DefensiveReboundTendency >= 70)
                    {
                        w *= 1.12f;
                    }
                }

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
                Player rebounder = offense.RotationPlayers.Last();
                for (int i = 0; i < offense.RotationPlayers.Count; i++)
                {
                    roll -= offWeights[i];
                    if (roll <= 0)
                    {
                        rebounder = offense.RotationPlayers[i];
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
                Player rebounder = defense.RotationPlayers.Last();
                for (int i = 0; i < defense.RotationPlayers.Count; i++)
                {
                    roll -= defWeights[i];
                    if (roll <= 0)
                    {
                        rebounder = defense.RotationPlayers[i];
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
