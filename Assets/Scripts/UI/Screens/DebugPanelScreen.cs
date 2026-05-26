using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    /// 单场结果分三个标签页：播报 / 球员数据 / 球队数据。
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

        // 顶部 UI
        private Label _homeName, _homeSub, _awayName, _awaySub;
        private VisualElement _resultContent;
        private Label _resultEmpty;
        private VisualElement _settingsBody;
        private Button _settingsToggleBtn;
        private bool _settingsExpanded = true;

        // 单场结果标签页
        private Button _tabBtnPbp, _tabBtnPlayer, _tabBtnTeam;
        private VisualElement _pagePbp, _pagePlayer, _pageTeam;

        // 播报播放器状态
        private MatchResult _currentResult;
        private List<VisualElement> _pbpEventRows = new List<VisualElement>();
        private List<int> _pbpRowEventIdx = new List<int>(); // -1=节标题，>=0=PlayByPlay事件序号
        private int _pbpIndex;        // _pbpEventRows 中已显示到的位置
        private int _pbpEventsShown;  // 已显示的真实事件数（不含节标题）
        private bool _pbpPlaying;
        private Coroutine _pbpCoroutine;
        private ScrollView _pbpScroll;
        private Label _pbpProgressLabel;
        private Label _pbpSpeedLabel;
        private Button _pbpPlayPauseBtn;
        private readonly float[] _pbpSpeeds = { 1f, 2f, 4f };
        private int _pbpSpeedIdx = 0; // 默认 ×1

        // 播报列表容器（行在协程中 Insert(0,...) 顶部插入）
        private VisualElement _pbpListContainer;

        // 比分卡动态标签
        private Label _scorecardHeadline;
        private Label _scorecardQuarters;

        // 球员/球队页的 ScrollView 引用，用于切换时动态重建内容
        private ScrollView _playerPageScroll;
        private ScrollView _teamPageScroll;

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

            _settingsBody       = Root.Q<VisualElement>("settings-body");
            _settingsToggleBtn  = Root.Q<Button>("btn-settings-toggle");
            _settingsToggleBtn.clicked += ToggleSettings;

            _resultContent = Root.Q<VisualElement>("result-content");
            _resultEmpty   = Root.Q<Label>("result-empty");
        }

        protected override void OnEnter()
        {
            if (_teamRepository == null) return;
            _teams = _teamRepository.GetAllTeams().ToList();
            if (_teams.Count >= 2) { _homeIdx = 0; _awayIdx = 1; }
            else                   { _homeIdx = 0; _awayIdx = 0; }
            RefreshTeamCards();
            ClearResults(showEmpty: true);
            SetSettingsExpanded(true); // 每次进入重置为展开
        }

        private void ToggleSettings() => SetSettingsExpanded(!_settingsExpanded);

        private void SetSettingsExpanded(bool expanded)
        {
            _settingsExpanded = expanded;
            if (_settingsBody != null)
                _settingsBody.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            if (_settingsToggleBtn != null)
                _settingsToggleBtn.text = expanded ? "▲ 收起" : "▼ 展开";
        }

        protected override void OnExit()
        {
            StopPbpPlayback();
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
            SetSettingsExpanded(false);

            var homePlayers = _playerRepository.GetPlayersByTeamId(home.Id);
            var awayPlayers = _playerRepository.GetPlayersByTeamId(away.Id);
            var profiles = _profileRepository.GetAllProfiles();

            var simulator = new MatchSimulator();
            var result = simulator.Simulate(home, homePlayers, profiles, away, awayPlayers, profiles,
                new MatchConfig { EnablePlayByPlay = true });

            RenderSingleMatch(result);
        }

        private void SimulateBatch()
        {
            if (_teams.Count < 2) return;
            var home = _teams[_homeIdx];
            var away = _teams[_awayIdx];
            if (home.Id == away.Id) return;
            SetSettingsExpanded(false);

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
        //  清空 / 空态
        // ============================================================

        private void ClearResults(bool showEmpty)
        {
            StopPbpPlayback();
            _currentResult = null;
            _tabBtnPbp = _tabBtnPlayer = _tabBtnTeam = null;
            _pagePbp = _pagePlayer = _pageTeam = null;
            _pbpEventRows?.Clear();
            _pbpRowEventIdx?.Clear();
            _pbpIndex = 0;
            _pbpEventsShown = 0;
            _pbpScroll = null;
            _pbpListContainer = null;
            _playerPageScroll = null;
            _teamPageScroll = null;
            _scorecardHeadline = null;
            _scorecardQuarters = null;
            _pbpProgressLabel = null;
            _pbpSpeedLabel = null;
            _pbpPlayPauseBtn = null;

            for (int i = _resultContent.childCount - 1; i >= 0; i--)
            {
                var child = _resultContent[i];
                if (child == _resultEmpty) continue;
                _resultContent.RemoveAt(i);
            }
            _resultEmpty.style.display = showEmpty ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ============================================================
        //  渲染：单场结果（三标签页）
        // ============================================================

        private void RenderSingleMatch(MatchResult result)
        {
            StopPbpPlayback();
            ClearResults(showEmpty: false);
            _currentResult = result;

            // 比分头（标签页外，始终可见）
            _resultContent.Add(BuildScoreCard(result));

            // 标签栏
            var tabBar = BuildTabBar(out _tabBtnPbp, out _tabBtnPlayer, out _tabBtnTeam);
            _resultContent.Add(tabBar);

            // 三个页面（球员/球队页只建容器，内容在切换时按当前进度填充）
            _pagePbp    = BuildPbpPage(result);
            _pagePlayer = BuildStatsPageShell(out _playerPageScroll);
            _pageTeam   = BuildStatsPageShell(out _teamPageScroll);
            _resultContent.Add(_pagePbp);
            _resultContent.Add(_pagePlayer);
            _resultContent.Add(_pageTeam);

            // 绑定标签切换
            _tabBtnPbp.clicked    += () => SwitchTab(_pagePbp,    _tabBtnPbp);
            _tabBtnPlayer.clicked += () => SwitchTab(_pagePlayer,  _tabBtnPlayer);
            _tabBtnTeam.clicked   += () => SwitchTab(_pageTeam,    _tabBtnTeam);

            // 初始显示播报页，自动开始播放
            SwitchTab(_pagePbp, _tabBtnPbp);
            if (result.PlayByPlay.Count > 0)
                _pbpCoroutine = StartCoroutine(PbpCoroutine());
        }

        // 球员/球队页共用的外壳（内容在 SwitchTab 时填充）
        private static VisualElement BuildStatsPageShell(out ScrollView scroll)
        {
            var page = new VisualElement();
            page.style.backgroundColor = new Color(0.086f, 0.105f, 0.129f);
            page.style.borderBottomLeftRadius  = 6;
            page.style.borderBottomRightRadius = 6;
            page.style.flexGrow = 1;
            page.style.display = DisplayStyle.None;

            scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 360;
            scroll.style.paddingTop = 8;
            scroll.style.paddingBottom = 8;
            page.Add(scroll);
            return page;
        }

        // ============================================================
        //  比分卡（标签页外）
        // ============================================================

        private VisualElement BuildScoreCard(MatchResult result)
        {
            var card = NewCard();
            _scorecardHeadline = new Label($"{result.HomeTeamName}   0 - 0   {result.AwayTeamName}");
            _scorecardHeadline.AddToClassList("score-headline");
            card.Add(_scorecardHeadline);

            _scorecardQuarters = new Label(string.Empty);
            _scorecardQuarters.AddToClassList("score-quarters");
            card.Add(_scorecardQuarters);
            return card;
        }

        // ============================================================
        //  标签栏
        // ============================================================

        private static VisualElement BuildTabBar(out Button btnPbp, out Button btnPlayer, out Button btnTeam)
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.backgroundColor = new Color(0.086f, 0.105f, 0.129f);
            bar.style.borderTopLeftRadius  = 6;
            bar.style.borderTopRightRadius = 6;
            bar.style.marginTop = 8;
            bar.style.borderBottomWidth = 1;
            bar.style.borderBottomColor = new Color(0.19f, 0.21f, 0.24f);
            bar.style.paddingLeft = 4;
            bar.style.paddingRight = 4;

            btnPbp    = MakeTabButton("📡 播报");
            btnPlayer = MakeTabButton("球员数据");
            btnTeam   = MakeTabButton("球队数据");
            bar.Add(btnPbp);
            bar.Add(btnPlayer);
            bar.Add(btnTeam);
            return bar;
        }

        private static Button MakeTabButton(string text)
        {
            var btn = new Button { text = text };
            btn.style.height = 38;
            btn.style.minWidth = 90;
            btn.style.paddingLeft = 16;
            btn.style.paddingRight = 16;
            btn.style.backgroundColor = Color.clear;
            btn.style.borderTopWidth    = 0;
            btn.style.borderLeftWidth   = 0;
            btn.style.borderRightWidth  = 0;
            btn.style.borderBottomWidth = 2;
            btn.style.borderBottomColor = Color.clear;
            btn.style.borderTopLeftRadius     = 0;
            btn.style.borderTopRightRadius    = 0;
            btn.style.borderBottomLeftRadius  = 0;
            btn.style.borderBottomRightRadius = 0;
            btn.style.color = new Color(0.55f, 0.58f, 0.62f);
            btn.style.fontSize = 13;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            return btn;
        }

        private void SwitchTab(VisualElement targetPage, Button targetBtn)
        {
            if (_pagePbp    != null) _pagePbp.style.display    = DisplayStyle.None;
            if (_pagePlayer != null) _pagePlayer.style.display  = DisplayStyle.None;
            if (_pageTeam   != null) _pageTeam.style.display    = DisplayStyle.None;

            foreach (var b in new[] { _tabBtnPbp, _tabBtnPlayer, _tabBtnTeam })
            {
                if (b == null) continue;
                b.style.color = new Color(0.55f, 0.58f, 0.62f);
                b.style.borderBottomColor = Color.clear;
            }
            targetBtn.style.color = new Color(0.90f, 0.93f, 0.95f);
            targetBtn.style.borderBottomColor = new Color(0.23f, 0.51f, 0.96f);

            // 动态填充球员/球队页（截止到当前播报进度的统计）
            if (_currentResult != null)
            {
                if (targetPage == _pagePlayer && _playerPageScroll != null)
                {
                    _playerPageScroll.Clear();
                    ComputePartialPlayerStats(_currentResult, _pbpEventsShown,
                        out var homeStats, out var awayStats);
                    _playerPageScroll.Add(BuildSinglePlayersCard(_currentResult.HomeTeamName, homeStats));
                    _playerPageScroll.Add(BuildSinglePlayersCard(_currentResult.AwayTeamName, awayStats));
                }
                else if (targetPage == _pageTeam && _teamPageScroll != null)
                {
                    _teamPageScroll.Clear();
                    _teamPageScroll.style.paddingLeft = 8;
                    _teamPageScroll.style.paddingRight = 8;
                    ComputePartialPlayerStats(_currentResult, _pbpEventsShown,
                        out var homeStats, out var awayStats);
                    _teamPageScroll.Add(BuildPartialTeamCard(_currentResult, homeStats, awayStats));
                }
            }

            targetPage.style.display = DisplayStyle.Flex;
        }

        // ============================================================
        //  播报页（标签页 1）
        // ============================================================

        private VisualElement BuildPbpPage(MatchResult result)
        {
            var page = new VisualElement();
            page.style.backgroundColor = new Color(0.086f, 0.105f, 0.129f);
            page.style.borderBottomLeftRadius  = 6;
            page.style.borderBottomRightRadius = 6;

            // 控制栏
            var controls = new VisualElement();
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.alignItems = Align.Center;
            controls.style.paddingLeft = 12;
            controls.style.paddingRight = 12;
            controls.style.paddingTop = 8;
            controls.style.paddingBottom = 8;
            controls.style.borderBottomWidth = 1;
            controls.style.borderBottomColor = new Color(0.19f, 0.21f, 0.24f);

            _pbpPlayPauseBtn = new Button(TogglePbpPlayback) { text = "▶ 播放" };
            StyleCtrlBtn(_pbpPlayPauseBtn);
            _pbpPlayPauseBtn.style.marginRight = 16;
            controls.Add(_pbpPlayPauseBtn);

            var slowLbl = new Label("速度");
            slowLbl.style.color = new Color(0.55f, 0.58f, 0.62f);
            slowLbl.style.fontSize = 12;
            slowLbl.style.marginRight = 6;
            controls.Add(slowLbl);

            var slowBtn = new Button(() => AdjustSpeed(-1)) { text = "◀ 减速" };
            StyleCtrlBtn(slowBtn);
            controls.Add(slowBtn);

            _pbpSpeedLabel = new Label($"×{(int)_pbpSpeeds[_pbpSpeedIdx]}");
            _pbpSpeedLabel.style.width = 42;
            _pbpSpeedLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _pbpSpeedLabel.style.color = new Color(0.90f, 0.93f, 0.95f);
            _pbpSpeedLabel.style.fontSize = 13;
            _pbpSpeedLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            controls.Add(_pbpSpeedLabel);

            var fastBtn = new Button(() => AdjustSpeed(+1)) { text = "加速 ▶" };
            StyleCtrlBtn(fastBtn);
            controls.Add(fastBtn);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            controls.Add(spacer);

            _pbpProgressLabel = new Label("0 / 0");
            _pbpProgressLabel.style.color = new Color(0.55f, 0.58f, 0.62f);
            _pbpProgressLabel.style.fontSize = 12;
            controls.Add(_pbpProgressLabel);

            page.Add(controls);

            // 事件列表 —— 行在协程里逐条 Insert(0,...) 到顶部，新事件始终在最上面
            _pbpScroll = new ScrollView();
            _pbpScroll.style.flexGrow = 1;
            _pbpScroll.style.minHeight = 360;
            _pbpScroll.style.paddingTop = 8;
            _pbpScroll.style.paddingBottom = 8;
            _pbpScroll.style.paddingLeft = 8;
            _pbpScroll.style.paddingRight = 8;

            _pbpListContainer = new VisualElement();
            _pbpScroll.Add(_pbpListContainer);

            _pbpEventRows = new List<VisualElement>();
            _pbpRowEventIdx = new List<int>();
            _pbpIndex = 0;
            _pbpEventsShown = 0;
            _pbpPlaying = false;

            int lastQ = -1;
            int pbpIdx = 0;
            foreach (var evt in result.PlayByPlay)
            {
                if (evt.Quarter != lastQ)
                {
                    string periodName = evt.Quarter <= 4
                        ? $"── 第 {evt.Quarter} 节 ──"
                        : $"── 加时 {evt.Quarter - 4} ──";
                    var qHdr = new Label(periodName);
                    qHdr.style.color = new Color(0.55f, 0.58f, 0.62f);
                    qHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
                    qHdr.style.fontSize = 11;
                    qHdr.style.marginTop = 10;
                    qHdr.style.marginBottom = 4;
                    _pbpEventRows.Add(qHdr);
                    _pbpRowEventIdx.Add(-1);
                    lastQ = evt.Quarter;
                }

                var row = BuildPbpEventRow(evt);
                _pbpEventRows.Add(row);
                _pbpRowEventIdx.Add(pbpIdx);
                pbpIdx++;
            }

            _pbpProgressLabel.text = $"0 / {result.PlayByPlay.Count}";
            page.Add(_pbpScroll);
            return page;
        }

        private static VisualElement BuildPbpEventRow(PlayByPlayEvent evt)
        {
            // 边界事件（jersey < 0）：换人用绿色，其余节/场事件用蓝色
            bool isBoundary = evt.JerseyNumber < 0;
            if (isBoundary)
            {
                bool isSub = evt.Description.StartsWith("换人 —");
                var banner = new VisualElement();
                banner.style.flexDirection = FlexDirection.Row;
                banner.style.alignItems = Align.Center;
                banner.style.marginBottom = isSub ? 3 : 6;
                banner.style.marginTop    = isSub ? 3 : 6;
                banner.style.paddingTop = 6;
                banner.style.paddingBottom = 6;
                banner.style.paddingLeft = 12;
                banner.style.paddingRight = 12;
                if (isSub)
                {
                    banner.style.backgroundColor = new Color(0.08f, 0.18f, 0.12f);
                    banner.style.borderLeftColor = new Color(0.20f, 0.70f, 0.35f);
                }
                else
                {
                    banner.style.backgroundColor = new Color(0.10f, 0.18f, 0.28f);
                    banner.style.borderLeftColor = new Color(0.23f, 0.51f, 0.96f);
                }
                banner.style.borderTopLeftRadius     = 4;
                banner.style.borderTopRightRadius    = 4;
                banner.style.borderBottomLeftRadius  = 4;
                banner.style.borderBottomRightRadius = 4;
                banner.style.borderLeftWidth = 3;

                var lbl = new Label(evt.Description);
                lbl.style.color = isSub
                    ? new Color(0.60f, 0.92f, 0.65f)
                    : new Color(0.85f, 0.92f, 1.00f);
                lbl.style.fontSize = isSub ? 12 : 13;
                lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                lbl.style.whiteSpace = WhiteSpace.Normal;
                banner.Add(lbl);
                return banner;
            }

            int min = evt.ClockSeconds / 60;
            int sec = evt.ClockSeconds % 60;
            string headerLine = $"Q{evt.Quarter}  {min:D2}:{sec:D2}  #{evt.JerseyNumber}  {evt.HomeScore}-{evt.AwayScore}";

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginBottom = 4;
            row.style.paddingTop = 5;
            row.style.paddingBottom = 5;
            row.style.paddingLeft = 10;
            row.style.paddingRight = 10;
            row.style.backgroundColor = new Color(0.12f, 0.15f, 0.18f);
            row.style.borderTopLeftRadius     = 3;
            row.style.borderTopRightRadius    = 3;
            row.style.borderBottomLeftRadius  = 3;
            row.style.borderBottomRightRadius = 3;

            var hdrLbl = new Label(headerLine);
            hdrLbl.style.color = new Color(0.55f, 0.58f, 0.62f);
            hdrLbl.style.fontSize = 11;
            hdrLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(hdrLbl);

            var descLbl = new Label(evt.Description);
            descLbl.style.color = new Color(0.90f, 0.93f, 0.95f);
            descLbl.style.fontSize = 13;
            descLbl.style.whiteSpace = WhiteSpace.Normal;
            row.Add(descLbl);

            return row;
        }

        // ============================================================
        //  阶段性统计计算 + 球队统计卡片
        // ============================================================

        private static void ComputePartialPlayerStats(MatchResult result, int eventsShown,
            out List<PlayerBoxScore> homeStats, out List<PlayerBoxScore> awayStats)
        {
            homeStats = result.HomePlayerStats.Select(p => new PlayerBoxScore
            {
                PlayerId = p.PlayerId, TeamId = p.TeamId,
                PlayerName = p.PlayerName, Position = p.Position,
                Minutes = p.Minutes
            }).ToList();
            awayStats = result.AwayPlayerStats.Select(p => new PlayerBoxScore
            {
                PlayerId = p.PlayerId, TeamId = p.TeamId,
                PlayerName = p.PlayerName, Position = p.Position,
                Minutes = p.Minutes
            }).ToList();

            var byId = new Dictionary<int, PlayerBoxScore>();
            foreach (var p in homeStats.Concat(awayStats))
                byId[p.PlayerId] = p;

            int count = 0;
            foreach (var evt in result.PlayByPlay)
            {
                if (count >= eventsShown) break;
                count++;
                if (evt.Credits == null) continue;
                foreach (var c in evt.Credits)
                {
                    if (!byId.TryGetValue(c.PlayerId, out var bs)) continue;
                    bs.Points                += c.Pts;
                    bs.FieldGoalsMade        += c.FgM;
                    bs.FieldGoalsAttempted   += c.FgA;
                    bs.ThreePointersMade     += c.Fg3M;
                    bs.ThreePointersAttempted+= c.Fg3A;
                    bs.FreeThrowsMade        += c.FtM;
                    bs.FreeThrowsAttempted   += c.FtA;
                    bs.OffensiveRebounds     += c.OffReb;
                    bs.DefensiveRebounds     += c.DefReb;
                    bs.Assists               += c.Ast;
                    bs.Steals                += c.Stl;
                    bs.Blocks                += c.Blk;
                    bs.Turnovers             += c.Tov;
                    bs.Fouls                 += c.PF;
                }
            }
        }

        private static VisualElement BuildPartialTeamCard(MatchResult result,
            List<PlayerBoxScore> homeStats, List<PlayerBoxScore> awayStats)
        {
            int hPts = homeStats.Sum(p => p.Points);
            int aPts = awayStats.Sum(p => p.Points);
            int hFgm = homeStats.Sum(p => p.FieldGoalsMade);
            int hFga = homeStats.Sum(p => p.FieldGoalsAttempted);
            int aFgm = awayStats.Sum(p => p.FieldGoalsMade);
            int aFga = awayStats.Sum(p => p.FieldGoalsAttempted);
            int h3m  = homeStats.Sum(p => p.ThreePointersMade);
            int h3a  = homeStats.Sum(p => p.ThreePointersAttempted);
            int a3m  = awayStats.Sum(p => p.ThreePointersMade);
            int a3a  = awayStats.Sum(p => p.ThreePointersAttempted);
            int hFtm = homeStats.Sum(p => p.FreeThrowsMade);
            int hFta = homeStats.Sum(p => p.FreeThrowsAttempted);
            int aFtm = awayStats.Sum(p => p.FreeThrowsMade);
            int aFta = awayStats.Sum(p => p.FreeThrowsAttempted);
            int hOreb= homeStats.Sum(p => p.OffensiveRebounds);
            int hDreb= homeStats.Sum(p => p.DefensiveRebounds);
            int aOreb= awayStats.Sum(p => p.OffensiveRebounds);
            int aDreb= awayStats.Sum(p => p.DefensiveRebounds);
            int hAst = homeStats.Sum(p => p.Assists);
            int aAst = awayStats.Sum(p => p.Assists);
            int hStl = homeStats.Sum(p => p.Steals);
            int aStl = awayStats.Sum(p => p.Steals);
            int hBlk = homeStats.Sum(p => p.Blocks);
            int aBlk = awayStats.Sum(p => p.Blocks);
            int hTov = homeStats.Sum(p => p.Turnovers);
            int aTov = awayStats.Sum(p => p.Turnovers);
            int hPf  = homeStats.Sum(p => p.Fouls);
            int aPf  = awayStats.Sum(p => p.Fouls);

            var card = NewCard();
            AddCardHeader(card, "球队统计", result.HomeTeamName, result.AwayTeamName);
            AddStatRow(card, "得分",   hPts.ToString(), aPts.ToString());
            AddStatRow(card, "投篮",   $"{hFgm}/{hFga}", $"{aFgm}/{aFga}");
            AddStatRow(card, "命中率", Pct(hFgm, hFga),  Pct(aFgm, aFga));
            AddStatRow(card, "三分",   $"{h3m}/{h3a}", $"{a3m}/{a3a}");
            AddStatRow(card, "三分率", Pct(h3m, h3a),  Pct(a3m, a3a));
            AddStatRow(card, "罚球",   $"{hFtm}/{hFta}", $"{aFtm}/{aFta}");
            AddStatRow(card, "罚球率", Pct(hFtm, hFta),  Pct(aFtm, aFta));
            AddStatRow(card, "篮板",   (hOreb + hDreb).ToString(), (aOreb + aDreb).ToString());
            AddStatRow(card, "前场板", hOreb.ToString(), aOreb.ToString());
            AddStatRow(card, "助攻",   hAst.ToString(), aAst.ToString());
            AddStatRow(card, "抢断",   hStl.ToString(), aStl.ToString());
            AddStatRow(card, "盖帽",   hBlk.ToString(), aBlk.ToString());
            AddStatRow(card, "失误",   hTov.ToString(), aTov.ToString());
            AddStatRow(card, "犯规",   hPf.ToString(),  aPf.ToString());
            return card;
        }

        // ============================================================
        //  播报播放控制
        // ============================================================

        private void TogglePbpPlayback()
        {
            if (_pbpPlaying)
            {
                StopPbpPlayback();
            }
            else if (_pbpEventRows == null || _pbpIndex >= _pbpEventRows.Count)
            {
                // 已播完，不做任何事
            }
            else
            {
                _pbpCoroutine = StartCoroutine(PbpCoroutine()); // 开始或继续
            }
        }

        private void StopPbpPlayback()
        {
            if (_pbpCoroutine != null)
            {
                StopCoroutine(_pbpCoroutine);
                _pbpCoroutine = null;
            }
            _pbpPlaying = false;
            UpdatePbpControls();
        }

        private void AdjustSpeed(int delta)
        {
            _pbpSpeedIdx = Mathf.Clamp(_pbpSpeedIdx + delta, 0, _pbpSpeeds.Length - 1);
            UpdatePbpControls();
            if (_pbpPlaying)
            {
                StopCoroutine(_pbpCoroutine);
                _pbpCoroutine = StartCoroutine(PbpCoroutine());
            }
        }

        private void UpdatePbpControls()
        {
            if (_pbpPlayPauseBtn != null)
            {
                bool finished = _pbpEventRows != null && _pbpIndex >= _pbpEventRows.Count && _pbpEventRows.Count > 0;
                _pbpPlayPauseBtn.text = _pbpPlaying ? "⏸ 暂停"
                    : finished ? "✓ 完成"
                    : _pbpIndex > 0 ? "▶ 继续"
                    : "▶ 播放";
                _pbpPlayPauseBtn.SetEnabled(!finished || _pbpPlaying);
            }
            if (_pbpSpeedLabel != null)
                _pbpSpeedLabel.text = $"×{(int)_pbpSpeeds[_pbpSpeedIdx]}";
        }

        private void UpdateScoreCard()
        {
            if (_currentResult == null) return;

            int h = 0, a = 0;
            if (_pbpEventsShown > 0)
            {
                var last = _currentResult.PlayByPlay[_pbpEventsShown - 1];
                h = last.HomeScore;
                a = last.AwayScore;
            }

            if (_scorecardHeadline != null)
                _scorecardHeadline.text = $"{_currentResult.HomeTeamName}   {h} - {a}   {_currentResult.AwayTeamName}";

            if (_scorecardQuarters == null) return;

            // 从已播出的事件计算各节得分
            int curQ = 0, qStartH = 0, qStartA = 0, prevH = 0, prevA = 0;
            int count = 0;
            var qLines = new System.Collections.Generic.List<string>();

            foreach (var evt in _currentResult.PlayByPlay)
            {
                if (count >= _pbpEventsShown) break;
                count++;
                if (evt.Quarter != curQ)
                {
                    if (curQ > 0)
                    {
                        string name = curQ <= 4 ? $"Q{curQ}" : $"OT{curQ - 4}";
                        qLines.Add($"{name} {prevH - qStartH}-{prevA - qStartA}");
                    }
                    curQ = evt.Quarter;
                    qStartH = prevH;
                    qStartA = prevA;
                }
                prevH = evt.HomeScore;
                prevA = evt.AwayScore;
            }
            if (curQ > 0)
            {
                string name = curQ <= 4 ? $"Q{curQ}" : $"OT{curQ - 4}";
                qLines.Add($"{name} {prevH - qStartH}-{prevA - qStartA}");
            }

            _scorecardQuarters.text = string.Join("  |  ", qLines);
        }

        private IEnumerator PbpCoroutine()
        {
            _pbpPlaying = true;
            UpdatePbpControls();

            int total = _currentResult?.PlayByPlay.Count ?? 0;

            while (_pbpIndex < _pbpEventRows.Count)
            {
                var row = _pbpEventRows[_pbpIndex];
                bool isEvent = _pbpRowEventIdx != null && _pbpIndex < _pbpRowEventIdx.Count
                               && _pbpRowEventIdx[_pbpIndex] >= 0;

                // 插到顶部——新内容始终在最上面
                _pbpListContainer?.Insert(0, row);
                _pbpIndex++;
                if (isEvent) _pbpEventsShown++;

                if (_pbpProgressLabel != null)
                    _pbpProgressLabel.text = $"{_pbpEventsShown} / {total}";

                if (isEvent) UpdateScoreCard();

                // 节标题不计时，直接显示下一行
                if (!isEvent) continue;

                yield return new WaitForSeconds(1f / _pbpSpeeds[_pbpSpeedIdx]);
            }

            _pbpPlaying = false;
            _pbpCoroutine = null;
            UpdatePbpControls();
        }

        private static void StyleCtrlBtn(Button btn)
        {
            btn.style.paddingLeft  = 10;
            btn.style.paddingRight = 10;
            btn.style.marginLeft   = 2;
            btn.style.marginRight  = 2;
            btn.style.height = 28;
        }

        // ============================================================
        //  渲染：批量结果（不含标签页）
        // ============================================================

        private void RenderBatchReport(MatchSimulationReport report)
        {
            ClearResults(showEmpty: false);

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

            var startersCard = NewCard();
            AddCardHeader(startersCard, "首发阵容", report.HomeTeamName, report.AwayTeamName);
            foreach (var pos in new[] { Position.PG, Position.SG, Position.SF, Position.PF, Position.C })
            {
                string h = report.HomeStarters.TryGetValue(pos, out var hv) ? hv : "-";
                string a = report.AwayStarters.TryGetValue(pos, out var av) ? av : "-";
                AddStatRow(startersCard, pos.ToString(), h, a);
            }
            _resultContent.Add(startersCard);

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

            view.columns.Add(Col("name", "球员", 130, Length.Percent(17), () => Cell(),                     (e, i) => ((Label)e).text = data[i].PlayerName));
            view.columns.Add(Col("min",  "时间",  40,  Length.Percent(5),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = data[i].Minutes.ToString()));
            view.columns.Add(Col("pts",  "得分",  44,  Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Points.ToString()));
            view.columns.Add(Col("reb",  "篮板",  44,  Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Rebounds.ToString()));
            view.columns.Add(Col("ast",  "助攻",  44,  Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Assists.ToString()));
            view.columns.Add(Col("stl",  "抢断",  40,  Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Steals.ToString()));
            view.columns.Add(Col("blk",  "盖帽",  40,  Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Blocks.ToString()));
            view.columns.Add(Col("tov",  "失误",  40,  Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Turnovers.ToString()));
            view.columns.Add(Col("pf",   "犯规",  36,  Length.Percent(4),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Fouls.ToString()));
            view.columns.Add(Col("pm",   "正负",  44,  Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var lbl = (Label)e;
                int pm = data[i].PlusMinus;
                lbl.text = pm > 0 ? $"+{pm}" : pm.ToString();
                lbl.RemoveFromClassList("table-cell--pos");
                lbl.RemoveFromClassList("table-cell--neg");
                if (pm > 0) lbl.AddToClassList("table-cell--pos");
                else if (pm < 0) lbl.AddToClassList("table-cell--neg");
            }));
            view.columns.Add(Col("fg",   "投篮", 72,  Length.Percent(11), () => Cell("table-cell--center"), (e, i) => ((Label)e).text = $"{data[i].FieldGoalsMade}/{data[i].FieldGoalsAttempted}"));
            view.columns.Add(Col("tp",   "三分", 72,  Length.Percent(11), () => Cell("table-cell--center"), (e, i) => ((Label)e).text = $"{data[i].ThreePointersMade}/{data[i].ThreePointersAttempted}"));
            view.columns.Add(Col("ft",   "罚球", 72,  Length.Percent(9),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = $"{data[i].FreeThrowsMade}/{data[i].FreeThrowsAttempted}"));

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

            view.columns.Add(Col("name", "球员", 130, Length.Percent(16), () => Cell(),                     (e, i) => ((Label)e).text = data[i].PlayerName));
            view.columns.Add(Col("pos",  "位置", 55,  Length.Percent(6),  () => Cell("table-cell--center"), (e, i) =>
            {
                var p = data[i];
                ((Label)e).text = p.SecondaryPosition.HasValue ? $"{p.PrimaryPosition}/{p.SecondaryPosition.Value}" : p.PrimaryPosition.ToString();
            }));
            view.columns.Add(Col("min", "时间", 44,  Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Minutes.ToString("F1")));
            view.columns.Add(Col("pts", "得分", 44,  Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Points.ToString("F1")));
            view.columns.Add(Col("reb", "篮板", 44,  Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Rebounds.ToString("F1")));
            view.columns.Add(Col("ast", "助攻", 44,  Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Assists.ToString("F1")));
            view.columns.Add(Col("pm",  "正负", 44,  Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var lbl = (Label)e;
                float pm = data[i].PlusMinus;
                lbl.text = pm > 0 ? $"+{pm:F1}" : pm.ToString("F1");
                lbl.RemoveFromClassList("table-cell--pos");
                lbl.RemoveFromClassList("table-cell--neg");
                if (pm > 0.05f) lbl.AddToClassList("table-cell--pos");
                else if (pm < -0.05f) lbl.AddToClassList("table-cell--neg");
            }));
            view.columns.Add(Col("fg",  "投篮", 90,  Length.Percent(10), () => Cell("table-cell--center"), (e, i) =>
            {
                var p = data[i];
                ((Label)e).text = FormatMakeAtt(p.FieldGoalsMade, p.FieldGoalAttempts);
            }));
            view.columns.Add(Col("fg%", "投篮%", 46,  Length.Percent(6),  () => Cell("table-cell--num"), (e, i) =>
            {
                var p = data[i];
                ((Label)e).text = p.FieldGoalAttempts < 0.01f ? "—" : $"{p.FieldGoalsMade / p.FieldGoalAttempts * 100f:F1}%";
            }));
            view.columns.Add(Col("tp",  "三分", 90,  Length.Percent(10), () => Cell("table-cell--center"), (e, i) =>
            {
                var p = data[i];
                ((Label)e).text = FormatMakeAtt(p.ThreePointersMade, p.ThreePointAttempts);
            }));
            view.columns.Add(Col("tp%", "三分%", 46,  Length.Percent(6),  () => Cell("table-cell--num"), (e, i) =>
            {
                var p = data[i];
                ((Label)e).text = p.ThreePointAttempts < 0.01f ? "—" : $"{p.ThreePointersMade / p.ThreePointAttempts * 100f:F1}%";
            }));
            view.columns.Add(Col("ft",  "罚球", 90,  Length.Percent(10), () => Cell("table-cell--center"), (e, i) =>
            {
                var p = data[i];
                ((Label)e).text = FormatMakeAtt(p.FreeThrowsMade, p.FreeThrowAttempts);
            }));
            view.columns.Add(Col("ft%", "罚球%", 46,  Length.Percent(6),  () => Cell("table-cell--num"), (e, i) =>
            {
                var p = data[i];
                ((Label)e).text = p.FreeThrowAttempts < 0.01f ? "—" : $"{p.FreeThrowsMade / p.FreeThrowAttempts * 100f:F1}%";
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
        //  通用 helpers
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
                    if (!string.IsNullOrEmpty(c)) lbl.AddToClassList(c);
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
