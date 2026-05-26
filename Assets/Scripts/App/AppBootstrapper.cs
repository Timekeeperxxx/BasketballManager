using System.Collections;
using System.IO;
using BasketballManager.Core.Services;
using BasketballManager.Database;
using BasketballManager.Seasons;
using BasketballManager.UI.Screens;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace BasketballManager.App
{
    public sealed class AppBootstrapper : MonoBehaviour
    {
        private static bool _created;

        // 每个 UI 阶段用独立 GameObject 宿主，避免多 UIDocument 挂在同一对象上冲突
        private GameObject _preMenuHost;
        private UIDocument _preMenuDoc;

        private GameObject       _saveSelectHost;
        private SaveSelectScreen _saveSelect;
        private UIDocument       _saveSelectDoc;

        private GameObject _mainScreenHost;
        private MainScreen _mainScreen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrapper()
        {
            if (_created || FindFirstObjectByType<AppBootstrapper>() != null)
                return;

            var go = new GameObject("AppBootstrapper");
            go.AddComponent<AppBootstrapper>();
            _created = true;
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private IEnumerator Start()
        {
            yield return CopyDatabaseFromStreamingAssets();
            ShowPreGameMenu();
        }

        // ============================================================
        //  Phase 0：预游戏主菜单（独立宿主）
        // ============================================================

        private void ShowPreGameMenu()
        {
            TearDownGame();
            TearDownSaveSelect();

            _preMenuHost = new GameObject("PreMenuHost");
            DontDestroyOnLoad(_preMenuHost);

            _preMenuDoc               = _preMenuHost.AddComponent<UIDocument>();
            _preMenuDoc.panelSettings = LoadOrCreatePanelSettings();

            var tree = Resources.Load<VisualTreeAsset>("UI/Screens/MainMenu");
            if (tree == null) { Debug.LogError("[AppBootstrapper] 未能加载 MainMenu.uxml"); return; }
            _preMenuDoc.visualTreeAsset = tree;

            var root = _preMenuDoc.rootVisualElement;
            var themeStyle = Resources.Load<StyleSheet>("UI/Theme/theme");
            if (themeStyle != null) root.styleSheets.Add(themeStyle);
            root.style.flexGrow = 1f;

            root.Q<Button>("btn-season").clicked += ShowSaveSelect;
            root.Q<Button>("btn-quit").clicked   += QuitGame;

            // 预游戏阶段无 DB，隐藏这两个按钮
            var btnOpt = root.Q<Button>("btn-options");
            var btnRet = root.Q<Button>("btn-return-save");
            if (btnOpt != null) btnOpt.style.display = DisplayStyle.None;
            if (btnRet != null) btnRet.style.display = DisplayStyle.None;
        }

        private void TearDownPreGameMenu()
        {
            if (_preMenuHost != null)
            {
                Destroy(_preMenuHost);
                _preMenuHost = null;
                _preMenuDoc  = null;
            }
        }

        // ============================================================
        //  Phase 1：存档选择（独立宿主，推迟一帧避免中断 UI 事件链）
        // ============================================================

        private void ShowSaveSelect()
        {
            StartCoroutine(ShowSaveSelectCoroutine());
        }

        private IEnumerator ShowSaveSelectCoroutine()
        {
            yield return null; // 等当前帧 UI 事件链结束

            TearDownPreGameMenu();

            yield return null; // 再等一帧，让 Destroy 完成

            _saveSelectHost = new GameObject("SaveSelectHost");
            DontDestroyOnLoad(_saveSelectHost);

            var ps = LoadOrCreatePanelSettings();
            _saveSelectDoc               = _saveSelectHost.AddComponent<UIDocument>();
            _saveSelectDoc.panelSettings = ps;

            _saveSelect = _saveSelectHost.AddComponent<SaveSelectScreen>();
            _saveSelect.Build(_saveSelectDoc);
            _saveSelect.OnSlotConfirmed += EnterGame;
            _saveSelect.OnBackClicked   += OnSaveSelectBack;
            _saveSelect.OnQuitClicked   += QuitGame;
        }

        private void OnSaveSelectBack()
        {
            TearDownSaveSelect();
            ShowPreGameMenu();
        }

        private void TearDownSaveSelect()
        {
            if (_saveSelectHost != null)
            {
                Destroy(_saveSelectHost);
                _saveSelectHost = null;
                _saveSelect     = null;
                _saveSelectDoc  = null;
            }
        }

        private void EnterGame(int slotId)
        {
            TearDownSaveSelect();
            TearDownPreGameMenu();
            SaveManager.SetActiveSlot(slotId);
            InitializeGame();
        }

        // ============================================================
        //  Phase 2：游戏主体
        // ============================================================

        // 游戏内点"赛季模式"：销毁当前游戏后重新显示存档选择
        private void SwitchSave()
        {
            TearDownGame();
            StartCoroutine(ShowSaveSelectCoroutine());
        }

        private void TearDownGame()
        {
            if (_mainScreenHost != null)
            {
                Destroy(_mainScreenHost);
                _mainScreenHost = null;
                _mainScreen     = null;
            }
        }

        private void InitializeGame()
        {
            string fileName        = SaveManager.GetSlotFileName(SaveManager.ActiveSlotId);
            var databaseManager    = new DatabaseManager(fileName);
            var teamRepository     = new TeamRepository(databaseManager);
            var playerRepository   = new PlayerRepository(databaseManager);
            var profileRepository  = new SimulationProfileRepository(databaseManager);
            var seasonRepository   = new SeasonRepository(databaseManager);
            var developmentService = new PlayerDevelopmentService();
            var playerGenerator    = new PlayerGenerator();
            var freeAgencyService  = new FreeAgencyService(teamRepository, playerRepository);
            var draftService       = new DraftService(playerRepository, seasonRepository, playerGenerator);
            var seasonService      = new SeasonService(teamRepository, playerRepository,
                                                       profileRepository, seasonRepository,
                                                       developmentService, freeAgencyService, draftService);

            _mainScreenHost = new GameObject("MainScreenHost");
            DontDestroyOnLoad(_mainScreenHost);

            _mainScreen = _mainScreenHost.AddComponent<MainScreen>();
            _mainScreen.SetStartDirectlyAtSeason(true);
            _mainScreen.OnReturnToSaveSelect += SwitchSave;
            _mainScreen.Initialize(databaseManager, teamRepository, playerRepository,
                                   profileRepository, seasonRepository, seasonService);
        }

        // ============================================================
        //  工具方法
        // ============================================================

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static PanelSettings LoadOrCreatePanelSettings()
        {
            var ps = Resources.Load<PanelSettings>("UI/AppPanelSettings");
            if (ps != null) return ps;

            ps                    = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name               = "AppPanelSettings";
            ps.scaleMode          = PanelScaleMode.ScaleWithScreenSize;
            ps.screenMatchMode    = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.referenceResolution = new Vector2Int(1600, 900);
            ps.match              = 0.5f;
            ps.sortingOrder       = 10;
            var theme = Resources.Load<ThemeStyleSheet>("UI/Theme/AppTheme");
            if (theme != null) ps.themeStyleSheet = theme;
            return ps;
        }

        // ============================================================
        //  Android DB 模板复制
        // ============================================================

        private static IEnumerator CopyDatabaseFromStreamingAssets()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string PrefKey = "db_copied_version";
            var destPath         = Path.Combine(Application.persistentDataPath, DatabaseManager.DatabaseFileName);
            var srcUrl           = Path.Combine(Application.streamingAssetsPath, DatabaseManager.DatabaseFileName);
            var curVersion       = Application.version;

            bool needCopy = !File.Exists(destPath)
                         || UnityEngine.PlayerPrefs.GetString(PrefKey, "") != curVersion;

            if (needCopy)
            {
                using var req = UnityWebRequest.Get(srcUrl);
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    File.WriteAllBytes(destPath, req.downloadHandler.data);
                    UnityEngine.PlayerPrefs.SetString(PrefKey, curVersion);
                    UnityEngine.PlayerPrefs.Save();
                    Debug.Log($"[Database] 已从 StreamingAssets 复制数据库到 {destPath}（版本 {curVersion}）");
                }
                else
                {
                    Debug.LogError($"[Database] 复制数据库失败：{req.error}");
                }
            }
#else
            yield break;
#endif
        }
    }
}
