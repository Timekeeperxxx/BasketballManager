using System;
using System.Collections.Generic;
using System.Linq;
using BasketballManager.Core.Models;
using BasketballManager.Database;
using BasketballManager.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using Position = BasketballManager.Core.Enums.Position;
using NameOrder = BasketballManager.Core.Enums.NameOrder;

namespace BasketballManager.UI.Screens
{
    /// <summary>
    /// 阵容编辑（UI Toolkit 原生）。两栏布局：左侧球队下拉 + 球员列表，右侧英雄头 + 三 Tab 表单。
    /// 所有字段值变更（失焦/回车）后自动写库并刷新总评。
    /// </summary>
    public sealed class RosterScreen : UIToolkitScreenBase
    {
        public const string Id = "Roster";

        public event Action OnBackClicked;

        // 依赖
        private TeamRepository _teamRepository;
        private PlayerRepository _playerRepository;

        // 数据
        private List<Team> _teams = new List<Team>();
        private List<Player> _players = new List<Player>();
        private Team _selectedTeam;
        private Player _selectedPlayer;
        private bool _suppressSave; // 在程序化赋值时阻断 ValueChanged → Save 回环

        // UI 引用：顶栏 & Hero
        private Label _saveStatus;
        private Label _heroJersey, _heroName, _heroSub, _heroOverall;
        private Label _rosterCount;

        // 左侧
        private VisualElement _teamPickerTrigger;
        private Label _teamPickerCurrent;
        private VisualElement _teamPickerPopup;
        private TextField _teamPickerSearch;
        private ListView _teamPickerList;
        private List<Team> _filteredTeams = new List<Team>();
        private ListView _playersList;

        // Tabs
        private Button _tabBasic, _tabAttr, _tabTend;
        private VisualElement _pageBasic, _pageAttr, _pageTend;

        // 基础信息字段
        private TextField _fFirstName, _fLastName;
        private DropdownField _fNameOrder, _fPosition, _fSecPos, _fStatus;
        private IntegerField _fJersey, _fHeight, _fWeight, _fAge;

        // 状态下拉项
        private const string STATUS_CURRENT = "现役球员";
        private const string STATUS_HISTORICAL = "历史球员";

        // 能力 / 倾向字段——按 setter 索引
        private readonly List<IntegerField> _attrFields = new List<IntegerField>();
        private readonly List<IntegerField> _tendFields = new List<IntegerField>();

        // Secondary position 的特殊"空"项
        private const string SEC_POS_EMPTY = "—";

        private void Awake() { ScreenId = Id; }

        public void Initialize(TeamRepository teamRepository, PlayerRepository playerRepository)
        {
            _teamRepository = teamRepository;
            _playerRepository = playerRepository;
        }

        // ============================================================
        //  Build
        // ============================================================

        protected override void OnBuilt()
        {
            Root.Q<Button>("btn-back").clicked += () => OnBackClicked?.Invoke();

            _saveStatus  = Root.Q<Label>("save-status");
            _rosterCount = Root.Q<Label>("roster-count");
            _heroJersey  = Root.Q<Label>("hero-jersey");
            _heroName    = Root.Q<Label>("hero-name");
            _heroSub     = Root.Q<Label>("hero-sub");
            _heroOverall = Root.Q<Label>("hero-overall");

            // 左侧：球队搜索下拉 + 球员列表
            _teamPickerTrigger = Root.Q<VisualElement>("team-picker-trigger");
            _teamPickerCurrent = Root.Q<Label>("team-picker-current");
            _teamPickerPopup   = Root.Q<VisualElement>("team-picker-popup");
            _teamPickerSearch  = Root.Q<TextField>("team-picker-search");
            _teamPickerList    = Root.Q<ListView>("team-picker-list");

            _teamPickerList.fixedItemHeight = 32;
            _teamPickerList.makeItem = () =>
            {
                var l = new Label();
                l.AddToClassList("team-picker__list-item");
                return l;
            };
            _teamPickerList.bindItem = (e, i) =>
            {
                if (i < 0 || i >= _filteredTeams.Count) return;
                ((Label)e).text = GetTeamTitle(_filteredTeams[i]);
            };
            _teamPickerList.selectionChanged += sel =>
            {
                var t = sel?.FirstOrDefault() as Team;
                if (t != null)
                {
                    SelectTeam(t);
                    CloseTeamPicker();
                }
            };

            _teamPickerTrigger.RegisterCallback<PointerDownEvent>(_ => ToggleTeamPicker());
            _teamPickerSearch.RegisterValueChangedCallback(evt => FilterTeams(evt.newValue));

            // 点击外部关闭弹层
            Root.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);

