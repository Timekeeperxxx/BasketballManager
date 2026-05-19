using System;
using System.Collections.Generic;
using System.Linq;
using BasketballManager.Core.Models;
using BasketballManager.Database;
using BasketballManager.Seasons;
using BasketballManager.UI.Core;
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

        // ---------- 顶栏 ----------
        private Label _seasonNameLabel;

        // ---------- tab 系统 ----------
        private readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, VisualElement> _tabPages = new Dictionary<string, VisualElement>();
        private string _currentTab = "overview";

        private readonly Dictionary<string, Button> _subTabButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, VisualElement> _subTabPages = new Dictionary<string, VisualElement>();
        private string _currentSubTab = "players";

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

        private void Awake() { ScreenId = Id; }

        public void Initialize(SeasonService seasonService, SeasonRepository seasonRepository, TeamRepository teamRepository)
        {
            _seasonService = seasonService;
            _seasonRepository = seasonRepository;
            _teamRepository = teamRepository;
        }

        protected override void OnBuilt()
        {
            // 顶栏
            Root.Q<Button>("btn-back").clicked += () => OnBackClicked?.Invoke();
            Root.Q<Button>("btn-new").clicked += OnCreateNewSeason;
            Root.Q<Button>("btn-sim-next").clicked += OnSimulateNext;
            Root.Q<Button>("btn-sim-all").clicked += OnSimulateAllRemaining;

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
        }

        protected override void OnEnter()
        {
            RefreshFromLatestSeason();
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

            _seasonNameLabel.text = _currentSeason != null ? _currentSeason.Name : "—";
            RefreshCurrentTab();
        }

        private void RefreshAfterSim()
        {
            // 比赛模拟后所有 tab 数据都可能变了，但只刷当前可见的，
            // 切换到其他 tab 时会再次拉取。
            RefreshCurrentTab();
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
            cols.Add(Col("team", "球队",  140, Length.Percent(34), () => Cell(),                     (e, i) => ((Label)e).text = _standings[i].TeamName));
            cols.Add(Col("w",    "W",     40, Length.Percent(8),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = _standings[i].Wins.ToString()));
            cols.Add(Col("l",    "L",     40, Length.Percent(8),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = _standings[i].Losses.ToString()));
            cols.Add(Col("pct",  "胜率",  60, Length.Percent(12), () => Cell("table-cell--center"), (e, i) => ((Label)e).text = $"{_standings[i].WinPercentage * 100f:F1}%"));
            cols.Add(Col("pf",   "PF",    50, Length.Percent(10), () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = _standings[i].PointsFor.ToString()));
            cols.Add(Col("pa",   "PA",    50, Length.Percent(10), () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = _standings[i].PointsAgainst.ToString()));
            cols.Add(Col("diff", "+/-",   60, Length.Percent(12), () => Cell("table-cell--num"),    (e, i) =>
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
            cols.Add(Col("day", "D",   36, Length.Percent(8),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = _schedule[i].Day.ToString()));
            cols.Add(Col("home","主队", 120, Length.Percent(30), () => Cell(),                     (e, i) => ((Label)e).text = NameOf(_schedule[i].HomeTeamId)));
            cols.Add(Col("score","比分",80, Length.Percent(20), () => Cell("table-cell--center"), (e, i) =>
            {
                var g = _schedule[i];
                ((Label)e).text = g.Status == "PLAYED" ? $"{g.HomeScore} - {g.AwayScore}" : "—";
            }));
            cols.Add(Col("away","客队", 120, Length.Percent(30), () => Cell(),                     (e, i) => ((Label)e).text = NameOf(_schedule[i].AwayTeamId)));
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
            _schedule.Clear();
            if (_currentSeason != null) _schedule.AddRange(_seasonRepository.GetSeasonGames(_currentSeason.Id));
            int played = _schedule.Count(g => g.Status == "PLAYED");
            _scheduleCount.text = _schedule.Count > 0 ? $"{played} / {_schedule.Count} 场" : "";
            _scheduleView.itemsSource = _schedule;
            _scheduleView.Rebuild();
        }

        // ============================================================
        //  数据/球员
        // ============================================================

        private void ConfigurePlayersColumns()
        {
            var cols = _playersView.columns;
            cols.Clear();
            cols.Add(Col("rank","#",     36, Length.Percent(5),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = (i + 1).ToString()));
            cols.Add(Col("name","球员",  140, Length.Percent(22), () => Cell(),                     (e, i) => ((Label)e).text = _playerStats[i].PlayerName));
            cols.Add(Col("team","队",    100, Length.Percent(15), () => Cell(),                     (e, i) => ((Label)e).text = NameOf(_playerStats[i].TeamId)));
            cols.Add(Col("gp",  "GP",    40, Length.Percent(6),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = _playerStats[i].GamesPlayed.ToString()));
            cols.Add(Col("min", "MIN",   50, Length.Percent(8),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{_playerStats[i].MinutesPerGame:F1}"));
            cols.Add(Col("pts", "PTS",   50, Length.Percent(8),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{_playerStats[i].PointsPerGame:F1}"));
            cols.Add(Col("reb", "REB",   50, Length.Percent(8),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{_playerStats[i].ReboundsPerGame:F1}"));
            cols.Add(Col("ast", "AST",   50, Length.Percent(8),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{_playerStats[i].AssistsPerGame:F1}"));
            cols.Add(Col("stl", "STL",   50, Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{(_playerStats[i].GamesPlayed == 0 ? 0f : (float)_playerStats[i].Steals / _playerStats[i].GamesPlayed):F1}"));
            cols.Add(Col("blk", "BLK",   50, Length.Percent(6),  () => Cell("table-cell--num"),    (e, i) => ((Label)e).text = $"{(_playerStats[i].GamesPlayed == 0 ? 0f : (float)_playerStats[i].Blocks / _playerStats[i].GamesPlayed):F1}"));
            cols.Add(Col("ts",  "TS%",   60, Length.Percent(8),  () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = _playerStats[i];
                float tsa = s.FieldGoalsAttempted + 0.44f * s.FreeThrowsAttempted;
                ((Label)e).text = tsa <= 0f ? "—" : $"{(s.Points / (2f * tsa)) * 100f:F1}%";
            }));
        }

        private void RefreshPlayerStats()
        {
            _playerStats.Clear();
            _playerStats.AddRange(GatherAllPlayerStats().OrderByDescending(s => s.PointsPerGame));
            _playersCount.text = _playerStats.Count > 0 ? $"{_playerStats.Count} 名球员" : "";
            _playersView.itemsSource = _playerStats;
            _playersView.Rebuild();
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
            cols.Add(Col("gp",  "GP",    40, Length.Percent(8),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = _teamStats[i].GamesPlayed.ToString()));
            cols.Add(Col("w",   "W",     40, Length.Percent(8),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = _teamStats[i].Wins.ToString()));
            cols.Add(Col("l",   "L",     40, Length.Percent(8),  () => Cell("table-cell--center"), (e, i) => ((Label)e).text = _teamStats[i].Losses.ToString()));
            cols.Add(Col("ppg", "PF/G",  60, Length.Percent(13), () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = _teamStats[i];
                ((Label)e).text = s.GamesPlayed == 0 ? "—" : $"{(float)s.PointsFor / s.GamesPlayed:F1}";
            }));
            cols.Add(Col("oppg","PA/G",  60, Length.Percent(13), () => Cell("table-cell--num"),    (e, i) =>
            {
                var s = _teamStats[i];
                ((Label)e).text = s.GamesPlayed == 0 ? "—" : $"{(float)s.PointsAgainst / s.GamesPlayed:F1}";
            }));
            cols.Add(Col("diff","+/-/G", 64, Length.Percent(14), () => Cell("table-cell--num"),    (e, i) =>
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
            var result = _seasonService.SimulateNextGame(_currentSeason.Id);
            if (result != null) _lastResult = result;
            RefreshAfterSim();
        }

        private void OnSimulateAllRemaining()
        {
            if (_currentSeason == null || _isSimulating) return;
            StartCoroutine(SimulateAllRemainingCoroutine());
        }

        private System.Collections.IEnumerator SimulateAllRemainingCoroutine()
        {
            _isSimulating = true;
            int seasonId = _currentSeason.Id;
            while (true)
            {
                var result = _seasonService.SimulateNextGame(seasonId);
                if (result == null) break;
                _lastResult = result;
                RefreshAfterSim();
                yield return null;
            }
            _isSimulating = false;
        }

        // ============================================================
        //  helpers
        // ============================================================

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
    }
}
