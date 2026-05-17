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
        }

        private void SimulatePossession(MatchTeamSnapshot offense, MatchTeamSnapshot defense)
        {
            var attacker = SelectInitiator(offense);
            var defender = SelectRandomDefender(defense);

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
            var finDefender = SelectRandomDefender(defense);
            var finDefStats = defense.PlayerStatsById[finDefender.Id];
            
            var shotType = SelectShotType(finisher);

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
                
                ResolveRebound(offense, defense);
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
                if (attacker.Id != finisher.Id)
                {
                    float assistChance = (attacker.Attributes.Passing + attacker.Tendencies.PassTendency) / 200f;
                    if (_random.Chance(assistChance))
                    {
                        offStats.Assists++;
                        offense.TeamStats.Assists++;
                    }
                }
            }
            else
            {
                // Missed shot -> rebound
                ResolveRebound(offense, defense);
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

                if (p.Position == BasketballManager.Core.Enums.PlayerPosition.PG)
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

        private Player SelectRandomDefender(MatchTeamSnapshot defense)
        {
            var rotation = defense.RotationPlayers;
            return rotation[_random.Range(0, rotation.Count)];
        }

        private ShotType SelectShotType(Player player)
        {
            var t = player.Tendencies;
            float total = t.ThreeTendency + t.TwoPointTendency + t.DriveTendency + t.PostTendency + t.CloseShotTendency;
            if (total == 0) return ShotType.TwoPoint;

            float roll = _random.Range(0f, total);
            
            roll -= t.ThreeTendency;
            if (roll <= 0) return ShotType.ThreePoint;
            
            roll -= t.TwoPointTendency;
            if (roll <= 0) return ShotType.TwoPoint;
            
            roll -= t.DriveTendency;
            if (roll <= 0) return ShotType.Layup;
            
            roll -= t.PostTendency;
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

        private void ResolveRebound(MatchTeamSnapshot offense, MatchTeamSnapshot defense)
        {
            var offRebounder = SelectRandomDefender(offense); // just pick a random from floor
            var defRebounder = SelectRandomDefender(defense);

            float offWeight = offRebounder.Attributes.OffensiveRebound + offRebounder.Tendencies.OffensiveReboundTendency + offRebounder.Attributes.Strength + offRebounder.HeightCm;
            float defWeight = defRebounder.Attributes.DefensiveRebound + defRebounder.Tendencies.DefensiveReboundTendency + defRebounder.Attributes.Strength + defRebounder.HeightCm;

            float offTotal = 0.35f * offWeight;
            float defTotal = 0.65f * defWeight;

            if (_random.Range(0f, offTotal + defTotal) < offTotal)
            {
                // Offensive Rebound
                offense.PlayerStatsById[offRebounder.Id].OffensiveRebounds++;
                offense.TeamStats.OffensiveRebounds++;
            }
            else
            {
                // Defensive Rebound
                defense.PlayerStatsById[defRebounder.Id].DefensiveRebounds++;
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
