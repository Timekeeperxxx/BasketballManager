using System.Collections.Generic;
using System.Linq;
using BasketballManager.Core.Models;

namespace BasketballManager.Simulation
{
    public sealed class MatchSimulationBatchRunner
    {
        public MatchSimulationReport Run(
            Team homeTeam,
            IReadOnlyList<Player> homePlayers,
            Team awayTeam,
            IReadOnlyList<Player> awayPlayers,
            int games,
            int baseSeed)
        {
            var report = new MatchSimulationReport
            {
                HomeTeamId = homeTeam.Id,
                AwayTeamId = awayTeam.Id,
                HomeTeamName = homeTeam.Name,
                AwayTeamName = awayTeam.Name,
                Games = games
            };

            float homeTotalScore = 0;
            float awayTotalScore = 0;
            
            float homeFGM = 0, homeFGA = 0;
            float awayFGM = 0, awayFGA = 0;
            float home3PM = 0, home3PA = 0;
            float away3PM = 0, away3PA = 0;
            float homeFTM = 0, homeFTA = 0;
            float awayFTM = 0, awayFTA = 0;
            
            float homeReb = 0, awayReb = 0;
            float homeAst = 0, awayAst = 0;
            float homeTov = 0, awayTov = 0;
            float homePf = 0, awayPf = 0;

            var homePlayerStats = new Dictionary<int, PlayerAverageStatLine>();
            var awayPlayerStats = new Dictionary<int, PlayerAverageStatLine>();

            foreach (var p in homePlayers)
            {
                homePlayerStats[p.Id] = new PlayerAverageStatLine { PlayerId = p.Id, PlayerName = p.GetDisplayName() };
            }
            foreach (var p in awayPlayers)
            {
                awayPlayerStats[p.Id] = new PlayerAverageStatLine { PlayerId = p.Id, PlayerName = p.GetDisplayName() };
            }

            var config = new MatchConfig
            {
                UseFixedSeed = true
            };

            for (int i = 0; i < games; i++)
            {
                config.Seed = baseSeed + i;
                var simulator = new MatchSimulator();
                var result = simulator.Simulate(homeTeam, homePlayers, awayTeam, awayPlayers, config);

                if (result.HomeScore > result.AwayScore)
                {
                    report.HomeWins++;
                }
                else
                {
                    report.AwayWins++;
                }

                homeTotalScore += result.HomeScore;
                awayTotalScore += result.AwayScore;

                homeFGM += result.HomeTeamStats.FieldGoalsMade;
                homeFGA += result.HomeTeamStats.FieldGoalsAttempted;
                home3PM += result.HomeTeamStats.ThreePointersMade;
                home3PA += result.HomeTeamStats.ThreePointersAttempted;
                homeFTM += result.HomeTeamStats.FreeThrowsMade;
                homeFTA += result.HomeTeamStats.FreeThrowsAttempted;
                
                homeReb += result.HomeTeamStats.Rebounds;
                homeAst += result.HomeTeamStats.Assists;
                homeTov += result.HomeTeamStats.Turnovers;
                homePf += result.HomeTeamStats.Fouls;

                awayFGM += result.AwayTeamStats.FieldGoalsMade;
                awayFGA += result.AwayTeamStats.FieldGoalsAttempted;
                away3PM += result.AwayTeamStats.ThreePointersMade;
                away3PA += result.AwayTeamStats.ThreePointersAttempted;
                awayFTM += result.AwayTeamStats.FreeThrowsMade;
                awayFTA += result.AwayTeamStats.FreeThrowsAttempted;
                
                awayReb += result.AwayTeamStats.Rebounds;
                awayAst += result.AwayTeamStats.Assists;
                awayTov += result.AwayTeamStats.Turnovers;
                awayPf += result.AwayTeamStats.Fouls;

                foreach (var ps in result.HomePlayerStats)
                {
                    if (homePlayerStats.TryGetValue(ps.PlayerId, out var statLine))
                    {
                        statLine.Points += ps.Points;
                        statLine.Rebounds += ps.Rebounds;
                        statLine.Assists += ps.Assists;
                        statLine.FieldGoalsMade += ps.FieldGoalsMade;
                        statLine.FieldGoalAttempts += ps.FieldGoalsAttempted;
                        statLine.ThreePointersMade += ps.ThreePointersMade;
                        statLine.ThreePointAttempts += ps.ThreePointersAttempted;
                        statLine.FreeThrowsMade += ps.FreeThrowsMade;
                        statLine.FreeThrowAttempts += ps.FreeThrowsAttempted;
                    }
                }

                foreach (var ps in result.AwayPlayerStats)
                {
                    if (awayPlayerStats.TryGetValue(ps.PlayerId, out var statLine))
                    {
                        statLine.Points += ps.Points;
                        statLine.Rebounds += ps.Rebounds;
                        statLine.Assists += ps.Assists;
                        statLine.FieldGoalsMade += ps.FieldGoalsMade;
                        statLine.FieldGoalAttempts += ps.FieldGoalsAttempted;
                        statLine.ThreePointersMade += ps.ThreePointersMade;
                        statLine.ThreePointAttempts += ps.ThreePointersAttempted;
                        statLine.FreeThrowsMade += ps.FreeThrowsMade;
                        statLine.FreeThrowAttempts += ps.FreeThrowsAttempted;
                    }
                }
            }

            report.AverageHomeScore = homeTotalScore / games;
            report.AverageAwayScore = awayTotalScore / games;
            report.AverageTotalScore = report.AverageHomeScore + report.AverageAwayScore;

            report.HomeFieldGoalPercent = homeFGA > 0 ? homeFGM / homeFGA : 0;
            report.AwayFieldGoalPercent = awayFGA > 0 ? awayFGM / awayFGA : 0;
            report.HomeThreePointPercent = home3PA > 0 ? home3PM / home3PA : 0;
            report.AwayThreePointPercent = away3PA > 0 ? away3PM / away3PA : 0;
            report.HomeFreeThrowPercent = homeFTA > 0 ? homeFTM / homeFTA : 0;
            report.AwayFreeThrowPercent = awayFTA > 0 ? awayFTM / awayFTA : 0;

            report.HomeThreePointAttempts = home3PA / games;
            report.AwayThreePointAttempts = away3PA / games;
            report.HomeFreeThrowAttempts = homeFTA / games;
            report.AwayFreeThrowAttempts = awayFTA / games;

            report.HomeRebounds = homeReb / games;
            report.AwayRebounds = awayReb / games;
            report.HomeAssists = homeAst / games;
            report.AwayAssists = awayAst / games;
            report.HomeTurnovers = homeTov / games;
            report.AwayTurnovers = awayTov / games;
            report.HomeFouls = homePf / games;
            report.AwayFouls = awayPf / games;

            foreach (var ps in homePlayerStats.Values)
            {
                ps.Points /= games;
                ps.Rebounds /= games;
                ps.Assists /= games;
                ps.FieldGoalsMade /= games;
                ps.FieldGoalAttempts /= games;
                ps.ThreePointersMade /= games;
                ps.ThreePointAttempts /= games;
                ps.FreeThrowsMade /= games;
                ps.FreeThrowAttempts /= games;
            }

            foreach (var ps in awayPlayerStats.Values)
            {
                ps.Points /= games;
                ps.Rebounds /= games;
                ps.Assists /= games;
                ps.FieldGoalsMade /= games;
                ps.FieldGoalAttempts /= games;
                ps.ThreePointersMade /= games;
                ps.ThreePointAttempts /= games;
                ps.FreeThrowsMade /= games;
                ps.FreeThrowAttempts /= games;
            }

            report.TopHomeScorers = homePlayerStats.Values.Where(p => p.FieldGoalAttempts > 0 || p.Points > 0 || p.Rebounds > 0 || p.Assists > 0).OrderByDescending(p => p.Points).ToList();
            report.TopAwayScorers = awayPlayerStats.Values.Where(p => p.FieldGoalAttempts > 0 || p.Points > 0 || p.Rebounds > 0 || p.Assists > 0).OrderByDescending(p => p.Points).ToList();

            return report;
        }
    }
}
