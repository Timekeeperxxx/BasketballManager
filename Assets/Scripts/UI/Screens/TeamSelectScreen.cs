using System;
using System.Collections.Generic;
using System.Linq;
using BasketballManager.App;
using BasketballManager.Core.Models;
using BasketballManager.Database;
using BasketballManager.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BasketballManager.UI.Screens
{
    public sealed class TeamSelectScreen : UIToolkitScreenBase
    {
        public const string Id = "TeamSelect";

        public event Action<string> OnTeamConfirmed;
        public event Action OnBackClicked;

        private TeamRepository _teamRepository;
        private readonly List<Team> _teams = new List<Team>();
        private Team _selectedTeam;

        private ListView _teamList;
        private Label _selectedLabel;
        private Button _confirmBtn;

        private void Awake() { ScreenId = Id; }

        public void Initialize(TeamRepository teamRepository)
        {
            _teamRepository = teamRepository;
        }

        protected override void OnBuilt()
        {
            Root.Q<Button>("btn-back").clicked += () => OnBackClicked?.Invoke();

            _teamList = Root.Q<ListView>("team-list");
            _selectedLabel = Root.Q<Label>("team-select__selected-label");
            _confirmBtn = Root.Q<Button>("btn-confirm");

            _teamList.makeItem = MakeTeamRow;
            _teamList.bindItem = BindTeamRow;
            _teamList.fixedItemHeight = 52;
            _teamList.selectionType = SelectionType.Single;
            _teamList.selectionChanged += OnSelectionChanged;

            _confirmBtn.SetEnabled(false);
            _confirmBtn.clicked += OnConfirm;
        }

        protected override void OnEnter()
        {
            _teams.Clear();
            if (_teamRepository != null)
                _teams.AddRange(_teamRepository.GetAllTeams()
                    .Where(t => t.Id != "__FA__" && t.Id != "__DRAFT_POOL__"));

            _teamList.itemsSource = _teams;
            _teamList.Rebuild();

            string saved = PlayerPrefs.GetString(SaveManager.GetUserTeamIdKey(SaveManager.ActiveSlotId), "");
            if (!string.IsNullOrEmpty(saved))
            {
                int idx = _teams.FindIndex(t => t.Id == saved);
                if (idx >= 0)
                {
                    _teamList.SetSelectionWithoutNotify(new[] { idx });
                    _selectedTeam = _teams[idx];
                    _selectedLabel.text = $"已选：{_selectedTeam.City} {_selectedTeam.Name}";
                    _confirmBtn.SetEnabled(true);
                    return;
                }
            }
            _selectedTeam = null;
            _selectedLabel.text = "请选择一支球队";
            _confirmBtn.SetEnabled(false);
        }

        private VisualElement MakeTeamRow()
        {
            var row = new VisualElement();
            row.AddToClassList("team-list-item");

            var nameLabel = new Label();
            nameLabel.AddToClassList("team-list-item__name");
            row.Add(nameLabel);

            var metaLabel = new Label();
            metaLabel.AddToClassList("team-list-item__meta");
            row.Add(metaLabel);

            return row;
        }

        private void BindTeamRow(VisualElement e, int i)
        {
            var team = _teams[i];
            e.Q<Label>(null, "team-list-item__name").text = $"{team.City} {team.Name}";
            string era = team.Era > 0 ? $"{team.Era} 年代" : "现役";
            e.Q<Label>(null, "team-list-item__meta").text = era;
        }

        private void OnSelectionChanged(IEnumerable<object> items)
        {
            _selectedTeam = null;
            foreach (var item in items)
            {
                if (item is Team t) { _selectedTeam = t; break; }
            }

            if (_selectedTeam != null)
            {
                _selectedLabel.text = $"已选：{_selectedTeam.City} {_selectedTeam.Name}";
                _confirmBtn.SetEnabled(true);
            }
            else
            {
                _selectedLabel.text = "请选择一支球队";
                _confirmBtn.SetEnabled(false);
            }
        }

        private void OnConfirm()
        {
            if (_selectedTeam == null) return;
            PlayerPrefs.SetString(SaveManager.GetUserTeamIdKey(SaveManager.ActiveSlotId), _selectedTeam.Id);
            PlayerPrefs.Save();
            OnTeamConfirmed?.Invoke(_selectedTeam.Id);
        }
    }
}
