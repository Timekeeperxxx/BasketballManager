using System;
using System.Collections.Generic;
using BasketballManager.Core.Enums;
using BasketballManager.Core.Models;
using BasketballManager.Database;
using BasketballManager.Simulation;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace BasketballManager.UI.Screens
{
    [System.Obsolete("Use RosterManagementScreen and MatchCenterScreen instead.")]
    public sealed class PlayerManagementScreen : MonoBehaviour
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
        private Text _statusText;
        private Text _overallText;
        private bool _built;

        public void Initialize(DatabaseManager databaseManager, TeamRepository teamRepository, PlayerRepository playerRepository)
        {
            _databaseManager = databaseManager;
            _teamRepository = teamRepository;
            _playerRepository = playerRepository;
        }

        private void Start()
        {
            if (_built || _databaseManager == null || _teamRepository == null || _playerRepository == null)
            {
                return;
            }

            EnsureEventSystem();
            BuildUi();
            RefreshTeams();
            _built = true;
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
#else
            eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
        }

        private void BuildUi()
        {
            var canvasObject = new GameObject("ManagementCanvas");
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<GraphicRaycaster>();

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;

            var root = CreatePanel("Root", canvasObject.transform, new Color(0.08f, 0.09f, 0.11f));
            Stretch(root);

            var layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 16f;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;

            var teamsPanel = CreateColumnPanel(root, 260f);
            _teamHeader = CreateHeader(teamsPanel, "\u7403\u961f");
            _teamListContent = CreateScrollList(teamsPanel);

            var rosterPanel = CreateColumnPanel(root, 360f);
            _rosterHeader = CreateHeader(rosterPanel, "\u9635\u5bb9");
            _rosterListContent = CreateScrollList(rosterPanel);

            var editorPanel = CreateFlexiblePanel(root);
            _editorHeader = CreateHeader(editorPanel, "\u7403\u5458\u7f16\u8f91");

            var editorScroll = CreateScrollView(editorPanel, out var editorContent);
            var editorScrollLayout = editorScroll.gameObject.AddComponent<LayoutElement>();
            editorScrollLayout.flexibleHeight = 1f;

            var editorLayout = editorContent.gameObject.AddComponent<VerticalLayoutGroup>();
            editorLayout.padding = new RectOffset(12, 12, 12, 12);
            editorLayout.spacing = 10f;
            editorLayout.childControlWidth = true;
            editorLayout.childControlHeight = true;
            editorLayout.childForceExpandWidth = true;
            editorLayout.childForceExpandHeight = false;
            editorContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildEditor(editorContent);

            var footer = CreatePanel("Footer", editorPanel, new Color(0.11f, 0.12f, 0.16f));
            var footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            footerLayout.padding = new RectOffset(12, 12, 10, 10);
            footerLayout.spacing = 12f;
            footerLayout.childAlignment = TextAnchor.MiddleLeft;
            footerLayout.childForceExpandWidth = false;
            footerLayout.childForceExpandHeight = false;
            LayoutElementWithHeight(footer.gameObject, 64f);

            var saveButton = CreateButton(footer, "\u4fdd\u5b58\u7403\u5458", SaveCurrentPlayer);
            LayoutElementWithWidth(saveButton.gameObject, 180f);

            var simulateButton = CreateButton(footer, "\u6a21\u62df\u524d\u4e24\u652f\u7403\u961f\u6bd4\u8d5b", SimulateFirstTwoTeams);
            LayoutElementWithWidth(simulateButton.gameObject, 220f);

            _statusText = CreateBodyText(footer, $"\u6570\u636e\u5e93: {_databaseManager.PersistentDatabasePath}");
            _statusText.alignment = TextAnchor.MiddleLeft;
            _statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            var statusLayout = _statusText.gameObject.AddComponent<LayoutElement>();
            statusLayout.flexibleWidth = 1f;
        }

        private void BuildEditor(RectTransform parent)
        {
            CreateSection(parent, "\u57fa\u7840\u8d44\u6599", section =>
            {
                _overallText = CreateBodyText(section, "\u603b\u8bc4\uff1a-\uff08\u7cfb\u7edf\u6839\u636e\u80fd\u529b\u5c5e\u6027\u81ea\u52a8\u8ba1\u7b97\uff09");
                LayoutElementWithHeight(_overallText.gameObject, 36f);
                AddField(CreateTextBinding(section, "\u540d", p => p.FirstName, (p, value) => p.FirstName = value));
                AddField(CreateTextBinding(section, "\u59d3", p => p.LastName, (p, value) => p.LastName = value));
                AddField(CreateTextBinding(section, "\u663e\u793a\u540d", p => p.DisplayName, (p, value) => p.DisplayName = value));
                AddField(CreateEnumDropdownBinding(section, "\u59d3\u540d\u987a\u5e8f", Enum.GetNames(typeof(NameOrder)), p => p.NameOrder.ToString(), (p, value) => p.NameOrder = Enum.TryParse(value, out NameOrder parsed) ? parsed : NameOrder.WESTERN));
                AddField(CreateTextBinding(section, "\u56fd\u7c4d", p => p.Nationality, (p, value) => p.Nationality = value));
                AddField(CreateTextBinding(section, "\u5730\u533a\u7c7b\u578b", p => p.RegionType, (p, value) => p.RegionType = value));
                AddField(CreateEnumDropdownBinding(section, "\u4f4d\u7f6e", Enum.GetNames(typeof(Position)), p => p.Position.ToString(), (p, value) => p.Position = Enum.TryParse(value, out Position parsed) ? parsed : Position.PG));
                AddField(CreateIntBinding(section, "\u8eab\u9ad8(cm)", p => p.HeightCm, (p, value) => p.HeightCm = value));
                AddField(CreateIntBinding(section, "\u4f53\u91cd(kg)", p => p.WeightKg, (p, value) => p.WeightKg = value));
                AddField(CreateIntBinding(section, "\u5e74\u9f84", p => p.Age, (p, value) => p.Age = value));
                AddField(CreateIntBinding(section, "\u7403\u8863\u53f7\u7801", p => p.JerseyNumber, (p, value) => p.JerseyNumber = value));
            });

            CreateSection(parent, "\u80fd\u529b\u5c5e\u6027", section =>
            {
                AddField(CreateIntBinding(section, "\u4e24\u5206", p => p.Attributes.TwoPoint, (p, value) => p.Attributes.TwoPoint = value));
                AddField(CreateIntBinding(section, "\u4e09\u5206", p => p.Attributes.ThreePoint, (p, value) => p.Attributes.ThreePoint = value));
                AddField(CreateIntBinding(section, "\u4e0a\u7bee", p => p.Attributes.Layup, (p, value) => p.Attributes.Layup = value));
                AddField(CreateIntBinding(section, "\u8fd1\u6295", p => p.Attributes.CloseShot, (p, value) => p.Attributes.CloseShot = value));
                AddField(CreateIntBinding(section, "\u80cc\u8eab\u5f97\u5206", p => p.Attributes.PostScoring, (p, value) => p.Attributes.PostScoring = value));
                AddField(CreateIntBinding(section, "\u7f5a\u7403", p => p.Attributes.FreeThrow, (p, value) => p.Attributes.FreeThrow = value));
                AddField(CreateIntBinding(section, "\u4f20\u7403", p => p.Attributes.Passing, (p, value) => p.Attributes.Passing = value));
                AddField(CreateIntBinding(section, "\u63a7\u7403", p => p.Attributes.BallHandle, (p, value) => p.Attributes.BallHandle = value));
                AddField(CreateIntBinding(section, "\u7a81\u7834", p => p.Attributes.Drive, (p, value) => p.Attributes.Drive = value));
                AddField(CreateIntBinding(section, "\u9020\u72af\u89c4", p => p.Attributes.DrawFoul, (p, value) => p.Attributes.DrawFoul = value));
                AddField(CreateIntBinding(section, "\u8fdb\u653b\u7a33\u5b9a\u6027", p => p.Attributes.OffensiveConsistency, (p, value) => p.Attributes.OffensiveConsistency = value));
                AddField(CreateIntBinding(section, "\u5916\u7ebf\u9632\u5b88", p => p.Attributes.PerimeterDefense, (p, value) => p.Attributes.PerimeterDefense = value));
                AddField(CreateIntBinding(section, "\u5185\u7ebf\u9632\u5b88", p => p.Attributes.InteriorDefense, (p, value) => p.Attributes.InteriorDefense = value));
                AddField(CreateIntBinding(section, "\u62a2\u65ad", p => p.Attributes.Steal, (p, value) => p.Attributes.Steal = value));
                AddField(CreateIntBinding(section, "\u76d6\u5e3d", p => p.Attributes.Block, (p, value) => p.Attributes.Block = value));
                AddField(CreateIntBinding(section, "\u8fdb\u653b\u7bee\u677f", p => p.Attributes.OffensiveRebound, (p, value) => p.Attributes.OffensiveRebound = value));
                AddField(CreateIntBinding(section, "\u9632\u5b88\u7bee\u677f", p => p.Attributes.DefensiveRebound, (p, value) => p.Attributes.DefensiveRebound = value));
                AddField(CreateIntBinding(section, "\u9632\u5b88\u7a33\u5b9a\u6027", p => p.Attributes.DefensiveConsistency, (p, value) => p.Attributes.DefensiveConsistency = value));
                AddField(CreateIntBinding(section, "\u901f\u5ea6", p => p.Attributes.Speed, (p, value) => p.Attributes.Speed = value));
                AddField(CreateIntBinding(section, "\u529b\u91cf", p => p.Attributes.Strength, (p, value) => p.Attributes.Strength = value));
                AddField(CreateIntBinding(section, "\u4f53\u80fd", p => p.Attributes.Stamina, (p, value) => p.Attributes.Stamina = value));
            });

            CreateSection(parent, "\u8fdb\u653b\u503e\u5411", section =>
            {
                AddField(CreateIntBinding(section, "\u51fa\u624b\u503e\u5411", p => p.Tendencies.ShotTendency, (p, value) => p.Tendencies.ShotTendency = value));
                AddField(CreateIntBinding(section, "\u4e09\u5206\u503e\u5411", p => p.Tendencies.ThreeTendency, (p, value) => p.Tendencies.ThreeTendency = value));
                AddField(CreateIntBinding(section, "\u4e24\u5206\u503e\u5411", p => p.Tendencies.TwoPointTendency, (p, value) => p.Tendencies.TwoPointTendency = value));
                AddField(CreateIntBinding(section, "\u7a81\u7834\u503e\u5411", p => p.Tendencies.DriveTendency, (p, value) => p.Tendencies.DriveTendency = value));
                AddField(CreateIntBinding(section, "\u80cc\u8eab\u503e\u5411", p => p.Tendencies.PostTendency, (p, value) => p.Tendencies.PostTendency = value));
                AddField(CreateIntBinding(section, "\u8fd1\u6295\u503e\u5411", p => p.Tendencies.CloseShotTendency, (p, value) => p.Tendencies.CloseShotTendency = value));
                AddField(CreateIntBinding(section, "\u4f20\u7403\u503e\u5411", p => p.Tendencies.PassTendency, (p, value) => p.Tendencies.PassTendency = value));
                AddField(CreateIntBinding(section, "\u9020\u72af\u89c4\u503e\u5411", p => p.Tendencies.DrawFoulTendency, (p, value) => p.Tendencies.DrawFoulTendency = value));
            });

            CreateSection(parent, "\u9632\u5b88\u503e\u5411", section =>
            {
                AddField(CreateIntBinding(section, "\u62a2\u65ad\u503e\u5411", p => p.Tendencies.StealTendency, (p, value) => p.Tendencies.StealTendency = value));
                AddField(CreateIntBinding(section, "\u76d6\u5e3d\u503e\u5411", p => p.Tendencies.BlockTendency, (p, value) => p.Tendencies.BlockTendency = value));
                AddField(CreateIntBinding(section, "\u72af\u89c4\u503e\u5411", p => p.Tendencies.FoulTendency, (p, value) => p.Tendencies.FoulTendency = value));
                AddField(CreateIntBinding(section, "\u534f\u9632\u503e\u5411", p => p.Tendencies.HelpDefenseTendency, (p, value) => p.Tendencies.HelpDefenseTendency = value));
                AddField(CreateIntBinding(section, "\u8fdb\u653b\u7bee\u677f\u503e\u5411", p => p.Tendencies.OffensiveReboundTendency, (p, value) => p.Tendencies.OffensiveReboundTendency = value));
                AddField(CreateIntBinding(section, "\u9632\u5b88\u7bee\u677f\u503e\u5411", p => p.Tendencies.DefensiveReboundTendency, (p, value) => p.Tendencies.DefensiveReboundTendency = value));
            });
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
                _statusText.text = $"\u5df2\u52a0\u8f7d {teams.Count} \u652f\u7403\u961f\n\u6570\u636e\u5e93: {_databaseManager.PersistentDatabasePath}";

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
                _statusText.text = $"{exception.Message}\n\u6570\u636e\u5e93: {_databaseManager.PersistentDatabasePath}";
                Debug.LogError(exception);
            }
        }

        private void SelectTeam(Team team)
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
                    var label = $"#{player.JerseyNumber} {player.GetDisplayName()} ({player.Position}) \u603b\u8bc4 {player.Overall}";
                    CreateListButton(_rosterListContent, label, () => SelectPlayer(player.Id));
                }

                _statusText.text = $"\u5df2\u52a0\u8f7d {teamTitle} \u7684 {players.Count} \u540d\u7403\u5458\n\u6570\u636e\u5e93: {_databaseManager.PersistentDatabasePath}";

                if (players.Count > 0)
                {
                    SelectPlayer(players[0].Id);
                }
                else
                {
                    _selectedPlayer = null;
                    _editorHeader.text = "\u7403\u5458\u7f16\u8f91";
                    ClearBindings();
                    RefreshOverallText();
                }
            }
            catch (Exception exception)
            {
                _statusText.text = $"{exception.Message}\n\u6570\u636e\u5e93: {_databaseManager.PersistentDatabasePath}";
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
                    _statusText.text = $"\u672a\u627e\u5230\u7403\u5458 {playerId}\n\u6570\u636e\u5e93: {_databaseManager.PersistentDatabasePath}";
                    return;
                }

                _editorHeader.text = $"\u7403\u5458\u7f16\u8f91 - {_selectedPlayer.GetDisplayName()}";
                foreach (var binding in _bindings)
                {
                    binding.Load(_selectedPlayer);
                }

                RefreshOverallText();
                _statusText.text = $"\u6b63\u5728\u7f16\u8f91 {_selectedPlayer.GetDisplayName()}\n\u6570\u636e\u5e93: {_databaseManager.PersistentDatabasePath}";
            }
            catch (Exception exception)
            {
                _statusText.text = $"{exception.Message}\n\u6570\u636e\u5e93: {_databaseManager.PersistentDatabasePath}";
                Debug.LogError(exception);
            }
        }

        private void SaveCurrentPlayer()
        {
            if (_selectedPlayer == null)
            {
                _statusText.text = "\u8bf7\u5148\u9009\u62e9\u7403\u5458";
                return;
            }

            foreach (var binding in _bindings)
            {
                if (!binding.Save(_selectedPlayer, out var error))
                {
                    _statusText.text = error;
                    return;
                }
            }

            try
            {
                _playerRepository.UpdatePlayer(_selectedPlayer);
                _selectedPlayer = _playerRepository.GetPlayerById(_selectedPlayer.Id);
                RefreshOverallText();
            }
            catch (Exception exception)
            {
                _statusText.text = $"{exception.Message}\n\u6570\u636e\u5e93: {_databaseManager.PersistentDatabasePath}";
                Debug.LogError(exception);
                return;
            }

            _statusText.text = $"\u5df2\u4fdd\u5b58 {_selectedPlayer.GetDisplayName()}\n\u6570\u636e\u5e93: {_databaseManager.PersistentDatabasePath}";

            if (_selectedTeam != null)
            {
                SelectTeam(_selectedTeam);
                SelectPlayer(_selectedPlayer.Id);
            }
        }

        private void SimulateFirstTwoTeams()
        {
            var teams = _teamRepository.GetAllTeams();
            if (teams.Count < 2)
            {
                _statusText.text = "球队数量不足以模拟比赛";
                return;
            }

            var homeTeam = teams[0];
            var awayTeam = teams[1];

            var homePlayers = _playerRepository.GetPlayersByTeamId(homeTeam.Id);
            var awayPlayers = _playerRepository.GetPlayersByTeamId(awayTeam.Id);

            var simulator = new MatchSimulator();
            var config = new MatchConfig();
            
            var result = simulator.Simulate(homeTeam, homePlayers, awayTeam, awayPlayers, config);
            
            var text = $"{result.HomeTeamName} {result.HomeScore} - {result.AwayScore} {result.AwayTeamName}\n";
            text += $"每节: {string.Join("-", result.HomeQuarterScores)} | {string.Join("-", result.AwayQuarterScores)}\n";
            
            text += "主队得分前五: ";
            var homeTop = result.HomePlayerStats.OrderByDescending(p => p.Points).Take(5);
            text += string.Join(", ", homeTop.Select(p => $"{p.PlayerName}({p.Points})")) + "  ";
            text += $"({result.HomeTeamStats.FieldGoalsMade}/{result.HomeTeamStats.FieldGoalsAttempted})\n";
            
            text += "客队得分前五: ";
            var awayTop = result.AwayPlayerStats.OrderByDescending(p => p.Points).Take(5);
            text += string.Join(", ", awayTop.Select(p => $"{p.PlayerName}({p.Points})")) + "  ";
            text += $"({result.AwayTeamStats.FieldGoalsMade}/{result.AwayTeamStats.FieldGoalsAttempted})";

            _statusText.text = text;
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

            CreateHeader(section, title, 24);
            addFields(section);
        }

        private void AddField(IFieldBinding binding)
        {
            _bindings.Add(binding);
        }

        private static string GetTeamTitle(Team team)
        {
            if (team.Era > 0)
            {
                return $"{team.Era} {team.Name}";
            }

            return string.IsNullOrWhiteSpace(team.City) ? team.Name : $"{team.City} {team.Name}";
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
            LayoutElementWithWidth(labelText.gameObject, 230f);
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

        private static RectTransform CreateColumnPanel(RectTransform parent, float width)
        {
            var panel = CreatePanel("ColumnPanel", parent, new Color(0.11f, 0.12f, 0.16f));
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var element = panel.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.flexibleHeight = 1f;
            return panel;
        }

        private static RectTransform CreateFlexiblePanel(RectTransform parent)
        {
            var panel = CreatePanel("FlexiblePanel", parent, new Color(0.11f, 0.12f, 0.16f));
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var element = panel.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.flexibleHeight = 1f;
            return panel;
        }

        private static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var panelObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            var panel = panelObject.GetComponent<RectTransform>();
            panel.SetParent(parent, false);
            panelObject.GetComponent<Image>().color = color;
            return panel;
        }

        private static Text CreateHeader(RectTransform parent, string text, int fontSize = 28)
        {
            var label = CreateBodyText(parent, text);
            label.fontSize = fontSize;
            label.fontStyle = FontStyle.Bold;
            label.color = Color.white;
            LayoutElementWithHeight(label.gameObject, fontSize + 18f);
            return label;
        }

        private static Text CreateBodyText(Transform parent, string text)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            var textComponent = textObject.GetComponent<Text>();
            textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textComponent.fontSize = 20;
            textComponent.color = new Color(0.9f, 0.92f, 0.96f);
            textComponent.alignment = TextAnchor.MiddleLeft;
            textComponent.text = text;
            return textComponent;
        }

        private static RectTransform CreateScrollList(RectTransform parent)
        {
            var scroll = CreateScrollView(parent, out var content);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollLayout = scroll.gameObject.AddComponent<LayoutElement>();
            scrollLayout.flexibleHeight = 1f;
            return content;
        }

        private static RectTransform CreateScrollView(RectTransform parent, out RectTransform content)
        {
            var scrollObject = new GameObject("ScrollView", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollObject.transform.SetParent(parent, false);
            scrollObject.GetComponent<Image>().color = new Color(0.09f, 0.10f, 0.13f);

            var scrollRect = scrollObject.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;

            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            viewportObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.03f);

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewportObject.transform, false);

            var scrollRectTransform = scrollObject.GetComponent<RectTransform>();
            Stretch(scrollRectTransform);

            var viewport = viewportObject.GetComponent<RectTransform>();
            Stretch(viewport);

            content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            scrollRect.viewport = viewport;
            scrollRect.content = content;
            return scrollRectTransform;
        }

        private static Button CreateButton(RectTransform parent, string text, Action onClick)
        {
            var buttonObject = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.23f, 0.49f, 0.81f);

            var button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(() => onClick.Invoke());

            var layout = buttonObject.AddComponent<LayoutElement>();
            layout.minHeight = 44f;

            var label = CreateBodyText(buttonObject.transform, text);
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            Stretch(label.rectTransform);
            return button;
        }

        private static void CreateListButton(RectTransform parent, string text, Action onClick)
        {
            var button = CreateButton(parent, text, onClick);
            var image = button.GetComponent<Image>();
            image.color = new Color(0.18f, 0.20f, 0.26f);

            var layout = button.gameObject.GetComponent<LayoutElement>();
            layout.minHeight = 52f;
            layout.flexibleWidth = 1f;
        }

        private static InputField CreateInputField(RectTransform parent)
        {
            var inputObject = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(InputField));
            inputObject.transform.SetParent(parent, false);

            inputObject.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.21f);
            var layout = inputObject.AddComponent<LayoutElement>();
            layout.minHeight = 38f;
            layout.flexibleWidth = 1f;

            var input = inputObject.GetComponent<InputField>();
            var text = CreateBodyText(inputObject.transform, string.Empty);
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            Stretch(text.rectTransform, 10f, 10f, 6f, 6f);

            var placeholder = CreateBodyText(inputObject.transform, "\u8bf7\u8f93\u5165");
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);
            placeholder.alignment = TextAnchor.MiddleLeft;
            Stretch(placeholder.rectTransform, 10f, 10f, 6f, 6f);

            input.textComponent = text;
            input.placeholder = placeholder;
            return input;
        }

        private static Dropdown CreateDropdown(RectTransform parent, IReadOnlyList<string> options)
        {
            var dropdownObject = new GameObject("Dropdown", typeof(RectTransform), typeof(Image), typeof(Dropdown));
            dropdownObject.transform.SetParent(parent, false);
            dropdownObject.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.21f);

            var layout = dropdownObject.AddComponent<LayoutElement>();
            layout.minHeight = 38f;
            layout.flexibleWidth = 1f;

            var captionText = CreateBodyText(dropdownObject.transform, string.Empty);
            captionText.alignment = TextAnchor.MiddleLeft;
            Stretch(captionText.rectTransform, 10f, 30f, 6f, 6f);

            var arrowText = CreateBodyText(dropdownObject.transform, "v");
            arrowText.alignment = TextAnchor.MiddleCenter;
            arrowText.color = Color.white;
            var arrowRect = arrowText.rectTransform;
            arrowRect.anchorMin = new Vector2(1f, 0f);
            arrowRect.anchorMax = new Vector2(1f, 1f);
            arrowRect.pivot = new Vector2(1f, 0.5f);
            arrowRect.sizeDelta = new Vector2(24f, 0f);
            arrowRect.anchoredPosition = new Vector2(-8f, 0f);

            var dropdown = dropdownObject.GetComponent<Dropdown>();
            dropdown.targetGraphic = dropdownObject.GetComponent<Image>();
            dropdown.captionText = captionText;
            dropdown.options = new List<Dropdown.OptionData>();
            foreach (var option in options)
            {
                dropdown.options.Add(new Dropdown.OptionData(option));
            }

            var template = CreateDropdownTemplate(dropdownObject.transform, options);
            dropdown.template = template;
            dropdown.itemText = template.GetComponentInChildren<Text>();

            return dropdown;
        }

        private static RectTransform CreateDropdownTemplate(Transform parent, IReadOnlyList<string> options)
        {
            var templateObject = new GameObject("Template", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            templateObject.transform.SetParent(parent, false);
            templateObject.SetActive(false);
            templateObject.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.21f);

            var templateRect = templateObject.GetComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(1f, 0f);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, -4f);
            templateRect.sizeDelta = new Vector2(0f, Mathf.Max(120f, options.Count * 30f + 12f));

            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportObject.transform.SetParent(templateObject.transform, false);
            viewportObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;
            Stretch(viewportObject.GetComponent<RectTransform>());

            var contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(ToggleGroup));
            contentObject.transform.SetParent(viewportObject.transform, false);
            var contentRect = contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;

            var contentLayout = contentObject.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 2f;
            contentLayout.childControlHeight = true;
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollRect = templateObject.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.viewport = viewportObject.GetComponent<RectTransform>();
            scrollRect.content = contentRect;

            Toggle firstToggle = null;
            foreach (var option in options)
            {
                var itemObject = new GameObject(option, typeof(RectTransform), typeof(Image), typeof(Toggle));
                itemObject.transform.SetParent(contentObject.transform, false);
                itemObject.GetComponent<Image>().color = new Color(0.18f, 0.20f, 0.26f);

                var itemLayout = itemObject.AddComponent<LayoutElement>();
                itemLayout.minHeight = 28f;

                var itemText = CreateBodyText(itemObject.transform, option);
                itemText.alignment = TextAnchor.MiddleLeft;
                Stretch(itemText.rectTransform, 10f, 10f, 4f, 4f);

                var toggle = itemObject.GetComponent<Toggle>();
                toggle.targetGraphic = itemObject.GetComponent<Image>();
                toggle.graphic = null;
                if (firstToggle == null)
                {
                    firstToggle = toggle;
                }
            }

            var dropdown = parent.GetComponent<Dropdown>();
            if (dropdown != null && firstToggle != null)
            {
                dropdown.itemText = firstToggle.GetComponentInChildren<Text>();
            }

            return templateRect;
        }

        private static void LayoutElementWithWidth(GameObject gameObject, float width)
        {
            var layout = gameObject.GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
        }

        private static void LayoutElementWithHeight(GameObject gameObject, float height)
        {
            var layout = gameObject.GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
        }

        private static void Stretch(RectTransform rectTransform, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(left, bottom);
            rectTransform.offsetMax = new Vector2(-right, -top);
        }

        private static void ClearChildren(RectTransform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
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
