using BasketballManager.Database;
using BasketballManager.UI.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace BasketballManager.UI.Screens
{
    public sealed class MainScreen : UIScreenBase
    {
        private DatabaseManager _databaseManager;
        private TeamRepository _teamRepository;
        private PlayerRepository _playerRepository;

        private RosterManagementScreen _rosterScreen;
        private MatchCenterScreen _matchScreen;

        private Button _rosterNavBtn;
        private Button _matchNavBtn;

        private Color _navActiveColor = new Color(0.23f, 0.49f, 0.81f);
        private Color _navInactiveColor = new Color(0.18f, 0.20f, 0.26f);

        public void Initialize(DatabaseManager databaseManager, TeamRepository teamRepository, PlayerRepository playerRepository)
        {
            _databaseManager = databaseManager;
            _teamRepository = teamRepository;
            _playerRepository = playerRepository;
        }

        private void Start()
        {
            if (_databaseManager == null) return;

            EnsureEventSystem();
            BuildAppShell();
            
            ShowRosterScreen();
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

        private void BuildAppShell()
        {
            var canvasObject = new GameObject("MainCanvas");
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<GraphicRaycaster>();

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;

            var root = CreatePanel("AppRoot", canvasObject.transform, new Color(0.05f, 0.06f, 0.08f));
            Stretch(root);

            var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // Top Nav
            var topNav = CreatePanel("TopNav", root, new Color(0.11f, 0.12f, 0.16f));
            LayoutElementWithHeight(topNav.gameObject, 60f);
            
            var navLayout = topNav.gameObject.AddComponent<HorizontalLayoutGroup>();
            navLayout.padding = new RectOffset(20, 20, 10, 10);
            navLayout.spacing = 16f;
            navLayout.childAlignment = TextAnchor.MiddleLeft;
            navLayout.childForceExpandWidth = false;
            navLayout.childForceExpandHeight = false;

            var title = CreateHeader(topNav, "Basketball Manager", 24);
            LayoutElementWithWidth(title.gameObject, 280f);

            _rosterNavBtn = CreateButton(topNav, "\u9635\u5bb9\u7ba1\u7406", ShowRosterScreen); // 阵容管理
            LayoutElementWithWidth(_rosterNavBtn.gameObject, 150f);

            _matchNavBtn = CreateButton(topNav, "\u6bd4\u8d5b\u4e2d\u5fc3", ShowMatchScreen); // 比赛中心
            LayoutElementWithWidth(_matchNavBtn.gameObject, 150f);

            // Content Area
            var contentArea = CreatePanel("ContentArea", root, new Color(0.05f, 0.06f, 0.08f));
            var contentLayoutElement = contentArea.gameObject.AddComponent<LayoutElement>();
            contentLayoutElement.flexibleHeight = 1f;

            // Instantiate screens
            _rosterScreen = gameObject.AddComponent<RosterManagementScreen>();
            _rosterScreen.Initialize(_databaseManager, _teamRepository, _playerRepository);
            _rosterScreen.BuildUi(contentArea);

            _matchScreen = gameObject.AddComponent<MatchCenterScreen>();
            _matchScreen.Initialize(_databaseManager, _teamRepository, _playerRepository);
            _matchScreen.BuildUi(contentArea);
        }

        private void ShowRosterScreen()
        {
            _rosterNavBtn.GetComponent<Image>().color = _navActiveColor;
            _matchNavBtn.GetComponent<Image>().color = _navInactiveColor;

            _rosterScreen.Show();
            _matchScreen.Hide();
        }

        private void ShowMatchScreen()
        {
            _rosterNavBtn.GetComponent<Image>().color = _navInactiveColor;
            _matchNavBtn.GetComponent<Image>().color = _navActiveColor;

            _rosterScreen.Hide();
            _matchScreen.Show();
        }
    }
}
