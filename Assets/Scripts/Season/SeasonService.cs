using System;
using System.Collections.Generic;
using System.Linq;
using BasketballManager.Core.Models;
using BasketballManager.Core.Services;
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
        private readonly PlayerDevelopmentService _developmentService;
        private readonly FreeAgencyService _freeAgencyService;
        private readonly DraftService _draftService;

        public SeasonService(
            TeamRepository teamRepository,
            PlayerRepository playerRepository,
            SimulationProfileRepository profileRepository,
            SeasonRepository seasonRepository,
            PlayerDevelopmentService developmentService = null,
            FreeAgencyService freeAgencyService = null,
            DraftService draftService = null)
        {
            _teamRepository     = teamRepository;
            _playerRepository   = playerRepository;
            _profileRepository  = profileRepository;
            _seasonRepository   = seasonRepository;
            _developmentService = developmentService ?? new PlayerDevelopmentService();
            _freeAgencyService  = freeAgencyService;
            _draftService       = draftService;
        }

        public FreeAgencyService FreeAgency => _freeAgencyService;
        public DraftService Draft => _draftService;

        /// <summary>
        /// 用当前数据库里的所有球队建一个新赛季。返回新赛季 id（teams &lt; 2 时返回 0）。
        /// </summary>
        public int CreateNewSeasonFromAllTeams(string name, int seasonNumber = 1)
        {
            // 排除 __FA__ / __DRAFT_POOL__ 虚拟球队，只用真实参赛球队建赛程。
            var teams = _teamRepository.GetAllTeams()
                .Where(t => t.Id != "__FA__" && t.Id != "__DRAFT_POOL__")
                .ToList();
            if (teams == null || teams.Count < 2) return 0;

            var games = ScheduleGenerator.Generate(teams);
            return _seasonRepository.CreateSeason(name, teams, games, seasonNumber);
        }

        /// <summary>
        /// 休赛期第一阶段：球员发展 + 合同到期 + 标记自由球员。
        /// 返回发展结果列表与退役球员姓名列表（供 UI 展示）。
        /// </summary>
        public (List<PlayerDevelopmentResult> devResults, List<string> retiredNames)
            AdvanceOffseasonPhase1(int currentSeasonId)
        {
            var allPlayers = _playerRepository.GetAllPlayers();
            var devResults = _developmentService.ApplySeasonEndDevelopment(allPlayers, _playerRepository);

            List<string> retiredNames = new List<string>();
            if (_freeAgencyService != null)
                retiredNames = _freeAgencyService.ExpireContracts();

            return (devResults, retiredNames);
        }

        /// <summary>
        /// 休赛期第二阶段：AI 签约 + 创建新赛季。
        /// 需在选秀结束后调用。
        /// </summary>
        public int AdvanceOffseasonPhase2(int currentSeasonId, string userTeamId)
        {
            _freeAgencyService?.RunAISignings(userTeamId);

            var currentSeason = _seasonRepository.GetSeasonById(currentSeasonId);
            int nextNumber    = (currentSeason?.SeasonNumber ?? 0) + 1;
            string name       = $"第{nextNumber}赛季";
            return CreateNewSeasonFromAllTeams(name, nextNumber);
        }

        /// <summary>
        /// 赛季结束后推进到下一赛季（兼容旧调用，不含 FA/选秀系统）。
        /// </summary>
        public (int newSeasonId, List<PlayerDevelopmentResult> devResults)
            AdvanceToNextSeason(int currentSeasonId)
        {
            var currentSeason = _seasonRepository.GetSeasonById(currentSeasonId);
            int nextNumber    = (currentSeason?.SeasonNumber ?? 0) + 1;

            var allPlayers = _playerRepository.GetAllPlayers();
            var devResults = _developmentService.ApplySeasonEndDevelopment(allPlayers, _playerRepository);

            string name       = $"第{nextNumber}赛季";
            int    newSeasonId = CreateNewSeasonFromAllTeams(name, nextNumber);

            return (newSeasonId, devResults);
        }

        /// <summary>
        /// 模拟当前赛季下一场未打的比赛，返回结果（赛程已打完则返回 null）。
        /// </summary>
        public MatchResult SimulateNextGame(int seasonId, bool enablePlayByPlay = false)
        {
            var nextGame = _seasonRepository.GetNextScheduledGame(seasonId);
            if (nextGame == null) return null;

            return SimulateOne(seasonId, nextGame, enablePlayByPlay);
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

        /// <summary>
        /// 常规赛是否全部打完（phase='REGULAR' 且无剩余 SCHEDULED 对局）。
        /// </summary>
        public bool IsRegularSeasonComplete(int seasonId)
        {
            var season = _seasonRepository.GetSeasonById(seasonId);
            if (season == null || season.Phase != "REGULAR") return false;
            return _seasonRepository.GetNextScheduledGame(seasonId) == null;
        }

        /// <summary>
        /// 按积分榜前N名（最多8队）初始化季后赛，生成系列赛对阵与对局。
        /// </summary>
        public void StartPlayoffs(int seasonId)
        {
            var standings = _seasonRepository.GetStandings(seasonId);
            int n = Math.Min(8, standings.Count);
            if (n < 2) return;

            var series = new List<PlayoffSeries>();
            for (int i = 0; i < n / 2; i++)
            {
                series.Add(new PlayoffSeries
                {
                    SeasonId = seasonId,
                    Round = 1,
                    Seed1 = i + 1,
                    Seed2 = n - i,
                    Team1Id = standings[i].TeamId,
                    Team2Id = standings[n - 1 - i].TeamId,
                });
            }
            _seasonRepository.InitializePlayoffs(seasonId, series);
        }

        // -------- 内部 --------

        private MatchResult SimulateOne(int seasonId, SeasonGame game, bool enablePlayByPlay = false)
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
            var config = new MatchConfig { EnablePlayByPlay = enablePlayByPlay };
            var result = simulator.Simulate(home, homePlayers, allProfiles, away, awayPlayers, allProfiles, config);

            _seasonRepository.SaveGameResult(game.Id, result);
            _seasonRepository.SaveGamePlayerStats(game.Id, result);
            _seasonRepository.SaveGameZoneStats(game.Id, result);

            if (game.Phase == "PLAYOFF")
            {
                bool seriesComplete = _seasonRepository.UpdatePlayoffSeries(game.SeriesId, result);
                _seasonRepository.UpdatePlayoffPlayerStats(seasonId, result);
                _seasonRepository.UpdatePlayoffZoneStats(seasonId, result);
                if (seriesComplete)
                    _seasonRepository.TryAdvancePlayoffRound(seasonId);
            }
            else
            {
                _seasonRepository.UpdateStandings(seasonId, result);
                _seasonRepository.UpdatePlayerStats(seasonId, result);
                _seasonRepository.UpdateSeasonZoneStats(seasonId, result);
            }

            return result;
        }
    }
}
