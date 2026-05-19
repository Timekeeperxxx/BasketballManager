using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BasketballManager.Core.Models;
using BasketballManager.Database;
using BasketballManager.Simulation;
using BasketballManager.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using Position = BasketballManager.Core.Enums.Position;

namespace BasketballManager.UI.Screens
{
    /// <summary>
    /// 调试模式（UI Toolkit）：选主客队、单场模拟、批量 100 场模拟。
    /// 选队采用 NBA 2K 快速比赛风格——大卡片 + 左右箭头切换。
    /// </summary>
    public sealed class DebugPanelScreen : UIToolkitScreenBase
    {
        public const string Id = "Debug";

        public event Action OnBackClicked;

        // 依赖
        private TeamRepository _teamRepository;
        private PlayerRepository _playerRepository;
        private SimulationProfileRepository _profileRepository;

        // 队伍
        private List<Team> _teams = new List<Team>();
        private int _homeIdx;
        private int _awayIdx;

        // UI 引用
        private Label _homeName, _homeSub, _awayName, _awaySub;
        private VisualElement _resultContent;
        private Label _resultEmpty;

        private void Awake() { ScreenId = Id; }

        public void Initialize(TeamRepository teamRepository, PlayerRepository playerRepository, SimulationProfileRepository profileRepository)
        {
            _teamRepository = teamRepository;
            _playerRepository = playerRepository;
            _profileRepository = profileRepository;
        }

        protected override void OnBuilt()
        {
            Root.Q<Button>("btn-back").clicked += () => OnBackClicked?.Invoke();

            _homeName = Root.Q<Label>("home-name");
            _homeSub  = Root.Q<Label>("home-sub");
            _awayName = Root.Q<Label>("away-name");
            _awaySub  = Root.Q<Label>("away-sub");

            Root.Q<Button>("btn-home-prev").clicked += () => StepHome(-1);
            Root.Q<Button>("btn-home-next").clicked += () => StepHome(+1);
            Root.Q<Button>("btn-away-prev").clicked += () => StepAway(-1);
            Root.Q<Button>("btn-away-next").clicked += () => StepAway(+1);

            Root.Q<Button>("btn-swap").clicked += SwapTeams;
            Root.Q<Button>("btn-sim").clicked += SimulateMatch;
            Root.Q<Button>("btn-batch").clicked += SimulateBatch;

            _resultContent = Root.Q<VisualElement>("result-content");
            _resultEmpty   = Root.Q<Label>("result-empty");
        }

        protected override void OnEnter()
        {
            if (_teamRepository == null) return;
            _teams = _teamRepository.GetAllTeams().ToList();
            if (_teams.Count >= 2)
            {
                _homeIdx = 0;
                _awayIdx = 1;
            }
            else
            {
                _homeIdx = 0;
                _awayIdx = 0;
            }
            RefreshTeamCards();
            ClearResults(showEmpty: true);
        }

        // ============================================================
        //  选队
        // ============================================================

        private void StepHome(int dir)
        {
            _homeIdx = ComputeNextIndex(_homeIdx, _awayIdx, dir);
            RefreshTeamCards();
            ClearResults(showEmpty: true);
        }

        private void StepAway(int dir)
        {
            _awayIdx = ComputeNextIndex(_awayIdx, _homeIdx, dir);
            RefreshTeamCards();
            ClearResults(showEmpty: true);
        }

        private int ComputeNextIndex(int current, int other, int dir)
        {
            if (_teams.Count == 0) return 0;
            int n = _teams.Count;
            int next = current;
            // 跳过和对方相同的队
            for (int i = 0; i < n; i++)
            {
                next = ((next + dir) % n + n) % n;
                if (next != other) break;
            }
            return next;
        }

        private void SwapTeams()
        {
            (_homeIdx, _awayIdx) = (_awayIdx, _homeIdx);
            RefreshTeamCards();
            ClearResults(showEmpty: true);
        }

        private void RefreshTeamCards()
        {
            if (_teams.Count == 0)
            {
                _homeName.text = _awayName.text = "—";
                _homeSub.text = _awaySub.text = "球队数据缺失";
                return;
            }
            var h = _teams[_homeIdx];
            var a = _teams[_awayIdx];
            _homeName.text = h.Name;
            _homeSub.text  = FormatSub(h);
            _awayName.text = a.Name;
            _awaySub.text  = FormatSub(a);
        }