            _playersList = Root.Q<ListView>("players-list");
            _playersList.fixedItemHeight = 36;
            _playersList.makeItem = MakePlayerRow;
            _playersList.bindItem = BindPlayerRow;
            _playersList.selectionChanged += OnPlayerSelectionChanged;

            // Tabs
            _tabBasic = Root.Q<Button>("tab-basic");
            _tabAttr  = Root.Q<Button>("tab-attr");
            _tabTend  = Root.Q<Button>("tab-tend");
            _pageBasic = Root.Q<VisualElement>("page-basic");
            _pageAttr  = Root.Q<VisualElement>("page-attr");
            _pageTend  = Root.Q<VisualElement>("page-tend");
            _tabBasic.clicked += () => SwitchTab(0);
            _tabAttr.clicked  += () => SwitchTab(1);
            _tabTend.clicked  += () => SwitchTab(2);

            // 基础信息字段
            _fFirstName = Root.Q<TextField>("field-firstname");
            _fLastName  = Root.Q<TextField>("field-lastname");
            _fNameOrder = Root.Q<DropdownField>("field-nameorder");
            _fPosition  = Root.Q<DropdownField>("field-position");
            _fSecPos    = Root.Q<DropdownField>("field-secpos");
            _fJersey    = Root.Q<IntegerField>("field-jersey");
            _fHeight    = Root.Q<IntegerField>("field-height");
            _fWeight    = Root.Q<IntegerField>("field-weight");
            _fAge       = Root.Q<IntegerField>("field-age");
            _fStatus    = Root.Q<DropdownField>("field-status");

            // 下拉选项
            _fNameOrder.choices = Enum.GetNames(typeof(NameOrder)).ToList();
            _fPosition.choices  = Enum.GetNames(typeof(Position)).ToList();
            var secPosOptions = new List<string> { SEC_POS_EMPTY };
            secPosOptions.AddRange(Enum.GetNames(typeof(Position)));
            _fSecPos.choices = secPosOptions;
            _fStatus.choices = new List<string> { STATUS_HISTORICAL, STATUS_CURRENT };

            // 注册基础信息回调
            _fFirstName.RegisterValueChangedCallback(evt => Mutate(p => p.FirstName = evt.newValue?.Trim() ?? string.Empty));
            _fLastName.RegisterValueChangedCallback (evt => Mutate(p => p.LastName  = evt.newValue?.Trim() ?? string.Empty));
            _fNameOrder.RegisterValueChangedCallback(evt => Mutate(p =>
            {
                if (Enum.TryParse<NameOrder>(evt.newValue, out var parsed)) p.NameOrder = parsed;
            }));
            _fPosition.RegisterValueChangedCallback(evt => Mutate(p =>
            {
                if (Enum.TryParse<Position>(evt.newValue, out var parsed)) p.Position = parsed;
                // 主副位置冲突 → 清空副位置
                if (p.SecondaryPosition.HasValue && p.SecondaryPosition.Value == p.Position)
                {
                    p.SecondaryPosition = null;
                    _suppressSave = true;
                    _fSecPos.SetValueWithoutNotify(SEC_POS_EMPTY);
                    _suppressSave = false;
                    Notify("主副位置冲突，已自动清空第二位置。");
                }
            }));
            _fSecPos.RegisterValueChangedCallback(evt => Mutate(p =>
            {
                if (evt.newValue == SEC_POS_EMPTY)
                {
                    p.SecondaryPosition = null;
                }
                else if (Enum.TryParse<Position>(evt.newValue, out var parsed))
                {
                    if (parsed == p.Position)
                    {
                        p.SecondaryPosition = null;
                        _suppressSave = true;
                        _fSecPos.SetValueWithoutNotify(SEC_POS_EMPTY);
                        _suppressSave = false;
                        Notify("第二位置不能等于主位置，已忽略。");
                    }
                    else
                    {
                        p.SecondaryPosition = parsed;
                    }
                }
            }));
            _fJersey.RegisterValueChangedCallback(evt => Mutate(p => p.JerseyNumber = evt.newValue));
            _fHeight.RegisterValueChangedCallback(evt => Mutate(p => p.HeightCm     = evt.newValue));
            _fWeight.RegisterValueChangedCallback(evt => Mutate(p => p.WeightKg     = evt.newValue));
            _fAge.RegisterValueChangedCallback   (evt => Mutate(p => p.Age          = evt.newValue));
            _fStatus.RegisterValueChangedCallback(evt => Mutate(p => p.IsCurrent    = evt.newValue == STATUS_CURRENT));

