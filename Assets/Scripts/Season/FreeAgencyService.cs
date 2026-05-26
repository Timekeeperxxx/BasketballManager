using System.Collections.Generic;
using System.Linq;
using BasketballManager.Core.Models;
using BasketballManager.Database;
using UnityEngine;

namespace BasketballManager.Seasons
{
    public sealed class FreeAgencyService
    {
        public const int SalaryCap = 120;  // 百万

        private readonly TeamRepository   _teamRepository;
        private readonly PlayerRepository _playerRepository;

        public FreeAgencyService(TeamRepository teamRepository, PlayerRepository playerRepository)
        {
            _teamRepository   = teamRepository;
            _playerRepository = playerRepository;
        }

        /// <summary>
        /// 对所有真实球队的球员减1年合同；到期则转入自由球员池。
        /// 同时处理退役（年龄>38 或 OVR<50&&年龄>34）。
        /// 返回退役球员姓名列表。
        /// </summary>
        public List<string> ExpireContracts()
        {
            var retired = new List<string>();
            // 使用与 CreateNewSeasonFromAllTeams 一致的球队集合，避免 is_current 标记不一致导致 FA 池为空。
            var teams   = _teamRepository.GetAllTeams()
                              .Where(t => t.Id != "__FA__" && t.Id != "__DRAFT_POOL__")
                              .ToList();

            foreach (var team in teams)
            {
                var players = _playerRepository.GetPlayersByTeamId(team.Id);
                foreach (var p in players)
                {
                    // 退役判断
                    if (p.Age > 38 || (p.Overall < 50 && p.Age > 34))
                    {
                        p.IsCurrent = false;
                        p.TeamId    = "__FA__";
                        p.ContractYears  = 0;
                        p.ContractSalary = 0;
                        _playerRepository.UpdatePlayer(p);
                        retired.Add(p.GetDisplayName());
                        continue;
                    }

                    p.ContractYears--;
                    if (p.ContractYears <= 0)
                    {
                        int asking = CalcMarketValue(p.Overall);
                        p.TeamId         = "__FA__";
                        p.ContractYears  = 0;
                        p.ContractSalary = 0;
                        _playerRepository.UpdatePlayer(p);
                        _playerRepository.AddToFreeAgents(p.Id, asking);
                    }
                    else
                    {
                        _playerRepository.UpdatePlayer(p);
                    }
                }
            }
            return retired;
        }

        /// <summary>
        /// AI 球队在剩余自由球员中贪心签约（跳过用户球队）。
        /// 每队最多签2人，roster ≤ 15。
        /// </summary>
        public void RunAISignings(string excludeTeamId)
        {
            var teams  = _teamRepository.GetAllTeams()
                              .Where(t => t.Id != "__FA__" && t.Id != "__DRAFT_POOL__" && t.Id != excludeTeamId)
                              .ToList();
            var freeAgents = _playerRepository.GetFreeAgents()
                                              .OrderByDescending(p => p.Overall)
                                              .ToList();

            foreach (var team in teams)
            {
                int capUsed   = GetTeamSalary(team.Id);
                int rosterSize = _playerRepository.GetPlayersByTeamId(team.Id).Count;
                int signed    = 0;

                foreach (var fa in freeAgents.ToList())
                {
                    if (signed >= 2 || rosterSize >= 15) break;
                    int asking = _playerRepository.GetFreeAgentAskingSalary(fa.Id);
                    if (capUsed + asking > SalaryCap) continue;

                    SignPlayer(fa.Id, team.Id, asking, 2);
                    freeAgents.Remove(fa);
                    capUsed   += asking;
                    rosterSize++;
                    signed++;
                }
            }
        }

        public void SignPlayer(int playerId, string teamId, int salary, int years)
        {
            var player = _playerRepository.GetPlayerById(playerId);
            if (player == null) return;

            player.TeamId         = teamId;
            player.ContractSalary = salary;
            player.ContractYears  = years;
            _playerRepository.UpdatePlayer(player);
            _playerRepository.RemoveFromFreeAgents(playerId);
        }

        public int GetTeamSalary(string teamId)
        {
            var players = _playerRepository.GetPlayersByTeamId(teamId);
            int total = 0;
            foreach (var p in players) total += p.ContractSalary;
            return total;
        }

        public static int CalcMarketValue(int ovr)
        {
            if (ovr <= 70) return 3;
            if (ovr <= 80) return 5 + Mathf.RoundToInt(0.8f * (ovr - 70));
            if (ovr <= 90) return 13 + Mathf.RoundToInt(1.5f * (ovr - 80));
            return 28 + 2 * (ovr - 91);
        }
    }
}
