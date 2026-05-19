using System.Collections.Generic;
using BasketballManager.Core.Models;
using BasketballManager.Database;
using BasketballManager.Simulation;

namespace BasketballManager.Seasons
{
    /// <summary>
    /// 赛季业务编排：创建赛季、模拟下一场 / 剩余全部比赛。
    /// 复用现有 MatchSimulator 跑单场，不动其核心算法。
    /// </summary>
    public sealed class SeasonService
    {
        private readonly TeamRepository _teamRepository;
        private readonly PlayerRepository _playerRepository;
        private readonly SimulationProfileRepository _profileRepository;
        private readonly SeasonRepository _seasonRepository;

        public SeasonService(
            TeamRepository teamRepository,
            PlayerRepository playerRepository,
            SimulationProfileRepository profileRepository,
            SeasonRepository seasonRepository)
        {
            _teamRepository = teamRepository;
            _playerRepository = playerRepository;
            _profileRepository = profileRepository;
            _seasonRepository = seasonRepository;
        }

        /// <summary>
        /// 用当前数据库里的所有球队建一个新赛季。返回新赛季 id（teams &lt; 2 时返回 0）。
        /// </summary>
        public int CreateNewSeasonFromAllTeams(string name)
        {
            var teams = _teamRepository.GetAllTeams();
            if (teams == null || teams.Count < 2) return 0;

            var games = ScheduleGenerator.Generate(teams);
            return _seasonRepository.CreateSeason(name, teams, games);
        }

        /// <summary>
        /// 模拟当前赛季下一场未打的比赛，返回结果（赛程已打完则返回 null）。
        /// </summary>
        public MatchResult SimulateNextGame(int seasonId)
        {
            var nextGame = _seasonRepository.GetNextScheduledGame(seasonId);
            if (nextGame == null) return null;

            return SimulateOne(seasonId, nextGame);
        }

        /// <summary>
        /// 一次性模拟到赛季结束。返回模拟的场次数。
        /// </summary>
        public int SimulateAllRemainingGames(int seasonId)
        {
            int count = 0;
            while (true)
            {
                var nextGame = _seasonRepository.GetNextScheduledGame(seasonId);
                if (nextGame == null) break;

                SimulateOne(seasonId, nextGame);
                count++;
            }
            return count;
        }

        // -------- 内部 --------

        private MatchResult SimulateOne(int seasonId, SeasonGame game)
        {
            var teams = _teamRepository.GetAllTeams();
            Team home = null, away = null;
            foreach (var t in teams)
            {
                if (t.Id == game.HomeTeamId) home = t;
                else if (t.Id == game.AwayTeamId) away = t;
            }
            if (home == null || away == null) return null;

            var homePlayers = _playerRepository.GetPlayersByTeamId(home.Id);
            var awayPlayers = _playerRepository.GetPlayersByTeamId(away.Id);
            var allProfiles = _profileRepository.GetAllProfiles();

            var simulator = new MatchSimulator();
            var config = new MatchConfig();
            var result = simulator.Simulate(home, homePlayers, allProfiles, away, awayPlayers, allProfiles, config);

            _seasonRepository.SaveGameResult(game.Id, result);
            _seasonRepository.UpdateStandings(seasonId, result);
            _seasonRepository.UpdatePlayerStats(seasonId, result);

            return result;
        }
    }
}
