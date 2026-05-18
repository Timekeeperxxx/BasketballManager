using System;
using System.Collections.Generic;
using BasketballManager.Core.Enums;
using BasketballManager.Core.Models;
using BasketballManager.Database;
using UnityEngine;
using UnityEngine.UI;
using BasketballManager.UI.Core;

namespace BasketballManager.UI.Screens
{
    public sealed class RosterManagementScreen : UIScreenBase
    {
        private readonly List<IFieldBinding> _bindings = new List<IFieldBinding>();

        private DatabaseManager _databaseManager;
        private TeamRepository _teamRepository;
        private PlayerRepository _playerRepository;

        private Team _selectedTeam;
        private Player _selectedPlayer;
        private RectTransform _teamListContent;
        private RectTransform _rosterListContent;
        private Text _teamHeader;
        private Text _rosterHeader;
        private Text _editorHeader;
        private Text _overallText;
        private bool _built;
        private int _currentTabIndex;
        private RectTransform _attributesPage;
        private RectTransform _tendencyPage;
        private Image _btnAttributesImg;
        private Image _btnTendencyImg;

        private RectTransform _rootPanel;

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
            if (_built || _databaseManager == null || _teamRepository == null || _playerRepository == null)
            {
                return;
            }

            _rootPanel = CreatePanel("RosterRoot", parent, new Color(0.08f, 0.09f, 0.11f));
            Stretch(_rootPanel);

            var rootLayout = _rootPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            rootLayout.padding = new RectOffset(16, 16, 16, 16);
            rootLayout.spacing = 16f;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = true;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;

            var mainContentRow = CreatePanel("MainContent", _rootPanel, Color.clear);
            var mainLayout = mainContentRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            mainLayout.spacing = 16f;
            mainLayout.childForceExpandHeight = true;
            mainLayout.childForceExpandWidth = false;
            mainLayout.childControlWidth = true;
            mainLayout.childControlHeight = true;

            var mainLayoutElement = mainContentRow.gameObject.AddComponent<LayoutElement>();
            mainLayoutElement.flexibleHeight = 1f;

            var teamsPanel = CreateColumnPanel(mainContentRow, 180f);
            _teamHeader = CreateHeader(teamsPanel, "\u7403\u961f");
            _teamListContent = CreateScrollList(teamsPanel);

            var rosterPanel = CreateColumnPanel(mainContentRow, 220f);
            _rosterHeader = CreateHeader(rosterPanel, "\u9635\u5bb9");
            _rosterListContent = CreateScrollList(rosterPanel);

            var basicInfoPanel = CreateColumnPanel(mainContentRow, 300f);
            CreateHeader(basicInfoPanel, "\u57fa\u672c\u4fe1\u606f");
            var basicInfoScroll = CreateScrollView(basicInfoPanel, out var basicInfoContent);
            var basicInfoScrollLayout = basicInfoScroll.gameObject.AddComponent<LayoutElement>();
            basicInfoScrollLayout.flexibleHeight = 1f;
            SetupContentLayout(basicInfoContent);
            BuildAvatarArea(basicInfoContent);
            BuildBasicInfo(basicInfoContent);

            var detailsPanel = CreateFlexiblePanel(mainContentRow);
            _editorHeader = CreateHeader(detailsPanel, "\u5c5e\u6027"); // Header will be updated dynamically
            var detailsScroll = CreateScrollView(detailsPanel, out var detailsContent);
            var detailsScrollLayout = detailsScroll.gameObject.AddComponent<LayoutElement>();
            detailsScrollLayout.flexibleHeight = 1f;

            // Container to hold all pages (it must take full height/width inside detailsContent)
            var pagesLayout = detailsContent.gameObject.AddComponent<VerticalLayoutGroup>();
            pagesLayout.childControlWidth = true;
            pagesLayout.childControlHeight = true;
            pagesLayout.childForceExpandWidth = true;
            pagesLayout.childForceExpandHeight = true;
            detailsContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Create pages
            _attributesPage = CreatePanel("AttributesPage", detailsContent, Color.clear);
            SetupContentLayout(_attributesPage);
            BuildAttributes(_attributesPage);

