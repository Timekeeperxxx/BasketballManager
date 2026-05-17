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
            LayoutElementWithHeight(settingsPanel.gameObject, 180f);

            CreateHeader(settingsPanel, "\u6bd4\u8d5b\u8bbe\u7f6e", 24); // 比赛设置

            var selectionRow = CreatePanel("SelectionRow", settingsPanel, Color.clear);
            var rowLayout = selectionRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 20f;
            rowLayout.childForceExpandWidth = true;
            LayoutElementWithHeight(selectionRow.gameObject, 60f);

            // Home Team Selection
            var homeGroup = CreatePanel("HomeGroup", selectionRow, Color.clear);
            var homeLayout = homeGroup.gameObject.AddComponent<HorizontalLayoutGroup>();
            homeLayout.spacing = 8f;
            CreateBodyText(homeGroup, "\u4e3b\u961f:").color = new Color(0.8f, 0.8f, 0.8f); // 主队:
            
            foreach (var team in _teams)
            {
                var t = team;
                var btn = CreateButton(homeGroup, t.Name, () => SelectHomeTeam(t));
                LayoutElementWithWidth(btn.gameObject, 120f);
                _homeTeamButtons.Add(btn.GetComponent<Image>());
            }

            // Swap Button
            var swapBtn = CreateButton(selectionRow, "\u21c4 \u4ea4\u6362", SwapTeams); // ⇄ 交换
            LayoutElementWithWidth(swapBtn.gameObject, 100f);

            // Away Team Selection
            var awayGroup = CreatePanel("AwayGroup", selectionRow, Color.clear);
            var awayLayout = awayGroup.gameObject.AddComponent<HorizontalLayoutGroup>();
            awayLayout.spacing = 8f;
            CreateBodyText(awayGroup, "\u5ba2\u961f:").color = new Color(0.8f, 0.8f, 0.8f); // 客队:
            
            foreach (var team in _teams)
            {
                var t = team;
                var btn = CreateButton(awayGroup, t.Name, () => SelectAwayTeam(t));
                LayoutElementWithWidth(btn.gameObject, 120f);
                _awayTeamButtons.Add(btn.GetComponent<Image>());
            }

            var simBtnRow = CreatePanel("SimBtnRow", settingsPanel, Color.clear);
            var simLayout = simBtnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            simLayout.childAlignment = TextAnchor.MiddleRight;
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
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 20f;
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Score Summary
            var scorePanel = CreatePanel("ScoreSummary", content, new Color(0.12f, 0.14f, 0.18f));
            var scoreLayout = scorePanel.gameObject.AddComponent<VerticalLayoutGroup>();
            scoreLayout.padding = new RectOffset(20, 20, 20, 20);
            scoreLayout.spacing = 10f;
            scoreLayout.childAlignment = TextAnchor.UpperCenter;

            var titleText = CreateHeader(scorePanel, $"{result.HomeTeamName} {result.HomeScore} - {result.AwayScore} {result.AwayTeamName}", 32);
            titleText.alignment = TextAnchor.MiddleCenter;
            
            var quartersText = CreateBodyText(scorePanel, $"Q1: {result.HomeQuarterScores[0]}-{result.AwayQuarterScores[0]} | " +
                                                          $"Q2: {result.HomeQuarterScores[1]}-{result.AwayQuarterScores[1]} | " +
                                                          $"Q3: {result.HomeQuarterScores[2]}-{result.AwayQuarterScores[2]} | " +
                                                          $"Q4: {result.HomeQuarterScores[3]}-{result.AwayQuarterScores[3]}");
            quartersText.alignment = TextAnchor.MiddleCenter;
            quartersText.color = new Color(0.7f, 0.7f, 0.7f);

            // Team Stats Summary
            var statsPanel = CreatePanel("TeamStats", content, new Color(0.11f, 0.12f, 0.16f));
            var statsLayout = statsPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            statsLayout.padding = new RectOffset(16, 16, 16, 16);
            statsLayout.spacing = 8f;
            CreateHeader(statsPanel, "\u7403\u961f\u7edf\u8ba1", 20); // 球队统计

            AddTeamStatRow(statsPanel, "PTS", result.HomeTeamStats.Points.ToString(), result.AwayTeamStats.Points.ToString());
            AddTeamStatRow(statsPanel, "FG", $"{result.HomeTeamStats.FieldGoalsMade}/{result.HomeTeamStats.FieldGoalsAttempted}", $"{result.AwayTeamStats.FieldGoalsMade}/{result.AwayTeamStats.FieldGoalsAttempted}");
            AddTeamStatRow(statsPanel, "3PT", $"{result.HomeTeamStats.ThreePointersMade}/{result.HomeTeamStats.ThreePointersAttempted}", $"{result.AwayTeamStats.ThreePointersMade}/{result.AwayTeamStats.ThreePointersAttempted}");
            AddTeamStatRow(statsPanel, "FT", $"{result.HomeTeamStats.FreeThrowsMade}/{result.HomeTeamStats.FreeThrowsAttempted}", $"{result.AwayTeamStats.FreeThrowsMade}/{result.AwayTeamStats.FreeThrowsAttempted}");
            AddTeamStatRow(statsPanel, "REB", result.HomeTeamStats.Rebounds.ToString(), result.AwayTeamStats.Rebounds.ToString());
            AddTeamStatRow(statsPanel, "AST", result.HomeTeamStats.Assists.ToString(), result.AwayTeamStats.Assists.ToString());
            AddTeamStatRow(statsPanel, "STL", result.HomeTeamStats.Steals.ToString(), result.AwayTeamStats.Steals.ToString());
            AddTeamStatRow(statsPanel, "BLK", result.HomeTeamStats.Blocks.ToString(), result.AwayTeamStats.Blocks.ToString());
            AddTeamStatRow(statsPanel, "TOV", result.HomeTeamStats.Turnovers.ToString(), result.AwayTeamStats.Turnovers.ToString());
            AddTeamStatRow(statsPanel, "PF", result.HomeTeamStats.Fouls.ToString(), result.AwayTeamStats.Fouls.ToString());

            // Player Stats
            var playerStatsRow = CreatePanel("PlayerStatsRow", content, Color.clear);
            var psLayout = playerStatsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            psLayout.spacing = 16f;
            psLayout.childForceExpandWidth = true;

            var homePSPanel = CreateColumnPanel(playerStatsRow, 0f);
            CreateHeader(homePSPanel, result.HomeTeamName, 20);
            RenderPlayerStatsTable(homePSPanel, result.HomePlayerStats);

            var awayPSPanel = CreateColumnPanel(playerStatsRow, 0f);
            CreateHeader(awayPSPanel, result.AwayTeamName, 20);
            RenderPlayerStatsTable(awayPSPanel, result.AwayPlayerStats);
        }

        private void AddTeamStatRow(RectTransform parent, string label, string homeVal, string awayVal)
        {
            var row = CreatePanel("Row", parent, Color.clear);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = true;
            LayoutElementWithHeight(row.gameObject, 24f);

            var homeText = CreateBodyText(row, homeVal);
            homeText.alignment = TextAnchor.MiddleCenter;

            var labelText = CreateBodyText(row, label);
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.color = new Color(0.6f, 0.6f, 0.6f);

            var awayText = CreateBodyText(row, awayVal);
            awayText.alignment = TextAnchor.MiddleCenter;
        }

        private void RenderPlayerStatsTable(RectTransform parent, List<PlayerBoxScore> players)
        {
            var headerRow = CreatePanel("Header", parent, new Color(0.15f, 0.16f, 0.20f));
            var hLayout = headerRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hLayout.padding = new RectOffset(8, 8, 4, 4);
            LayoutElementWithHeight(headerRow.gameObject, 30f);

            CreateTextWithWidth(headerRow, "Name", 120f);
            CreateTextWithWidth(headerRow, "MIN", 40f);
            CreateTextWithWidth(headerRow, "PTS", 40f);
            CreateTextWithWidth(headerRow, "REB", 40f);
            CreateTextWithWidth(headerRow, "AST", 40f);
            CreateTextWithWidth(headerRow, "FG", 60f);
            CreateTextWithWidth(headerRow, "3PT", 60f);
            CreateTextWithWidth(headerRow, "FT", 60f);

            foreach (var p in players.OrderByDescending(x => x.Points))
            {
                var row = CreatePanel("Row", parent, Color.clear);
                var rLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                rLayout.padding = new RectOffset(8, 8, 4, 4);
                LayoutElementWithHeight(row.gameObject, 28f);

                CreateTextWithWidth(row, p.PlayerName, 120f).fontSize = 16;
                CreateTextWithWidth(row, p.Minutes.ToString(), 40f).fontSize = 16;
                CreateTextWithWidth(row, p.Points.ToString(), 40f).fontSize = 16;
                CreateTextWithWidth(row, p.Rebounds.ToString(), 40f).fontSize = 16;
                CreateTextWithWidth(row, p.Assists.ToString(), 40f).fontSize = 16;
                CreateTextWithWidth(row, $"{p.FieldGoalsMade}/{p.FieldGoalsAttempted}", 60f).fontSize = 16;
                CreateTextWithWidth(row, $"{p.ThreePointersMade}/{p.ThreePointersAttempted}", 60f).fontSize = 16;
                CreateTextWithWidth(row, $"{p.FreeThrowsMade}/{p.FreeThrowsAttempted}", 60f).fontSize = 16;
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
