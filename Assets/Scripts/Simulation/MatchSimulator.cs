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
            Team awayTeam,
            IReadOnlyList<Player> awayPlayers,
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
            DistributeMinutes(homeSnapshot, config);
            DistributeMinutes(awaySnapshot, config);

            for (int q = 0; q < config.QuarterCount; q++)
            {
                SimulateQuarter(homeSnapshot, awaySnapshot, config);
                result.HomeQuarterScores.Add(homeSnapshot.TeamStats.Points - result.HomeScore);
                result.AwayQuarterScores.Add(awaySnapshot.TeamStats.Points - result.AwayScore);
                result.HomeScore = homeSnapshot.TeamStats.Points;
                result.AwayScore = awaySnapshot.TeamStats.Points;
            }

            result.HomeTeamStats = homeSnapshot.TeamStats;
            result.AwayTeamStats = awaySnapshot.TeamStats;
            result.HomePlayerStats = homeSnapshot.PlayerStatsById.Values.ToList();
            result.AwayPlayerStats = awaySnapshot.PlayerStatsById.Values.ToList();
            
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
            }

            return snapshot;
        }

        private void SimulateQuarter(MatchTeamSnapshot home, MatchTeamSnapshot away, MatchConfig config)
        {
            int quarterPossessions = _random.Range(config.MinPossessionsPerTeam, config.MaxPossessionsPerTeam) / config.QuarterCount;
            
            for (int i = 0; i < quarterPossessions; i++)
            {
                SimulatePossession(home, away);
                SimulatePossession(away, home);
            }
        }

        private void DistributeMinutes(MatchTeamSnapshot snapshot, MatchConfig config)
        {
            int totalMinutes = config.QuarterCount * config.MinutesPerQuarter * 5;
            var rotation = snapshot.RotationPlayers;
            
            if (rotation.Count == 0) return;

            int[] targetMinutes = { 37, 35, 33, 31, 28, 24, 20, 16, 10, 6 };
            int index = 0;
            foreach (var player in rotation)
            {
                int min = index < targetMinutes.Length ? targetMinutes[index] : 0;
                snapshot.PlayerStatsById[player.Id].Minutes = min;
                totalMinutes -= min;
                index++;
            }
            
            if (totalMinutes != 0 && rotation.Count > 0)
            {
                float assignedMins = (config.QuarterCount * config.MinutesPerQuarter * 5) - totalMinutes;
                if (assignedMins <= 0) assignedMins = 1;
                float ratio = (float)(config.QuarterCount * config.MinutesPerQuarter * 5) / assignedMins;
                int newTotal = 0;
                for (int i = 0; i < rotation.Count; i++)
                {
                    var pStats = snapshot.PlayerStatsById[rotation[i].Id];
                    if (i == rotation.Count - 1)
                    {
                        pStats.Minutes = (config.QuarterCount * config.MinutesPerQuarter * 5) - newTotal;
                    }
                    else
                    {
                        pStats.Minutes = Mathf.RoundToInt(pStats.Minutes * ratio);
                        newTotal += pStats.Minutes;
                    }
                }
            }

            var playmakerSorted = rotation.OrderByDescending(p =>
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
        }

        private void SimulatePossession(MatchTeamSnapshot offense, MatchTeamSnapshot defense)
        {
            var attacker = SelectInitiator(offense);
            var defender = SelectMatchedDefender(defense, attacker, null);

            var offStats = offense.PlayerStatsById[attacker.Id];
            var defStats = defense.PlayerStatsById[defender.Id];

            // 15. Turnovers
            float turnoverChance = CalculateTurnoverChance(attacker, defender);
            if (_random.Chance(turnoverChance))
            {
                offStats.Turnovers++;
                offense.TeamStats.Turnovers++;
                
                if (_random.Chance(0.5f)) // 50% of TOs are Steals
                {
                    defStats.Steals++;
                    defense.TeamStats.Steals++;
                }
                return;
            }

            // Select Finisher (could be the same as initiator)
            var finisher = SelectFinisher(offense);
            var finStats = offense.PlayerStatsById[finisher.Id];
            
            var shotType = SelectShotType(finisher);

            var finDefender = SelectMatchedDefender(defense, finisher, shotType);
            var finDefStats = defense.PlayerStatsById[finDefender.Id];

            // 16. Blocks
            float blockChance = CalculateBlockChance(shotType, finDefender);
            if (_random.Chance(blockChance))
            {
                finDefStats.Blocks++;
                defense.TeamStats.Blocks++;
                
                finStats.FieldGoalsAttempted++;
                offense.TeamStats.FieldGoalsAttempted++;
                if (shotType == ShotType.ThreePoint)
                {
                    finStats.ThreePointersAttempted++;
                    offense.TeamStats.ThreePointersAttempted++;
                }
                
                ResolveRebound(offense, defense, shotType);
                return;
            }

            // 12. Fouls
            bool isFoul = ResolveFoul(finisher, finDefender, shotType, out int freeThrows);
            if (isFoul)
            {
                finDefStats.Fouls++;
                defense.TeamStats.Fouls++;
                
                finStats.FreeThrowsAttempted += freeThrows;
                offense.TeamStats.FreeThrowsAttempted += freeThrows;
                
                int ftMade = 0;
                for (int i = 0; i < freeThrows; i++)
                {
                    float ftChance = (finisher.Attributes.FreeThrow / 100f) * 0.85f + 0.05f; // Simplify
                    if (_random.Chance(ftChance))
                    {
                        ftMade++;
                    }
                }
                
                finStats.FreeThrowsMade += ftMade;
                finStats.Points += ftMade;
                offense.TeamStats.FreeThrowsMade += ftMade;
                offense.TeamStats.Points += ftMade;
                
                // Possible And-1 if random dictates? Simplified to just FTs for foul.
                return; 
            }

            // 11. Shot Success
            float fgChance = CalculateFgChance(finisher, finDefender, shotType);
            bool isMade = _random.Chance(fgChance);

            finStats.FieldGoalsAttempted++;
            offense.TeamStats.FieldGoalsAttempted++;
            if (shotType == ShotType.ThreePoint)
            {
                finStats.ThreePointersAttempted++;
                offense.TeamStats.ThreePointersAttempted++;
            }

            if (isMade)
            {
                int pts = shotType == ShotType.ThreePoint ? 3 : 2;
                finStats.FieldGoalsMade++;
                finStats.Points += pts;
                offense.TeamStats.FieldGoalsMade++;
                offense.TeamStats.Points += pts;

                if (shotType == ShotType.ThreePoint)
                {
                    finStats.ThreePointersMade++;
                    offense.TeamStats.ThreePointersMade++;
                }

                // 14. Assists
                ResolveAssist(offense, attacker, finisher, shotType, true);
            }
            else
            {
                // Missed shot -> rebound
                ResolveRebound(offense, defense, shotType);
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

        private Player SelectFinisher(MatchTeamSnapshot offense)
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

        private ShotType SelectShotType(Player player)
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

            float total = threeWeight + twoPointWeight + driveWeight + closeWeight + postWeight;
            if (total == 0) return ShotType.TwoPoint;

            float roll = _random.Range(0f, total);
            
            roll -= threeWeight;
            if (roll <= 0) return ShotType.ThreePoint;
            
            roll -= twoPointWeight;
            if (roll <= 0) return ShotType.TwoPoint;
            
            roll -= driveWeight;
            if (roll <= 0) return ShotType.Layup;
            
            roll -= postWeight;
            if (roll <= 0) return ShotType.Post;
            
            return ShotType.CloseShot;
        }

        private float CalculateTurnoverChance(Player attacker, Player defender)
        {
            float off = (attacker.Attributes.BallHandle + attacker.Attributes.Passing + attacker.Attributes.OffensiveConsistency) / 3f;
            float def = (defender.Attributes.Steal + defender.Tendencies.StealTendency + defender.Attributes.PerimeterDefense) / 3f;
            
            float baseChance = 0.12f;
            float delta = (def - off) / 100f * 0.1f; 
            return Mathf.Clamp(baseChance + delta, 0.05f, 0.25f);
        }

        private float CalculateBlockChance(ShotType shotType, Player defender)
        {
            if (shotType == ShotType.ThreePoint) return 0.01f;
            if (shotType == ShotType.TwoPoint) return 0.03f;
            
            float defAbility = (defender.Attributes.Block + defender.Tendencies.BlockTendency + defender.Attributes.InteriorDefense) / 3f;
            return Mathf.Clamp(defAbility / 100f * 0.15f, 0.02f, 0.15f);
        }

        private bool ResolveFoul(Player attacker, Player defender, ShotType shotType, out int freeThrows)
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

            chance *= 0.72f;

            if (shotType == ShotType.ThreePoint) chance *= 0.35f;
            else if (shotType == ShotType.TwoPoint) chance *= 0.65f;
            else if (shotType == ShotType.Layup) chance *= 0.90f;
            else if (shotType == ShotType.CloseShot) chance *= 0.95f;
            else if (shotType == ShotType.Post) chance *= 0.85f;

            float drawFoulScore = attacker.Attributes.DrawFoul * 0.55f + attacker.Tendencies.DrawFoulTendency * 0.45f;
            if (drawFoulScore < 70) chance *= 0.75f;
            if (drawFoulScore >= 88) chance *= 1.10f;

            if (_random.Chance(chance))
            {
                freeThrows = shotType == ShotType.ThreePoint ? 3 : 2;
                return true;
            }
            return false;
        }

        private float CalculateFgChance(Player attacker, Player defender, ShotType shotType)
        {
            float offAttr = 60f;
            float defAttr = 60f;

            switch (shotType)
            {
                case ShotType.ThreePoint:
                    offAttr = attacker.Attributes.ThreePoint;
                    defAttr = defender.Attributes.PerimeterDefense;
                    break;
                case ShotType.TwoPoint:
                    offAttr = attacker.Attributes.TwoPoint;
                    defAttr = defender.Attributes.PerimeterDefense;
                    break;
                case ShotType.Layup:
                    offAttr = (attacker.Attributes.Layup + attacker.Attributes.Drive) / 2f;
                    defAttr = (defender.Attributes.InteriorDefense + defender.Attributes.Block + defender.Attributes.Strength) / 3f;
                    break;
                case ShotType.CloseShot:
                    offAttr = attacker.Attributes.CloseShot;
                    defAttr = (defender.Attributes.InteriorDefense + defender.Attributes.Block + defender.Attributes.Strength) / 3f;
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
                ShotType.ThreePoint => 0.35f + delta,
                ShotType.TwoPoint => 0.45f + delta,
                ShotType.Layup => 0.55f + delta,
                ShotType.CloseShot => 0.58f + delta,
                ShotType.Post => 0.50f + delta,
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
                    if (candidate.Attributes.Passing < 65 && candidate.Tendencies.PassTendency < 65 && candidate.Attributes.BallHandle < 70)
                    {
                        w *= 0.55f;
                    }
                    else if (candidate.Attributes.ThreePoint >= 80 && candidate.Tendencies.ThreeTendency >= 80 && candidate.Tendencies.PassTendency < 65)
                    {
                        w *= 0.60f;
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

            float assistChance = baseAssistChance + modifier - penalty + passBonus;
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

            if (reboundCore >= 88f) return 1.35f;
            if (reboundCore >= 82f) return 1.20f;
            if (reboundCore >= 76f) return 1.10f;
            return 1.0f;
        }

        private void ResolveRebound(MatchTeamSnapshot offense, MatchTeamSnapshot defense, ShotType? shotType)
        {
            float teamOffWeight = 0;
            float teamDefWeight = 0;

            var offWeights = new float[offense.RotationPlayers.Count];
            for (int i = 0; i < offense.RotationPlayers.Count; i++)
            {
                var p = offense.RotationPlayers[i];
                int mins = offense.PlayerStatsById[p.Id].Minutes;
                float w = p.Attributes.OffensiveRebound * 1.65f + p.Tendencies.OffensiveReboundTendency * 1.25f + p.Attributes.Strength * 0.35f + p.HeightCm * 0.16f + mins * 0.45f;

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
                w *= GetEliteRebounderBoost(p, false);

                offWeights[i] = w;
                teamOffWeight += w;
            }

            var defWeights = new float[defense.RotationPlayers.Count];
            for (int i = 0; i < defense.RotationPlayers.Count; i++)
            {
                var p = defense.RotationPlayers[i];
                int mins = defense.PlayerStatsById[p.Id].Minutes;
                float w = p.Attributes.DefensiveRebound * 1.65f + p.Tendencies.DefensiveReboundTendency * 1.25f + p.Attributes.Strength * 0.35f + p.HeightCm * 0.16f + mins * 0.45f;

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
                w *= GetEliteRebounderBoost(p, true);

                defWeights[i] = w;
                teamDefWeight += w;
            }

            if (teamOffWeight + teamDefWeight == 0) return;

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
