using System;
using BasketballManager.App;
using BasketballManager.Core.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace BasketballManager.UI.Screens
{
    public sealed class SaveSelectScreen : MonoBehaviour
    {
        public event Action<int> OnSlotConfirmed;
        public event Action      OnQuitClicked;
        public event Action      OnBackClicked;

        private VisualElement _root;
        private VisualElement _slotList;
        private VisualElement _deleteOverlay;
        private Label         _deleteConfirmMsg;
        private int           _pendingDeleteSlot = -1;

        public void Build(UIDocument doc)
        {
            // panelSettings 优先由调用方（AppBootstrapper）预先配置，此处仅兜底
            if (doc.panelSettings == null)
            {
                var ps = Resources.Load<PanelSettings>("UI/AppPanelSettings");
                if (ps == null)
                {
                    ps = ScriptableObject.CreateInstance<PanelSettings>();
                    ps.scaleMode          = PanelScaleMode.ScaleWithScreenSize;
                    ps.screenMatchMode    = PanelScreenMatchMode.MatchWidthOrHeight;
                    ps.referenceResolution = new Vector2Int(1600, 900);
                    ps.match              = 0.5f;
                    ps.sortingOrder       = 10;
                    var theme = Resources.Load<ThemeStyleSheet>("UI/Theme/AppTheme");
                    if (theme != null) ps.themeStyleSheet = theme;
                }
                doc.panelSettings = ps;
            }

            var tree = Resources.Load<VisualTreeAsset>("UI/Screens/SaveSelectScreen");
            if (tree == null)
            {
                Debug.LogError("[SaveSelectScreen] 未能加载 SaveSelectScreen.uxml");
                return;
            }
            doc.visualTreeAsset = tree;

            var root = doc.rootVisualElement;
            var themeStyle = Resources.Load<StyleSheet>("UI/Theme/theme");
            if (themeStyle != null) root.styleSheets.Add(themeStyle);
            root.style.flexGrow = 1f;

            _root             = root.Q<VisualElement>("save-select-root");
            _slotList         = root.Q<VisualElement>("slot-list");
            _deleteOverlay    = root.Q<VisualElement>("delete-confirm-overlay");
            _deleteConfirmMsg = root.Q<Label>("delete-confirm-msg");

            root.Q<Button>("btn-back").clicked           += () => OnBackClicked?.Invoke();
            root.Q<Button>("btn-quit").clicked           += () => OnQuitClicked?.Invoke();
            root.Q<Button>("btn-delete-cancel").clicked  += CloseDeleteConfirm;
            root.Q<Button>("btn-delete-confirm").clicked += ExecuteDelete;

            RefreshSlots();
        }

        private void RefreshSlots()
        {
            if (_slotList == null) return;
            _slotList.Clear();

            for (int i = 1; i <= SaveManager.MaxSlots; i++)
            {
                var info = SaveManager.ReadSlotInfo(i);
                _slotList.Add(BuildSlotCard(info));
            }
        }

        private VisualElement BuildSlotCard(SaveSlotInfo info)
        {
            var card = new VisualElement();
            card.AddToClassList("save-slot-card");

            // 槽号 Badge
            var badge = new VisualElement();
            badge.AddToClassList("save-slot-badge");
            var badgeText = new Label(info.SlotId.ToString());
            badgeText.AddToClassList("save-slot-badge__text");
            badge.Add(badgeText);
            card.Add(badge);

            // 信息区
            var infoArea = new VisualElement();
            infoArea.AddToClassList("save-slot-info");

            if (info.IsEmpty)
            {
                var emptyLbl = new Label("空存档");
                emptyLbl.AddToClassList("save-slot-info__empty");
                infoArea.Add(emptyLbl);
            }
            else
            {
                string displayName = string.IsNullOrEmpty(info.LastSeasonName)
                    ? $"存档 {info.SlotId}"
                    : info.LastSeasonName;

                var nameLbl = new Label(displayName);
                nameLbl.AddToClassList("save-slot-info__name");
                infoArea.Add(nameLbl);

                string metaParts = info.LastModified != default
                    ? info.LastModified.ToString("yyyy-MM-dd HH:mm")
                    : "";
                if (!string.IsNullOrEmpty(info.UserTeamName))
                    metaParts = $"{info.UserTeamName}  ·  {metaParts}";

                var metaLbl = new Label(metaParts);
                metaLbl.AddToClassList("save-slot-info__meta");
                infoArea.Add(metaLbl);
            }
            card.Add(infoArea);

            // 操作按钮
            var actions = new VisualElement();
            actions.AddToClassList("save-slot-actions");

            int slotId = info.SlotId;

            if (info.IsEmpty)
            {
                var btnNew = new Button(() => OnNewSlot(slotId)) { text = "新建存档" };
                btnNew.AddToClassList("btn");
                btnNew.AddToClassList("btn--primary");
                actions.Add(btnNew);
            }
            else
            {
                var btnLoad = new Button(() => OnLoadSlot(slotId)) { text = "继续游戏" };
                btnLoad.AddToClassList("btn");
                btnLoad.AddToClassList("btn--primary");
                actions.Add(btnLoad);

                var btnDel = new Button(() => ShowDeleteConfirm(slotId)) { text = "删除" };
                btnDel.AddToClassList("btn");
                btnDel.AddToClassList("btn--danger");
                actions.Add(btnDel);
            }
            card.Add(actions);

            return card;
        }

        private void OnNewSlot(int slotId)
        {
            if (!SaveManager.CreateSlot(slotId))
            {
                Debug.LogError($"[SaveSelectScreen] 创建存档 {slotId} 失败");
                return;
            }
            OnSlotConfirmed?.Invoke(slotId);
        }

        private void OnLoadSlot(int slotId)
        {
            OnSlotConfirmed?.Invoke(slotId);
        }

        private void ShowDeleteConfirm(int slotId)
        {
            _pendingDeleteSlot = slotId;
            if (_deleteConfirmMsg != null)
                _deleteConfirmMsg.text = $"确定要删除存档 {slotId} 吗？此操作无法撤销。";
            if (_deleteOverlay != null)
                _deleteOverlay.style.display = DisplayStyle.Flex;
        }

        private void CloseDeleteConfirm()
        {
            _pendingDeleteSlot = -1;
            if (_deleteOverlay != null)
                _deleteOverlay.style.display = DisplayStyle.None;
        }

        private void ExecuteDelete()
        {
            if (_pendingDeleteSlot > 0)
                SaveManager.DeleteSlot(_pendingDeleteSlot);
            CloseDeleteConfirm();
            RefreshSlots();
        }
    }
}
