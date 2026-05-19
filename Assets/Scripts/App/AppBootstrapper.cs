using BasketballManager.Database;
using BasketballManager.Seasons;
using BasketballManager.UI.Screens;
using UnityEngine;

namespace BasketballManager.App
{
    public sealed class AppBootstrapper : MonoBehaviour
    {
        private static bool _created;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrapper()
        {
            if (_created || FindFirstObjectByType<AppBootstrapper>() != null)
            {
                return;
            }

            var bootstrapperObject = new GameObject("AppBootstrapper");
            bootstrapperObject.AddComponent<AppBootstrapper>();
            _created = true;
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);

            var databaseManager = new DatabaseManager();
            var teamRepository = new TeamRepository(databaseManager);
            var playerRepository = new PlayerRepository(databaseManager);
            var profileRepository = new SimulationProfileRepository(databaseManager);
            var seasonRepository = new SeasonRepository(databaseManager);
            var seasonService = new SeasonService(teamRepository, playerRepository, profileRepository, seasonRepository);

            var screen = GetComponent<MainScreen>();
            if (screen == null)
            {
                screen = gameObject.AddComponent<MainScreen>();
            }

            screen.Initialize(databaseManager, teamRepository, playerRepository, profileRepository, seasonRepository, seasonService);
        }
    }
}
