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
            simLayout.childForceExpandWidth = true;
            simLayout.childForceExpandHeight = false;
            simLayout.childControlHeight = true;
            simLayout.childControlWidth = true;
            LayoutElementWithHeight(simBtnRow.gameObject, 50f);
            
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
    }
}