            // 能力（21）&倾向（14）字段：注入到两列
            BuildAttributeFields();
            BuildTendencyFields();

            SwitchTab(0);
        }

        // ============================================================
        //  能力 / 倾向 字段表（动态注入）
        // ============================================================

        private void BuildAttributeFields()
        {
            var col1 = Root.Q<VisualElement>("attr-col1");
            var col2 = Root.Q<VisualElement>("attr-col2");

            AddAttr(col1, "两分",         p => p.Attributes.TwoPoint,             (p, v) => p.Attributes.TwoPoint = v);
            AddAttr(col1, "三分",         p => p.Attributes.ThreePoint,           (p, v) => p.Attributes.ThreePoint = v);
            AddAttr(col1, "上篮",         p => p.Attributes.Layup,                (p, v) => p.Attributes.Layup = v);
            AddAttr(col1, "近投",         p => p.Attributes.CloseShot,            (p, v) => p.Attributes.CloseShot = v);
            AddAttr(col1, "背身得分",     p => p.Attributes.PostScoring,          (p, v) => p.Attributes.PostScoring = v);
            AddAttr(col1, "罚球",         p => p.Attributes.FreeThrow,            (p, v) => p.Attributes.FreeThrow = v);
            AddAttr(col1, "传球",         p => p.Attributes.Passing,              (p, v) => p.Attributes.Passing = v);
            AddAttr(col1, "控球",         p => p.Attributes.BallHandle,           (p, v) => p.Attributes.BallHandle = v);
            AddAttr(col1, "突破",         p => p.Attributes.Drive,                (p, v) => p.Attributes.Drive = v);
            AddAttr(col1, "造犯规",       p => p.Attributes.DrawFoul,             (p, v) => p.Attributes.DrawFoul = v);
            AddAttr(col1, "进攻稳定性",   p => p.Attributes.OffensiveConsistency, (p, v) => p.Attributes.OffensiveConsistency = v);

            AddAttr(col2, "外线防守",     p => p.Attributes.PerimeterDefense,     (p, v) => p.Attributes.PerimeterDefense = v);
            AddAttr(col2, "内线防守",     p => p.Attributes.InteriorDefense,      (p, v) => p.Attributes.InteriorDefense = v);
            AddAttr(col2, "抢断",         p => p.Attributes.Steal,                (p, v) => p.Attributes.Steal = v);
            AddAttr(col2, "盖帽",         p => p.Attributes.Block,                (p, v) => p.Attributes.Block = v);
            AddAttr(col2, "进攻篮板",     p => p.Attributes.OffensiveRebound,     (p, v) => p.Attributes.OffensiveRebound = v);
            AddAttr(col2, "防守篮板",     p => p.Attributes.DefensiveRebound,     (p, v) => p.Attributes.DefensiveRebound = v);
            AddAttr(col2, "防守稳定性",   p => p.Attributes.DefensiveConsistency, (p, v) => p.Attributes.DefensiveConsistency = v);
            AddAttr(col2, "速度",         p => p.Attributes.Speed,                (p, v) => p.Attributes.Speed = v);
            AddAttr(col2, "力量",         p => p.Attributes.Strength,             (p, v) => p.Attributes.Strength = v);
            AddAttr(col2, "体能",         p => p.Attributes.Stamina,              (p, v) => p.Attributes.Stamina = v);
        }

