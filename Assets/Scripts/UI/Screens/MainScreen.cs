using BasketballManager.Database;
using BasketballManager.Seasons;
using BasketballManager.UI.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using Button = UnityEngine.UIElements.Button;

namespace BasketballManager.UI.Screens
{
    /// <summary>
    /// 应用壳：搭起 UI Toolkit 容器 + 路由，注册并连接所有屏幕。
    /// 阶段三架构：MainMenu / Season / Options / Roster / Debug，全部 UI Toolkit 原生。
    /// </summary>
    public sealed class MainScreen : MonoBehaviour
    {
        // ---------- 依赖 ----------
        private DatabaseManager _databaseManager;
        private TeamRepository _teamRepository;
        private PlayerRepository _playerRepository;
        private SimulationProfileRepository _profileRepository;
        private SeasonRepository _seasonRepository;
        private SeasonService _seasonService;

        // ---------- UI Toolkit ----------
        private UIDocument _document;
        private PanelSettings _panelSettings;
        private VisualElement _screenHost;
        private Button _debugFab;

        // ---------- 路由 + 屏幕 ----------
        private ScreenRouter _router;
        private MainMenuScreen _mainMenu;
        private SeasonScreen _seasonScreen;
        private OptionsScreen _optionsScreen;
        private RosterScreen _rosterScreen;
        private DebugPanelScreen _debugHost;

        public void Initialize(DatabaseManager databaseManager, TeamRepository teamRepository, PlayerRepository playerRepository, SimulationProfileRepository profileRepository, SeasonRepository seasonRepository, SeasonService seasonService)
        {
            _databaseManager = databaseManager;
            _teamRepository = teamRepository;
            _playerRepository = playerRepository;
            _profileRepository = profileRepository;
            _seasonRepository = seasonRepository;
            _seasonService = seasonService;
        }

        private void Start()
        {
            if (_databaseManager == null) return;

            try { EnsureEventSystem();   Debug.Log("[MainScreen] EnsureEventSystem OK"); }   catch (System.Exception e) { Debug.LogException(e); Debug.LogError("[MainScreen] FAILED at EnsureEventSystem"); return; }
            try { BuildUIDocument();     Debug.Log("[MainScreen] BuildUIDocument OK"); }     catch (System.Exception e) { Debug.LogException(e); Debug.LogError("[MainScreen] FAILED at BuildUIDocument"); return; }
            try { CreateToolkitScreens();Debug.Log("[MainScreen] CreateToolkitScreens OK"); }catch (System.Exception e) { Debug.LogException(e); Debug.LogError("[MainScreen] FAILED at CreateToolkitScreens"); return; }
            try { WireEvents();          Debug.Log("[MainScreen] WireEvents OK"); }          catch (System.Exception e) { Debug.LogException(e); Debug.LogError("[MainScreen] FAILED at WireEvents"); return; }

            try
            {
                _router.ReplaceRoot(MainMenuScreen.Id);
                UpdateDebugFabVisibility(_router.Current);
                Debug.Log("[MainScreen] ReplaceRoot(MainMenu) OK");
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("[MainScreen] FAILED at ReplaceRoot(MainMenu)");
            }
        }

        // ============================================================
        //  Infrastructure
        // ============================================================

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        private void BuildUIDocument()
        {
            // 优先尝试加载用户在 Editor 中创建的 PanelSettings 资产，
            // 否则程序化创建（缺主题，仅作降级使用）。
            _panelSettings = Resources.Load<PanelSettings>("UI/AppPanelSettings");
            if (_panelSettings == null)
            {
                _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                _panelSettings.name = "AppPanelSettings";
                _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                _panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                _panelSettings.referenceResolution = new Vector2Int(1600, 900);
                _panelSettings.match = 0.5f;

                var appTheme = Resources.Load<ThemeStyleSheet>("UI/Theme/AppTheme");
                if (appTheme != null)
                {
                    _panelSettings.themeStyleSheet = appTheme;
                }
                else
                {
                    Debug.LogWarning(
                        "MainScreen: 未能加载默认运行时主题（Resources/UI/Theme/AppTheme.tss）。" +
                        "请在 Unity 中右键 Assets/Resources/UI/ → Create → UI Toolkit → Panel Settings Asset → 命名 AppPanelSettings 以解决渲染问题。");
                }
            }
            _panelSettings.sortingOrder = 10;

            // 加载 UXML / USS
            var shellTree = Resources.Load<VisualTreeAsset>("UI/Shell/AppShell");
            var themeStyle = Resources.Load<StyleSheet>("UI/Theme/theme");
            var shellStyle = Resources.Load<StyleSheet>("UI/Shell/AppShell");
            if (shellTree == null) Debug.LogError("MainScreen: Failed to load Resources/UI/Shell/AppShell.uxml");
            if (themeStyle == null) Debug.LogError("MainScreen: Failed to load Resources/UI/Theme/theme.uss");

            _document = gameObject.AddComponent<UIDocument>();
            _document.panelSettings = _panelSettings;
            _document.visualTreeAsset = shellTree;

            var root = _document.rootVisualElement;
            if (themeStyle != null) root.styleSheets.Add(themeStyle);
            if (shellStyle != null) root.styleSheets.Add(shellStyle);
            root.style.flexGrow = 1f;

            _screenHost = root.Q<VisualElement>("screen-host");
            _debugFab = root.Q<Button>("btn-debug");
        }

