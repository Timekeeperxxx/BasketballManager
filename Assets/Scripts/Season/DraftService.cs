using System.Collections.Generic;
using System.Linq;
using BasketballManager.Core.Models;
using BasketballManager.Core.Services;
using BasketballManager.Database;

namespace BasketballManager.Seasons
{
    public sealed class DraftService
    {
        private readonly PlayerRepository   _playerRepository;
        private readonly SeasonRepository   _seasonRepository;
        private readonly PlayerGenerator    _generator;

        public DraftService(
            PlayerRepository   playerRepository,
            SeasonRepository   seasonRepository,
            PlayerGenerator    generator)
        {
            _playerRepository = playerRepository;
            _seasonRepository = seasonRepository;
            _generator        = generator;
        }

        /// <summary>
        /// 按上赛季积分榜战绩（升序，垫底优先）生成2轮选秀顺序。
        /// </summary>
        public List<DraftSlot> BuildDraftOrder(int seasonId)
        {
            var standings = _seasonRepository.GetStandings(seasonId);
            // 去除虚拟球队
            var real = standings
                .Where(s => s.TeamId != "__FA__" && s.TeamId != "__DRAFT_POOL__")
                .OrderBy(s => s.Wins)
                .ToList();

            var slots = new List<DraftSlot>();
            int pick = 1;
            // 第一轮
            foreach (var s in real)
                slots.Add(new DraftSlot { Pick = pick++, Round = 1, TeamId = s.TeamId, TeamName = s.TeamName });
            // 第二轮（顺序）
            foreach (var s in real)
                slots.Add(new DraftSlot { Pick = pick++, Round = 2, TeamId = s.TeamId, TeamName = s.TeamName });

            return slots;
        }

        /// <summary>
        /// 为本届选秀生成新秀池（总人数 = 球队数 × 2），写入 DB。
        /// </summary>
        public List<Player> GenerateDraftClass(int draftYear, int teamCount)
        {
            _playerRepository.ClearDraftClass();

            int total = teamCount * 2;
            var prospects = new List<Player>(total);

            for (int i = 0; i < total; i++)
            {
                int ovrMin, ovrMax;
                if      (i < 3)          { ovrMin = 80; ovrMax = 85; }  // 状元/榜眼/探花
                else if (i < teamCount)  { ovrMin = 70; ovrMax = 79; }  // 首轮其余
                else                     { ovrMin = 55; ovrMax = 69; }  // 第二轮

                var p = _generator.Generate("__DRAFT_POOL__", ovrMin, ovrMax, draftYear);
                int pid = _playerRepository.InsertPlayer(p);
                _playerRepository.AddToDraftClass(pid, draftYear);
                p.Id = pid;
                prospects.Add(p);
            }

            return prospects;
        }

        /// <summary>
        /// AI 代选：从剩余新秀中选综合值最高的。
        /// </summary>
        public Player AIAutoPick(IList<Player> available)
        {
            if (available.Count == 0) return null;
            Player best = available[0];
            foreach (var p in available)
                if (p.Overall > best.Overall) best = p;
            return best;
        }

        /// <summary>
        /// 将选中的新秀分配给球队，并赋予合同。
        /// </summary>
        public void AssignPick(Player prospect, string teamId, int pickNumber, int totalTeams)
        {
            int salary, years;
            if      (pickNumber <= totalTeams / 3) { salary = 6; years = 3; }
            else if (pickNumber <= totalTeams)      { salary = 4; years = 3; }
            else                                    { salary = 1; years = 3; }

            prospect.TeamId         = teamId;
            prospect.ContractSalary = salary;
            prospect.ContractYears  = years;
            _playerRepository.UpdatePlayer(prospect);
            _playerRepository.UpdateSimProfileTeam(prospect.Id, teamId);
        }
    }

    public sealed class DraftSlot
    {
        public int    Pick;
        public int    Round;
        public string TeamId;
        public string TeamName;
        public Player SelectedPlayer;  // null = 未选
    }
}
