using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BasketballManager.Core.Models;
using BasketballManager.Core.Services;
using BasketballManager.Database;
using BasketballManager.Seasons;
using BasketballManager.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BasketballManager.UI.Screens
{
    /// <summary>
    /// 赛季界面（UI Toolkit + 分页）：
    /// - 顶栏：返回 / 操作按钮（创建/模拟下一场/模拟剩余）
    /// - 一级 tab：总览 / 积分榜 / 赛程 / 数据
    /// - 数据下二级 tab：球员 / 球队
    /// </summary>
    public sealed class SeasonScreen : UIToolkitScreenBase
    {
        public const string Id = "Season";

        public event Action OnBackClicked;

        private SeasonService _seasonService;
        private SeasonRepository _seasonRepository;
        private TeamRepository _teamRepository;

        private Season _currentSeason;
        private MatchResult _lastResult;
        private readonly Dictionary<string, string> _teamNameById = new Dictionary<string, string>();
        private bool _isSimulating;

        // ---------- 比赛弹窗 ----------
        private VisualElement _modalOverlay;
        private Label _modalScoreHeadline;
        private Label _modalScoreQuarters;
        private VisualElement _modalTabContent;
        private Button _modalTabBtnPbp, _modalTabBtnPlayer, _modalTabBtnTeam;
        private VisualElement _modalPagePbp, _modalPagePlayer, _modalPageTeam;

        // 弹窗播报状态
        private MatchResult _modalResult;
        private int _modalSeasonId;
        private bool _modalIsOpen;
        private bool _schedulePointerDown; // 区分"点击触发选中"与"hover/键盘触发选中"
        private List<VisualElement> _modalPbpEventRows = new List<VisualElement>();
        private List<int> _modalPbpRowEventIdx = new List<int>();
        private int _modalPbpIndex;
        private int _modalPbpEventsShown;
        private bool _modalPbpPlaying;
        private Coroutine _modalPbpCoroutine;
        private ScrollView _modalPbpScroll;
        private Label _modalPbpProgressLabel;
        private Label _modalPbpSpeedLabel;
        private Button _modalPbpPlayPauseBtn;
        private readonly float[] _modalPbpSpeeds = { 1f, 2f, 4f };
        private int _modalPbpSpeedIdx;
        private VisualElement _modalPbpListContainer;
        private ScrollView _modalPlayerScroll;
        private ScrollView _modalTeamScroll;

        // ---------- 顶栏 ----------
        private Label _seasonNameLabel;

        // ---------- tab 系统 ----------
        private readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, VisualElement> _tabPages = new Dictionary<string, VisualElement>();
        private string _currentTab = "overview";

        private readonly Dictionary<string, Button> _subTabButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, VisualElement> _subTabPages = new Dictionary<string, VisualElement>();
        private string _currentSubTab = "players";

        // ---------- 顶栏按钮缓存 ----------
        private Button _btnSimNext;
        private Button _btnSimAll;
        private Button _btnNextSeason;

        // ---------- 赛季间总结弹窗 ----------
        private VisualElement _offseasonOverlay;
        private ScrollView _offseasonScroll;
        private Button _btnOffseasonConfirm;

        // ---------- FA overlay ----------
        private VisualElement _faOverlay;
        private Label _faCapSpaceLabel;
        private ScrollView _faList;

        // ---------- 选秀 overlay ----------
        private VisualElement _draftOverlay;
        private Label _draftPickInfo;
        private ScrollView _draftLog;
        private ScrollView _draftProspects;
        private Button _btnDraftAuto;
        private Button _btnDraftDone;

        // ---------- 休赛期状态 ----------
        private int _offseasonSeasonId;
        private List<DraftSlot> _draftSlots;
        private List<Player> _availableProspects;
        private int _currentDraftPickIdx;
        private bool _waitingForUserDraftPick;
        private Coroutine _draftCoroutine;
        private int _realTeamCount;

        // ---------- 用户球队 ----------
        private string _userTeamId = "";
        private PlayerRepository _playerRepository;
        private SimulationProfileRepository _profileRepository;

        // ---------- 赛前决策面板 ----------
        private VisualElement _pregameOverlay;
        private Label _pregameTitle;
        private Label _pregameMatchup;
        private VisualElement _pregamePlayerList;
        private SeasonGame _pendingGame;
        private readonly List<(Player player, float sourceMpg)> _pregameRoster
            = new List<(Player, float)>();

        // ---------- 季后赛 ----------
        private Button _btnStartPlayoffs;
        private VisualElement _bracketContainer;
        private VisualElement _statsPhaseRow;
        private Button _statsPhaseRegularBtn;
        private Button _statsPhasePlayoffBtn;
        private string _statsPhase = "REGULAR";

        // ---------- overview ----------
        private Label _ovLastHeadline, _ovLastMeta;
        private VisualElement _ovRecentList, _ovStandoutList;
        private Label _ovRecentEmpty, _ovStandoutEmpty;

        // ---------- 积分榜 ----------
        private MultiColumnListView _standingsView;
        private Label _standingsCount;
        private readonly List<SeasonTeamStanding> _standings = new List<SeasonTeamStanding>();

        // ---------- 赛程 ----------
        private MultiColumnListView _scheduleView;
        private Label _scheduleCount;
        private readonly List<SeasonGame> _schedule = new List<SeasonGame>();

        // ---------- 数据/球员 ----------
        private MultiColumnListView _playersView;
        private Label _playersCount;
        private readonly List<SeasonPlayerStat> _playerStats = new List<SeasonPlayerStat>();

        // ---------- 数据/球队 ----------
        private MultiColumnListView _teamsView;
        private Label _teamsCount;
        private readonly List<SeasonTeamStanding> _teamStats = new List<SeasonTeamStanding>();

        // ---------- 球员详情弹窗 ----------
        private VisualElement _pdOverlay;
        private Label _pdName, _pdMeta, _pdInjury;
        private Button _pdClose;
        private Button _pdTabInfo, _pdTabStats, _pdTabCareer;
        private ScrollView _pdContent;
        private int _pdActivePlayerId;
        private string _pdActiveTab = "info";

        private void Awake() { ScreenId = Id; }

        public void Initialize(SeasonService seasonService, SeasonRepository seasonRepository,
            TeamRepository teamRepository, PlayerRepository playerRepository = null,
            SimulationProfileRepository profileRepository = null)
        {
            _seasonService = seasonService;
            _seasonRepository = seasonRepository;
            _teamRepository = teamRepository;
            _playerRepository = playerRepository;
            _profileRepository = profileRepository;
        }

        public void SetUserTeam(string teamId) { _userTeamId = teamId; }

        private bool IsUserTeamGame(SeasonGame game)
            => !string.IsNullOrEmpty(_userTeamId)
               && (game.HomeTeamId == _userTeamId || game.AwayTeamId == _userTeamId);

        protected override void OnBuilt()
        {
            // 顶栏
            Root.Q<Button>("btn-back").clicked += () => OnBackClicked?.Invoke();
            Root.Q<Button>("btn-new").clicked += OnCreateNewSeason;
            _btnSimNext = Root.Q<Button>("btn-sim-next");
            _btnSimAll  = Root.Q<Button>("btn-sim-all");
            _btnSimNext.clicked += OnSimulateNext;
            _btnSimAll.clicked  += OnSimulateAllRemaining;

            _btnStartPlayoffs = Root.Q<Button>("btn-start-playoffs");
            _btnStartPlayoffs.clicked += OnStartPlayoffs;
            _btnStartPlayoffs.style.display = DisplayStyle.None;

            _btnNextSeason = Root.Q<Button>("btn-next-season");
            _btnNextSeason.clicked += OnNextSeasonClicked;
            _btnNextSeason.style.display = DisplayStyle.None;

            _bracketContainer = Root.Q<VisualElement>("bracket-container");
            _bracketContainer.style.display = DisplayStyle.None;

            _statsPhaseRow      = Root.Q<VisualElement>("stats-phase-row");
            _statsPhaseRegularBtn = Root.Q<Button>("statphase-regular");
            _statsPhasePlayoffBtn = Root.Q<Button>("statphase-playoff");
            _statsPhaseRegularBtn.clicked += () => SwitchStatsPhase("REGULAR");
            _statsPhasePlayoffBtn.clicked += () => SwitchStatsPhase("PLAYOFF");
            _statsPhaseRow.style.display = DisplayStyle.None;

            _seasonNameLabel = Root.Q<Label>("season-name");

            // tab 注册
            RegisterTab("overview",  "tab-overview",  "page-overview");
            RegisterTab("standings", "tab-standings", "page-standings");
            RegisterTab("schedule",  "tab-schedule",  "page-schedule");
            RegisterTab("data",      "tab-data",      "page-data");
            foreach (var kv in _tabButtons)
            {
                var id = kv.Key;
                kv.Value.clicked += () => SwitchTab(id);
            }

            RegisterSubTab("players", "subtab-players", "subpage-players");
            RegisterSubTab("teams",   "subtab-teams",   "subpage-teams");
            foreach (var kv in _subTabButtons)
            {
                var id = kv.Key;
                kv.Value.clicked += () => SwitchSubTab(id);
            }

            // overview
            _ovLastHeadline = Root.Q<Label>("ov-last-headline");
            _ovLastMeta     = Root.Q<Label>("ov-last-meta");
            _ovRecentList   = Root.Q<VisualElement>("ov-recent-list");
            _ovStandoutList = Root.Q<VisualElement>("ov-standout-list");
            _ovRecentEmpty  = Root.Q<Label>("ov-recent-empty");
            _ovStandoutEmpty= Root.Q<Label>("ov-standout-empty");

            // standings
            _standingsView  = Root.Q<MultiColumnListView>("standings");
            _standingsCount = Root.Q<Label>("standings-count");
            _standingsView.itemsSource = _standings;
            ConfigureStandingsColumns();

            // schedule
            _scheduleView  = Root.Q<MultiColumnListView>("schedule");
            _scheduleCount = Root.Q<Label>("schedule-count");
            _scheduleView.itemsSource = _schedule;
            ConfigureScheduleColumns();

            // data > players
            _playersView  = Root.Q<MultiColumnListView>("players-stats");
            _playersCount = Root.Q<Label>("players-count");
            _playersView.itemsSource = _playerStats;
            ConfigurePlayersColumns();

            // data > teams
            _teamsView  = Root.Q<MultiColumnListView>("teams-stats");
            _teamsCount = Root.Q<Label>("teams-count");
            _teamsView.itemsSource = _teamStats;
            ConfigureTeamsColumns();

            // 弹窗
            // 赛前决策面板
            _pregameOverlay    = Root.Q<VisualElement>("pregame-modal-overlay");
            _pregameTitle      = Root.Q<Label>("pregame-title");
            _pregameMatchup    = Root.Q<Label>("pregame-matchup");
            _pregamePlayerList = Root.Q<VisualElement>("pregame-player-list");
            Root.Q<Button>("btn-pregame-skip").clicked    += OnPregameSkip;
            Root.Q<Button>("btn-pregame-confirm").clicked += OnPregameConfirm;
            _pregameOverlay.style.display = DisplayStyle.None;

            _modalOverlay       = Root.Q<VisualElement>("match-modal-overlay");
            _modalScoreHeadline = Root.Q<Label>("modal-score-headline");
            _modalScoreQuarters = Root.Q<Label>("modal-score-quarters");
            Root.Q<Button>("modal-close-btn").clicked += CloseMatchModal;
            _modalTabBtnPbp    = Root.Q<Button>("modal-tab-pbp");
            _modalTabBtnPlayer = Root.Q<Button>("modal-tab-player");
            _modalTabBtnTeam   = Root.Q<Button>("modal-tab-team");
            _modalTabContent   = Root.Q<VisualElement>("modal-tab-content");

            _modalTabBtnPbp.clicked    += () => SwitchModalTab(_modalPagePbp,    _modalTabBtnPbp);
            _modalTabBtnPlayer.clicked += () => SwitchModalTab(_modalPagePlayer, _modalTabBtnPlayer);
            _modalTabBtnTeam.clicked   += () => SwitchModalTab(_modalPageTeam,   _modalTabBtnTeam);

            _modalOverlay.style.display = DisplayStyle.None;

            _offseasonOverlay    = Root.Q<VisualElement>("offseason-overlay");
            _offseasonScroll     = Root.Q<ScrollView>("offseason-scroll");
            _btnOffseasonConfirm = Root.Q<Button>("btn-offseason-confirm");
            _btnOffseasonConfirm.clicked += OnOffseasonConfirm;
            _offseasonOverlay.style.display = DisplayStyle.None;

            _faOverlay       = Root.Q<VisualElement>("fa-overlay");
            _faCapSpaceLabel = Root.Q<Label>("fa-cap-space");
            _faList          = Root.Q<ScrollView>("fa-list");
            Root.Q<Button>("btn-fa-done").clicked += OnFADone;
            _faOverlay.style.display = DisplayStyle.None;

            _draftOverlay   = Root.Q<VisualElement>("draft-overlay");
            _draftPickInfo  = Root.Q<Label>("draft-pick-info");
            _draftLog       = Root.Q<ScrollView>("draft-log");
            _draftProspects = Root.Q<ScrollView>("draft-prospects");
            _btnDraftAuto   = Root.Q<Button>("btn-draft-auto");
            _btnDraftDone   = Root.Q<Button>("btn-draft-done");
            _btnDraftAuto.clicked += OnDraftAutoClicked;
            _btnDraftDone.clicked += OnDraftDoneClicked;
            _draftOverlay.style.display = DisplayStyle.None;

            // 球员详情弹窗
            _pdOverlay  = Root.Q<VisualElement>("player-detail-overlay");
            _pdName     = Root.Q<Label>("pd-name");
            _pdMeta     = Root.Q<Label>("pd-meta");
            _pdInjury   = Root.Q<Label>("pd-injury");
            _pdClose    = Root.Q<Button>("pd-close");
            _pdTabInfo  = Root.Q<Button>("pd-tab-info");
            _pdTabStats = Root.Q<Button>("pd-tab-stats");
            _pdTabCareer= Root.Q<Button>("pd-tab-career");
            _pdContent  = Root.Q<ScrollView>("pd-content");
            _pdClose.clicked    += () => { _pdOverlay.style.display = DisplayStyle.None; };
            _pdTabInfo.clicked  += () => ShowPdTab("info");
            _pdTabStats.clicked += () => ShowPdTab("stats");
            _pdTabCareer.clicked+= () => ShowPdTab("career");
            _pdOverlay.style.display = DisplayStyle.None;

            // 赛程行点击：仅响应真实鼠标点击，不响应 hover/键盘引起的 selectionChanged
            _scheduleView.selectionType = SelectionType.Single;
            _scheduleView.selectionChanged += OnScheduleRowSelected;
            _scheduleView.RegisterCallback<PointerDownEvent>(_ => _schedulePointerDown = true, TrickleDown.TrickleDown);
            // PointerUp 注册在 Root 上：点击行后弹窗出现，PointerUp 落在 overlay 而非 scheduleView，
            // 必须在全屏范围内监听才能保证 flag 被复位
            Root.RegisterCallback<PointerUpEvent>    (_ => _schedulePointerDown = false, TrickleDown.TrickleDown);
            Root.RegisterCallback<PointerCancelEvent>(_ => _schedulePointerDown = false, TrickleDown.TrickleDown);
        }

        protected override void OnEnter()
        {
            RefreshFromLatestSeason();
        }

        protected override void OnExit()
        {
            CloseMatchModal();
            ClosePreGamePanel();
            if (_draftCoroutine != null)
            {
                StopCoroutine(_draftCoroutine);
                _draftCoroutine = null;
            }
        }

        // ============================================================
        //  Tab system
        // ============================================================

        private void RegisterTab(string id, string buttonName, string pageName)
        {
            _tabButtons[id] = Root.Q<Button>(buttonName);
            _tabPages[id] = Root.Q<VisualElement>(pageName);
        }

        private void RegisterSubTab(string id, string buttonName, string pageName)
        {
            _subTabButtons[id] = Root.Q<Button>(buttonName);
            _subTabPages[id] = Root.Q<VisualElement>(pageName);
        }

        private void SwitchTab(string id)
        {
            if (_currentTab == id) return;
            _currentTab = id;
            foreach (var kv in _tabButtons)
            {
                if (kv.Key == id) kv.Value.AddToClassList("tab--active");
                else kv.Value.RemoveFromClassList("tab--active");
            }
            foreach (var kv in _tabPages)
            {
                if (kv.Key == id) kv.Value.RemoveFromClassList("tab-page--hidden");
                else if (!kv.Value.ClassListContains("tab-page--hidden")) kv.Value.AddToClassList("tab-page--hidden");
            }
            RefreshCurrentTab();
        }

        private void SwitchSubTab(string id)
        {
            if (_currentSubTab == id) return;
            _currentSubTab = id;
            foreach (var kv in _subTabButtons)
            {
                if (kv.Key == id) kv.Value.AddToClassList("tab--active");
                else kv.Value.RemoveFromClassList("tab--active");
            }
            foreach (var kv in _subTabPages)
            {
                if (kv.Key == id) kv.Value.RemoveFromClassList("tab-page--hidden");
                else if (!kv.Value.ClassListContains("tab-page--hidden")) kv.Value.AddToClassList("tab-page--hidden");
            }
            RefreshCurrentSubTab();
        }

        private void RefreshCurrentTab()
        {
            switch (_currentTab)
            {
                case "overview":  RefreshOverview();  break;
                case "standings": RefreshStandings(); break;
                case "schedule":  RefreshSchedule();  break;
                case "data":      RefreshCurrentSubTab(); break;
            }
        }

        private void RefreshCurrentSubTab()
        {
            switch (_currentSubTab)
            {
                case "players": RefreshPlayerStats(); break;
                case "teams":   RefreshTeamStats();   break;
            }
        }

        // ============================================================
        //  数据刷新 - 总入口
        // ============================================================

        private void RefreshFromLatestSeason()
        {
            CacheTeamNames();
            _currentSeason = _seasonRepository?.GetLatestSeason();
            _seasonNameLabel.text = _currentSeason != null
                ? $"第{_currentSeason.SeasonNumber}赛季"
                : "—";
            UpdateTopBarButtons();
            RefreshCurrentTab();
        }

        private void RefreshAfterSim()
        {
            if (_currentSeason != null)
                _currentSeason = _seasonRepository?.GetSeasonById(_currentSeason.Id) ?? _currentSeason;
            UpdateTopBarButtons();
            RefreshCurrentTab();
        }

        private void UpdateTopBarButtons()
        {
            if (_currentSeason == null)
            {
                _btnStartPlayoffs.style.display = DisplayStyle.None;
                _btnNextSeason.style.display    = DisplayStyle.None;
                _statsPhaseRow.style.display    = DisplayStyle.None;
                return;
            }

            bool finished        = _currentSeason.Status == "FINISHED";
            bool isPlayoff       = _currentSeason.Phase == "PLAYOFF";
            bool regularComplete = !isPlayoff && _seasonService.IsRegularSeasonComplete(_currentSeason.Id);

            // "开始季后赛"：常规赛已结束 + 未进入季后赛
            _btnStartPlayoffs.style.display = (regularComplete && !finished)
                ? DisplayStyle.Flex : DisplayStyle.None;

            // 模拟按钮：未结束 且 不处于"等待开始季后赛"状态
            bool canSim = !finished && !(regularComplete && !isPlayoff);
            _btnSimNext.style.display = canSim ? DisplayStyle.Flex : DisplayStyle.None;
            _btnSimAll.style.display  = canSim ? DisplayStyle.Flex : DisplayStyle.None;

            // "下一赛季"：赛季已结束
            _btnNextSeason.style.display = finished ? DisplayStyle.Flex : DisplayStyle.None;

            // stats 相位切换：仅季后赛阶段显示
            _statsPhaseRow.style.display = isPlayoff ? DisplayStyle.Flex : DisplayStyle.None;
            if (!isPlayoff) _statsPhase = "REGULAR";
        }

        // ============================================================
        //  总览
        // ============================================================

        private void RefreshOverview()
        {
            // ---- 最近一场卡片 ----
            string headline = null, meta = null;
            SeasonGame last = FindLastPlayedGame();
            if (_lastResult != null)
            {
                headline = $"{_lastResult.HomeTeamName}   {_lastResult.HomeScore} - {_lastResult.AwayScore}   {_lastResult.AwayTeamName}";
                string winner = _lastResult.WinnerTeamId == _lastResult.HomeTeamId ? _lastResult.HomeTeamName : _lastResult.AwayTeamName;
                meta = $"胜者：{winner}";
            }
            else if (last != null)
            {
                headline = $"{NameOf(last.HomeTeamId)}   {last.HomeScore} - {last.AwayScore}   {NameOf(last.AwayTeamId)}";
                meta = $"第 {last.Day} 日";
            }

            if (headline != null)
            {
                _ovLastHeadline.text = headline;
                _ovLastHeadline.RemoveFromClassList("muted");
                _ovLastMeta.text = meta ?? string.Empty;
            }
            else
            {
                _ovLastHeadline.text = "尚未模拟任何比赛。";
                if (!_ovLastHeadline.ClassListContains("muted")) _ovLastHeadline.AddToClassList("muted");
                _ovLastMeta.text = string.Empty;
            }

            // ---- 近期比赛列表 ----
            _ovRecentList.Clear();
            var recent = new List<SeasonGame>();
            if (_currentSeason != null)
            {
                foreach (var g in _seasonRepository.GetSeasonGames(_currentSeason.Id))
                {
                    if (g.Status == "PLAYED") recent.Add(g);
                }
            }
            recent.Reverse(); // 最新在前
            if (recent.Count > 5) recent.RemoveRange(5, recent.Count - 5);

            _ovRecentEmpty.style.display = recent.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var g in recent)
            {
                var row = new VisualElement();
                row.AddToClassList("recent-row");

                var day = new Label($"D{g.Day}");
                day.AddToClassList("recent-row__day");
                row.Add(day);

                var teams = new Label($"{NameOf(g.HomeTeamId)}  vs  {NameOf(g.AwayTeamId)}");
                teams.AddToClassList("recent-row__teams");
                row.Add(teams);

                var score = new Label($"{g.HomeScore} - {g.AwayScore}");
                score.AddToClassList("recent-row__score");
                row.Add(score);

                _ovRecentList.Add(row);
            }

            // ---- 突出表现 ----
            _ovStandoutList.Clear();
            var allStats = GatherAllPlayerStats();
            if (allStats.Count == 0)
            {
                _ovStandoutEmpty.style.display = DisplayStyle.Flex;
                return;
            }
            _ovStandoutEmpty.style.display = DisplayStyle.None;

            var topScorer  = allStats.OrderByDescending(s => s.PointsPerGame).First();
            var topRebound = allStats.OrderByDescending(s => s.ReboundsPerGame).First();
            var topAssist  = allStats.OrderByDescending(s => s.AssistsPerGame).First();

            AppendStandoutRow("PTS", topScorer,  topScorer.PointsPerGame);
            AppendStandoutRow("REB", topRebound, topRebound.ReboundsPerGame);
            AppendStandoutRow("AST", topAssist,  topAssist.AssistsPerGame);
        }

        private void AppendStandoutRow(string label, SeasonPlayerStat stat, float value)
        {
            var row = new VisualElement();
            row.AddToClassList("standout-row");

            var lab = new Label(label);
            lab.AddToClassList("standout-row__label");
            row.Add(lab);

            var player = new Label(stat.PlayerName);
            player.AddToClassList("standout-row__player");
            row.Add(player);

            var team = new Label(NameOf(stat.TeamId));
            team.AddToClassList("standout-row__team");
            row.Add(team);

            var val = new Label($"{value:F1}");
            val.AddToClassList("standout-row__value");
            row.Add(val);

            _ovStandoutList.Add(row);
        }

        private SeasonGame FindLastPlayedGame()
        {
            if (_currentSeason == null) return null;
            SeasonGame last = null;
            foreach (var g in _seasonRepository.GetSeasonGames(_currentSeason.Id))
            {
                if (g.Status == "PLAYED") last = g;
            }
            return last;
        }

        private List<SeasonPlayerStat> GatherAllPlayerStats()
        {
            var list = new List<SeasonPlayerStat>();
            if (_currentSeason == null) return list;
            var teams = _seasonRepository.GetSeasonTeams(_currentSeason.Id);
            foreach (var t in teams)
            {
                foreach (var s in _seasonRepository.GetPlayerSeasonStats(_currentSeason.Id, t.Id))
                {
                    if (s.GamesPlayed > 0) list.Add(s);
                }
            }
            return list;
        }

        // ============================================================
        //  积分榜
        // ============================================================

        private void ConfigureStandingsColumns()
        {
            var cols = _standingsView.columns;
            cols.Clear();
            cols.Add(Col("rank", "#",    36, Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = (i + 1).ToString()));
            cols.Add(Col("team", "球队",  140, Length.Percent(34), () => Cell(), (e, i) =>
            {
                var lbl = (Label)e;
                bool isUser = _standings[i].TeamId == _userTeamId;
                lbl.text = isUser ? "▶ " + _standings[i].TeamName : _standings[i].TeamName;
                if (isUser) lbl.AddToClassList("cell--user-team");
                else lbl.RemoveFromClassList("cell--user-team");
            }));
            cols.Add(Col("w",    "胜",     40, Length.Percent(8),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = _standings[i].Wins.ToString()));
            cols.Add(Col("l",    "负",     40, Length.Percent(8),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = _standings[i].Losses.ToString()));
            cols.Add(Col("pct",  "胜率",  60, Length.Percent(12), () => Cell("table-cell--center"), (e, i) => ((Label)e).text = $"{_standings[i].WinPercentage * 100f:F1}%"));
            cols.Add(Col("pf",   "总得分", 50, Length.Percent(10), () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = _standings[i].PointsFor.ToString()));
            cols.Add(Col("pa",   "总失分", 50, Length.Percent(10), () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = _standings[i].PointsAgainst.ToString()));
            cols.Add(Col("diff", "正负",   60, Length.Percent(12), () => Cell("table-cell--num"),    (e, i) =>
            {
                var lbl = (Label)e;
                int d = _standings[i].PointDifferential;
                lbl.text = d > 0 ? $"+{d}" : d.ToString();
                lbl.RemoveFromClassList("table-cell--pos");
                lbl.RemoveFromClassList("table-cell--neg");
                if (d > 0) lbl.AddToClassList("table-cell--pos");
                else if (d < 0) lbl.AddToClassList("table-cell--neg");
            }));
        }

        private void RefreshStandings()
        {
            _standings.Clear();
            if (_currentSeason != null) _standings.AddRange(_seasonRepository.GetStandings(_currentSeason.Id));
            _standingsCount.text = _standings.Count > 0 ? $"{_standings.Count} 支球队" : "";
            _standingsView.itemsSource = _standings;
            _standingsView.Rebuild();
        }

        // ============================================================
        //  赛程
        // ============================================================

        private void ConfigureScheduleColumns()
        {
            var cols = _scheduleView.columns;
            cols.Clear();
            cols.Add(Col("day", "天",   36, Length.Percent(8),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = _schedule[i].Day.ToString()));
            cols.Add(Col("home","主队", 120, Length.Percent(30), () => Cell(), (e, i) =>
            {
                var lbl = (Label)e;
                bool isUser = _schedule[i].HomeTeamId == _userTeamId;
                lbl.text = NameOf(_schedule[i].HomeTeamId);
                if (isUser) lbl.AddToClassList("cell--user-team");
                else lbl.RemoveFromClassList("cell--user-team");
            }));
            cols.Add(Col("score","比分",80, Length.Percent(20), () => Cell("table-cell--center"), (e, i) =>
            {
                var g = _schedule[i];
                ((Label)e).text = g.Status == "PLAYED" ? $"{g.HomeScore} - {g.AwayScore}" : "—";
            }));
            cols.Add(Col("away","客队", 120, Length.Percent(30), () => Cell(), (e, i) =>
            {
                var lbl = (Label)e;
                bool isUser = _schedule[i].AwayTeamId == _userTeamId;
                lbl.text = NameOf(_schedule[i].AwayTeamId);
                if (isUser) lbl.AddToClassList("cell--user-team");
                else lbl.RemoveFromClassList("cell--user-team");
            }));
            cols.Add(Col("status","状态",60, Length.Percent(12), () => Cell("table-cell--center"), (e, i) =>
            {
                var lbl = (Label)e;
                bool played = _schedule[i].Status == "PLAYED";
                lbl.text = played ? "已赛" : "未赛";
                lbl.RemoveFromClassList("muted");
                if (!played) lbl.AddToClassList("muted");
            }));
        }

        private void RefreshSchedule()
        {
            if (_currentSeason != null && _currentSeason.Phase == "PLAYOFF")
            {
                _scheduleView.style.display    = DisplayStyle.None;
                _bracketContainer.style.display = DisplayStyle.Flex;
                var all = _seasonRepository.GetSeasonGames(_currentSeason.Id);
                int pp = all.Count(g => g.Phase == "PLAYOFF" && g.Status == "PLAYED");
                int pt = all.Count(g => g.Phase == "PLAYOFF" && g.Status != "CANCELLED");
                _scheduleCount.text = $"季后赛 {pp}/{pt} 场";
                RefreshBracket(_currentSeason.Id);
            }
            else
            {
                _scheduleView.style.display    = DisplayStyle.Flex;
                _bracketContainer.style.display = DisplayStyle.None;
                _schedule.Clear();
                if (_currentSeason != null) _schedule.AddRange(_seasonRepository.GetSeasonGames(_currentSeason.Id));
                int played = _schedule.Count(g => g.Status == "PLAYED");
                _scheduleCount.text = _schedule.Count > 0 ? $"{played} / {_schedule.Count} 场" : "";
                _scheduleView.itemsSource = _schedule;
                _scheduleView.Rebuild();
            }
        }

        private void RefreshBracket(int seasonId)
        {
            _bracketContainer.Clear();
            var allSeries = _seasonRepository.GetPlayoffSeries(seasonId);
            if (allSeries.Count == 0) return;

            int maxRound = allSeries.Max(s => s.Round);
            var byRound = allSeries.GroupBy(s => s.Round).OrderBy(g => g.Key);
            foreach (var roundGroup in byRound)
            {
                var title = new Label(GetRoundName(roundGroup.Key, maxRound));
                title.AddToClassList("bracket-round-title");
                _bracketContainer.Add(title);
                foreach (var series in roundGroup)
                    _bracketContainer.Add(BuildSeriesRow(series));
            }
        }

        private static string GetRoundName(int round, int maxRound)
        {
            if (round == maxRound)      return "总  决  赛";
            if (round == maxRound - 1)  return maxRound <= 2 ? "首  轮" : "半  决  赛";
            return "首  轮";
        }

        private VisualElement BuildSeriesRow(PlayoffSeries series)
        {
            var row = new VisualElement();
            row.AddToClassList("series-row");

            var seed1 = new Label($"({series.Seed1})");
            seed1.AddToClassList("series-row__seed");
            row.Add(seed1);

            var team1 = new Label(series.Team1Name);
            team1.AddToClassList("series-row__team");
            if (series.Status == "COMPLETE" && series.WinnerTeamId == series.Team1Id)
                team1.AddToClassList("series-row__winner");
            row.Add(team1);

            var rec1 = new Label(series.Team1Wins.ToString());
            rec1.AddToClassList("series-row__record");
            row.Add(rec1);

            var vs = new Label("-");
            vs.AddToClassList("series-row__vs");
            row.Add(vs);

            var rec2 = new Label(series.Team2Wins.ToString());
            rec2.AddToClassList("series-row__record");
            row.Add(rec2);

            var team2 = new Label(series.Team2Name);
            team2.AddToClassList("series-row__team");
            if (series.Status == "COMPLETE" && series.WinnerTeamId == series.Team2Id)
                team2.AddToClassList("series-row__winner");
            row.Add(team2);

            var seed2 = new Label($"({series.Seed2})");
            seed2.AddToClassList("series-row__seed");
            row.Add(seed2);

            var status = new Label(series.Status == "COMPLETE" ? "完成" : "进行中");
            status.AddToClassList("series-row__status");
            row.Add(status);

            return row;
        }

        // ============================================================
        //  数据/球员
        // ============================================================

        private void ConfigurePlayersColumns()
        {
            var cols = _playersView.columns;
            cols.Clear();
            cols.Add(Col("rank","#",     30, Length.Percent(4),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = (i + 1).ToString()));
            cols.Add(Col("name","球员",  130, Length.Percent(15), () =>
            {
                var lbl = Cell();
                lbl.AddToClassList("cell--clickable");
                lbl.RegisterCallback<ClickEvent>(_ =>
                {
                    if (lbl.userData is int id) ShowPlayerDetail(id);
                });
                return lbl;
            }, (e, i) =>
            {
                var lbl = (Label)e;
                lbl.text = _playerStats[i].PlayerName;
                lbl.userData = _playerStats[i].PlayerId;
            }));
            cols.Add(Col("team","队",    80,  Length.Percent(10), () => Cell(),                     (e, i) => ((Label)e).text = NameOf(_playerStats[i].TeamId)));
            cols.Add(Col("gp",  "场次",   36, Length.Percent(4),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = _playerStats[i].GamesPlayed.ToString()));
            cols.Add(Col("min", "时间",   44, Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{_playerStats[i].MinutesPerGame:F1}"));
            cols.Add(Col("pts", "得分",   44, Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{_playerStats[i].PointsPerGame:F1}"));
            cols.Add(Col("reb", "篮板",   44, Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{_playerStats[i].ReboundsPerGame:F1}"));
            cols.Add(Col("ast", "助攻",   44, Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{_playerStats[i].AssistsPerGame:F1}"));
            cols.Add(Col("stl", "抢断",   40, Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{(_playerStats[i].GamesPlayed == 0 ? 0f : (float)_playerStats[i].Steals    / _playerStats[i].GamesPlayed):F1}"));
            cols.Add(Col("blk", "盖帽",   40, Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{(_playerStats[i].GamesPlayed == 0 ? 0f : (float)_playerStats[i].Blocks    / _playerStats[i].GamesPlayed):F1}"));
            cols.Add(Col("tov", "失误",   40, Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{(_playerStats[i].GamesPlayed == 0 ? 0f : (float)_playerStats[i].Turnovers / _playerStats[i].GamesPlayed):F1}"));
            cols.Add(Col("fg",  "投篮%",  50, Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = _playerStats[i];
                ((Label)e).text = s.FieldGoalsAttempted == 0 ? "—" : $"{(float)s.FieldGoalsMade / s.FieldGoalsAttempted * 100f:F1}%";
            }));
            cols.Add(Col("tp",  "三分%",  50, Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = _playerStats[i];
                ((Label)e).text = s.ThreePointersAttempted == 0 ? "—" : $"{(float)s.ThreePointersMade / s.ThreePointersAttempted * 100f:F1}%";
            }));
            cols.Add(Col("ft",  "罚球%",  50, Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = _playerStats[i];
                ((Label)e).text = s.FreeThrowsAttempted == 0 ? "—" : $"{(float)s.FreeThrowsMade / s.FreeThrowsAttempted * 100f:F1}%";
            }));
            cols.Add(Col("pm",  "正负/场", 50, Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = _playerStats[i];
                if (s.GamesPlayed == 0) { ((Label)e).text = "—"; return; }
                float pmg = (float)s.PlusMinus / s.GamesPlayed;
                var lbl = (Label)e;
                lbl.text = pmg > 0f ? $"+{pmg:F1}" : $"{pmg:F1}";
                lbl.RemoveFromClassList("table-cell--pos");
                lbl.RemoveFromClassList("table-cell--neg");
                if (pmg > 0.05f) lbl.AddToClassList("table-cell--pos");
                else if (pmg < -0.05f) lbl.AddToClassList("table-cell--neg");
            }));
            cols.Add(Col("ts",  "真实%",  50, Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = _playerStats[i];
                float tsa = s.FieldGoalsAttempted + 0.44f * s.FreeThrowsAttempted;
                ((Label)e).text = tsa <= 0f ? "—" : $"{(s.Points / (2f * tsa)) * 100f:F1}%";
            }));
        }

        private void RefreshPlayerStats()
        {
            _playerStats.Clear();
            var src = _statsPhase == "PLAYOFF"
                ? GatherAllPlayerPlayoffStats()
                : GatherAllPlayerStats();
            _playerStats.AddRange(src.OrderByDescending(s => s.PointsPerGame));
            _playersCount.text = _playerStats.Count > 0 ? $"{_playerStats.Count} 名球员" : "";
            _playersView.itemsSource = _playerStats;
            _playersView.Rebuild();
        }

        private List<SeasonPlayerStat> GatherAllPlayerPlayoffStats()
        {
            var list = new List<SeasonPlayerStat>();
            if (_currentSeason == null) return list;
            foreach (var t in _seasonRepository.GetSeasonTeams(_currentSeason.Id))
            {
                foreach (var s in _seasonRepository.GetPlayerPlayoffStats(_currentSeason.Id, t.Id))
                    if (s.GamesPlayed > 0) list.Add(s);
            }
            return list;
        }

        private void SwitchStatsPhase(string phase)
        {
            if (_statsPhase == phase) return;
            _statsPhase = phase;
            _statsPhaseRegularBtn.RemoveFromClassList("tab--active");
            _statsPhasePlayoffBtn.RemoveFromClassList("tab--active");
            if (phase == "REGULAR") _statsPhaseRegularBtn.AddToClassList("tab--active");
            else _statsPhasePlayoffBtn.AddToClassList("tab--active");
            RefreshCurrentSubTab();
        }

        // ============================================================
        //  数据/球队
        // ============================================================

        private void ConfigureTeamsColumns()
        {
            var cols = _teamsView.columns;
            cols.Clear();
            cols.Add(Col("rank","#",     36, Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = (i + 1).ToString()));
            cols.Add(Col("team","球队",  160, Length.Percent(30), () => Cell(),                     (e, i) => ((Label)e).text = _teamStats[i].TeamName));
            cols.Add(Col("gp",  "场次",   40, Length.Percent(8),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = _teamStats[i].GamesPlayed.ToString()));
            cols.Add(Col("w",   "胜",     40, Length.Percent(8),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = _teamStats[i].Wins.ToString()));
            cols.Add(Col("l",   "负",     40, Length.Percent(8),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = _teamStats[i].Losses.ToString()));
            cols.Add(Col("ppg", "得分/场", 60, Length.Percent(13), () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = _teamStats[i];
                ((Label)e).text = s.GamesPlayed == 0 ? "—" : $"{(float)s.PointsFor / s.GamesPlayed:F1}";
            }));
            cols.Add(Col("oppg","失分/场", 60, Length.Percent(13), () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = _teamStats[i];
                ((Label)e).text = s.GamesPlayed == 0 ? "—" : $"{(float)s.PointsAgainst / s.GamesPlayed:F1}";
            }));
            cols.Add(Col("diff","正负/场", 64, Length.Percent(14), () => Cell("table-cell--num"),    (e, i) =>
            {
                var lbl = (Label)e;
                var s = _teamStats[i];
                lbl.RemoveFromClassList("table-cell--pos");
                lbl.RemoveFromClassList("table-cell--neg");
                if (s.GamesPlayed == 0) { lbl.text = "—"; return; }
                float d = (float)(s.PointsFor - s.PointsAgainst) / s.GamesPlayed;
                lbl.text = d > 0 ? $"+{d:F1}" : d.ToString("F1");
                if (d > 0) lbl.AddToClassList("table-cell--pos");
                else if (d < 0) lbl.AddToClassList("table-cell--neg");
            }));
        }

        private void RefreshTeamStats()
        {
            _teamStats.Clear();
            if (_currentSeason != null) _teamStats.AddRange(_seasonRepository.GetStandings(_currentSeason.Id));
            _teamsCount.text = _teamStats.Count > 0 ? $"{_teamStats.Count} 支球队" : "";
            _teamsView.itemsSource = _teamStats;
            _teamsView.Rebuild();
        }

        // ============================================================
        //  事件
        // ============================================================

        private void OnCreateNewSeason()
        {
            if (_isSimulating) return;
            string name = $"赛季 {DateTime.Now:yyyy-MM-dd HH:mm}";
            int seasonId = _seasonService.CreateNewSeasonFromAllTeams(name);
            if (seasonId <= 0) return;
            _lastResult = null;
            RefreshFromLatestSeason();
        }

        private void OnSimulateNext()
        {
            if (_currentSeason == null || _isSimulating) return;
            var nextGame = _seasonRepository.GetNextScheduledGame(_currentSeason.Id);
            if (nextGame != null && IsUserTeamGame(nextGame))
            {
                ShowPreGamePanel(nextGame);
                return;
            }
            var result = _seasonService.SimulateNextGame(_currentSeason.Id, enablePlayByPlay: true);
            if (result == null) return;
            _lastResult = result;
            RefreshAfterSim();
            ShowMatchModal(result, hasPbp: true);
        }

        private void OnSimulateAllRemaining()
        {
            if (_currentSeason == null || _isSimulating) return;
            StartCoroutine(SimulateAllRemainingCoroutine());
        }

        private void OnStartPlayoffs()
        {
            if (_currentSeason == null || _isSimulating) return;
            _seasonService.StartPlayoffs(_currentSeason.Id);
            _statsPhase = "REGULAR";
            RefreshFromLatestSeason();
        }

        private void OnNextSeasonClicked()
        {
            if (_currentSeason == null || _isSimulating) return;
            _offseasonSeasonId = _currentSeason.Id;

            if (_seasonService.FreeAgency != null)
            {
                var (devResults, retiredNames) = _seasonService.AdvanceOffseasonPhase1(_offseasonSeasonId);
                _btnOffseasonConfirm.text = "继续 →";
                Root.Q<Label>("offseason-subtitle").text = "球员成长与退役";

                _offseasonScroll.Clear();
                if (retiredNames.Count > 0)
                {
                    var hdr = new Label($"退役（{retiredNames.Count} 人）");
                    hdr.AddToClassList("card-title");
                    hdr.style.paddingLeft = 14;
                    hdr.style.paddingTop  = 8;
                    _offseasonScroll.Add(hdr);
                    foreach (var n in retiredNames)
                    {
                        var lbl = new Label(n);
                        lbl.style.paddingLeft = 20;
                        lbl.style.paddingTop  = 2;
                        lbl.style.color       = new Color(0.97f, 0.45f, 0.45f);
                        lbl.style.fontSize    = 13;
                        _offseasonScroll.Add(lbl);
                    }
                }
                foreach (var r in devResults)
                    _offseasonScroll.Add(BuildDevRow(r));
                if (devResults.Count == 0 && retiredNames.Count == 0)
                {
                    var empty = new Label("本赛季所有球员属性无变化。");
                    empty.AddToClassList("muted");
                    empty.AddToClassList("caption");
                    empty.style.paddingTop  = 12;
                    empty.style.paddingLeft = 14;
                    _offseasonScroll.Add(empty);
                }
            }
            else
            {
                var (newSeasonId, devResults) = _seasonService.AdvanceToNextSeason(_offseasonSeasonId);
                if (newSeasonId <= 0) return;
                var newSeason = _seasonRepository.GetSeasonById(newSeasonId);
                _btnOffseasonConfirm.text = "进入新赛季";
                Root.Q<Label>("offseason-subtitle").text =
                    $"球员成长与衰退 — 即将进入{newSeason?.Name ?? "新赛季"}";
                _offseasonScroll.Clear();
                foreach (var r in devResults)
                    _offseasonScroll.Add(BuildDevRow(r));
                if (devResults.Count == 0)
                {
                    var empty = new Label("本赛季所有球员属性无变化。");
                    empty.AddToClassList("muted");
                    empty.AddToClassList("caption");
                    empty.style.paddingTop  = 12;
                    empty.style.paddingLeft = 14;
                    _offseasonScroll.Add(empty);
                }
            }

            _lastResult = null;
            _offseasonOverlay.style.display = DisplayStyle.Flex;
        }

        private void OnOffseasonConfirm()
        {
            _offseasonOverlay.style.display = DisplayStyle.None;
            if (_seasonService.FreeAgency != null)
                ShowFAOverlay();
            else
                RefreshFromLatestSeason();
        }

        // ============================================================
        //  自由球员市场
        // ============================================================

        private void ShowFAOverlay()
        {
            RefreshFAOverlay();
            _faOverlay.style.display = DisplayStyle.Flex;
        }

        private void RefreshFAOverlay()
        {
            if (_playerRepository == null) return;
            var freeAgents = _playerRepository.GetFreeAgents();
            int capUsed    = _seasonService.FreeAgency.GetTeamSalary(_userTeamId);
            int capSpace   = FreeAgencyService.SalaryCap - capUsed;
            int roster     = _playerRepository.GetPlayersByTeamId(_userTeamId).Count;

            _faCapSpaceLabel.text = $"薪资帽剩余：{capSpace} M   阵容：{roster} / 15";

            _faList.Clear();
            if (freeAgents.Count == 0)
            {
                var empty = new Label("暂无自由球员");
                empty.AddToClassList("muted");
                empty.AddToClassList("caption");
                empty.style.paddingTop  = 12;
                empty.style.paddingLeft = 14;
                _faList.Add(empty);
                return;
            }
            foreach (var fa in freeAgents)
            {
                int asking = _playerRepository.GetFreeAgentAskingSalary(fa.Id);
                bool canSign = capSpace >= asking && roster < 15;
                _faList.Add(BuildFACard(fa, asking, canSign));
            }
        }

        private VisualElement BuildFACard(Player fa, int asking, bool canSign)
        {
            var row = new VisualElement();
            row.AddToClassList("fa-card");

            var name = new Label(fa.GetDisplayName());
            name.AddToClassList("fa-card__name");
            row.Add(name);

            var pos = new Label(fa.Position.ToString());
            pos.AddToClassList("fa-card__pos");
            row.Add(pos);

            var ovr = new Label($"OVR {fa.Overall}");
            ovr.AddToClassList("fa-card__ovr");
            row.Add(ovr);

            var salary = new Label($"{asking} M/年");
            salary.AddToClassList("fa-card__salary");
            row.Add(salary);

            var localFaId = fa.Id;
            var localAsking = asking;
            var btn = new Button();
            btn.text = "签约";
            btn.AddToClassList("btn");
            btn.SetEnabled(canSign);
            btn.clicked += () =>
            {
                _seasonService.FreeAgency.SignPlayer(localFaId, _userTeamId, localAsking, 2);
                RefreshFAOverlay();
            };
            row.Add(btn);

            return row;
        }

        private void OnFADone()
        {
            _faOverlay.style.display = DisplayStyle.None;
            ShowDraftOverlay();
        }

        // ============================================================
        //  选秀大会
        // ============================================================

        private void ShowDraftOverlay()
        {
            if (_seasonService.Draft == null) { FinishOffseason(); return; }

            var season   = _seasonRepository.GetSeasonById(_offseasonSeasonId);
            int nextYear = (season?.SeasonNumber ?? 0) + 1;

            // 先确定选秀顺序（基于积分榜），再从中推算球队数量，
            // 避免依赖 GetCurrentTeams().Count 可能与实际参赛队数不一致的问题。
            _draftSlots    = _seasonService.Draft.BuildDraftOrder(_offseasonSeasonId);
            _realTeamCount = _draftSlots.Count / 2;  // BuildDraftOrder 固定生成 2 轮

            // 直接使用 GenerateDraftClass 的返回值，不额外查询 GetDraftPool()
            _availableProspects = _seasonService.Draft.GenerateDraftClass(nextYear, _realTeamCount);
            _currentDraftPickIdx = 0;

            if (_draftSlots == null || _draftSlots.Count == 0)
            {
                FinishOffseason();
                return;
            }

            _draftLog.Clear();
            _draftProspects.Clear();
            _btnDraftDone.style.display = DisplayStyle.None;
            _btnDraftAuto.style.display = DisplayStyle.Flex;
            _btnDraftAuto.SetEnabled(false);

            UpdateDraftPickInfo();
            _draftOverlay.style.display = DisplayStyle.Flex;

            _waitingForUserDraftPick = false;
            if (_draftCoroutine != null) StopCoroutine(_draftCoroutine);
            _draftCoroutine = StartCoroutine(DraftCoroutine());
        }

        private IEnumerator DraftCoroutine()
        {
            while (_currentDraftPickIdx < _draftSlots.Count)
            {
                var slot = _draftSlots[_currentDraftPickIdx];
                UpdateDraftPickInfo();

                if (slot.TeamId == _userTeamId)
                {
                    _waitingForUserDraftPick = true;
                    _btnDraftAuto.SetEnabled(true);
                    RefreshDraftProspects(slot);
                    yield return new WaitUntil(() => !_waitingForUserDraftPick);
                }
                else
                {
                    _btnDraftAuto.SetEnabled(false);
                    var pick = _seasonService.Draft.AIAutoPick(_availableProspects);
                    if (pick != null)
                    {
                        _seasonService.Draft.AssignPick(pick, slot.TeamId, slot.Pick, _realTeamCount);
                        _availableProspects.Remove(pick);
                        slot.SelectedPlayer = pick;
                        AddDraftLogEntry(slot, pick);
                    }
                    _currentDraftPickIdx++;
                    yield return new WaitForSeconds(0.15f);
                }
            }

            _draftCoroutine = null;
            _btnDraftAuto.style.display = DisplayStyle.None;
            _btnDraftDone.style.display = DisplayStyle.Flex;
            _draftPickInfo.text = "全部顺位已完成！";
        }

        private void OnDraftAutoClicked()
        {
            if (!_waitingForUserDraftPick || _currentDraftPickIdx >= _draftSlots.Count) return;
            var slot = _draftSlots[_currentDraftPickIdx];
            var pick = _seasonService.Draft.AIAutoPick(_availableProspects);
            if (pick != null) ExecuteDraftPick(slot, pick);
        }

        private void OnUserPickProspect(Player prospect)
        {
            if (!_waitingForUserDraftPick || _currentDraftPickIdx >= _draftSlots.Count) return;
            ExecuteDraftPick(_draftSlots[_currentDraftPickIdx], prospect);
        }

        private void ExecuteDraftPick(DraftSlot slot, Player prospect)
        {
            _seasonService.Draft.AssignPick(prospect, slot.TeamId, slot.Pick, _realTeamCount);
            _availableProspects.Remove(prospect);
            slot.SelectedPlayer = prospect;
            AddDraftLogEntry(slot, prospect);
            _currentDraftPickIdx++;
            _waitingForUserDraftPick = false;
            _btnDraftAuto.SetEnabled(false);
            _draftProspects.Clear();
        }

        private void RefreshDraftProspects(DraftSlot slot)
        {
            _draftProspects.Clear();
            var hdr = new Label($"轮到你了！— {slot.TeamName}");
            hdr.style.paddingTop    = 8;
            hdr.style.paddingLeft   = 10;
            hdr.style.paddingBottom = 6;
            hdr.style.color         = new Color(0.40f, 0.85f, 0.55f);
            hdr.style.fontSize      = 13;
            hdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            _draftProspects.Add(hdr);

            foreach (var p in _availableProspects)
                _draftProspects.Add(BuildProspectCard(p));
        }

        private VisualElement BuildProspectCard(Player p)
        {
            var row = new VisualElement();
            row.AddToClassList("draft-prospect-card");

            var name = new Label(p.GetDisplayName());
            name.AddToClassList("draft-prospect-card__name");
            row.Add(name);

            var pos = new Label(p.Position.ToString());
            pos.AddToClassList("draft-prospect-card__pos");
            row.Add(pos);

            var ovr = new Label($"OVR {p.Overall}");
            ovr.AddToClassList("draft-prospect-card__ovr");
            row.Add(ovr);

            var localP = p;
            var btn = new Button();
            btn.text = "选择";
            btn.AddToClassList("btn");
            btn.clicked += () => OnUserPickProspect(localP);
            row.Add(btn);

            return row;
        }

        private void AddDraftLogEntry(DraftSlot slot, Player player)
        {
            var row = new VisualElement();
            row.AddToClassList("draft-log-row");
            if (slot.TeamId == _userTeamId)
                row.AddToClassList("draft-log-row--user");

            var pick = new Label($"R{slot.Round}·#{slot.Pick}");
            pick.AddToClassList("draft-log-row__pick");
            row.Add(pick);

            var team = new Label(slot.TeamName);
            team.AddToClassList("draft-log-row__team");
            row.Add(team);

            var pname = new Label(player.GetDisplayName());
            pname.AddToClassList("draft-log-row__player");
            row.Add(pname);

            var detail = new Label($"{player.Position}  OVR {player.Overall}");
            detail.AddToClassList("draft-log-row__detail");
            row.Add(detail);

            _draftLog.Insert(0, row);
        }

        private void UpdateDraftPickInfo()
        {
            if (_draftSlots == null || _currentDraftPickIdx >= _draftSlots.Count)
            {
                _draftPickInfo.text = "";
                return;
            }
            var slot = _draftSlots[_currentDraftPickIdx];
            string who = slot.TeamId == _userTeamId ? "你的顺位！" : slot.TeamName;
            _draftPickInfo.text = $"R{slot.Round} · #{slot.Pick}  ·  {who}";
        }

        private void OnDraftDoneClicked()
        {
            if (_draftCoroutine != null) { StopCoroutine(_draftCoroutine); _draftCoroutine = null; }
            _draftOverlay.style.display = DisplayStyle.None;
            FinishOffseason();
        }

        private void FinishOffseason()
        {
            _seasonService.AdvanceOffseasonPhase2(_offseasonSeasonId, _userTeamId);
            _lastResult = null;
            RefreshFromLatestSeason();
        }

        private void OnScheduleRowSelected(IEnumerable<object> items)
        {
            if (_modalIsOpen) return;
            if (!_schedulePointerDown) return; // hover / 键盘 / 程序化触发，忽略
            _schedulePointerDown = false; // 立即复位，防止 flag 在弹窗打开后残留

            SeasonGame picked = null;
            foreach (var item in items)
            {
                if (item is SeasonGame g && g.Status == "PLAYED") { picked = g; break; }
            }
            if (picked == null)
            {
                _scheduleView.SetSelectionWithoutNotify(Array.Empty<int>());
                return;
            }

            // 在 SetSelectionWithoutNotify 之前置锁，防止该调用同步触发 selectionChanged 导致重入
            _modalIsOpen = true;
            _scheduleView.SetSelectionWithoutNotify(Array.Empty<int>());

            var result = new MatchResult
            {
                HomeTeamId   = picked.HomeTeamId,
                AwayTeamId   = picked.AwayTeamId,
                HomeTeamName = NameOf(picked.HomeTeamId),
                AwayTeamName = NameOf(picked.AwayTeamId),
                HomeScore    = picked.HomeScore,
                AwayScore    = picked.AwayScore,
                WinnerTeamId = picked.HomeScore >= picked.AwayScore ? picked.HomeTeamId : picked.AwayTeamId,
            };
            // 加载当场球员数据（新游戏有记录；旧游戏返回空列表，回退到赛季场均）
            if (_seasonRepository != null)
            {
                result.HomePlayerStats = _seasonRepository.GetGamePlayerStats(picked.Id, picked.HomeTeamId);
                result.AwayPlayerStats = _seasonRepository.GetGamePlayerStats(picked.Id, picked.AwayTeamId);
            }
            ShowMatchModal(result, hasPbp: false, seasonId: _currentSeason?.Id ?? 0);
        }

        private System.Collections.IEnumerator SimulateAllRemainingCoroutine()
        {
            _isSimulating = true;
            int seasonId = _currentSeason.Id;
            while (true)
            {
                var nextGame = _seasonRepository.GetNextScheduledGame(seasonId);
                if (nextGame == null) break;
                var result = _seasonService.SimulateNextGame(seasonId);
                if (result == null) break;
                _lastResult = result;
                RefreshAfterSim();
                yield return null;
            }
            _isSimulating = false;
        }

        // ============================================================
        //  赛前决策面板
        // ============================================================

        private void ShowPreGamePanel(SeasonGame game)
        {
            if (_playerRepository == null || _profileRepository == null)
            {
                var fallback = _seasonService.SimulateNextGame(_currentSeason.Id, enablePlayByPlay: true);
                if (fallback == null) return;
                _lastResult = fallback;
                RefreshAfterSim();
                ShowMatchModal(fallback, hasPbp: true);
                return;
            }

            _pendingGame = game;
            bool isHome = game.HomeTeamId == _userTeamId;
            string opponent = NameOf(isHome ? game.AwayTeamId : game.HomeTeamId);
            _pregameTitle.text = $"第 {game.Day} 日  ·  赛前准备";
            _pregameMatchup.text = isHome ? $"主场  vs  {opponent}" : $"客场  @  {opponent}";

            var players = _playerRepository.GetPlayersByTeamId(_userTeamId);
            var profiles = _profileRepository.GetAllProfiles();

            _pregameRoster.Clear();
            foreach (var p in players)
            {
                float mpg = profiles.TryGetValue(p.Id, out var prof) ? prof.SourceMpg : 0f;
                _pregameRoster.Add((p, mpg));
            }
            _pregameRoster.Sort((a, b) => b.sourceMpg.CompareTo(a.sourceMpg));

            RebuildPreGameRosterList();
            _pregameOverlay.style.display = DisplayStyle.Flex;
        }

        private void RebuildPreGameRosterList()
        {
            _pregamePlayerList.Clear();
            for (int i = 0; i < _pregameRoster.Count; i++)
            {
                var (player, mpg) = _pregameRoster[i];
                int idx = i;
                bool isStarter = i < 5;

                var row = new VisualElement();
                row.AddToClassList("pregame-player-row");
                if (isStarter) row.AddToClassList("pregame-player-row--starter");
                if (player.IsInjured) row.AddToClassList("pregame-player-row--injured");

                var up = new Button() { text = "↑" };
                up.AddToClassList("pregame-arrow-btn");
                up.SetEnabled(idx > 0 && !player.IsInjured);
                up.clicked += () => MoveRosterEntry(idx, -1);
                row.Add(up);

                var dn = new Button() { text = "↓" };
                dn.AddToClassList("pregame-arrow-btn");
                dn.SetEnabled(idx < _pregameRoster.Count - 1 && !player.IsInjured);
                dn.clicked += () => MoveRosterEntry(idx, +1);
                row.Add(dn);

                var info = new Label($"#{player.JerseyNumber}  {player.GetDisplayName()}  ({player.Position})");
                info.AddToClassList("pregame-player-info");
                row.Add(info);

                if (player.IsInjured)
                {
                    var injuryBadge = new Label($"⚕ 伤缺 {player.InjuryGamesRemaining} 场");
                    injuryBadge.AddToClassList("injury-badge");
                    row.Add(injuryBadge);
                }

                var badge = new Label(isStarter ? "首发" : "替补");
                badge.AddToClassList("pregame-badge");
                if (isStarter) badge.AddToClassList("pregame-badge--starter");
                row.Add(badge);

                var mpgLbl = new Label($"{mpg:F1}'");
                mpgLbl.AddToClassList("pregame-mpg");
                row.Add(mpgLbl);

                _pregamePlayerList.Add(row);

                if (i == 4 && _pregameRoster.Count > 5)
                {
                    var div = new VisualElement();
                    div.AddToClassList("pregame-divider");
                    _pregamePlayerList.Add(div);
                }
            }
        }

        private void MoveRosterEntry(int fromIdx, int delta)
        {
            int toIdx = fromIdx + delta;
            if (toIdx < 0 || toIdx >= _pregameRoster.Count) return;
            var tmp = _pregameRoster[fromIdx];
            _pregameRoster[fromIdx] = _pregameRoster[toIdx];
            _pregameRoster[toIdx] = tmp;
            RebuildPreGameRosterList();
        }

        private void ClosePreGamePanel()
        {
            if (_pregameOverlay != null)
                _pregameOverlay.style.display = DisplayStyle.None;
            _pendingGame = null;
        }

        private void OnPregameSkip()
        {
            var game = _pendingGame;
            ClosePreGamePanel();
            if (game == null || _currentSeason == null) return;
            var result = _seasonService.SimulateNextGame(_currentSeason.Id, enablePlayByPlay: true);
            if (result == null) return;
            _lastResult = result;
            RefreshAfterSim();
            ShowMatchModal(result, hasPbp: true);
        }

        private void OnPregameConfirm()
        {
            // 将用户排好的顺序映射到 SourceMpg 槽位（原始 mpg 从高到低分配）
            var sortedMpgs = new List<float>(_pregameRoster.Count);
            foreach (var entry in _pregameRoster) sortedMpgs.Add(entry.sourceMpg);
            sortedMpgs.Sort((a, b) => b.CompareTo(a));

            for (int i = 0; i < _pregameRoster.Count; i++)
            {
                int playerId = _pregameRoster[i].player.Id;
                float newMpg = sortedMpgs[i];
                if (System.Math.Abs(_pregameRoster[i].sourceMpg - newMpg) > 0.01f)
                    _profileRepository.UpdateSourceMpg(playerId, newMpg);
                _pregameRoster[i] = (_pregameRoster[i].player, newMpg);
            }

            var game = _pendingGame;
            ClosePreGamePanel();
            if (game == null || _currentSeason == null) return;
            var result = _seasonService.SimulateNextGame(_currentSeason.Id, enablePlayByPlay: true);
            if (result == null) return;
            _lastResult = result;
            RefreshAfterSim();
            ShowMatchModal(result, hasPbp: true);
        }

        // ============================================================
        //  比赛弹窗
        // ============================================================

        private void ShowMatchModal(MatchResult result, bool hasPbp, int seasonId = 0)
        {
            _modalIsOpen = true;
            StopModalPbpPlayback();
            _modalResult = result;
            _modalSeasonId = seasonId;
            _modalTabContent.Clear();
            _modalPagePbp = _modalPagePlayer = _modalPageTeam = null;
            _modalPbpEventRows.Clear();
            _modalPbpRowEventIdx.Clear();
            _modalPbpIndex = 0;
            _modalPbpEventsShown = 0;
            _modalPbpSpeedIdx = 0;
            _modalPbpScroll = _modalPlayerScroll = _modalTeamScroll = null;
            _modalPbpListContainer = null;
            _modalPbpProgressLabel = _modalPbpSpeedLabel = null;
            _modalPbpPlayPauseBtn = null;

            _modalPagePlayer = BuildModalStatsPageShell(out _modalPlayerScroll);
            _modalPageTeam   = BuildModalStatsPageShell(out _modalTeamScroll);

            if (hasPbp)
            {
                _modalScoreHeadline.text = $"{result.HomeTeamName}   0 - 0   {result.AwayTeamName}";
                _modalScoreQuarters.text = string.Empty;
                _modalPagePbp = BuildModalPbpPage(result);

                _modalTabContent.Add(_modalPagePbp);
                _modalTabContent.Add(_modalPagePlayer);
                _modalTabContent.Add(_modalPageTeam);
                _modalPagePlayer.style.display = DisplayStyle.None;
                _modalPageTeam.style.display   = DisplayStyle.None;

                _modalTabBtnPbp.style.display = DisplayStyle.Flex;
                SetModalTabActive(_modalTabBtnPbp);
                _modalOverlay.style.display = DisplayStyle.Flex;

                if (result.PlayByPlay != null && result.PlayByPlay.Count > 0)
                    _modalPbpCoroutine = StartCoroutine(ModalPbpCoroutine());
            }
            else
            {
                _modalScoreHeadline.text = $"{result.HomeTeamName}   {result.HomeScore} - {result.AwayScore}   {result.AwayTeamName}";
                _modalScoreQuarters.text = "历史比赛";

                _modalTabContent.Add(_modalPagePlayer);
                _modalTabContent.Add(_modalPageTeam);
                _modalPagePlayer.style.display = DisplayStyle.None;
                _modalPageTeam.style.display   = DisplayStyle.None;

                _modalTabBtnPbp.style.display = DisplayStyle.None; // 历史比赛无播报，隐藏该 tab
                _modalOverlay.style.display = DisplayStyle.Flex;
                SwitchModalTab(_modalPagePlayer, _modalTabBtnPlayer); // 默认显示球员数据
            }
        }

        private void CloseMatchModal()
        {
            _modalIsOpen = false;
            StopModalPbpPlayback();
            _modalResult = null;
            if (_modalOverlay != null)
                _modalOverlay.style.display = DisplayStyle.None;
            _scheduleView?.SetSelectionWithoutNotify(Array.Empty<int>());
        }

        // ---- 弹窗标签切换 ----

        private void SwitchModalTab(VisualElement targetPage, Button targetBtn)
        {
            if (targetPage == null) return;

            if (_modalPagePbp    != null) _modalPagePbp.style.display    = DisplayStyle.None;
            if (_modalPagePlayer != null) _modalPagePlayer.style.display  = DisplayStyle.None;
            if (_modalPageTeam   != null) _modalPageTeam.style.display    = DisplayStyle.None;

            SetModalTabActive(targetBtn);

            if (_modalResult != null)
            {
                if (targetPage == _modalPagePlayer && _modalPlayerScroll != null)
                {
                    _modalPlayerScroll.Clear();
                    _modalPlayerScroll.style.paddingLeft  = 8;
                    _modalPlayerScroll.style.paddingRight = 8;
                    bool hasPbp = _modalResult.PlayByPlay != null && _modalResult.PlayByPlay.Count > 0;
                    if (hasPbp)
                    {
                        // 有播报：按当前播报进度实时累积
                        ComputePartialPlayerStats(_modalResult, _modalPbpEventsShown,
                            out var homeStats, out var awayStats);
                        _modalPlayerScroll.Add(BuildSinglePlayersCard(_modalResult.HomeTeamName, homeStats));
                        _modalPlayerScroll.Add(BuildSinglePlayersCard(_modalResult.AwayTeamName, awayStats));
                    }
                    else if (_modalResult.HomePlayerStats.Count > 0 || _modalResult.AwayPlayerStats.Count > 0)
                    {
                        // 历史比赛 + per-game 数据已加载
                        _modalPlayerScroll.Add(BuildSinglePlayersCard(_modalResult.HomeTeamName, _modalResult.HomePlayerStats));
                        _modalPlayerScroll.Add(BuildSinglePlayersCard(_modalResult.AwayTeamName, _modalResult.AwayPlayerStats));
                    }
                    else if (_modalSeasonId > 0 && _seasonRepository != null)
                    {
                        // 兼容旧数据：没有 per-game 记录，退回赛季场均
                        var hSeason = _seasonRepository.GetPlayerSeasonStats(_modalSeasonId, _modalResult.HomeTeamId);
                        var aSeason = _seasonRepository.GetPlayerSeasonStats(_modalSeasonId, _modalResult.AwayTeamId);
                        _modalPlayerScroll.Add(BuildSeasonPlayersCard(_modalResult.HomeTeamName, hSeason));
                        _modalPlayerScroll.Add(BuildSeasonPlayersCard(_modalResult.AwayTeamName, aSeason));
                    }
                }
                else if (targetPage == _modalPageTeam && _modalTeamScroll != null)
                {
                    _modalTeamScroll.Clear();
                    _modalTeamScroll.style.paddingLeft  = 8;
                    _modalTeamScroll.style.paddingRight = 8;
                    bool hasPbp = _modalResult.PlayByPlay != null && _modalResult.PlayByPlay.Count > 0;
                    if (hasPbp)
                    {
                        // 有播报：按当前播报进度实时累积
                        ComputePartialPlayerStats(_modalResult, _modalPbpEventsShown,
                            out var homeStats, out var awayStats);
                        _modalTeamScroll.Add(BuildPartialTeamCard(_modalResult, homeStats, awayStats));
                    }
                    else if (_modalResult.HomePlayerStats.Count > 0 || _modalResult.AwayPlayerStats.Count > 0)
                    {
                        // 历史比赛 + per-game 数据已加载
                        _modalTeamScroll.Add(BuildPartialTeamCard(_modalResult, _modalResult.HomePlayerStats, _modalResult.AwayPlayerStats));
                    }
                    else if (_modalSeasonId > 0 && _seasonRepository != null)
                    {
                        // 兼容旧数据：没有 per-game 记录，退回赛季积分榜数据
                        var standings = _seasonRepository.GetStandings(_modalSeasonId);
                        var hS = standings.FirstOrDefault(s => s.TeamId == _modalResult.HomeTeamId);
                        var aS = standings.FirstOrDefault(s => s.TeamId == _modalResult.AwayTeamId);
                        _modalTeamScroll.Add(BuildSeasonTeamCard(_modalResult, hS, aS));
                    }
                }
            }

            targetPage.style.display = DisplayStyle.Flex;
        }

        private void SetModalTabActive(Button active)
        {
            foreach (var btn in new[] { _modalTabBtnPbp, _modalTabBtnPlayer, _modalTabBtnTeam })
            {
                if (btn == null) continue;
                btn.RemoveFromClassList("modal-tab--active");
            }
            active?.AddToClassList("modal-tab--active");
        }

        // ---- 弹窗 PBP 页 ----

        private VisualElement BuildModalPbpPage(MatchResult result)
        {
            var page = new VisualElement();
            page.style.flexGrow = 1;
            page.style.flexDirection = FlexDirection.Column;

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
            controls.style.flexShrink = 0;

            _modalPbpPlayPauseBtn = new Button(ToggleModalPbpPlayback) { text = "▶ 播放" };
            StyleModalCtrlBtn(_modalPbpPlayPauseBtn);
            _modalPbpPlayPauseBtn.style.marginRight = 16;
            controls.Add(_modalPbpPlayPauseBtn);

            var speedLbl = new Label("速度");
            speedLbl.style.color = new Color(0.55f, 0.58f, 0.62f);
            speedLbl.style.fontSize = 12;
            speedLbl.style.marginRight = 6;
            controls.Add(speedLbl);

            var slowBtn = new Button(() => AdjustModalSpeed(-1)) { text = "◀ 减速" };
            StyleModalCtrlBtn(slowBtn);
            controls.Add(slowBtn);

            _modalPbpSpeedLabel = new Label($"×{(int)_modalPbpSpeeds[_modalPbpSpeedIdx]}");
            _modalPbpSpeedLabel.style.width = 42;
            _modalPbpSpeedLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _modalPbpSpeedLabel.style.color = new Color(0.90f, 0.93f, 0.95f);
            _modalPbpSpeedLabel.style.fontSize = 13;
            _modalPbpSpeedLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            controls.Add(_modalPbpSpeedLabel);

            var fastBtn = new Button(() => AdjustModalSpeed(+1)) { text = "加速 ▶" };
            StyleModalCtrlBtn(fastBtn);
            controls.Add(fastBtn);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            controls.Add(spacer);

            _modalPbpProgressLabel = new Label("0 / 0");
            _modalPbpProgressLabel.style.color = new Color(0.55f, 0.58f, 0.62f);
            _modalPbpProgressLabel.style.fontSize = 12;
            controls.Add(_modalPbpProgressLabel);

            page.Add(controls);

            _modalPbpScroll = new ScrollView();
            _modalPbpScroll.style.flexGrow = 1;
            _modalPbpScroll.style.minHeight = 360;
            _modalPbpScroll.style.paddingTop = 8;
            _modalPbpScroll.style.paddingBottom = 8;
            _modalPbpScroll.style.paddingLeft = 8;
            _modalPbpScroll.style.paddingRight = 8;

            _modalPbpListContainer = new VisualElement();
            _modalPbpScroll.Add(_modalPbpListContainer);

            _modalPbpEventRows.Clear();
            _modalPbpRowEventIdx.Clear();
            _modalPbpIndex = 0;
            _modalPbpEventsShown = 0;
            _modalPbpPlaying = false;

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
                    _modalPbpEventRows.Add(qHdr);
                    _modalPbpRowEventIdx.Add(-1);
                    lastQ = evt.Quarter;
                }
                var row = BuildPbpEventRow(evt);
                _modalPbpEventRows.Add(row);
                _modalPbpRowEventIdx.Add(pbpIdx);
                pbpIdx++;
            }

            _modalPbpProgressLabel.text = $"0 / {result.PlayByPlay.Count}";
            page.Add(_modalPbpScroll);
            return page;
        }

        private static VisualElement BuildModalEmptyPbpPage()
        {
            var page = new VisualElement();
            page.style.flexGrow = 1;
            page.style.flexDirection = FlexDirection.Column;
            var lbl = new Label("此比赛无播报记录");
            lbl.style.color = new Color(0.55f, 0.58f, 0.62f);
            lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
            lbl.style.marginTop = 48;
            page.Add(lbl);
            return page;
        }

        private static VisualElement BuildModalStatsPageShell(out ScrollView scroll)
        {
            var page = new VisualElement();
            page.style.flexGrow = 1;
            page.style.flexDirection = FlexDirection.Column;

            scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 360;
            page.Add(scroll);
            return page;
        }

        // ---- 弹窗播放控制 ----

        private void ToggleModalPbpPlayback()
        {
            if (_modalPbpPlaying)
            {
                StopModalPbpPlayback();
            }
            else if (_modalPbpEventRows == null || _modalPbpIndex >= _modalPbpEventRows.Count)
            {
                // 已播完，不做任何事
            }
            else
            {
                _modalPbpCoroutine = StartCoroutine(ModalPbpCoroutine());
            }
        }

        private void StopModalPbpPlayback()
        {
            if (_modalPbpCoroutine != null)
            {
                StopCoroutine(_modalPbpCoroutine);
                _modalPbpCoroutine = null;
            }
            _modalPbpPlaying = false;
            UpdateModalPbpControls();
        }

        private void AdjustModalSpeed(int delta)
        {
            _modalPbpSpeedIdx = Mathf.Clamp(_modalPbpSpeedIdx + delta, 0, _modalPbpSpeeds.Length - 1);
            UpdateModalPbpControls();
            if (_modalPbpPlaying)
            {
                StopCoroutine(_modalPbpCoroutine);
                _modalPbpCoroutine = StartCoroutine(ModalPbpCoroutine());
            }
        }

        private void UpdateModalPbpControls()
        {
            if (_modalPbpPlayPauseBtn != null)
            {
                bool finished = _modalPbpEventRows != null
                    && _modalPbpIndex >= _modalPbpEventRows.Count
                    && _modalPbpEventRows.Count > 0;
                _modalPbpPlayPauseBtn.text = _modalPbpPlaying ? "⏸ 暂停"
                    : finished ? "✓ 完成"
                    : _modalPbpIndex > 0 ? "▶ 继续"
                    : "▶ 播放";
                _modalPbpPlayPauseBtn.SetEnabled(!finished || _modalPbpPlaying);
            }
            if (_modalPbpSpeedLabel != null)
                _modalPbpSpeedLabel.text = $"×{(int)_modalPbpSpeeds[_modalPbpSpeedIdx]}";
        }

        private void UpdateModalScoreCard()
        {
            if (_modalResult == null) return;

            int h = 0, a = 0;
            if (_modalPbpEventsShown > 0)
            {
                var last = _modalResult.PlayByPlay[_modalPbpEventsShown - 1];
                h = last.HomeScore;
                a = last.AwayScore;
            }

            if (_modalScoreHeadline != null)
                _modalScoreHeadline.text = $"{_modalResult.HomeTeamName}   {h} - {a}   {_modalResult.AwayTeamName}";

            if (_modalScoreQuarters == null) return;

            int curQ = 0, qStartH = 0, qStartA = 0, prevH = 0, prevA = 0;
            int count = 0;
            var qLines = new List<string>();

            foreach (var evt in _modalResult.PlayByPlay)
            {
                if (count >= _modalPbpEventsShown) break;
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

            _modalScoreQuarters.text = string.Join("  |  ", qLines);
        }

        private IEnumerator ModalPbpCoroutine()
        {
            _modalPbpPlaying = true;
            UpdateModalPbpControls();

            int total = _modalResult?.PlayByPlay.Count ?? 0;

            while (_modalPbpIndex < _modalPbpEventRows.Count)
            {
                var row = _modalPbpEventRows[_modalPbpIndex];
                bool isEvent = _modalPbpRowEventIdx != null
                    && _modalPbpIndex < _modalPbpRowEventIdx.Count
                    && _modalPbpRowEventIdx[_modalPbpIndex] >= 0;

                _modalPbpListContainer?.Insert(0, row);
                _modalPbpIndex++;
                if (isEvent) _modalPbpEventsShown++;

                if (_modalPbpProgressLabel != null)
                    _modalPbpProgressLabel.text = $"{_modalPbpEventsShown} / {total}";

                if (isEvent) UpdateModalScoreCard();

                if (!isEvent) continue;

                yield return new WaitForSeconds(1f / _modalPbpSpeeds[_modalPbpSpeedIdx]);
            }

            _modalPbpPlaying = false;
            _modalPbpCoroutine = null;
            UpdateModalPbpControls();
        }

        private static void StyleModalCtrlBtn(Button btn)
        {
            btn.style.paddingLeft  = 10;
            btn.style.paddingRight = 10;
            btn.style.marginLeft   = 2;
            btn.style.marginRight  = 2;
            btn.style.height = 28;
        }

        // ---- 统计计算 ----

        private static void ComputePartialPlayerStats(MatchResult result, int eventsShown,
            out List<PlayerBoxScore> homeStats, out List<PlayerBoxScore> awayStats)
        {
            homeStats = result.HomePlayerStats.Select(p => new PlayerBoxScore
            {
                PlayerId = p.PlayerId, TeamId = p.TeamId,
                PlayerName = p.PlayerName, Position = p.Position,
                Minutes = p.Minutes,
                PlusMinus = p.PlusMinus
            }).ToList();
            awayStats = result.AwayPlayerStats.Select(p => new PlayerBoxScore
            {
                PlayerId = p.PlayerId, TeamId = p.TeamId,
                PlayerName = p.PlayerName, Position = p.Position,
                Minutes = p.Minutes,
                PlusMinus = p.PlusMinus
            }).ToList();

            var byId = new Dictionary<int, PlayerBoxScore>();
            foreach (var p in homeStats.Concat(awayStats))
                byId[p.PlayerId] = p;

            int count = 0;
            if (result.PlayByPlay == null) return;
            foreach (var evt in result.PlayByPlay)
            {
                if (count >= eventsShown) break;
                count++;
                if (evt.Credits == null) continue;
                foreach (var c in evt.Credits)
                {
                    if (!byId.TryGetValue(c.PlayerId, out var bs)) continue;
                    bs.Points                 += c.Pts;
                    bs.FieldGoalsMade         += c.FgM;
                    bs.FieldGoalsAttempted    += c.FgA;
                    bs.ThreePointersMade      += c.Fg3M;
                    bs.ThreePointersAttempted += c.Fg3A;
                    bs.FreeThrowsMade         += c.FtM;
                    bs.FreeThrowsAttempted    += c.FtA;
                    bs.OffensiveRebounds      += c.OffReb;
                    bs.DefensiveRebounds      += c.DefReb;
                    bs.Assists                += c.Ast;
                    bs.Steals                 += c.Stl;
                    bs.Blocks                 += c.Blk;
                    bs.Turnovers              += c.Tov;
                    bs.Fouls                  += c.PF;
                }
            }
        }

        private VisualElement BuildSinglePlayersCard(string teamName, List<PlayerBoxScore> stats)
        {
            var card = ModalNewCard();
            card.style.marginBottom = 12;

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
            view.style.height = Mathf.Clamp(data.Count * 28 + 30, 80, 340);

            view.columns.Add(Col("name","球员",130, Length.Percent(17), () => Cell(),                     (e, i) => ((Label)e).text = data[i].PlayerName));
            view.columns.Add(Col("min", "时间",  40, Length.Percent(5),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = data[i].Minutes.ToString()));
            view.columns.Add(Col("pts", "得分",  44, Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Points.ToString()));
            view.columns.Add(Col("reb", "篮板",  44, Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Rebounds.ToString()));
            view.columns.Add(Col("ast", "助攻",  44, Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Assists.ToString()));
            view.columns.Add(Col("stl", "抢断",  40, Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Steals.ToString()));
            view.columns.Add(Col("blk", "盖帽",  40, Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Blocks.ToString()));
            view.columns.Add(Col("tov", "失误",  40, Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Turnovers.ToString()));
            view.columns.Add(Col("pf",  "犯规",  36, Length.Percent(4),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = data[i].Fouls.ToString()));
            view.columns.Add(Col("pm",  "正负",  44, Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var lbl = (Label)e;
                int pm = data[i].PlusMinus;
                lbl.text = pm > 0 ? $"+{pm}" : pm.ToString();
                lbl.RemoveFromClassList("table-cell--pos");
                lbl.RemoveFromClassList("table-cell--neg");
                if (pm > 0) lbl.AddToClassList("table-cell--pos");
                else if (pm < 0) lbl.AddToClassList("table-cell--neg");
            }));
            view.columns.Add(Col("fg",  "投篮", 72,  Length.Percent(11), () => Cell("table-cell--center"), (e, i) => ((Label)e).text = $"{data[i].FieldGoalsMade}/{data[i].FieldGoalsAttempted}"));
            view.columns.Add(Col("tp",  "三分", 72,  Length.Percent(11), () => Cell("table-cell--center"), (e, i) => ((Label)e).text = $"{data[i].ThreePointersMade}/{data[i].ThreePointersAttempted}"));
            view.columns.Add(Col("ft",  "罚球", 72,  Length.Percent(9),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = $"{data[i].FreeThrowsMade}/{data[i].FreeThrowsAttempted}"));

            card.Add(view);
            return card;
        }

        private static VisualElement BuildPartialTeamCard(MatchResult result,
            List<PlayerBoxScore> homeStats, List<PlayerBoxScore> awayStats)
        {
            int hPts = homeStats.Sum(p => p.Points),  aPts = awayStats.Sum(p => p.Points);
            int hFgm = homeStats.Sum(p => p.FieldGoalsMade),  hFga = homeStats.Sum(p => p.FieldGoalsAttempted);
            int aFgm = awayStats.Sum(p => p.FieldGoalsMade),  aFga = awayStats.Sum(p => p.FieldGoalsAttempted);
            int h3m  = homeStats.Sum(p => p.ThreePointersMade),  h3a = homeStats.Sum(p => p.ThreePointersAttempted);
            int a3m  = awayStats.Sum(p => p.ThreePointersMade),  a3a = awayStats.Sum(p => p.ThreePointersAttempted);
            int hFtm = homeStats.Sum(p => p.FreeThrowsMade),  hFta = homeStats.Sum(p => p.FreeThrowsAttempted);
            int aFtm = awayStats.Sum(p => p.FreeThrowsMade),  aFta = awayStats.Sum(p => p.FreeThrowsAttempted);
            int hOreb = homeStats.Sum(p => p.OffensiveRebounds), hDreb = homeStats.Sum(p => p.DefensiveRebounds);
            int aOreb = awayStats.Sum(p => p.OffensiveRebounds), aDreb = awayStats.Sum(p => p.DefensiveRebounds);
            int hAst = homeStats.Sum(p => p.Assists),  aAst = awayStats.Sum(p => p.Assists);
            int hStl = homeStats.Sum(p => p.Steals),   aStl = awayStats.Sum(p => p.Steals);
            int hBlk = homeStats.Sum(p => p.Blocks),   aBlk = awayStats.Sum(p => p.Blocks);
            int hTov = homeStats.Sum(p => p.Turnovers), aTov = awayStats.Sum(p => p.Turnovers);
            int hPf  = homeStats.Sum(p => p.Fouls),    aPf  = awayStats.Sum(p => p.Fouls);

            var card = ModalNewCard();
            AddModalCardHeader(card, "球队统计", result.HomeTeamName, result.AwayTeamName);
            AddModalStatRow(card, "得分",   hPts.ToString(), aPts.ToString());
            AddModalStatRow(card, "投篮",   $"{hFgm}/{hFga}", $"{aFgm}/{aFga}");
            AddModalStatRow(card, "命中率", Pct(hFgm, hFga),  Pct(aFgm, aFga));
            AddModalStatRow(card, "三分",   $"{h3m}/{h3a}", $"{a3m}/{a3a}");
            AddModalStatRow(card, "三分率", Pct(h3m, h3a),  Pct(a3m, a3a));
            AddModalStatRow(card, "罚球",   $"{hFtm}/{hFta}", $"{aFtm}/{aFta}");
            AddModalStatRow(card, "罚球率", Pct(hFtm, hFta),  Pct(aFtm, aFta));
            AddModalStatRow(card, "篮板",   (hOreb + hDreb).ToString(), (aOreb + aDreb).ToString());
            AddModalStatRow(card, "前场板", hOreb.ToString(), aOreb.ToString());
            AddModalStatRow(card, "助攻",   hAst.ToString(), aAst.ToString());
            AddModalStatRow(card, "抢断",   hStl.ToString(), aStl.ToString());
            AddModalStatRow(card, "盖帽",   hBlk.ToString(), aBlk.ToString());
            AddModalStatRow(card, "失误",   hTov.ToString(), aTov.ToString());
            AddModalStatRow(card, "犯规",   hPf.ToString(),  aPf.ToString());
            return card;
        }

        // ---- 弹窗通用 helpers ----

        private static VisualElement ModalNewCard()
        {
            var v = new VisualElement();
            v.AddToClassList("card");
            return v;
        }

        private static void AddModalCardHeader(VisualElement card, string title, string homeName, string awayName)
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

        private static void AddModalStatRow(VisualElement card, string label, string home, string away)
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

        private VisualElement BuildSeasonPlayersCard(string teamName, IReadOnlyList<SeasonPlayerStat> stats)
        {
            var card = ModalNewCard();
            card.style.marginBottom = 12;

            var header = new VisualElement();
            header.AddToClassList("card-header");
            var t = new Label($"{teamName}（赛季场均）");
            t.AddToClassList("card-title");
            header.Add(t);
            card.Add(header);

            var data = stats.Where(s => s.GamesPlayed > 0).ToList();
            var view = new MultiColumnListView
            {
                itemsSource = data,
                showFoldoutHeader = false,
                reorderable = false,
                showBorder = false,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
            };
            view.style.height = Mathf.Clamp(data.Count * 28 + 30, 80, 310);

            view.columns.Add(Col("name","球员",120, Length.Percent(17), () => Cell(), (e, i) => ((Label)e).text = data[i].PlayerName));
            view.columns.Add(Col("gp",  "场次", 32,  Length.Percent(5),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = data[i].GamesPlayed.ToString()));
            view.columns.Add(Col("min", "时间", 44,  Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{data[i].MinutesPerGame:F1}"));
            view.columns.Add(Col("pts", "得分", 44,  Length.Percent(7),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{data[i].PointsPerGame:F1}"));
            view.columns.Add(Col("reb", "篮板", 44,  Length.Percent(7),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{data[i].ReboundsPerGame:F1}"));
            view.columns.Add(Col("ast", "助攻", 44,  Length.Percent(7),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{data[i].AssistsPerGame:F1}"));
            view.columns.Add(Col("stl", "抢断", 40,  Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = data[i];
                ((Label)e).text = s.GamesPlayed == 0 ? "—" : $"{(float)s.Steals / s.GamesPlayed:F1}";
            }));
            view.columns.Add(Col("blk", "盖帽", 40,  Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = data[i];
                ((Label)e).text = s.GamesPlayed == 0 ? "—" : $"{(float)s.Blocks / s.GamesPlayed:F1}";
            }));
            view.columns.Add(Col("tov", "失误", 40,  Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = data[i];
                ((Label)e).text = s.GamesPlayed == 0 ? "—" : $"{(float)s.Turnovers / s.GamesPlayed:F1}";
            }));
            view.columns.Add(Col("fg",  "投篮%", 46,  Length.Percent(7),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = data[i];
                ((Label)e).text = s.FieldGoalsAttempted == 0 ? "—" : $"{(float)s.FieldGoalsMade / s.FieldGoalsAttempted * 100f:F1}%";
            }));
            view.columns.Add(Col("tp",  "三分%", 46,  Length.Percent(7),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = data[i];
                ((Label)e).text = s.ThreePointersAttempted == 0 ? "—" : $"{(float)s.ThreePointersMade / s.ThreePointersAttempted * 100f:F1}%";
            }));
            view.columns.Add(Col("ft",  "罚球%", 46,  Length.Percent(7),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = data[i];
                ((Label)e).text = s.FreeThrowsAttempted == 0 ? "—" : $"{(float)s.FreeThrowsMade / s.FreeThrowsAttempted * 100f:F1}%";
            }));
            view.columns.Add(Col("ts",  "真实%", 46,  Length.Percent(7),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = data[i];
                float tsa = s.FieldGoalsAttempted + 0.44f * s.FreeThrowsAttempted;
                ((Label)e).text = tsa <= 0f ? "—" : $"{s.Points / (2f * tsa) * 100f:F1}%";
            }));

            card.Add(view);
            return card;
        }

        private static VisualElement BuildSeasonTeamCard(MatchResult result,
            SeasonTeamStanding home, SeasonTeamStanding away)
        {
            var card = ModalNewCard();
            AddModalCardHeader(card, "赛季数据", result.HomeTeamName, result.AwayTeamName);

            string Fmt(SeasonTeamStanding s, System.Func<SeasonTeamStanding, string> f)
                => s == null ? "—" : f(s);

            AddModalStatRow(card, "胜",
                Fmt(home, s => s.Wins.ToString()),
                Fmt(away, s => s.Wins.ToString()));
            AddModalStatRow(card, "负",
                Fmt(home, s => s.Losses.ToString()),
                Fmt(away, s => s.Losses.ToString()));
            AddModalStatRow(card, "胜率",
                Fmt(home, s => $"{s.WinPercentage * 100f:F1}%"),
                Fmt(away, s => $"{s.WinPercentage * 100f:F1}%"));
            AddModalStatRow(card, "PF/G",
                Fmt(home, s => s.GamesPlayed == 0 ? "—" : $"{(float)s.PointsFor  / s.GamesPlayed:F1}"),
                Fmt(away, s => s.GamesPlayed == 0 ? "—" : $"{(float)s.PointsFor  / s.GamesPlayed:F1}"));
            AddModalStatRow(card, "PA/G",
                Fmt(home, s => s.GamesPlayed == 0 ? "—" : $"{(float)s.PointsAgainst / s.GamesPlayed:F1}"),
                Fmt(away, s => s.GamesPlayed == 0 ? "—" : $"{(float)s.PointsAgainst / s.GamesPlayed:F1}"));
            AddModalStatRow(card, "+/-/G",
                Fmt(home, s => s.GamesPlayed == 0 ? "—" : $"{(float)s.PointDifferential / s.GamesPlayed:+0.0;-0.0}"),
                Fmt(away, s => s.GamesPlayed == 0 ? "—" : $"{(float)s.PointDifferential / s.GamesPlayed:+0.0;-0.0}"));

            return card;
        }

        private static string Pct(int made, int att) => att <= 0 ? "—" : $"{(float)made / att * 100f:F1}%";

        private static VisualElement BuildPbpEventRow(PlayByPlayEvent evt)
        {
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
                banner.style.backgroundColor = isSub
                    ? new Color(0.08f, 0.18f, 0.12f)
                    : new Color(0.10f, 0.18f, 0.28f);
                banner.style.borderLeftColor = isSub
                    ? new Color(0.20f, 0.70f, 0.35f)
                    : new Color(0.23f, 0.51f, 0.96f);
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
        //  helpers
        // ============================================================

        private static VisualElement BuildDevRow(PlayerDevelopmentResult r)
        {
            var row = new VisualElement();
            row.AddToClassList("dev-row");

            var name = new Label(r.PlayerName);
            name.style.width    = 110;
            name.style.fontSize = 13;
            row.Add(name);

            var age = new Label($"{r.OldAge}→{r.NewAge}岁");
            age.AddToClassList("caption");
            age.style.width            = 68;
            age.style.unityTextAlign   = TextAnchor.MiddleCenter;
            row.Add(age);

            int delta = r.OverallDelta;
            var ovr = new Label($"OVR {r.OldOverall}→{r.NewOverall} ({(delta > 0 ? "+" : "")}{delta})");
            ovr.style.width    = 160;
            ovr.style.fontSize = 13;
            if      (delta > 0) ovr.AddToClassList("dev-ovr-up");
            else if (delta < 0) ovr.AddToClassList("dev-ovr-down");
            else                ovr.AddToClassList("caption");
            row.Add(ovr);

            var topDeltas = r.AttributeDeltas
                .OrderByDescending(kv => Math.Abs(kv.Value))
                .Take(3)
                .ToList();
            string attrText = string.Join("  ", topDeltas.Select(kv =>
                $"{kv.Key}{(kv.Value > 0 ? "+" : "")}{kv.Value}"));
            var attrs = new Label(attrText);
            attrs.style.flexGrow = 1;
            attrs.style.fontSize = 12;
            attrs.style.color    = new Color(0.55f, 0.58f, 0.62f);
            row.Add(attrs);

            return row;
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

        private void CacheTeamNames()
        {
            _teamNameById.Clear();
            if (_teamRepository == null) return;
            foreach (var t in _teamRepository.GetAllTeams()) _teamNameById[t.Id] = t.Name;
        }

        private string NameOf(string teamId)
        {
            return _teamNameById.TryGetValue(teamId, out var name) ? name : teamId;
        }

        // ============================================================
        //  球员详情弹窗
        // ============================================================

        private void ShowPlayerDetail(int playerId)
        {
            if (_playerRepository == null || _pdOverlay == null) return;
            _pdActivePlayerId = playerId;

            var player = _playerRepository.GetPlayerById(playerId);
            if (player == null) return;

            _pdName.text = player.GetDisplayName();
            var pos = player.SecondaryPosition.HasValue
                ? $"{player.Position}/{player.SecondaryPosition.Value}"
                : player.Position.ToString();
            _pdMeta.text = $"OVR {player.Overall}  ·  {pos}  ·  {player.Age}岁  ·  合同 {player.ContractYears}年/{player.ContractSalary}M";
            if (player.IsInjured)
            {
                _pdInjury.text = InjuryService.GetInjuryLabel(player.InjuryGamesRemaining);
                _pdInjury.style.display = DisplayStyle.Flex;
            }
            else
            {
                _pdInjury.style.display = DisplayStyle.None;
            }

            _pdOverlay.style.display = DisplayStyle.Flex;
            ShowPdTab(_pdActiveTab);
        }

        private void ShowPdTab(string tab)
        {
            _pdActiveTab = tab;
            SetPdTabActive(_pdTabInfo,   tab == "info");
            SetPdTabActive(_pdTabStats,  tab == "stats");
            SetPdTabActive(_pdTabCareer, tab == "career");
            _pdContent.Clear();

            switch (tab)
            {
                case "info":   BuildPdInfoTab();   break;
                case "stats":  BuildPdStatsTab();  break;
                case "career": BuildPdCareerTab(); break;
            }
        }

        private void SetPdTabActive(Button btn, bool active)
        {
            if (active) btn.AddToClassList("modal-tab--active");
            else btn.RemoveFromClassList("modal-tab--active");
        }

        private void BuildPdInfoTab()
        {
            if (_playerRepository == null) return;
            var p = _playerRepository.GetPlayerById(_pdActivePlayerId);
            if (p == null) return;

            void Row(string label, string value)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.paddingTop    = row.style.paddingBottom = 4;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new UnityEngine.UIElements.StyleColor(new UnityEngine.Color(1,1,1,0.06f));
                var lbl = new Label(label); lbl.style.width = 140; lbl.AddToClassList("muted");
                var val = new Label(value); val.style.flexGrow = 1;
                row.Add(lbl); row.Add(val);
                _pdContent.Add(row);
            }

            void Section(string title)
            {
                var h = new Label(title);
                h.AddToClassList("modal-section-title");
                h.style.marginTop = 10; h.style.marginBottom = 4;
                _pdContent.Add(h);
            }

            Section("基本信息");
            Row("身高 / 体重", $"{p.HeightCm}cm / {p.WeightKg}kg");
            Row("年龄",       $"{p.Age} 岁");
            Row("位置",       p.SecondaryPosition.HasValue ? $"{p.Position} / {p.SecondaryPosition.Value}" : p.Position.ToString());
            Row("综合评分",   p.Overall.ToString());
            Row("潜力",       $"{p.PotentialMin}–{p.PotentialMax} (均值 {p.Potential})");
            Row("巅峰区间",   $"{p.PeakAgeStart}–{p.PeakAgeEnd} 岁");

            Section("合同");
            Row("剩余年限",   $"{p.ContractYears} 年");
            Row("年薪",       $"{p.ContractSalary} M");

            Section("健康状况");
            Row("伤病状态",   InjuryService.GetInjuryLabel(p.InjuryGamesRemaining));

            Section("属性");
            var attr = p.Attributes;
            Row("速度",       attr.Speed.ToString());        Row("力量",       attr.Strength.ToString());
            Row("两分投篮",   attr.TwoPoint.ToString());     Row("三分投篮",   attr.ThreePoint.ToString());
            Row("近距离出手", attr.CloseShot.ToString());    Row("上篮",       attr.Layup.ToString());
            Row("内线得分",   attr.PostScoring.ToString());  Row("罚球",       attr.FreeThrow.ToString());
            Row("传球",       attr.Passing.ToString());      Row("控球",       attr.BallHandle.ToString());
            Row("突破",       attr.Drive.ToString());        Row("制造犯规",   attr.DrawFoul.ToString());
            Row("外线防守",   attr.PerimeterDefense.ToString()); Row("内线防守", attr.InteriorDefense.ToString());
            Row("抢断",       attr.Steal.ToString());        Row("盖帽",       attr.Block.ToString());
            Row("前场篮板",   attr.OffensiveRebound.ToString()); Row("后场篮板", attr.DefensiveRebound.ToString());
            Row("体力",       attr.Stamina.ToString());
        }

        private void BuildPdStatsTab()
        {
            if (_currentSeason == null || _playerRepository == null) return;
            var allTeams = _seasonRepository.GetSeasonTeams(_currentSeason.Id);
            SeasonPlayerStat found = null;
            foreach (var t in allTeams)
            {
                foreach (var s in _seasonRepository.GetPlayerSeasonStats(_currentSeason.Id, t.Id))
                {
                    if (s.PlayerId == _pdActivePlayerId) { found = s; break; }
                }
                if (found != null) break;
            }

            if (found == null || found.GamesPlayed == 0)
            {
                _pdContent.Add(new Label("本赛季暂无上场数据") { style = { color = new UnityEngine.UIElements.StyleColor(new UnityEngine.Color(1,1,1,0.4f)), marginTop = 20 } });
                return;
            }

            void StatRow(string label, string value)
            {
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.paddingTop = row.style.paddingBottom = 4;
                row.style.borderBottomWidth = 1; row.style.borderBottomColor = new UnityEngine.UIElements.StyleColor(new UnityEngine.Color(1,1,1,0.06f));
                var lbl = new Label(label); lbl.style.width = 160; lbl.AddToClassList("muted");
                var val = new Label(value); val.style.flexGrow = 1;
                row.Add(lbl); row.Add(val); _pdContent.Add(row);
            }

            int gp = found.GamesPlayed;
            float Avg(int total) => gp > 0 ? (float)total / gp : 0f;
            StatRow("场次",   gp.ToString());
            StatRow("上场时间",  $"{Avg(found.Minutes):F1}'");
            StatRow("得分",   $"{Avg(found.Points):F1}");
            StatRow("篮板",   $"{Avg(found.Rebounds):F1}");
            StatRow("助攻",   $"{Avg(found.Assists):F1}");
            StatRow("抢断",   $"{Avg(found.Steals):F1}");
            StatRow("盖帽",   $"{Avg(found.Blocks):F1}");
            StatRow("失误",   $"{Avg(found.Turnovers):F1}");
            StatRow("犯规",   $"{Avg(found.Fouls):F1}");
            string fgPct = found.FieldGoalsAttempted > 0 ? $"{(float)found.FieldGoalsMade / found.FieldGoalsAttempted * 100f:F1}%" : "—";
            string tpPct = found.ThreePointersAttempted > 0 ? $"{(float)found.ThreePointersMade / found.ThreePointersAttempted * 100f:F1}%" : "—";
            string ftPct = found.FreeThrowsAttempted > 0 ? $"{(float)found.FreeThrowsMade / found.FreeThrowsAttempted * 100f:F1}%" : "—";
            StatRow("投篮%", fgPct); StatRow("三分%", tpPct); StatRow("罚球%", ftPct);
            float pmg = gp > 0 ? (float)found.PlusMinus / gp : 0f;
            StatRow("正负/场", pmg >= 0 ? $"+{pmg:F1}" : $"{pmg:F1}");
        }

        private void BuildPdCareerTab()
        {
            var career = _seasonRepository.GetPlayerCareerStats(_pdActivePlayerId);
            if (career.Count == 0)
            {
                _pdContent.Add(new Label("暂无赛季数据") { style = { color = new UnityEngine.UIElements.StyleColor(new UnityEngine.Color(1,1,1,0.4f)), marginTop = 20 } });
                return;
            }

            foreach (var (seasonNumber, seasonName, stat) in career)
            {
                var header = new Label($"赛季 {seasonNumber}  ·  {seasonName}");
                header.AddToClassList("modal-section-title");
                header.style.marginTop = 8; header.style.marginBottom = 2;
                _pdContent.Add(header);

                int gp = stat.GamesPlayed;
                float Avg(int total) => gp > 0 ? (float)total / gp : 0f;
                var summary = new Label(gp == 0
                    ? "无上场记录"
                    : $"{gp} 场  ·  {Avg(stat.Points):F1}分  {Avg(stat.Rebounds):F1}板  {Avg(stat.Assists):F1}助");
                summary.style.marginLeft = 8;
                summary.AddToClassList("muted");
                _pdContent.Add(summary);
            }
        }
    }
}