        private void BuildTendencyFields()
        {
            var col1 = Root.Q<VisualElement>("tend-col1");
            var col2 = Root.Q<VisualElement>("tend-col2");

            AddTend(col1, "出手倾向",     p => p.Tendencies.ShotTendency,             (p, v) => p.Tendencies.ShotTendency = v);
            AddTend(col1, "三分倾向",     p => p.Tendencies.ThreeTendency,            (p, v) => p.Tendencies.ThreeTendency = v);
            AddTend(col1, "两分倾向",     p => p.Tendencies.TwoPointTendency,         (p, v) => p.Tendencies.TwoPointTendency = v);
            AddTend(col1, "突破倾向",     p => p.Tendencies.DriveTendency,            (p, v) => p.Tendencies.DriveTendency = v);
            AddTend(col1, "背身倾向",     p => p.Tendencies.PostTendency,             (p, v) => p.Tendencies.PostTendency = v);
            AddTend(col1, "近投倾向",     p => p.Tendencies.CloseShotTendency,        (p, v) => p.Tendencies.CloseShotTendency = v);
            AddTend(col1, "传球倾向",     p => p.Tendencies.PassTendency,             (p, v) => p.Tendencies.PassTendency = v);

            AddTend(col2, "造犯规倾向",   p => p.Tendencies.DrawFoulTendency,         (p, v) => p.Tendencies.DrawFoulTendency = v);
            AddTend(col2, "抢断倾向",     p => p.Tendencies.StealTendency,            (p, v) => p.Tendencies.StealTendency = v);
            AddTend(col2, "盖帽倾向",     p => p.Tendencies.BlockTendency,            (p, v) => p.Tendencies.BlockTendency = v);
            AddTend(col2, "犯规倾向",     p => p.Tendencies.FoulTendency,             (p, v) => p.Tendencies.FoulTendency = v);
            AddTend(col2, "协防倾向",     p => p.Tendencies.HelpDefenseTendency,      (p, v) => p.Tendencies.HelpDefenseTendency = v);
            AddTend(col2, "进攻篮板倾向", p => p.Tendencies.OffensiveReboundTendency, (p, v) => p.Tendencies.OffensiveReboundTendency = v);
            AddTend(col2, "防守篮板倾向", p => p.Tendencies.DefensiveReboundTendency, (p, v) => p.Tendencies.DefensiveReboundTendency = v);
        }

        private void AddAttr(VisualElement col, string label, Func<Player, int> getter, Action<Player, int> setter)
        {
            var field = new IntegerField(label) { isDelayed = true };
            field.AddToClassList("roster-stat");
            field.RegisterValueChangedCallback(evt => Mutate(p => setter(p, evt.newValue)));
            field.userData = getter;
            col.Add(field);
            _attrFields.Add(field);
        }

        private void AddTend(VisualElement col, string label, Func<Player, int> getter, Action<Player, int> setter)
        {
            var field = new IntegerField(label) { isDelayed = true };
            field.AddToClassList("roster-stat");
            field.RegisterValueChangedCallback(evt => Mutate(p => setter(p, evt.newValue)));
            field.userData = getter;
            col.Add(field);
            _tendFields.Add(field);
        }

        // ============================================================
        //  Lifecycle
        // ============================================================

        protected override void OnEnter()
        {
            if (_teamRepository == null) return;
            _teams = _teamRepository.GetAllTeams().ToList();
            CloseTeamPicker();

            if (_teams.Count > 0)
            {
                SelectTeam(_teams[0]);
            }
            else
            {
                _selectedTeam = null;
                _players.Clear();
                _playersList.itemsSource = _players;
                _playersList.Rebuild();
                _teamPickerCurrent.text = "—";
                ClearEditor();
            }
        }

        // ============================================================
        //  Team / Player 选择
        // ============================================================

        private void SelectTeam(Team team)
        {
            _selectedTeam = team;
            _teamPickerCurrent.text = GetTeamTitle(team);
            _players = _playerRepository.GetPlayersByTeamId(team.Id).ToList();
            _playersList.itemsSource = _players;
            _playersList.Rebuild();
            _rosterCount.text = $"({_players.Count})";

            if (_players.Count > 0)
            {
                _playersList.SetSelection(0);
                SelectPlayer(_players[0]);
            }
            else
            {
                ClearEditor();
            }
        }

        // ============================================================
        //  球队搜索弹层
        // ============================================================

        private void ToggleTeamPicker()
        {
            if (_teamPickerPopup.ClassListContains("team-picker__popup--hidden"))
                OpenTeamPicker();
            else
                CloseTeamPicker();
        }

