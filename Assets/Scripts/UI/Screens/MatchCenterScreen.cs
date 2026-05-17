using System.Collections.Generic;
using System.Linq;
using BasketballManager.Core.Models;
using BasketballManager.Database;
using BasketballManager.Simulation;
using BasketballManager.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace BasketballManager.UI.Screens
{
    public sealed class MatchCenterScreen : UIScreenBase
    {
        private DatabaseManager _databaseManager;
        private TeamRepository _teamRepository;
        private PlayerRepository _playerRepository;

        private RectTransform _rootPanel;
        private RectTransform _resultsPanel;

        private IReadOnlyList<Team> _teams;
        private Team _homeTeam;
        private Team _awayTeam;

        private List<Image> _homeTeamButtons = new List<Image>();
        private List<Image> _awayTeamButtons = new List<Image>();

        private Color _btnActiveColor = new Color(0.23f, 0.49f, 0.81f);
        private Color _btnInactiveColor = new Color(0.18f, 0.20f, 0.26f);
        private Color _btnDisabledColor = new Color(0.12f, 0.13f, 0.15f);

        public void Initialize(DatabaseManager databaseManager, TeamRepository teamRepository, PlayerRepository playerRepository)
        {
            _databaseManager = databaseManager;
            _teamRepository = teamRepository;
            _playerRepository = playerRepository;
        }

        public void Show()
        {
            if (_rootPanel != null) _rootPanel.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (_rootPanel != null) _rootPanel.gameObject.SetActive(false);
        }

        public void BuildUi(RectTransform parent)
        {
            _teams = _teamRepository.GetAllTeams();
            if (_teams.Count >= 2)
            {
                _homeTeam = _teams[0];
                _awayTeam = _teams[1];
            }

            _rootPanel = CreatePanel("MatchCenterRoot", parent, new Color(0.08f, 0.09f, 0.11f));
            Stretch(_rootPanel);

            var mainLayout = _rootPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            mainLayout.padding = new RectOffset(16, 16, 16, 16);
            mainLayout.spacing = 16f;
            mainLayout.childForceExpandHeight = false;
            mainLayout.childForceExpandWidth = true;
            mainLayout.childControlHeight = true;
            mainLayout.childControlWidth = true;

            BuildSettingsArea(_rootPanel);
            
            _resultsPanel = CreateFlexiblePanel(_rootPanel);
            
            RefreshTeamSelections();
        }

        private void BuildSettingsArea(RectTransform parent)
        {
            var settingsPanel = CreatePanel("SettingsPanel", parent, new Color(0.11f, 0.12f, 0.16f));
            var layout = settingsPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 12f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            settingsPanel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateHeader(settingsPanel, "\u6bd4\u8d5b\u8bbe\u7f6e", 24); // 比赛设置

            var selectionRow = CreatePanel("SelectionRow", settingsPanel, Color.clear);
            var rowLayout = selectionRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 20f;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlHeight = true;
            rowLayout.childControlWidth = true;
            LayoutElementWithHeight(selectionRow.gameObject, 50f);

            // Home Team Selection
            var homeGroup = CreatePanel("HomeGroup", selectionRow, Color.clear);
            var homeLayout = homeGroup.gameObject.AddComponent<HorizontalLayoutGroup>();
            homeLayout.spacing = 8f;
            homeLayout.childForceExpandWidth = false;
            homeLayout.childForceExpandHeight = false;
            homeLayout.childControlHeight = true;
            homeLayout.childControlWidth = true;
            CreateBodyText(homeGroup, "\u4e3b\u961f:").color = new Color(0.8f, 0.8f, 0.8f); // 主队:
            
            foreach (var team in _teams)
            {
                var t = team;
                var shortName = t.Name.Contains(" ") ? t.Name.Substring(t.Name.LastIndexOf(' ') + 1) : t.Name;
                var label = t.Era > 0 ? $"{t.Era} {shortName}" : shortName;
                var btn = CreateButton(homeGroup, label, () => SelectHomeTeam(t));
                LayoutElementWithWidth(btn.gameObject, 180f);
                _homeTeamButtons.Add(btn.GetComponent<Image>());
            }

            // Swap Button
            var swapBtn = CreateButton(selectionRow, "\u21c4 \u4ea4\u6362", SwapTeams); // ⇄ 交换
            LayoutElementWithWidth(swapBtn.gameObject, 100f);

            // Away Team Selection
            var awayGroup = CreatePanel("AwayGroup", selectionRow, Color.clear);
            var awayLayout = awayGroup.gameObject.AddComponent<HorizontalLayoutGroup>();
            awayLayout.spacing = 8f;
            awayLayout.childForceExpandWidth = false;
            awayLayout.childForceExpandHeight = false;
            awayLayout.childControlHeight = true;
            awayLayout.childControlWidth = true;
            CreateBodyText(awayGroup, "\u5ba2\u961f:").color = new Color(0.8f, 0.8f, 0.8f); // 客队:
            
            foreach (var team in _teams)
            {
                var t = team;
                var shortName = t.Name.Contains(" ") ? t.Name.Substring(t.Name.LastIndexOf(' ') + 1) : t.Name;
                var label = t.Era > 0 ? $"{t.Era} {shortName}" : shortName;
                var btn = CreateButton(awayGroup, label, () => SelectAwayTeam(t));
                LayoutElementWithWidth(btn.gameObject, 180f);
                _awayTeamButtons.Add(btn.GetComponent<Image>());
            }

            var simBtnRow = CreatePanel("SimBtnRow", settingsPanel, Color.clear);
            var simLayout = simBtnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            simLayout.childAlignment = TextAnchor.MiddleRight;
            simLayout.spacing = 16f;
            simLayout.childForceExpandWidth = true;
            simLayout.childForceExpandHeight = false;
            simLayout.childControlHeight = true;
            simLayout.childControlWidth = true;
            LayoutElementWithHeight(simBtnRow.gameObject, 50f);
            
            var batchSimBtn = CreateButton(simBtnRow, "\u25b6 \u6279\u91cf\u6d4b\u8bd5100\u573a", SimulateBatch); // ▶ 批量测试100场
            batchSimBtn.GetComponent<Image>().color = new Color(0.81f, 0.43f, 0.23f); // Orange
            LayoutElementWithWidth(batchSimBtn.gameObject, 180f);

            var simBtn = CreateButton(simBtnRow, "\u25b6 \u6a21\u62df\u6bd4\u8d5b", SimulateMatch); // ▶ 模拟比赛
            simBtn.GetComponent<Image>().color = new Color(0.81f, 0.23f, 0.23f); // Reddish to stand out
            LayoutElementWithWidth(simBtn.gameObject, 160f);
        }

        private void SelectHomeTeam(Team team)
        {
            if (team == _awayTeam) return; // Prevent duplicate
            _homeTeam = team;
            ClearResults();
            RefreshTeamSelections();
        }

        private void SelectAwayTeam(Team team)
        {
            if (team == _homeTeam) return; // Prevent duplicate
            _awayTeam = team;
            ClearResults();
            RefreshTeamSelections();
        }

        private void SwapTeams()
        {
            if (_homeTeam == null || _awayTeam == null) return;
            (_homeTeam, _awayTeam) = (_awayTeam, _homeTeam);
            ClearResults();
            RefreshTeamSelections();
        }

        private void RefreshTeamSelections()
        {
            for (int i = 0; i < _teams.Count; i++)
            {
                var t = _teams[i];
                // Home buttons
                if (t == _homeTeam) _homeTeamButtons[i].color = _btnActiveColor;
                else if (t == _awayTeam) _homeTeamButtons[i].color = _btnDisabledColor;
                else _homeTeamButtons[i].color = _btnInactiveColor;

                // Away buttons
                if (t == _awayTeam) _awayTeamButtons[i].color = _btnActiveColor;
                else if (t == _homeTeam) _awayTeamButtons[i].color = _btnDisabledColor;
                else _awayTeamButtons[i].color = _btnInactiveColor;
            }
        }

        private void ClearResults()
        {
            if (_resultsPanel != null)
            {
                ClearChildren(_resultsPanel);
            }
        }

        private void SimulateBatch()
        {
            if (_homeTeam == null || _awayTeam == null) return;
            if (_homeTeam.Id == _awayTeam.Id) return;

            ClearResults();

            var homePlayers = _playerRepository.GetPlayersByTeamId(_homeTeam.Id);
            var awayPlayers = _playerRepository.GetPlayersByTeamId(_awayTeam.Id);

            var runner = new MatchSimulationBatchRunner();
            var report = runner.Run(_homeTeam, homePlayers, _awayTeam, awayPlayers, 100, 10000);

            RenderBatchReport(report);
        }

        private void SimulateMatch()
        {
            if (_homeTeam == null || _awayTeam == null) return;

            ClearResults();

            var homePlayers = _playerRepository.GetPlayersByTeamId(_homeTeam.Id);
            var awayPlayers = _playerRepository.GetPlayersByTeamId(_awayTeam.Id);

            var simulator = new MatchSimulator();
            var config = new MatchConfig();
            var result = simulator.Simulate(_homeTeam, homePlayers, _awayTeam, awayPlayers, config);

            RenderResults(result);
        }

        private void RenderResults(MatchResult result)
        {
            var scroll = CreateScrollView(_resultsPanel, out var content);
            var scrollLayout = scroll.gameObject.AddComponent<LayoutElement>();
            scrollLayout.flexibleHeight = 1f;
            scrollLayout.flexibleWidth = 1f;

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 20f;
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Score Summary
            var scorePanel = CreatePanel("ScoreSummary", content, new Color(0.12f, 0.14f, 0.18f));
            var scoreLayout = scorePanel.gameObject.AddComponent<VerticalLayoutGroup>();
            scoreLayout.padding = new RectOffset(20, 20, 20, 20);
            scoreLayout.spacing = 10f;
            scoreLayout.childAlignment = TextAnchor.UpperCenter;

            var titleText = CreateHeader(scorePanel, $"{result.HomeTeamName} {result.HomeScore} - {result.AwayScore} {result.AwayTeamName}", 32);
            titleText.alignment = TextAnchor.MiddleCenter;
            
            var quartersBuilder = new System.Text.StringBuilder();
            for (int i = 0; i < result.HomeQuarterScores.Count; i++)
            {
                var qName = i < 4 ? $"Q{i + 1}" : $"OT{i - 3}";
                quartersBuilder.Append($"{qName}: {result.HomeQuarterScores[i]}-{result.AwayQuarterScores[i]}");
                if (i < result.HomeQuarterScores.Count - 1) quartersBuilder.Append(" | ");
            }
            var quartersText = CreateBodyText(scorePanel, quartersBuilder.ToString());
            quartersText.alignment = TextAnchor.MiddleCenter;
            quartersText.color = new Color(0.7f, 0.7f, 0.7f);

            // Team Stats Summary
            var statsPanel = CreatePanel("TeamStats", content, new Color(0.11f, 0.12f, 0.16f));
            var statsLayout = statsPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            statsLayout.padding = new RectOffset(16, 16, 16, 16);
            statsLayout.spacing = 8f;
            statsLayout.childForceExpandWidth = true;
            statsLayout.childForceExpandHeight = false;
            statsLayout.childControlHeight = true;
            statsLayout.childControlWidth = true;
            CreateHeader(statsPanel, "\u7403\u961f\u7edf\u8ba1", 20); // 球队统计

            AddTeamStatRow(statsPanel, "PTS", result.HomeTeamStats.Points.ToString(), result.AwayTeamStats.Points.ToString());
            AddTeamStatRow(statsPanel, "FG", $"{result.HomeTeamStats.FieldGoalsMade}/{result.HomeTeamStats.FieldGoalsAttempted}", $"{result.AwayTeamStats.FieldGoalsMade}/{result.AwayTeamStats.FieldGoalsAttempted}");
            AddTeamStatRow(statsPanel, "FG%", FormatPercent(result.HomeTeamStats.FieldGoalsMade, result.HomeTeamStats.FieldGoalsAttempted), FormatPercent(result.AwayTeamStats.FieldGoalsMade, result.AwayTeamStats.FieldGoalsAttempted));
            AddTeamStatRow(statsPanel, "3PT", $"{result.HomeTeamStats.ThreePointersMade}/{result.HomeTeamStats.ThreePointersAttempted}", $"{result.AwayTeamStats.ThreePointersMade}/{result.AwayTeamStats.ThreePointersAttempted}");
            AddTeamStatRow(statsPanel, "3P%", FormatPercent(result.HomeTeamStats.ThreePointersMade, result.HomeTeamStats.ThreePointersAttempted), FormatPercent(result.AwayTeamStats.ThreePointersMade, result.AwayTeamStats.ThreePointersAttempted));
            AddTeamStatRow(statsPanel, "FT", $"{result.HomeTeamStats.FreeThrowsMade}/{result.HomeTeamStats.FreeThrowsAttempted}", $"{result.AwayTeamStats.FreeThrowsMade}/{result.AwayTeamStats.FreeThrowsAttempted}");
            AddTeamStatRow(statsPanel, "FT%", FormatPercent(result.HomeTeamStats.FreeThrowsMade, result.HomeTeamStats.FreeThrowsAttempted), FormatPercent(result.AwayTeamStats.FreeThrowsMade, result.AwayTeamStats.FreeThrowsAttempted));
            AddTeamStatRow(statsPanel, "REB", result.HomeTeamStats.Rebounds.ToString(), result.AwayTeamStats.Rebounds.ToString());
            AddTeamStatRow(statsPanel, "AST", result.HomeTeamStats.Assists.ToString(), result.AwayTeamStats.Assists.ToString());
            AddTeamStatRow(statsPanel, "STL", result.HomeTeamStats.Steals.ToString(), result.AwayTeamStats.Steals.ToString());
            AddTeamStatRow(statsPanel, "BLK", result.HomeTeamStats.Blocks.ToString(), result.AwayTeamStats.Blocks.ToString());
            AddTeamStatRow(statsPanel, "TOV", result.HomeTeamStats.Turnovers.ToString(), result.AwayTeamStats.Turnovers.ToString());
            AddTeamStatRow(statsPanel, "PF", result.HomeTeamStats.Fouls.ToString(), result.AwayTeamStats.Fouls.ToString());

            // Player Stats
            var playerStatsRow = CreatePanel("PlayerStatsRow", content, Color.clear);
            var psLayout = playerStatsRow.gameObject.AddComponent<VerticalLayoutGroup>();
            psLayout.spacing = 30f;
            psLayout.childForceExpandWidth = true;
            psLayout.childForceExpandHeight = false;
            psLayout.childControlHeight = true;
            psLayout.childControlWidth = true;

            var homePSPanel = CreatePanel("HomePSPanel", playerStatsRow, Color.clear);
            var homePSLayout = homePSPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            homePSLayout.spacing = 8f;
            homePSLayout.childForceExpandWidth = true;
            homePSLayout.childForceExpandHeight = false;
            homePSLayout.childControlHeight = true;
            homePSLayout.childControlWidth = true;
            CreateHeader(homePSPanel, result.HomeTeamName, 20);
            RenderPlayerStatsTable(homePSPanel, result.HomePlayerStats);

            var awayPSPanel = CreatePanel("AwayPSPanel", playerStatsRow, Color.clear);
            var awayPSLayout = awayPSPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            awayPSLayout.spacing = 8f;
            awayPSLayout.childForceExpandWidth = true;
            awayPSLayout.childForceExpandHeight = false;
            awayPSLayout.childControlHeight = true;
            awayPSLayout.childControlWidth = true;
            CreateHeader(awayPSPanel, result.AwayTeamName, 20);
            RenderPlayerStatsTable(awayPSPanel, result.AwayPlayerStats);
        }

        private void AddTeamStatRow(RectTransform parent, string label, string homeVal, string awayVal)
        {
            CreateStatRow(parent, label, homeVal, awayVal);
        }

        private void RenderPlayerStatsTable(RectTransform parent, List<PlayerBoxScore> players)
        {
            var columns = new List<(string, float)>
            {
                ("Name", 200f),
                ("MIN", 60f),
                ("PTS", 60f),
                ("REB", 60f),
                ("AST", 60f),
                ("FG", 80f),
                ("3PT", 80f),
                ("FT", 80f)
            };

            CreateTableHeaderRow(parent, columns);

            foreach (var p in players.Where(x => x.Minutes > 0).OrderByDescending(x => x.Points))
            {
                var rowData = new List<(string, float)>
                {
                    (p.PlayerName, 200f),
                    (p.Minutes.ToString(), 60f),
                    (p.Points.ToString(), 60f),
                    (p.Rebounds.ToString(), 60f),
                    (p.Assists.ToString(), 60f),
                    ($"{p.FieldGoalsMade}/{p.FieldGoalsAttempted}", 80f),
                    ($"{p.ThreePointersMade}/{p.ThreePointersAttempted}", 80f),
                    ($"{p.FreeThrowsMade}/{p.FreeThrowsAttempted}", 80f)
                };
                CreateTableDataRow(parent, rowData);
            }
        }

        private Text CreateTextWithWidth(RectTransform parent, string text, float width)
        {
            var t = CreateBodyText(parent, text);
            LayoutElementWithWidth(t.gameObject, width);
            return t;
        }
        private void RenderBatchPlayerStatsTable(RectTransform parent, List<PlayerAverageStatLine> players)
        {
            var columns = new List<(string, float)>
            {
                ("Name", 180f),
                ("PTS", 50f),
                ("REB", 50f),
                ("AST", 50f),
                ("FG", 130f),
                ("3PT", 130f),
                ("FT", 130f)
            };

            CreateTableHeaderRow(parent, columns);

            foreach (var p in players)
            {
                string fgPct = p.FieldGoalAttempts > 0 ? (p.FieldGoalsMade / p.FieldGoalAttempts * 100).ToString("F1") + "%" : "-";
                string fg = $"{p.FieldGoalsMade:F1}/{p.FieldGoalAttempts:F1} ({fgPct})";

                string tpPct = p.ThreePointAttempts > 0 ? (p.ThreePointersMade / p.ThreePointAttempts * 100).ToString("F1") + "%" : "-";
                string tp = $"{p.ThreePointersMade:F1}/{p.ThreePointAttempts:F1} ({tpPct})";

                string ftPct = p.FreeThrowAttempts > 0 ? (p.FreeThrowsMade / p.FreeThrowAttempts * 100).ToString("F1") + "%" : "-";
                string ft = $"{p.FreeThrowsMade:F1}/{p.FreeThrowAttempts:F1} ({ftPct})";

                var rowData = new List<(string, float)>
                {
                    (p.PlayerName, 180f),
                    (p.Points.ToString("F1"), 50f),
                    (p.Rebounds.ToString("F1"), 50f),
                    (p.Assists.ToString("F1"), 50f),
                    (fg, 130f),
                    (tp, 130f),
                    (ft, 130f)
                };
                CreateTableDataRow(parent, rowData);
            }
        }

        private void RenderBatchReport(MatchSimulationReport report)
        {
            var scroll = CreateScrollView(_resultsPanel, out var content);
            var scrollLayout = scroll.gameObject.AddComponent<LayoutElement>();
            scrollLayout.flexibleHeight = 1f;
            scrollLayout.flexibleWidth = 1f;

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 20f;
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scorePanel = CreatePanel("ScorePanel", content, new Color(0.13f, 0.14f, 0.18f));
            var scoreLayout = scorePanel.gameObject.AddComponent<VerticalLayoutGroup>();
            scoreLayout.padding = new RectOffset(20, 20, 20, 20);
            scoreLayout.spacing = 10f;
            scoreLayout.childForceExpandWidth = true;
            scoreLayout.childForceExpandHeight = false;
            scoreLayout.childControlHeight = true;
            scoreLayout.childControlWidth = true;

            var titleText = CreateHeader(scorePanel, $"{report.HomeTeamName} vs {report.AwayTeamName}", 28);
            titleText.alignment = TextAnchor.MiddleCenter;
            
            var gamesText = CreateBodyText(scorePanel, $"Games: {report.Games}");
            gamesText.alignment = TextAnchor.MiddleCenter;
            gamesText.color = new Color(0.7f, 0.7f, 0.7f);

            var homeWinPct = FormatPercent(report.HomeWins, report.Games);
            var awayWinPct = FormatPercent(report.AwayWins, report.Games);
            var winsText = CreateBodyText(scorePanel, $"{report.HomeTeamName}: {report.HomeWins}\u80dc ({homeWinPct}) | {report.AwayTeamName}: {report.AwayWins}\u80dc ({awayWinPct})");
            winsText.alignment = TextAnchor.MiddleCenter;
            winsText.fontSize = 24;

            var avgScoreText = CreateBodyText(scorePanel, $"{report.HomeTeamName} {report.AverageHomeScore:F1} - {report.AverageAwayScore:F1} {report.AwayTeamName}");
            avgScoreText.alignment = TextAnchor.MiddleCenter;
            avgScoreText.fontSize = 24;

            CheckWarnings(scorePanel, report);

            // Team Stats Summary
            var statsPanel = CreatePanel("TeamStats", content, new Color(0.11f, 0.12f, 0.16f));
            var statsLayout = statsPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            statsLayout.padding = new RectOffset(16, 16, 16, 16);
            statsLayout.spacing = 8f;
            statsLayout.childForceExpandWidth = true;
            statsLayout.childForceExpandHeight = false;
            statsLayout.childControlHeight = true;
            statsLayout.childControlWidth = true;
            CreateHeader(statsPanel, "\u6838\u5fc3\u6548\u7387", 20); // 核心效率

            AddTeamStatRow(statsPanel, "FG%", $"{(report.HomeFieldGoalPercent * 100):F1}%", $"{(report.AwayFieldGoalPercent * 100):F1}%");
            AddTeamStatRow(statsPanel, "3P%", $"{(report.HomeThreePointPercent * 100):F1}%", $"{(report.AwayThreePointPercent * 100):F1}%");
            AddTeamStatRow(statsPanel, "FT%", $"{(report.HomeFreeThrowPercent * 100):F1}%", $"{(report.AwayFreeThrowPercent * 100):F1}%");
            AddTeamStatRow(statsPanel, "3PA", report.HomeThreePointAttempts.ToString("F1"), report.AwayThreePointAttempts.ToString("F1"));
            AddTeamStatRow(statsPanel, "FTA", report.HomeFreeThrowAttempts.ToString("F1"), report.AwayFreeThrowAttempts.ToString("F1"));
            AddTeamStatRow(statsPanel, "REB", report.HomeRebounds.ToString("F1"), report.AwayRebounds.ToString("F1"));
            AddTeamStatRow(statsPanel, "AST", report.HomeAssists.ToString("F1"), report.AwayAssists.ToString("F1"));
            AddTeamStatRow(statsPanel, "TOV", report.HomeTurnovers.ToString("F1"), report.AwayTurnovers.ToString("F1"));
            AddTeamStatRow(statsPanel, "PF", report.HomeFouls.ToString("F1"), report.AwayFouls.ToString("F1"));

            // Player Stats
            var playerStatsRow = CreatePanel("PlayerStatsRow", content, Color.clear);
            var psLayout = playerStatsRow.gameObject.AddComponent<VerticalLayoutGroup>();
            psLayout.spacing = 30f;
            psLayout.childForceExpandWidth = true;
            psLayout.childForceExpandHeight = false;
            psLayout.childControlHeight = true;
            psLayout.childControlWidth = true;

            var homePSPanel = CreatePanel("HomePSPanel", playerStatsRow, Color.clear);
            var homePSLayout = homePSPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            homePSLayout.spacing = 8f;
            homePSLayout.childForceExpandWidth = true;
            homePSLayout.childForceExpandHeight = false;
            homePSLayout.childControlHeight = true;
            homePSLayout.childControlWidth = true;
            CreateHeader(homePSPanel, $"{report.HomeTeamName} Rotation Players", 20);
            RenderBatchPlayerStatsTable(homePSPanel, report.TopHomeScorers);

            var awayPSPanel = CreatePanel("AwayPSPanel", playerStatsRow, Color.clear);
            var awayPSLayout = awayPSPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            awayPSLayout.spacing = 8f;
            awayPSLayout.childForceExpandWidth = true;
            awayPSLayout.childForceExpandHeight = false;
            awayPSLayout.childControlHeight = true;
            awayPSLayout.childControlWidth = true;
            CreateHeader(awayPSPanel, $"{report.AwayTeamName} Rotation Players", 20);
            RenderBatchPlayerStatsTable(awayPSPanel, report.TopAwayScorers);
        }

        private void CheckWarnings(RectTransform parent, MatchSimulationReport report)
        {
            var warnings = new List<string>();

            if (report.AverageTotalScore < 180 || report.AverageTotalScore > 270)
                warnings.Add($"[WARN] AverageTotalScore ({report.AverageTotalScore:F1}) \u5f02\u5e38\uff08\u6b63\u5e38180-270\uff09");
            
            CheckPercentWarning(warnings, "FG%", report.HomeFieldGoalPercent, report.AwayFieldGoalPercent, 0.38f, 0.58f);
            CheckPercentWarning(warnings, "3P%", report.HomeThreePointPercent, report.AwayThreePointPercent, 0.25f, 0.48f);
            CheckPercentWarning(warnings, "FT%", report.HomeFreeThrowPercent, report.AwayFreeThrowPercent, 0.60f, 0.95f);
            
            CheckStatWarning(warnings, "TOV", report.HomeTurnovers, report.AwayTurnovers, 5f, 25f);
            CheckStatWarning(warnings, "FTA", report.HomeFreeThrowAttempts, report.AwayFreeThrowAttempts, 5f, 40f);
            CheckStatWarning(warnings, "REB", report.HomeRebounds, report.AwayRebounds, 25f, 70f);

            if (warnings.Count > 0)
            {
                var warningPanel = CreatePanel("Warnings", parent, new Color(0.35f, 0.1f, 0.1f));
                var wLayout = warningPanel.gameObject.AddComponent<VerticalLayoutGroup>();
                wLayout.padding = new RectOffset(10, 10, 10, 10);
                wLayout.childForceExpandWidth = true;
                wLayout.childForceExpandHeight = false;
                wLayout.childControlHeight = true;
                wLayout.childControlWidth = true;

                foreach (var w in warnings)
                {
                    var text = CreateBodyText(warningPanel, w);
                    text.color = new Color(1f, 0.6f, 0.6f);
                    text.alignment = TextAnchor.MiddleCenter;
                }
            }
        }

        private void CheckPercentWarning(List<string> warnings, string label, float homePct, float awayPct, float min, float max)
        {
            if (homePct < min || homePct > max) warnings.Add($"[WARN] Home {label} ({(homePct*100):F1}%) \u5f02\u5e38\uff08\u6b63\u5e38{(min*100):F0}-{(max*100):F0}%\uff09");
            if (awayPct < min || awayPct > max) warnings.Add($"[WARN] Away {label} ({(awayPct*100):F1}%) \u5f02\u5e38\uff08\u6b63\u5e38{(min*100):F0}-{(max*100):F0}%\uff09");
        }

        private void CheckStatWarning(List<string> warnings, string label, float homeStat, float awayStat, float min, float max)
        {
            if (homeStat < min || homeStat > max) warnings.Add($"[WARN] Home {label} ({homeStat:F1}) \u5f02\u5e38\uff08\u6b63\u5e38{min:F0}-{max:F0}\uff09");
            if (awayStat < min || awayStat > max) warnings.Add($"[WARN] Away {label} ({awayStat:F1}) \u5f02\u5e38\uff08\u6b63\u5e38{min:F0}-{max:F0}\uff09");
        }
    }
}