        private static string FormatSub(Team t)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(t.City)) parts.Add(t.City);
            if (t.Era > 0) parts.Add(t.Era.ToString());
            return parts.Count > 0 ? string.Join(" · ", parts) : string.Empty;
        }

        // ============================================================
        //  模拟
        // ============================================================

        private void SimulateMatch()
        {
            if (_teams.Count < 2) return;
            var home = _teams[_homeIdx];
            var away = _teams[_awayIdx];
            if (home.Id == away.Id) return;

            var homePlayers = _playerRepository.GetPlayersByTeamId(home.Id);
            var awayPlayers = _playerRepository.GetPlayersByTeamId(away.Id);
            var profiles = _profileRepository.GetAllProfiles();

            var simulator = new MatchSimulator();
            var result = simulator.Simulate(home, homePlayers, profiles, away, awayPlayers, profiles, new MatchConfig());

            RenderSingleMatch(result);
        }

        private void SimulateBatch()
        {
            if (_teams.Count < 2) return;
            var home = _teams[_homeIdx];
            var away = _teams[_awayIdx];
            if (home.Id == away.Id) return;

            var homePlayers = _playerRepository.GetPlayersByTeamId(home.Id);
            var awayPlayers = _playerRepository.GetPlayersByTeamId(away.Id);
            var profiles = _profileRepository.GetAllProfiles();

            int seed = UnityEngine.Random.Range(0, 1000000);
            var report = new MatchSimulationBatchRunner().Run(
                home, homePlayers, profiles,
                away, awayPlayers, profiles,
                100, seed);

            RenderBatchReport(report);
        }

        // ============================================================
        //  渲染：清空 / 空态
        // ============================================================

        private void ClearResults(bool showEmpty)
        {
            // 清掉 result-content 里除 result-empty 之外的所有动态元素
            for (int i = _resultContent.childCount - 1; i >= 0; i--)
            {
                var child = _resultContent[i];
                if (child == _resultEmpty) continue;
                _resultContent.RemoveAt(i);
            }
            _resultEmpty.style.display = showEmpty ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ============================================================
        //  渲染：单场结果
        // ============================================================

        private void RenderSingleMatch(MatchResult result)
        {
            ClearResults(showEmpty: false);

            // 比分卡
            var scoreCard = NewCard();
            var headline = new Label($"{result.HomeTeamName}   {result.HomeScore} - {result.AwayScore}   {result.AwayTeamName}");
            headline.AddToClassList("score-headline");
            scoreCard.Add(headline);

            var qs = new StringBuilder();
            for (int i = 0; i < result.HomeQuarterScores.Count; i++)
            {
                if (i > 0) qs.Append("  |  ");
                string name = i < 4 ? $"Q{i + 1}" : $"OT{i - 3}";
                qs.Append($"{name} {result.HomeQuarterScores[i]}-{result.AwayQuarterScores[i]}");
            }
            var qLabel = new Label(qs.ToString());
            qLabel.AddToClassList("score-quarters");
            scoreCard.Add(qLabel);
            _resultContent.Add(scoreCard);

            // 球队统计
            var statsCard = NewCard();
            AddCardHeader(statsCard, "球队统计", result.HomeTeamName, result.AwayTeamName);
            var hs = result.HomeTeamStats;
            var asx = result.AwayTeamStats;
            AddStatRow(statsCard, "得分",   hs.Points.ToString(), asx.Points.ToString());
            AddStatRow(statsCard, "投篮",   $"{hs.FieldGoalsMade}/{hs.FieldGoalsAttempted}", $"{asx.FieldGoalsMade}/{asx.FieldGoalsAttempted}");
            AddStatRow(statsCard, "命中率", Pct(hs.FieldGoalsMade, hs.FieldGoalsAttempted),  Pct(asx.FieldGoalsMade, asx.FieldGoalsAttempted));
            AddStatRow(statsCard, "三分",   $"{hs.ThreePointersMade}/{hs.ThreePointersAttempted}", $"{asx.ThreePointersMade}/{asx.ThreePointersAttempted}");
            AddStatRow(statsCard, "三分率", Pct(hs.ThreePointersMade, hs.ThreePointersAttempted), Pct(asx.ThreePointersMade, asx.ThreePointersAttempted));
            AddStatRow(statsCard, "罚球",   $"{hs.FreeThrowsMade}/{hs.FreeThrowsAttempted}", $"{asx.FreeThrowsMade}/{asx.FreeThrowsAttempted}");
            AddStatRow(statsCard, "罚球率", Pct(hs.FreeThrowsMade, hs.FreeThrowsAttempted),   Pct(asx.FreeThrowsMade, asx.FreeThrowsAttempted));
            AddStatRow(statsCard, "篮板",   hs.Rebounds.ToString(),         asx.Rebounds.ToString());
            AddStatRow(statsCard, "前场板", hs.OffensiveRebounds.ToString(),asx.OffensiveRebounds.ToString());
            AddStatRow(statsCard, "助攻",   hs.Assists.ToString(),          asx.Assists.ToString());
            AddStatRow(statsCard, "抢断",   hs.Steals.ToString(),           asx.Steals.ToString());
            AddStatRow(statsCard, "盖帽",   hs.Blocks.ToString(),           asx.Blocks.ToString());
            AddStatRow(statsCard, "失误",   hs.Turnovers.ToString(),        asx.Turnovers.ToString());
            AddStatRow(statsCard, "犯规",   hs.Fouls.ToString(),            asx.Fouls.ToString());
            _resultContent.Add(statsCard);

            // 球员表（主/客）
            _resultContent.Add(BuildSinglePlayersCard(result.HomeTeamName, result.HomePlayerStats));
            _resultContent.Add(BuildSinglePlayersCard(result.AwayTeamName, result.AwayPlayerStats));
        }

        // ============================================================
        //  渲染：批量结果
        // ============================================================

        private void RenderBatchReport(MatchSimulationReport report)
        {
            ClearResults(showEmpty: false);

            // 总览
            var overviewCard = NewCard();
            var title = new Label($"{report.HomeTeamName}   vs   {report.AwayTeamName}");
            title.AddToClassList("batch-headline");
            overviewCard.Add(title);

            var meta = new Label($"模拟场次：{report.Games}");
            meta.AddToClassList("batch-meta");
            overviewCard.Add(meta);

            var hPct = report.Games > 0 ? (float)report.HomeWins / report.Games * 100f : 0f;
            var aPct = report.Games > 0 ? (float)report.AwayWins / report.Games * 100f : 0f;
            var record = new Label($"{report.HomeTeamName}: {report.HomeWins} 胜 ({hPct:F1}%)    |    {report.AwayTeamName}: {report.AwayWins} 胜 ({aPct:F1}%)");
            record.AddToClassList("batch-record");
            overviewCard.Add(record);

            var avg = new Label($"{report.HomeTeamName}   {report.AverageHomeScore:F1} - {report.AverageAwayScore:F1}   {report.AwayTeamName}");
            avg.AddToClassList("batch-avg");
            overviewCard.Add(avg);
            _resultContent.Add(overviewCard);

            // 警告
            var warnings = CollectWarnings(report);
            if (warnings.Count > 0)
            {
                var warnCard = new VisualElement();
                warnCard.AddToClassList("warning-card");
                foreach (var w in warnings)
                {
                    var l = new Label(w);
                    l.AddToClassList("warning-line");
                    warnCard.Add(l);
                }
                _resultContent.Add(warnCard);
            }

            // 核心效率
            var effCard = NewCard();
            AddCardHeader(effCard, "核心效率", report.HomeTeamName, report.AwayTeamName);
            AddStatRow(effCard, "命中率",   $"{report.HomeFieldGoalPercent * 100:F1}%", $"{report.AwayFieldGoalPercent * 100:F1}%");
            AddStatRow(effCard, "三分率",   $"{report.HomeThreePointPercent * 100:F1}%", $"{report.AwayThreePointPercent * 100:F1}%");
            AddStatRow(effCard, "罚球率",   $"{report.HomeFreeThrowPercent * 100:F1}%", $"{report.AwayFreeThrowPercent * 100:F1}%");
            AddStatRow(effCard, "三分出手", report.HomeThreePointAttempts.ToString("F1"), report.AwayThreePointAttempts.ToString("F1"));
            AddStatRow(effCard, "罚球出手", report.HomeFreeThrowAttempts.ToString("F1"), report.AwayFreeThrowAttempts.ToString("F1"));
            AddStatRow(effCard, "篮板",     report.HomeRebounds.ToString("F1"), report.AwayRebounds.ToString("F1"));
            AddStatRow(effCard, "前场板",   report.HomeOffensiveRebounds.ToString("F1"), report.AwayOffensiveRebounds.ToString("F1"));
            AddStatRow(effCard, "助攻",     report.HomeAssists.ToString("F1"), report.AwayAssists.ToString("F1"));
            AddStatRow(effCard, "助攻率",   $"{report.HomeAssistRate * 100:F1}%", $"{report.AwayAssistRate * 100:F1}%");
            AddStatRow(effCard, "失误",     report.HomeTurnovers.ToString("F1"), report.AwayTurnovers.ToString("F1"));
            AddStatRow(effCard, "犯规",     report.HomeFouls.ToString("F1"), report.AwayFouls.ToString("F1"));
            _resultContent.Add(effCard);

            // 球队风格
            if (report.HomeStyleProfile != null && report.AwayStyleProfile != null)
            {
                var styleCard = NewCard();
                AddCardHeader(styleCard, "球队风格指标", report.HomeTeamName, report.AwayTeamName);
                var h = report.HomeStyleProfile;
                var a = report.AwayStyleProfile;
                AddStatRow(styleCard, "回合数",   h.PaceModifier.ToString("F2"),               a.PaceModifier.ToString("F2"));
                AddStatRow(styleCard, "空间",     h.SpacingModifier.ToString("F2"),            a.SpacingModifier.ToString("F2"));
                AddStatRow(styleCard, "三分牵制", h.ThreeGravity.ToString("F2"),               a.ThreeGravity.ToString("F2"));
                AddStatRow(styleCard, "攻框压力", h.RimPressure.ToString("F2"),                a.RimPressure.ToString("F2"));
                AddStatRow(styleCard, "组织梳理", h.AssistModifier.ToString("F2"),             a.AssistModifier.ToString("F2"));
                AddStatRow(styleCard, "前场拼抢", h.OffensiveReboundModifier.ToString("F2"),   a.OffensiveReboundModifier.ToString("F2"));
                AddStatRow(styleCard, "护框",     h.RimProtection.ToString("F2"),              a.RimProtection.ToString("F2"));
                AddStatRow(styleCard, "换防",     h.SwitchDefense.ToString("F2"),              a.SwitchDefense.ToString("F2"));
                AddStatRow(styleCard, "后场保护", h.DefensiveReboundModifier.ToString("F2"),   a.DefensiveReboundModifier.ToString("F2"));
                AddStatRow(styleCard, "失误控制", h.TurnoverControl.ToString("F2"),            a.TurnoverControl.ToString("F2"));
                AddStatRow(styleCard, "犯规压迫", h.FoulPressure.ToString("F2"),               a.FoulPressure.ToString("F2"));
                _resultContent.Add(styleCard);
            }

            // 首发阵容
            var startersCard = NewCard();
            AddCardHeader(startersCard, "首发阵容", report.HomeTeamName, report.AwayTeamName);
            foreach (var pos in new[] { Position.PG, Position.SG, Position.SF, Position.PF, Position.C })
            {
                string h = report.HomeStarters.TryGetValue(pos, out var hv) ? hv : "-";
                string a = report.AwayStarters.TryGetValue(pos, out var av) ? av : "-";
                AddStatRow(startersCard, pos.ToString(), h, a);
            }
            _resultContent.Add(startersCard);

            // 球员表
            _resultContent.Add(BuildBatchPlayersCard(report.HomeTeamName, report.TopHomeScorers));
            _resultContent.Add(BuildBatchPlayersCard(report.AwayTeamName, report.TopAwayScorers));
        }

        // ============================================================
        //  球员表（MultiColumnListView）
        // ============================================================

        private VisualElement BuildSinglePlayersCard(string teamName, List<PlayerBoxScore> stats)
        {
            var card = NewCard();
            card.AddToClassList("players-card");

            var header = new VisualElement();
            header.AddToClassList("card-header");
            var t = new Label(teamName);
            t.AddToClassList("card-title");
            header.Add(t);
            card.Add(header);

            var data = stats.Where(s => s.Minutes > 0).OrderByDescending(s => s.Points).ToList();
            var view = new MultiColumnListView
            {
                itemsSource = data,
                showFoldoutHeader = false,
                reorderable = false,
                showBorder = false,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
            };

            view.columns.Add(Col("name", "球员", 140, Length.Percent(22), () => Cell(),                     (e, i) => ((Label)e).text = data[i].PlayerName));
            view.columns.Add(Col("min",  "分钟", 50,  Length.Percent(7),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = data[i].Minutes.ToString()));
            view.columns.Add(Col("pts",  "得分", 50,  Length.Percent(8),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Points.ToString()));
            view.columns.Add(Col("reb",  "篮板", 50,  Length.Percent(8),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Rebounds.ToString()));
            view.columns.Add(Col("ast",  "助攻", 50,  Length.Percent(8),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Assists.ToString()));
            view.columns.Add(Col("pm",   "+/-", 50,  Length.Percent(7),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var lbl = (Label)e;
                int pm = data[i].PlusMinus;
                lbl.text = pm > 0 ? $"+{pm}" : pm.ToString();
                lbl.RemoveFromClassList("table-cell--pos");
                lbl.RemoveFromClassList("table-cell--neg");
                if (pm > 0) lbl.AddToClassList("table-cell--pos");
                else if (pm < 0) lbl.AddToClassList("table-cell--neg");
            }));
            view.columns.Add(Col("fg",   "投篮", 80,  Length.Percent(13), () => Cell("table-cell--center"), (e, i) => ((Label)e).text = $"{data[i].FieldGoalsMade}/{data[i].FieldGoalsAttempted}"));
            view.columns.Add(Col("tp",   "三分", 80,  Length.Percent(13), () => Cell("table-cell--center"), (e, i) => ((Label)e).text = $"{data[i].ThreePointersMade}/{data[i].ThreePointersAttempted}"));
            view.columns.Add(Col("ft",   "罚球", 80,  Length.Percent(14), () => Cell("table-cell--center"), (e, i) => ((Label)e).text = $"{data[i].FreeThrowsMade}/{data[i].FreeThrowsAttempted}"));

            card.Add(view);
            return card;
        }

        private VisualElement BuildBatchPlayersCard(string teamName, List<PlayerAverageStatLine> stats)
        {
            var card = NewCard();
            card.AddToClassList("players-card");

            var header = new VisualElement();
            header.AddToClassList("card-header");
            var t = new Label($"{teamName} 轮换球员");
            t.AddToClassList("card-title");
            header.Add(t);
            card.Add(header);

            var data = stats;
            var view = new MultiColumnListView
            {
                itemsSource = data,
                showFoldoutHeader = false,
                reorderable = false,
                showBorder = false,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
            };

            view.columns.Add(Col("name", "球员", 140, Length.Percent(20), () => Cell(),                     (e, i) => ((Label)e).text = data[i].PlayerName));
            view.columns.Add(Col("pos",  "位置", 60,  Length.Percent(7),  () => Cell("table-cell--center"), (e, i) =>
            {
                var p = data[i];
                ((Label)e).text = p.SecondaryPosition.HasValue ? $"{p.PrimaryPosition}/{p.SecondaryPosition.Value}" : p.PrimaryPosition.ToString();
            }));
            view.columns.Add(Col("min", "分钟", 50,  Length.Percent(7),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Minutes.ToString("F1")));
            view.columns.Add(Col("pts", "得分", 50,  Length.Percent(7),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Points.ToString("F1")));
            view.columns.Add(Col("reb", "篮板", 50,  Length.Percent(7),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Rebounds.ToString("F1")));
            view.columns.Add(Col("ast", "助攻", 50,  Length.Percent(7),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Assists.ToString("F1")));
            view.columns.Add(Col("pm",  "+/-", 50,  Length.Percent(7),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var lbl = (Label)e;
                float pm = data[i].PlusMinus;
                lbl.text = pm > 0 ? $"+{pm:F1}" : pm.ToString("F1");
                lbl.RemoveFromClassList("table-cell--pos");
                lbl.RemoveFromClassList("table-cell--neg");
                if (pm > 0.05f) lbl.AddToClassList("table-cell--pos");
                else if (pm < -0.05f) lbl.AddToClassList("table-cell--neg");
            }));
            view.columns.Add(Col("fg", "投篮", 110, Length.Percent(13), () => Cell("table-cell--center"), (e, i) =>
            {
                var p = data[i];
                ((Label)e).text = FormatMakeAtt(p.FieldGoalsMade, p.FieldGoalAttempts);
            }));
            view.columns.Add(Col("tp", "三分", 110, Length.Percent(13), () => Cell("table-cell--center"), (e, i) =>
            {
                var p = data[i];
                ((Label)e).text = FormatMakeAtt(p.ThreePointersMade, p.ThreePointAttempts);
            }));
            view.columns.Add(Col("ft", "罚球", 110, Length.Percent(12), () => Cell("table-cell--center"), (e, i) =>
            {
                var p = data[i];
                ((Label)e).text = FormatMakeAtt(p.FreeThrowsMade, p.FreeThrowAttempts);
            }));

            card.Add(view);
            return card;
        }

        // ============================================================
        //  警告
        // ============================================================

        private static List<string> CollectWarnings(MatchSimulationReport r)
        {
            var w = new List<string>();
            if (r.AverageTotalScore < 180 || r.AverageTotalScore > 270)
                w.Add($"[WARN] 平均总分 {r.AverageTotalScore:F1} 异常（正常 180-270）");

            CheckPct(w, "FG%", r.HomeFieldGoalPercent, r.AwayFieldGoalPercent, 0.38f, 0.58f);
            CheckPct(w, "3P%", r.HomeThreePointPercent, r.AwayThreePointPercent, 0.25f, 0.48f);
            CheckPct(w, "FT%", r.HomeFreeThrowPercent, r.AwayFreeThrowPercent, 0.60f, 0.95f);
            CheckRange(w, "TOV", r.HomeTurnovers, r.AwayTurnovers, 5f, 25f);
            CheckRange(w, "FTA", r.HomeFreeThrowAttempts, r.AwayFreeThrowAttempts, 5f, 40f);
            CheckRange(w, "REB", r.HomeRebounds, r.AwayRebounds, 25f, 70f);
            return w;
        }

        private static void CheckPct(List<string> w, string label, float h, float a, float min, float max)
        {
            if (h < min || h > max) w.Add($"[WARN] 主队 {label} {h * 100:F1}% 异常（正常 {min * 100:F0}-{max * 100:F0}%）");
            if (a < min || a > max) w.Add($"[WARN] 客队 {label} {a * 100:F1}% 异常（正常 {min * 100:F0}-{max * 100:F0}%）");
        }

        private static void CheckRange(List<string> w, string label, float h, float a, float min, float max)
        {
            if (h < min || h > max) w.Add($"[WARN] 主队 {label} {h:F1} 异常（正常 {min:F0}-{max:F0}）");
            if (a < min || a > max) w.Add($"[WARN] 客队 {label} {a:F1} 异常（正常 {min:F0}-{max:F0}）");
        }

        // ============================================================
        //  helpers
        // ============================================================

        private static VisualElement NewCard()
        {
            var v = new VisualElement();
            v.AddToClassList("card");
            return v;
        }

        private static void AddCardHeader(VisualElement card, string title, string homeName, string awayName)
        {
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            head.style.marginBottom = 8;

            var titleLbl = new Label(title);
            titleLbl.AddToClassList("card-title");
            titleLbl.style.flexGrow = 1f;
            head.Add(titleLbl);

            var hL = new Label(homeName);
            hL.AddToClassList("caption");
            hL.style.width = 110;
            hL.style.unityTextAlign = TextAnchor.MiddleRight;
            head.Add(hL);

            var spacer = new Label("");
            spacer.style.width = 24;
            head.Add(spacer);

            var aL = new Label(awayName);
            aL.AddToClassList("caption");
            aL.style.width = 110;
            aL.style.unityTextAlign = TextAnchor.MiddleLeft;
            head.Add(aL);

            card.Add(head);
        }

        private static void AddStatRow(VisualElement card, string label, string home, string away)
        {
            var row = new VisualElement();
            row.AddToClassList("stat-row");

            var h = new Label(home);
            h.AddToClassList("stat-row__home");
            row.Add(h);

            var l = new Label(label);
            l.AddToClassList("stat-row__label");
            row.Add(l);

            var a = new Label(away);
            a.AddToClassList("stat-row__away");
            row.Add(a);

            card.Add(row);
        }

        private static Column Col(string name, string title, int minWidth, Length width,
            Func<VisualElement> make, Action<VisualElement, int> bind)
        {
            return new Column
            {
                name = name,
                title = title,
                minWidth = minWidth,
                width = width,
                stretchable = true,
                makeCell = make,
                bindCell = bind,
            };
        }

        private static Label Cell(params string[] extraClasses)
        {
            var lbl = new Label();
            lbl.AddToClassList("table-cell");
            if (extraClasses != null)
            {
                foreach (var c in extraClasses)
                {
                    if (!string.IsNullOrEmpty(c)) lbl.AddToClassList(c);
                }
            }
            return lbl;
        }

        private static string Pct(int made, int att) => att <= 0 ? "—" : $"{(float)made / att * 100f:F1}%";

        private static string FormatMakeAtt(float made, float att)
        {
            string pct = att > 0f ? $"{made / att * 100f:F1}%" : "-";
            return $"{made:F1}/{att:F1} ({pct})";
        }
    }
}