            _tendencyPage = CreatePanel("TendencyPage", detailsContent, Color.clear);
            SetupContentLayout(_tendencyPage);
            BuildTendencies(_tendencyPage);

            var rightTabsPanel = CreateColumnPanel(mainContentRow, 80f);
            
            var btnAttr = CreateButton(rightTabsPanel, "\u5c5e\u6027", () => SwitchTab(0)); // 属性
            LayoutElementWithHeight(btnAttr.gameObject, 60f);
            _btnAttributesImg = btnAttr.GetComponent<Image>();

            var btnTen = CreateButton(rightTabsPanel, "\u503e\u5411", () => SwitchTab(1)); // 倾向
            LayoutElementWithHeight(btnTen.gameObject, 60f);
            _btnTendencyImg = btnTen.GetComponent<Image>();
            
            SwitchTab(0);

            var spacer = new GameObject("Spacer", typeof(RectTransform)).AddComponent<LayoutElement>();
            spacer.transform.SetParent(rightTabsPanel, false);
            spacer.flexibleHeight = 1f;

            var saveButton = CreateButton(rightTabsPanel, "\u4fdd\u5b58", SaveCurrentPlayer); // 保存
            LayoutElementWithHeight(saveButton.gameObject, 60f);

            RefreshTeams();
            _built = true;
        }