        // ============================================================
        //  Screens
        // ============================================================

        private void CreateToolkitScreens()
        {
            _router = new ScreenRouter();

            _mainMenu = gameObject.AddComponent<MainMenuScreen>();
            _mainMenu.BuildUi(_screenHost, LoadTree("UI/Screens/MainMenu"), LoadStyle("UI/Screens/MainMenu"));
            _router.Register(_mainMenu);

            _seasonScreen = gameObject.AddComponent<SeasonScreen>();
            _seasonScreen.Initialize(_seasonService, _seasonRepository, _teamRepository);
            _seasonScreen.BuildUi(_screenHost, LoadTree("UI/Screens/SeasonScreen"), LoadStyle("UI/Screens/SeasonScreen"));
            _router.Register(_seasonScreen);

            _optionsScreen = gameObject.AddComponent<OptionsScreen>();
            _optionsScreen.BuildUi(_screenHost, LoadTree("UI/Screens/OptionsScreen"), LoadStyle("UI/Screens/OptionsScreen"));
            _router.Register(_optionsScreen);

            _rosterScreen = gameObject.AddComponent<RosterScreen>();
            _rosterScreen.Initialize(_teamRepository, _playerRepository);
            _rosterScreen.BuildUi(_screenHost, LoadTree("UI/Screens/RosterScreen"), LoadStyle("UI/Screens/RosterScreen"));
            _router.Register(_rosterScreen);

            _debugHost = gameObject.AddComponent<DebugPanelScreen>();
            _debugHost.Initialize(_teamRepository, _playerRepository, _profileRepository);
            _debugHost.BuildUi(_screenHost, LoadTree("UI/Screens/DebugPanel"), LoadStyle("UI/Screens/DebugPanel"));
            _router.Register(_debugHost);
        }

        private static VisualTreeAsset LoadTree(string path)
        {
            var tree = Resources.Load<VisualTreeAsset>(path);
            if (tree == null) Debug.LogError($"MainScreen: Failed to load Resources/{path}.uxml");
            return tree;
        }

        private static StyleSheet LoadStyle(string path)
        {
            var sheet = Resources.Load<StyleSheet>(path);
            if (sheet == null) Debug.LogWarning($"MainScreen: Failed to load Resources/{path}.uss (UXML 内 <Style src> 已自带样式，此为冗余兜底)。");
            return sheet;
        }

        // ============================================================
        //  Routing wires
        // ============================================================

        private void WireEvents()
        {
            // 主菜单
            _mainMenu.OnSeasonClicked += () => _router.Push(SeasonScreen.Id);
            _mainMenu.OnOptionsClicked += () => _router.Push(OptionsScreen.Id);
            _mainMenu.OnQuitClicked += QuitGame;

            // 赛季
            _seasonScreen.OnBackClicked += () => _router.Pop();

            // 选项
            _optionsScreen.OnBackClicked += () => _router.Pop();
            _optionsScreen.OnRosterClicked += () => _router.Push(RosterScreen.Id);

            // 阵容编辑
            _rosterScreen.OnBackClicked += () => _router.Pop();

            // Debug host
            _debugHost.OnBackClicked += () => _router.Pop();

            // 右下角调试 FAB
            _debugFab.clicked += () => _router.Push(DebugPanelScreen.Id);

            // 装饰：路由切换时控制 FAB 可见性
            _router.CurrentChanged += UpdateDebugFabVisibility;
        }

        private void UpdateDebugFabVisibility(string currentScreenId)
        {
            if (_debugFab == null) return;
            bool show = currentScreenId == MainMenuScreen.Id;
            if (show) _debugFab.RemoveFromClassList("debug-fab--hidden");
            else if (!_debugFab.ClassListContains("debug-fab--hidden")) _debugFab.AddToClassList("debug-fab--hidden");
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