        private void OpenTeamPicker()
        {
            _filteredTeams = _teams.ToList();
            _teamPickerList.itemsSource = _filteredTeams;
            _teamPickerList.Rebuild();
            _teamPickerSearch.SetValueWithoutNotify(string.Empty);
            _teamPickerPopup.RemoveFromClassList("team-picker__popup--hidden");
            _teamPickerPopup.BringToFront();
            _teamPickerSearch.Focus();
        }

        private void CloseTeamPicker()
        {
            if (_teamPickerPopup == null) return;
            if (!_teamPickerPopup.ClassListContains("team-picker__popup--hidden"))
                _teamPickerPopup.AddToClassList("team-picker__popup--hidden");
        }

        private void FilterTeams(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                _filteredTeams = _teams.ToList();
            }
            else
            {
                var q = query.Trim().ToLowerInvariant();
                _filteredTeams = _teams.Where(t =>
                    GetTeamTitle(t).ToLowerInvariant().Contains(q) ||
                    (t.Name != null && t.Name.ToLowerInvariant().Contains(q))
                ).ToList();
            }
            _teamPickerList.itemsSource = _filteredTeams;
            _teamPickerList.Rebuild();
        }

        private void OnRootPointerDown(PointerDownEvent evt)
        {
            if (_teamPickerPopup == null) return;
            if (_teamPickerPopup.ClassListContains("team-picker__popup--hidden")) return;

            var target = evt.target as VisualElement;
            if (target == null) return;

            bool inTrigger = target == _teamPickerTrigger || IsDescendantOf(target, _teamPickerTrigger);
            bool inPopup   = target == _teamPickerPopup   || IsDescendantOf(target, _teamPickerPopup);
            if (!inTrigger && !inPopup) CloseTeamPicker();
        }

        private static bool IsDescendantOf(VisualElement element, VisualElement ancestor)
        {
            for (var p = element.parent; p != null; p = p.parent)
                if (p == ancestor) return true;
            return false;
        }

        private void OnPlayerSelectionChanged(IEnumerable<object> selected)
        {
            var p = selected?.FirstOrDefault() as Player;
            if (p != null) SelectPlayer(p);
        }

        private void SelectPlayer(Player player)
        {
            _selectedPlayer = player;
            LoadFieldsFromPlayer();
            RefreshHero();
        }

        // ============================================================
        //  Hero & 字段加载
        // ============================================================

        private void RefreshHero()
        {
            if (_selectedPlayer == null)
            {
                _heroJersey.text  = "--";
                _heroName.text    = "—";
                _heroSub.text     = string.Empty;
                _heroOverall.text = "--";
                return;
            }
            _heroJersey.text  = "#" + _selectedPlayer.JerseyNumber;
            _heroName.text    = _selectedPlayer.GetDisplayName();
            _heroSub.text     = FormatSub(_selectedPlayer);
            _heroOverall.text = _selectedPlayer.Overall.ToString();
        }

        private static string FormatSub(Player p)
        {
            var pos = p.SecondaryPosition.HasValue ? $"{p.Position}/{p.SecondaryPosition.Value}" : p.Position.ToString();
            return $"{pos}  ·  {p.HeightCm}cm  ·  {p.WeightKg}kg  ·  {p.Age}岁";
        }

        private void LoadFieldsFromPlayer()
        {
            if (_selectedPlayer == null)
            {
                ClearEditor();
                return;
            }

            _suppressSave = true;
            try
            {
                var p = _selectedPlayer;
                _fFirstName.SetValueWithoutNotify(p.FirstName);
                _fLastName.SetValueWithoutNotify(p.LastName);
                _fNameOrder.SetValueWithoutNotify(p.NameOrder.ToString());
                _fPosition.SetValueWithoutNotify(p.Position.ToString());
                _fSecPos.SetValueWithoutNotify(p.SecondaryPosition.HasValue ? p.SecondaryPosition.Value.ToString() : SEC_POS_EMPTY);
                _fJersey.SetValueWithoutNotify(p.JerseyNumber);
                _fHeight.SetValueWithoutNotify(p.HeightCm);
                _fWeight.SetValueWithoutNotify(p.WeightKg);
                _fAge.SetValueWithoutNotify(p.Age);
                _fStatus.SetValueWithoutNotify(p.IsCurrent ? STATUS_CURRENT : STATUS_HISTORICAL);

                foreach (var f in _attrFields)
                {
                    var getter = (Func<Player, int>)f.userData;
                    f.SetValueWithoutNotify(getter(p));
                }
                foreach (var f in _tendFields)
                {
                    var getter = (Func<Player, int>)f.userData;
                    f.SetValueWithoutNotify(getter(p));
                }
            }
            finally
            {
                _suppressSave = false;
            }
        }

        private void ClearEditor()
        {
            _selectedPlayer = null;
            _suppressSave = true;
            try
            {
                _fFirstName.SetValueWithoutNotify(string.Empty);
                _fLastName.SetValueWithoutNotify(string.Empty);
                _fNameOrder.SetValueWithoutNotify(string.Empty);
                _fPosition.SetValueWithoutNotify(string.Empty);
                _fSecPos.SetValueWithoutNotify(SEC_POS_EMPTY);
                _fJersey.SetValueWithoutNotify(0);
                _fHeight.SetValueWithoutNotify(0);
                _fWeight.SetValueWithoutNotify(0);
                _fAge.SetValueWithoutNotify(0);
                _fStatus.SetValueWithoutNotify(STATUS_HISTORICAL);
                foreach (var f in _attrFields) f.SetValueWithoutNotify(0);
                foreach (var f in _tendFields) f.SetValueWithoutNotify(0);
            }
            finally { _suppressSave = false; }
            RefreshHero();
        }

        // ============================================================
        //  自动保存
        // ============================================================

        private void Mutate(Action<Player> mutate)
        {
            if (_suppressSave || _selectedPlayer == null) return;
            mutate(_selectedPlayer);
            try
            {
                _playerRepository.UpdatePlayer(_selectedPlayer);
                // UpdatePlayer 内部已重算 Overall
                RefreshHero();
                RefreshSelectedRosterRow();
                Notify("已自动保存。");
            }
            catch (Exception ex)
            {
                Notify($"保存失败：{ex.Message}");
                Debug.LogException(ex);
            }
        }

        private void RefreshSelectedRosterRow()
        {
            int idx = _players.IndexOf(_selectedPlayer);
            if (idx >= 0) _playersList.RefreshItem(idx);
        }

        private void Notify(string message)
        {
            if (_saveStatus != null) _saveStatus.text = message;
        }

        // ============================================================
        //  Tab 切换
        // ============================================================

        private void SwitchTab(int index)
        {
            SetActive(_tabBasic, index == 0);
            SetActive(_tabAttr,  index == 1);
            SetActive(_tabTend,  index == 2);

            _pageBasic.EnableInClassList("tab-page--hidden", index != 0);
            _pageAttr.EnableInClassList ("tab-page--hidden", index != 1);
            _pageTend.EnableInClassList ("tab-page--hidden", index != 2);
        }

        private static void SetActive(Button btn, bool active)
        {
            btn.EnableInClassList("tab--active", active);
        }

        // ============================================================
        //  球员列表行
        // ============================================================

        private static VisualElement MakePlayerRow()
        {
            var row = new VisualElement();
            row.AddToClassList("roster-player-row");
            var jersey = new Label { name = "row-jersey" };
            jersey.AddToClassList("roster-player-row__jersey");
            var name = new Label { name = "row-name" };
            name.AddToClassList("roster-player-row__name");
            var pos = new Label { name = "row-pos" };
            pos.AddToClassList("roster-player-row__pos");
            row.Add(jersey);
            row.Add(name);
            row.Add(pos);
            return row;
        }

        private void BindPlayerRow(VisualElement element, int index)
        {
            if (index < 0 || index >= _players.Count) return;
            var p = _players[index];
            element.Q<Label>("row-jersey").text = "#" + p.JerseyNumber;
            element.Q<Label>("row-name").text   = p.GetDisplayName();
            element.Q<Label>("row-pos").text    = p.Position.ToString();
        }

        // ============================================================
        //  Helpers
        // ============================================================

        private static string GetTeamTitle(Team team)
        {
            var shortName = team.Name.Contains(" ") ? team.Name.Substring(team.Name.LastIndexOf(' ') + 1) : team.Name;
            return team.Era > 0 ? $"{team.Era} {shortName}" : shortName;
        }
    }
}