        private void SetupContentLayout(RectTransform content)
        {
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void BuildAvatarArea(RectTransform parent)
        {
            var avatarContainer = CreatePanel("AvatarContainer", parent, Color.clear);
            var layout = avatarContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            LayoutElementWithHeight(avatarContainer.gameObject, 160f);

            var avatarBg = new GameObject("AvatarBg", typeof(RectTransform), typeof(Image), typeof(Mask)).GetComponent<RectTransform>();
            avatarBg.SetParent(avatarContainer, false);
            avatarBg.sizeDelta = new Vector2(120f, 120f);
            
            var bgImg = avatarBg.GetComponent<Image>();
            // Use Unity default UI circle if possible, but here we can't easily reference standard assets without Resources
            // We just set a nice background color
            bgImg.color = new Color(0.18f, 0.20f, 0.26f);
            
            var mask = avatarBg.GetComponent<Mask>();
            mask.showMaskGraphic = true;

            var avatarIcon = new GameObject("AvatarIcon", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            avatarIcon.SetParent(avatarBg, false);
            avatarIcon.sizeDelta = new Vector2(120f, 120f);
            var iconImg = avatarIcon.GetComponent<Image>();
            iconImg.color = new Color(0.22f, 0.24f, 0.31f); // Inner color
        }

        private void BuildBasicInfo(RectTransform parent)
        {
            _overallText = CreateBodyText(parent, "\u603b\u8bc4\uff1a-\uff08\u7cfb\u7edf\u6839\u636e\u80fd\u529b\u5c5e\u6027\u81ea\u52a8\u8ba1\u7b97\uff09");
            LayoutElementWithHeight(_overallText.gameObject, 36f);
            _overallText.alignment = TextAnchor.MiddleCenter;
            _overallText.fontSize = 16;

            AddField(CreateTextBinding(parent, "\u540d", p => p.FirstName, (p, value) => p.FirstName = value));
            AddField(CreateTextBinding(parent, "\u59d3", p => p.LastName, (p, value) => p.LastName = value));
AddField(CreateEnumDropdownBinding(parent, "\u59d3\u540d\u987a\u5e8f", Enum.GetNames(typeof(NameOrder)), p => p.NameOrder.ToString(), (p, value) => p.NameOrder = Enum.TryParse(value, out NameOrder parsed) ? parsed : NameOrder.WESTERN));
AddField(CreateEnumDropdownBinding(parent, "\u4f4d\u7f6e", Enum.GetNames(typeof(Position)), p => p.Position.ToString(), (p, value) => p.Position = Enum.TryParse(value, out Position parsed) ? parsed : Position.PG));
            
            var secPosOptions = new List<string> { "空" };
            secPosOptions.AddRange(Enum.GetNames(typeof(Position)));
            AddField(CreateEnumDropdownBinding(parent, "\u7b2c\u4e8c\u4f4d\u7f6e", secPosOptions.ToArray(), 
                p => p.SecondaryPosition.HasValue ? p.SecondaryPosition.Value.ToString() : "空", 
                (p, value) => p.SecondaryPosition = value == "空" ? null : Enum.TryParse(value, out Position parsed) ? parsed : (Position?)null));

            AddField(CreateIntBinding(parent, "\u8eab\u9ad8(cm)", p => p.HeightCm, (p, value) => p.HeightCm = value));
            AddField(CreateIntBinding(parent, "\u4f53\u91cd(kg)", p => p.WeightKg, (p, value) => p.WeightKg = value));
            AddField(CreateIntBinding(parent, "\u5e74\u9f84", p => p.Age, (p, value) => p.Age = value));
            AddField(CreateIntBinding(parent, "\u7403\u8863\u53f7\u7801", p => p.JerseyNumber, (p, value) => p.JerseyNumber = value));
        }

        private void BuildAttributes(RectTransform parent)
        {
            var row = CreatePanel("Row", parent, Color.clear);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 16f;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlHeight = true;
            rowLayout.childControlWidth = true;

            var col1 = CreatePanel("Col1", row, Color.clear);
            var col1Layout = col1.gameObject.AddComponent<VerticalLayoutGroup>();
            col1Layout.spacing = 10f;
            col1Layout.childForceExpandWidth = true;
            col1Layout.childForceExpandHeight = false;
            col1Layout.childControlHeight = true;
            col1Layout.childControlWidth = true;

            var col2 = CreatePanel("Col2", row, Color.clear);
            var col2Layout = col2.gameObject.AddComponent<VerticalLayoutGroup>();
            col2Layout.spacing = 10f;
            col2Layout.childForceExpandWidth = true;
            col2Layout.childForceExpandHeight = false;
            col2Layout.childControlHeight = true;
            col2Layout.childControlWidth = true;

            AddField(CreateIntBinding(col1, "\u4e24\u5206", p => p.Attributes.TwoPoint, (p, value) => p.Attributes.TwoPoint = value));
            AddField(CreateIntBinding(col1, "\u4e09\u5206", p => p.Attributes.ThreePoint, (p, value) => p.Attributes.ThreePoint = value));
            AddField(CreateIntBinding(col1, "\u4e0a\u7bee", p => p.Attributes.Layup, (p, value) => p.Attributes.Layup = value));
            AddField(CreateIntBinding(col1, "\u8fd1\u6295", p => p.Attributes.CloseShot, (p, value) => p.Attributes.CloseShot = value));
            AddField(CreateIntBinding(col1, "\u80cc\u8eab\u5f97\u5206", p => p.Attributes.PostScoring, (p, value) => p.Attributes.PostScoring = value));
            AddField(CreateIntBinding(col1, "\u7f5a\u7403", p => p.Attributes.FreeThrow, (p, value) => p.Attributes.FreeThrow = value));
            AddField(CreateIntBinding(col1, "\u4f20\u7403", p => p.Attributes.Passing, (p, value) => p.Attributes.Passing = value));
            AddField(CreateIntBinding(col1, "\u63a7\u7403", p => p.Attributes.BallHandle, (p, value) => p.Attributes.BallHandle = value));
            AddField(CreateIntBinding(col1, "\u7a81\u7834", p => p.Attributes.Drive, (p, value) => p.Attributes.Drive = value));
            AddField(CreateIntBinding(col1, "\u9020\u72af\u89c4", p => p.Attributes.DrawFoul, (p, value) => p.Attributes.DrawFoul = value));
            AddField(CreateIntBinding(col1, "\u8fdb\u653b\u7a33\u5b9a\u6027", p => p.Attributes.OffensiveConsistency, (p, value) => p.Attributes.OffensiveConsistency = value));

            AddField(CreateIntBinding(col2, "\u5916\u7ebf\u9632\u5b88", p => p.Attributes.PerimeterDefense, (p, value) => p.Attributes.PerimeterDefense = value));
            AddField(CreateIntBinding(col2, "\u5185\u7ebf\u9632\u5b88", p => p.Attributes.InteriorDefense, (p, value) => p.Attributes.InteriorDefense = value));
            AddField(CreateIntBinding(col2, "\u62a2\u65ad", p => p.Attributes.Steal, (p, value) => p.Attributes.Steal = value));
            AddField(CreateIntBinding(col2, "\u76d6\u5e3d", p => p.Attributes.Block, (p, value) => p.Attributes.Block = value));
            AddField(CreateIntBinding(col2, "\u8fdb\u653b\u7bee\u677f", p => p.Attributes.OffensiveRebound, (p, value) => p.Attributes.OffensiveRebound = value));
            AddField(CreateIntBinding(col2, "\u9632\u5b88\u7bee\u677f", p => p.Attributes.DefensiveRebound, (p, value) => p.Attributes.DefensiveRebound = value));
            AddField(CreateIntBinding(col2, "\u9632\u5b88\u7a33\u5b9a\u6027", p => p.Attributes.DefensiveConsistency, (p, value) => p.Attributes.DefensiveConsistency = value));
            AddField(CreateIntBinding(col2, "\u901f\u5ea6", p => p.Attributes.Speed, (p, value) => p.Attributes.Speed = value));
            AddField(CreateIntBinding(col2, "\u529b\u91cf", p => p.Attributes.Strength, (p, value) => p.Attributes.Strength = value));
            AddField(CreateIntBinding(col2, "\u4f53\u80fd", p => p.Attributes.Stamina, (p, value) => p.Attributes.Stamina = value));
        }

        
        private void BuildTendencies(RectTransform parent)
        {
            var row = CreatePanel("Row", parent, Color.clear);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 16f;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlHeight = true;
            rowLayout.childControlWidth = true;

            var col1 = CreatePanel("Col1", row, Color.clear);
            var col1Layout = col1.gameObject.AddComponent<VerticalLayoutGroup>();
            col1Layout.spacing = 10f;
            col1Layout.childForceExpandWidth = true;
            col1Layout.childForceExpandHeight = false;
            col1Layout.childControlHeight = true;
            col1Layout.childControlWidth = true;

            var col2 = CreatePanel("Col2", row, Color.clear);
            var col2Layout = col2.gameObject.AddComponent<VerticalLayoutGroup>();
            col2Layout.spacing = 10f;
            col2Layout.childForceExpandWidth = true;
            col2Layout.childForceExpandHeight = false;
            col2Layout.childControlHeight = true;
            col2Layout.childControlWidth = true;

            AddField(CreateIntBinding(col1, "\u51fa\u624b\u503e\u5411", p => p.Tendencies.ShotTendency, (p, value) => p.Tendencies.ShotTendency = value));
            AddField(CreateIntBinding(col1, "\u4e09\u5206\u503e\u5411", p => p.Tendencies.ThreeTendency, (p, value) => p.Tendencies.ThreeTendency = value));
            AddField(CreateIntBinding(col1, "\u4e24\u5206\u503e\u5411", p => p.Tendencies.TwoPointTendency, (p, value) => p.Tendencies.TwoPointTendency = value));
            AddField(CreateIntBinding(col1, "\u7a81\u7834\u503e\u5411", p => p.Tendencies.DriveTendency, (p, value) => p.Tendencies.DriveTendency = value));
            AddField(CreateIntBinding(col1, "\u80cc\u8eab\u503e\u5411", p => p.Tendencies.PostTendency, (p, value) => p.Tendencies.PostTendency = value));
            AddField(CreateIntBinding(col1, "\u8fd1\u6295\u503e\u5411", p => p.Tendencies.CloseShotTendency, (p, value) => p.Tendencies.CloseShotTendency = value));
            AddField(CreateIntBinding(col1, "\u4f20\u7403\u503e\u5411", p => p.Tendencies.PassTendency, (p, value) => p.Tendencies.PassTendency = value));

            AddField(CreateIntBinding(col2, "\u9020\u72af\u89c4\u503e\u5411", p => p.Tendencies.DrawFoulTendency, (p, value) => p.Tendencies.DrawFoulTendency = value));
            AddField(CreateIntBinding(col2, "\u62a2\u65ad\u503e\u5411", p => p.Tendencies.StealTendency, (p, value) => p.Tendencies.StealTendency = value));
            AddField(CreateIntBinding(col2, "\u76d6\u5e3d\u503e\u5411", p => p.Tendencies.BlockTendency, (p, value) => p.Tendencies.BlockTendency = value));
            AddField(CreateIntBinding(col2, "\u72af\u89c4\u503e\u5411", p => p.Tendencies.FoulTendency, (p, value) => p.Tendencies.FoulTendency = value));
            AddField(CreateIntBinding(col2, "\u534f\u9632\u503e\u5411", p => p.Tendencies.HelpDefenseTendency, (p, value) => p.Tendencies.HelpDefenseTendency = value));
            AddField(CreateIntBinding(col2, "\u8fdb\u653b\u7bee\u677f\u503e\u5411", p => p.Tendencies.OffensiveReboundTendency, (p, value) => p.Tendencies.OffensiveReboundTendency = value));
            AddField(CreateIntBinding(col2, "\u9632\u5b88\u7bee\u677f\u503e\u5411", p => p.Tendencies.DefensiveReboundTendency, (p, value) => p.Tendencies.DefensiveReboundTendency = value));
        }

        
        private void SwitchTab(int index)
        {
            var activeColor = new Color(0.23f, 0.49f, 0.81f);
            var inactiveColor = new Color(0.18f, 0.20f, 0.26f);

            _attributesPage.gameObject.SetActive(index == 0);
            _tendencyPage.gameObject.SetActive(index == 1);

            _btnAttributesImg.color = index == 0 ? activeColor : inactiveColor;
            _btnTendencyImg.color = index == 1 ? activeColor : inactiveColor;
            
            _currentTabIndex = index;
            UpdateEditorHeader();
        }

        private void UpdateEditorHeader()
        {
            if (_editorHeader == null) return;
            var prefix = _currentTabIndex == 0 ? "\u80fd\u529b\u5c5e\u6027" : "\u7403\u5458\u503e\u5411"; // 能力属性 / 球员倾向
            if (_selectedPlayer != null)
            {
                _editorHeader.text = $"{prefix} - {_selectedPlayer.GetDisplayName()}";
            }
            else
            {
                _editorHeader.text = prefix;
            }
        }

        private void RefreshTeams()
        {
            ClearChildren(_teamListContent);
            try
            {
                var teams = _teamRepository.GetAllTeams();
                foreach (var team in teams)
                {
                    CreateListButton(_teamListContent, GetTeamTitle(team), () => SelectTeam(team));
                }

                _teamHeader.text = $"\u7403\u961f ({teams.Count})";

                if (teams.Count > 0)
                {
                    SelectTeam(teams[0]);
                }
            }
            catch (Exception exception)
            {
                _teamHeader.text = "\u7403\u961f";
                _rosterHeader.text = "\u9635\u5bb9";
                _editorHeader.text = "\u7403\u5458\u7f16\u8f91";
                Debug.LogError(exception);
            }
        }

        private void SelectTeam(Team team, int? defaultPlayerId = null)
        {
            _selectedTeam = team;
            var teamTitle = GetTeamTitle(team);
            _teamHeader.text = $"\u7403\u961f ({teamTitle})";
            _rosterHeader.text = $"\u9635\u5bb9 - {teamTitle}";

            ClearChildren(_rosterListContent);
            try
            {
                var players = _playerRepository.GetPlayersByTeamId(team.Id);
                foreach (var player in players)
                {
                    var label = $"#{player.JerseyNumber} {player.GetDisplayName()}";
                    CreateListButton(_rosterListContent, label, () => SelectPlayer(player.Id));
                }

                if (players.Count > 0)
                {
                    bool found = false;
                    if (defaultPlayerId.HasValue)
                    {
                        foreach (var p in players)
                        {
                            if (p.Id == defaultPlayerId.Value)
                            {
                                SelectPlayer(defaultPlayerId.Value);
                                found = true;
                                break;
                            }
                        }
                    }
                    if (!found)
                    {
                        SelectPlayer(players[0].Id);
                    }
                }
                else
                {
                    _selectedPlayer = null;
                    UpdateEditorHeader();
                    ClearBindings();
                    RefreshOverallText();
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
            }
        }

        private void SelectPlayer(int playerId)
        {
            try
            {
                _selectedPlayer = _playerRepository.GetPlayerById(playerId);
                if (_selectedPlayer == null)
                {
                    return;
                }

                UpdateEditorHeader();
                foreach (var binding in _bindings)
                {
                    binding.Load(_selectedPlayer);
                }

                RefreshOverallText();
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
            }
        }

        private void SaveCurrentPlayer()
        {
            if (_selectedPlayer == null)
            {
                return;
            }

            foreach (var binding in _bindings)
            {
                if (!binding.Save(_selectedPlayer, out var error))
                {
                    if (_editorHeader != null) _editorHeader.text = $"\u4fdd\u5b58\u5931\u8d25: {error}"; // "保存失败: {error}"
                    return;
                }
            }

            string warning = null;
            if (_selectedPlayer.SecondaryPosition.HasValue && _selectedPlayer.Position == _selectedPlayer.SecondaryPosition.Value)
            {
                _selectedPlayer.SecondaryPosition = null;
                warning = "\u4e3b\u526f\u4f4d\u7f6e\u51b2\u7a81\uff0c\u5df2\u81ea\u52a8\u6e05\u7a7a\u7b2c\u4e8c\u4f4d\u7f6e\u3002"; // "主副位置冲突，已自动清空第二位置。"
            }

            try
            {
                _playerRepository.UpdatePlayer(_selectedPlayer);
                _selectedPlayer = _playerRepository.GetPlayerById(_selectedPlayer.Id);
                RefreshOverallText();
                if (_editorHeader != null) 
                {
                    _editorHeader.text = warning ?? "\u4fdd\u5b58\u6210\u529f!"; // "保存成功!"
                }
            }
            catch (Exception exception)
            {
                if (_editorHeader != null) _editorHeader.text = $"\u6570\u636e\u5e93\u9519\u8bef: {exception.Message}"; // "数据库错误: ..."
                Debug.LogError(exception);
                return;
            }

            if (_selectedTeam != null)
            {
                var savedPlayerId = _selectedPlayer.Id;
                SelectTeam(_selectedTeam, savedPlayerId);
            }
        }

        private void CreateSection(RectTransform parent, string title, Action<RectTransform> addFields)
        {
            var section = CreatePanel(title, parent, new Color(0.12f, 0.14f, 0.18f));
            var layout = section.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            section.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateSectionTitle(section, title);
            addFields(section);
        }

        private void AddField(IFieldBinding binding)
        {
            _bindings.Add(binding);
        }

        private static string GetTeamTitle(Team team)
        {
            var shortName = team.Name.Contains(" ") ? team.Name.Substring(team.Name.LastIndexOf(' ') + 1) : team.Name;
            if (team.Era > 0)
            {
                return $"{team.Era} {shortName}";
            }

            return shortName;
        }

        private IFieldBinding CreateTextBinding(RectTransform parent, string label, Func<Player, string> getter, Action<Player, string> setter)
        {
            var row = CreateFieldRow(parent, label);
            var input = CreateInputField(row);
            return new TextFieldBinding(input, getter, setter);
        }

        private IFieldBinding CreateIntBinding(RectTransform parent, string label, Func<Player, int> getter, Action<Player, int> setter)
        {
            var row = CreateFieldRow(parent, label);
            var input = CreateInputField(row);
            input.contentType = InputField.ContentType.IntegerNumber;
            return new IntFieldBinding(label, input, getter, setter);
        }

        private IFieldBinding CreateEnumDropdownBinding(RectTransform parent, string label, IReadOnlyList<string> options, Func<Player, string> getter, Action<Player, string> setter)
        {
            var row = CreateFieldRow(parent, label);
            var dropdown = CreateDropdown(row, options);
            return new DropdownFieldBinding(dropdown, options, getter, setter);
        }

        private RectTransform CreateFieldRow(RectTransform parent, string label)
        {
            var rowObject = new GameObject($"{label}Row", typeof(RectTransform));
            var row = rowObject.GetComponent<RectTransform>();
            row.SetParent(parent, false);

            var layout = rowObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;

            var labelText = CreateBodyText(row, label);
            LayoutElementWithWidth(labelText.gameObject, 90f);
            return row;
        }

        private void ClearBindings()
        {
            foreach (var binding in _bindings)
            {
                binding.Clear();
            }
        }

        private void RefreshOverallText()
        {
            if (_overallText == null)
            {
                return;
            }

            if (_selectedPlayer == null)
            {
                _overallText.text = "\u603b\u8bc4\uff1a-\uff08\u7cfb\u7edf\u6839\u636e\u80fd\u529b\u5c5e\u6027\u81ea\u52a8\u8ba1\u7b97\uff09";
                return;
            }

            _overallText.text = $"\u603b\u8bc4\uff1a{_selectedPlayer.Overall}\uff08\u7cfb\u7edf\u6839\u636e\u80fd\u529b\u5c5e\u6027\u81ea\u52a8\u8ba1\u7b97\uff09";
        }

        private interface IFieldBinding
        {
            void Load(Player player);
            bool Save(Player player, out string error);
            void Clear();
        }

        private sealed class TextFieldBinding : IFieldBinding
        {
            private readonly InputField _input;
            private readonly Func<Player, string> _getter;
            private readonly Action<Player, string> _setter;

            public TextFieldBinding(InputField input, Func<Player, string> getter, Action<Player, string> setter)
            {
                _input = input;
                _getter = getter;
                _setter = setter;
            }

            public void Load(Player player)
            {
                _input.text = _getter(player);
            }

            public bool Save(Player player, out string error)
            {
                _setter(player, _input.text.Trim());
                error = string.Empty;
                return true;
            }

            public void Clear()
            {
                _input.text = string.Empty;
            }
        }

        private sealed class IntFieldBinding : IFieldBinding
        {
            private readonly string _label;
            private readonly InputField _input;
            private readonly Func<Player, int> _getter;
            private readonly Action<Player, int> _setter;

            public IntFieldBinding(string label, InputField input, Func<Player, int> getter, Action<Player, int> setter)
            {
                _label = label;
                _input = input;
                _getter = getter;
                _setter = setter;
            }

            public void Load(Player player)
            {
                _input.text = _getter(player).ToString();
            }

            public bool Save(Player player, out string error)
            {
                if (!int.TryParse(_input.text, out var value))
                {
                    error = $"{_label} \u5fc5\u987b\u662f\u6574\u6570";
                    return false;
                }

                _setter(player, value);
                error = string.Empty;
                return true;
            }

            public void Clear()
            {
                _input.text = "0";
            }
        }

        private sealed class DropdownFieldBinding : IFieldBinding
        {
            private readonly Dropdown _dropdown;
            private readonly IReadOnlyList<string> _options;
            private readonly Func<Player, string> _getter;
            private readonly Action<Player, string> _setter;

            public DropdownFieldBinding(Dropdown dropdown, IReadOnlyList<string> options, Func<Player, string> getter, Action<Player, string> setter)
            {
                _dropdown = dropdown;
                _options = options;
                _getter = getter;
                _setter = setter;
            }

            public void Load(Player player)
            {
                var currentValue = _getter(player);
                var index = 0;
                for (var i = 0; i < _options.Count; i++)
                {
                    if (string.Equals(_options[i], currentValue, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }

                _dropdown.value = index;
                _dropdown.RefreshShownValue();
            }

            public bool Save(Player player, out string error)
            {
                _setter(player, _options[_dropdown.value]);
                error = string.Empty;
                return true;
            }

            public void Clear()
            {
                _dropdown.value = 0;
                _dropdown.RefreshShownValue();
            }
        }
    }
}
