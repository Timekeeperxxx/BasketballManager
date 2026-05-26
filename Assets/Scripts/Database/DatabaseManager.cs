using System;
using System.IO;
using Mono.Data.Sqlite;
using UnityEngine;

namespace BasketballManager.Database
{
    public sealed class DatabaseManager
    {
        public const string DatabaseFileName = "game.db";

        private readonly string _streamingDatabasePath;
        private readonly string _persistentDatabasePath;
        private bool _initialized;

        public DatabaseManager(string fileName = DatabaseFileName)
        {
            // 模板始终来自 StreamingAssets/game.db；槽位文件名由 fileName 决定
            _streamingDatabasePath  = Path.Combine(UnityEngine.Application.streamingAssetsPath, DatabaseFileName);
            _persistentDatabasePath = Path.Combine(UnityEngine.Application.persistentDataPath,  fileName);
        }

        public string PersistentDatabasePath => _persistentDatabasePath;

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(UnityEngine.Application.persistentDataPath);
            Debug.Log($"[Database] Persistent path: {_persistentDatabasePath}");

            bool shouldCopyFromStreaming = !File.Exists(_persistentDatabasePath);

#if UNITY_EDITOR
            // In the Editor, the StreamingAssets DB is the source of truth for data
            // pipeline updates (build_unity_database.py / apply_real_stats_to_unity_database.py).
            // Refresh the persistent copy whenever StreamingAssets is newer so devs don't
            // have to manually delete persistentDataPath/game.db after running tools.
            if (!shouldCopyFromStreaming && File.Exists(_streamingDatabasePath))
            {
                var streamingMtime = File.GetLastWriteTimeUtc(_streamingDatabasePath);
                var persistentMtime = File.GetLastWriteTimeUtc(_persistentDatabasePath);
                if (streamingMtime > persistentMtime)
                {
                    Debug.Log($"[Database] StreamingAssets db is newer ({streamingMtime} > {persistentMtime}) — refreshing persistent copy.");
                    shouldCopyFromStreaming = true;
                }
            }
#endif

            if (shouldCopyFromStreaming)
            {
                if (File.Exists(_streamingDatabasePath))
                {
                    File.Copy(_streamingDatabasePath, _persistentDatabasePath, true);
                    Debug.Log($"[Database] Copied StreamingAssets database to persistent path: {_persistentDatabasePath}");
                }
                else
                {
                    Debug.LogWarning($"[Database] StreamingAssets database not found. Creating empty managed database at: {_persistentDatabasePath}");
                    CreateEmptyManagedDatabase();
                }
            }

            using var connection = CreateConnection();
            connection.Open();
            SetForeignKeys(connection, true);

            // 兼容旧 db：build_unity_database.py 生成的 db 没有 season_* 表，
            // 这里在不破坏 teams / players 数据的前提下按需补建。
            EnsureSeasonTables(connection);
            EnsurePlayoffColumns(connection);
            EnsurePlayoffTables(connection);
            EnsureTraitTables(connection);
            EnsureGrowthColumns(connection);
            EnsurePlayerIsCurrentColumn(connection);
            EnsurePlayerTendencyZoneColumns(connection);
            EnsureZoneStatsTables(connection);
            EnsurePlayerNamedViews(connection);
            EnsureContractColumns(connection);
            EnsureFreeAgencyTables(connection);
            EnsureInjuryColumn(connection);

            if (!HasManagedSchema(connection))
            {
                throw new InvalidOperationException(BuildSchemaMismatchMessage());
            }

            _initialized = true;
        }

        // 老 db 没有 players.is_current 列时按需补加，并用所属球队 is_current 回填。
        private static void EnsurePlayerIsCurrentColumn(SqliteConnection connection)
        {
            if (HasColumn(connection, "players", "is_current")) return;

            using var transaction = connection.BeginTransaction();
            ExecuteNonQuery(connection, transaction, "ALTER TABLE players ADD COLUMN is_current INTEGER NOT NULL DEFAULT 0;");
            ExecuteNonQuery(connection, transaction, @"
UPDATE players
SET is_current = COALESCE((SELECT is_current FROM teams WHERE teams.id = players.team_id), 0);");
            transaction.Commit();
            Debug.Log("[Database] 已为 players 表补加 is_current 列，并按所属球队回填初值。");
        }

        // 为 player_tendencies 按需补加 14 个投篮区域倾向列（旧 db 兼容）。
        private static void EnsurePlayerTendencyZoneColumns(SqliteConnection connection)
        {
            var cols = new (string col, int def)[]
            {
                ("zone_three_left_corner",  50),
                ("zone_three_right_corner", 50),
                ("zone_three_left_wing",    60),
                ("zone_three_right_wing",   60),
                ("zone_three_top_key",      70),
                ("zone_mid_left_corner",    40),
                ("zone_mid_right_corner",   40),
                ("zone_mid_left_elbow",     60),
                ("zone_mid_right_elbow",    60),
                ("zone_mid_top_key",        50),
                ("zone_close_left",         50),
                ("zone_close_center",       70),
                ("zone_close_right",        50),
            };

            using var transaction = connection.BeginTransaction();
            foreach (var (col, def) in cols)
            {
                if (!HasColumn(connection, "player_tendencies", col))
                    ExecuteNonQuery(connection, transaction,
                        $"ALTER TABLE player_tendencies ADD COLUMN {col} INTEGER DEFAULT {def};");
            }
            transaction.Commit();
        }

        // 按需创建投篮区域统计表（旧 db 兼容）。
        private static void EnsureZoneStatsTables(SqliteConnection connection)
        {
            using var tx = connection.BeginTransaction();
            ExecuteNonQuery(connection, tx, @"
CREATE TABLE IF NOT EXISTS game_zone_stats (
    game_id   INTEGER NOT NULL,
    player_id INTEGER NOT NULL,
    team_id   TEXT NOT NULL,
    zone      INTEGER NOT NULL,
    fgm       INTEGER NOT NULL DEFAULT 0,
    fga       INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (game_id, player_id, zone),
    FOREIGN KEY (game_id) REFERENCES season_games (id) ON DELETE CASCADE
);");
            ExecuteNonQuery(connection, tx, @"
CREATE TABLE IF NOT EXISTS season_zone_stats (
    season_id INTEGER NOT NULL,
    player_id INTEGER NOT NULL,
    team_id   TEXT NOT NULL,
    zone      INTEGER NOT NULL,
    fgm       INTEGER NOT NULL DEFAULT 0,
    fga       INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (season_id, player_id, zone),
    FOREIGN KEY (season_id) REFERENCES seasons (id) ON DELETE CASCADE
);");
            ExecuteNonQuery(connection, tx, @"
CREATE TABLE IF NOT EXISTS playoff_zone_stats (
    season_id INTEGER NOT NULL,
    player_id INTEGER NOT NULL,
    team_id   TEXT NOT NULL,
    zone      INTEGER NOT NULL,
    fgm       INTEGER NOT NULL DEFAULT 0,
    fga       INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (season_id, player_id, zone),
    FOREIGN KEY (season_id) REFERENCES seasons (id) ON DELETE CASCADE
);");
            tx.Commit();
        }

        private static void EnsureContractColumns(SqliteConnection connection)
        {
            using var tx = connection.BeginTransaction();
            if (!HasColumn(connection, "players", "contract_years"))
                ExecuteNonQuery(connection, tx, "ALTER TABLE players ADD COLUMN contract_years INTEGER NOT NULL DEFAULT 0;");
            if (!HasColumn(connection, "players", "contract_salary"))
                ExecuteNonQuery(connection, tx, "ALTER TABLE players ADD COLUMN contract_salary INTEGER NOT NULL DEFAULT 0;");
            tx.Commit();

            // 回填现有球员合同（仅 contract_years = 0 的真实球员）
            // 用 id % 3 散列使各球员合同年数不同（1/2/3 年各占约 1/3），
            // 保证每个赛季间歇期都有约 1/3 球员到期进入自由市场。
            using var tx2 = connection.BeginTransaction();
            ExecuteNonQuery(connection, tx2, @"
UPDATE players SET
    contract_years  = 1 + (id % 3),
    contract_salary = CASE
        WHEN overall >= 91 THEN 28
        WHEN overall >= 81 THEN 13
        WHEN overall >= 71 THEN 5
        ELSE 3 END
WHERE contract_years = 0
  AND team_id NOT IN ('__FA__', '__DRAFT_POOL__');");
            tx2.Commit();
        }

        private static void EnsureFreeAgencyTables(SqliteConnection connection)
        {
            using var tx = connection.BeginTransaction();

            // 虚拟球队（FK 锚点，用于自由球员和选秀池）
            // teams 表的 abbreviation 列可能不存在于旧 schema，先尝试加
            if (!HasColumn(connection, "teams", "abbreviation"))
                ExecuteNonQuery(connection, tx, "ALTER TABLE teams ADD COLUMN abbreviation TEXT NOT NULL DEFAULT '';");

            ExecuteNonQuery(connection, tx, @"
INSERT OR IGNORE INTO teams (id, name, city, era, is_current, abbreviation)
VALUES ('__FA__',         '自由球员', '', 0, 0, 'FA'),
       ('__DRAFT_POOL__', '选秀池',   '', 0, 0, 'DP');");

            ExecuteNonQuery(connection, tx, @"
CREATE TABLE IF NOT EXISTS free_agents (
    player_id   INTEGER PRIMARY KEY,
    asking_salary INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (player_id) REFERENCES players (id) ON DELETE CASCADE
);");

            ExecuteNonQuery(connection, tx, @"
CREATE TABLE IF NOT EXISTS draft_class (
    player_id   INTEGER PRIMARY KEY,
    draft_year  INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY (player_id) REFERENCES players (id) ON DELETE CASCADE
);");
            tx.Commit();
        }

        private static void EnsureInjuryColumn(SqliteConnection connection)
        {
            if (HasColumn(connection, "players", "injury_games_remaining")) return;
            using var tx = connection.BeginTransaction();
            ExecuteNonQuery(connection, tx,
                "ALTER TABLE players ADD COLUMN injury_games_remaining INTEGER NOT NULL DEFAULT 0;");
            tx.Commit();
        }

        // 确保 player_attributes_named / player_tendencies_named 视图 + INSTEAD OF UPDATE 触发器存在。
        private static void EnsurePlayerNamedViews(SqliteConnection connection)
        {
            using var transaction = connection.BeginTransaction();
            ExecuteNonQuery(connection, transaction, @"
CREATE VIEW IF NOT EXISTS player_attributes_named AS
SELECT p.id AS player_id, p.first_name, p.last_name, p.team_id,
       a.two_point, a.three_point, a.layup, a.close_shot, a.post_scoring, a.free_throw,
       a.passing, a.ball_handle, a.drive, a.draw_foul, a.offensive_consistency,
       a.perimeter_defense, a.interior_defense, a.steal, a.block,
       a.offensive_rebound, a.defensive_rebound, a.defensive_consistency,
       a.speed, a.strength, a.stamina
FROM player_attributes a
JOIN players p ON p.id = a.player_id;");
            ExecuteNonQuery(connection, transaction, @"
CREATE VIEW IF NOT EXISTS player_tendencies_named AS
SELECT p.id AS player_id, p.first_name, p.last_name, p.team_id,
       t.shot_tendency, t.three_tendency, t.two_point_tendency, t.drive_tendency,
       t.post_tendency, t.close_shot_tendency, t.pass_tendency, t.draw_foul_tendency,
       t.steal_tendency, t.block_tendency, t.foul_tendency, t.help_defense_tendency,
       t.offensive_rebound_tendency, t.defensive_rebound_tendency
FROM player_tendencies t
JOIN players p ON p.id = t.player_id;");
            ExecuteNonQuery(connection, transaction, @"
CREATE TRIGGER IF NOT EXISTS trg_attr_named_update
INSTEAD OF UPDATE ON player_attributes_named
BEGIN
    UPDATE player_attributes SET
        two_point = NEW.two_point, three_point = NEW.three_point,
        layup = NEW.layup, close_shot = NEW.close_shot, post_scoring = NEW.post_scoring,
        free_throw = NEW.free_throw, passing = NEW.passing, ball_handle = NEW.ball_handle,
        drive = NEW.drive, draw_foul = NEW.draw_foul, offensive_consistency = NEW.offensive_consistency,
        perimeter_defense = NEW.perimeter_defense, interior_defense = NEW.interior_defense,
        steal = NEW.steal, block = NEW.block,
        offensive_rebound = NEW.offensive_rebound, defensive_rebound = NEW.defensive_rebound,
        defensive_consistency = NEW.defensive_consistency,
        speed = NEW.speed, strength = NEW.strength, stamina = NEW.stamina
    WHERE player_id = OLD.player_id;
    UPDATE players SET first_name = NEW.first_name, last_name = NEW.last_name
    WHERE id = OLD.player_id;
END;");
            ExecuteNonQuery(connection, transaction, @"
CREATE TRIGGER IF NOT EXISTS trg_tend_named_update
INSTEAD OF UPDATE ON player_tendencies_named
BEGIN
    UPDATE player_tendencies SET
        shot_tendency = NEW.shot_tendency, three_tendency = NEW.three_tendency,
        two_point_tendency = NEW.two_point_tendency, drive_tendency = NEW.drive_tendency,
        post_tendency = NEW.post_tendency, close_shot_tendency = NEW.close_shot_tendency,
        pass_tendency = NEW.pass_tendency, draw_foul_tendency = NEW.draw_foul_tendency,
        steal_tendency = NEW.steal_tendency, block_tendency = NEW.block_tendency,
        foul_tendency = NEW.foul_tendency, help_defense_tendency = NEW.help_defense_tendency,
        offensive_rebound_tendency = NEW.offensive_rebound_tendency,
        defensive_rebound_tendency = NEW.defensive_rebound_tendency
    WHERE player_id = OLD.player_id;
    UPDATE players SET first_name = NEW.first_name, last_name = NEW.last_name
    WHERE id = OLD.player_id;
END;");
            transaction.Commit();
        }

        // 老 db 没有赛季相关表时按需补建（保留现有 teams / players 数据）。
        private static void EnsureSeasonTables(SqliteConnection connection)
        {
            bool needSeasons = !TableExists(connection, "seasons");
            bool needSeasonTeams = !TableExists(connection, "season_teams");
            bool needSeasonGames = !TableExists(connection, "season_games");
            bool needSeasonPlayerStats = !TableExists(connection, "season_player_stats");
            bool needGamePlayerStats = !TableExists(connection, "game_player_stats");

            if (!needSeasons && !needSeasonTeams && !needSeasonGames && !needSeasonPlayerStats && !needGamePlayerStats) return;

            using var transaction = connection.BeginTransaction();

            if (needSeasons)
            {
                ExecuteNonQuery(connection, transaction, @"
CREATE TABLE seasons (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'IN_PROGRESS',
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);");
            }

            if (needSeasonTeams)
            {
                ExecuteNonQuery(connection, transaction, @"
CREATE TABLE season_teams (
    season_id INTEGER NOT NULL,
    team_id TEXT NOT NULL,
    wins INTEGER NOT NULL DEFAULT 0,
    losses INTEGER NOT NULL DEFAULT 0,
    points_for INTEGER NOT NULL DEFAULT 0,
    points_against INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (season_id, team_id),
    FOREIGN KEY (season_id) REFERENCES seasons (id) ON UPDATE CASCADE ON DELETE CASCADE,
    FOREIGN KEY (team_id) REFERENCES teams (id) ON UPDATE CASCADE ON DELETE CASCADE
);");
                ExecuteNonQuery(connection, transaction, "CREATE INDEX IF NOT EXISTS idx_season_teams_season ON season_teams (season_id);");
            }

            if (needSeasonGames)
            {
                ExecuteNonQuery(connection, transaction, @"
CREATE TABLE season_games (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    season_id INTEGER NOT NULL,
    day INTEGER NOT NULL,
    home_team_id TEXT NOT NULL,
    away_team_id TEXT NOT NULL,
    home_score INTEGER NOT NULL DEFAULT 0,
    away_score INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'SCHEDULED',
    winner_team_id TEXT,
    played_at TEXT,
    FOREIGN KEY (season_id) REFERENCES seasons (id) ON UPDATE CASCADE ON DELETE CASCADE,
    FOREIGN KEY (home_team_id) REFERENCES teams (id) ON UPDATE CASCADE ON DELETE CASCADE,
    FOREIGN KEY (away_team_id) REFERENCES teams (id) ON UPDATE CASCADE ON DELETE CASCADE
);");
                ExecuteNonQuery(connection, transaction, "CREATE INDEX IF NOT EXISTS idx_season_games_season_day ON season_games (season_id, day);");
                ExecuteNonQuery(connection, transaction, "CREATE INDEX IF NOT EXISTS idx_season_games_status ON season_games (season_id, status);");
            }

            if (needSeasonPlayerStats)
            {
                ExecuteNonQuery(connection, transaction, @"
CREATE TABLE season_player_stats (
    season_id INTEGER NOT NULL,
    player_id INTEGER NOT NULL,
    team_id TEXT NOT NULL,
    games_played INTEGER NOT NULL DEFAULT 0,
    minutes INTEGER NOT NULL DEFAULT 0,
    points INTEGER NOT NULL DEFAULT 0,
    rebounds INTEGER NOT NULL DEFAULT 0,
    offensive_rebounds INTEGER NOT NULL DEFAULT 0,
    assists INTEGER NOT NULL DEFAULT 0,
    steals INTEGER NOT NULL DEFAULT 0,
    blocks INTEGER NOT NULL DEFAULT 0,
    turnovers INTEGER NOT NULL DEFAULT 0,
    fouls INTEGER NOT NULL DEFAULT 0,
    field_goals_made INTEGER NOT NULL DEFAULT 0,
    field_goals_attempted INTEGER NOT NULL DEFAULT 0,
    three_pointers_made INTEGER NOT NULL DEFAULT 0,
    three_pointers_attempted INTEGER NOT NULL DEFAULT 0,
    free_throws_made INTEGER NOT NULL DEFAULT 0,
    free_throws_attempted INTEGER NOT NULL DEFAULT 0,
    plus_minus INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (season_id, player_id),
    FOREIGN KEY (season_id) REFERENCES seasons (id) ON UPDATE CASCADE ON DELETE CASCADE,
    FOREIGN KEY (player_id) REFERENCES players (id) ON UPDATE CASCADE ON DELETE CASCADE
);");
                ExecuteNonQuery(connection, transaction, "CREATE INDEX IF NOT EXISTS idx_season_player_stats_team ON season_player_stats (season_id, team_id);");
            }

            if (needGamePlayerStats)
            {
                ExecuteNonQuery(connection, transaction, @"
CREATE TABLE game_player_stats (
    game_id INTEGER NOT NULL,
    player_id INTEGER NOT NULL,
    team_id TEXT NOT NULL,
    player_name TEXT NOT NULL DEFAULT '',
    position TEXT NOT NULL DEFAULT '',
    minutes INTEGER NOT NULL DEFAULT 0,
    points INTEGER NOT NULL DEFAULT 0,
    rebounds INTEGER NOT NULL DEFAULT 0,
    offensive_rebounds INTEGER NOT NULL DEFAULT 0,
    assists INTEGER NOT NULL DEFAULT 0,
    steals INTEGER NOT NULL DEFAULT 0,
    blocks INTEGER NOT NULL DEFAULT 0,
    turnovers INTEGER NOT NULL DEFAULT 0,
    fouls INTEGER NOT NULL DEFAULT 0,
    field_goals_made INTEGER NOT NULL DEFAULT 0,
    field_goals_attempted INTEGER NOT NULL DEFAULT 0,
    three_pointers_made INTEGER NOT NULL DEFAULT 0,
    three_pointers_attempted INTEGER NOT NULL DEFAULT 0,
    free_throws_made INTEGER NOT NULL DEFAULT 0,
    free_throws_attempted INTEGER NOT NULL DEFAULT 0,
    plus_minus INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (game_id, player_id),
    FOREIGN KEY (game_id) REFERENCES season_games (id) ON UPDATE CASCADE ON DELETE CASCADE
);");
                ExecuteNonQuery(connection, transaction, "CREATE INDEX IF NOT EXISTS idx_game_player_stats_team ON game_player_stats (game_id, team_id);");
            }

            transaction.Commit();
            Debug.Log("[Database] Season tables auto-created on existing db (没有破坏现有 teams / players 数据).");
        }

        private static void EnsurePlayoffColumns(SqliteConnection connection)
        {
            using var transaction = connection.BeginTransaction();
            if (!HasColumn(connection, "seasons", "phase"))
                ExecuteNonQuery(connection, transaction, "ALTER TABLE seasons ADD COLUMN phase TEXT NOT NULL DEFAULT 'REGULAR';");
            if (!HasColumn(connection, "season_games", "phase"))
                ExecuteNonQuery(connection, transaction, "ALTER TABLE season_games ADD COLUMN phase TEXT NOT NULL DEFAULT 'REGULAR';");
            if (!HasColumn(connection, "season_games", "series_id"))
                ExecuteNonQuery(connection, transaction, "ALTER TABLE season_games ADD COLUMN series_id INTEGER;");
            transaction.Commit();
        }

        private static void EnsurePlayoffTables(SqliteConnection connection)
        {
            using var transaction = connection.BeginTransaction();
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE IF NOT EXISTS playoff_series (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    season_id INTEGER NOT NULL,
    round INTEGER NOT NULL,
    seed1 INTEGER NOT NULL,
    seed2 INTEGER NOT NULL,
    team1_id TEXT NOT NULL,
    team2_id TEXT NOT NULL,
    team1_wins INTEGER NOT NULL DEFAULT 0,
    team2_wins INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'IN_PROGRESS',
    winner_team_id TEXT,
    FOREIGN KEY (season_id) REFERENCES seasons (id) ON UPDATE CASCADE ON DELETE CASCADE
);");
            ExecuteNonQuery(connection, transaction, "CREATE INDEX IF NOT EXISTS idx_playoff_series_season_round ON playoff_series (season_id, round);");
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE IF NOT EXISTS playoff_player_stats (
    season_id INTEGER NOT NULL,
    player_id INTEGER NOT NULL,
    team_id TEXT NOT NULL,
    games_played INTEGER NOT NULL DEFAULT 0,
    minutes INTEGER NOT NULL DEFAULT 0,
    points INTEGER NOT NULL DEFAULT 0,
    rebounds INTEGER NOT NULL DEFAULT 0,
    offensive_rebounds INTEGER NOT NULL DEFAULT 0,
    assists INTEGER NOT NULL DEFAULT 0,
    steals INTEGER NOT NULL DEFAULT 0,
    blocks INTEGER NOT NULL DEFAULT 0,
    turnovers INTEGER NOT NULL DEFAULT 0,
    fouls INTEGER NOT NULL DEFAULT 0,
    field_goals_made INTEGER NOT NULL DEFAULT 0,
    field_goals_attempted INTEGER NOT NULL DEFAULT 0,
    three_pointers_made INTEGER NOT NULL DEFAULT 0,
    three_pointers_attempted INTEGER NOT NULL DEFAULT 0,
    free_throws_made INTEGER NOT NULL DEFAULT 0,
    free_throws_attempted INTEGER NOT NULL DEFAULT 0,
    plus_minus INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (season_id, player_id),
    FOREIGN KEY (season_id) REFERENCES seasons (id) ON UPDATE CASCADE ON DELETE CASCADE,
    FOREIGN KEY (player_id) REFERENCES players (id) ON UPDATE CASCADE ON DELETE CASCADE
);");
            ExecuteNonQuery(connection, transaction, "CREATE INDEX IF NOT EXISTS idx_playoff_player_stats_team ON playoff_player_stats (season_id, team_id);");
            transaction.Commit();
        }

        private static void EnsureTraitTables(SqliteConnection connection)
        {
            using var transaction = connection.BeginTransaction();
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE IF NOT EXISTS traits (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name_key TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    category TEXT NOT NULL DEFAULT '',
    description_1 TEXT NOT NULL DEFAULT '',
    description_2 TEXT NOT NULL DEFAULT '',
    description_3 TEXT NOT NULL DEFAULT ''
);");
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE IF NOT EXISTS player_traits (
    player_id INTEGER NOT NULL,
    trait_id INTEGER NOT NULL,
    star_level INTEGER NOT NULL DEFAULT 1,
    PRIMARY KEY (player_id, trait_id),
    FOREIGN KEY (player_id) REFERENCES players (id) ON UPDATE CASCADE ON DELETE CASCADE,
    FOREIGN KEY (trait_id) REFERENCES traits (id) ON UPDATE CASCADE ON DELETE CASCADE
);");
            ExecuteNonQuery(connection, transaction, @"
INSERT OR IGNORE INTO traits (name_key, display_name, category, description_1, description_2, description_3)
VALUES
('clutch_performer', '关键先生', 'Scoring',
 '第四节剩余5分钟内分差<5分时，投篮命中率+1.5%',
 '第四节剩余5分钟内分差<5分时，投篮命中率+2.5%',
 '第四节剩余5分钟内分差<5分时，投篮命中率+4%'),
('catch_and_shoot', '接球即投', 'Shooting',
 '接到队友传球后不运球直接出手，投篮命中率+1%',
 '接到队友传球后不运球直接出手，投篮命中率+2%',
 '接到队友传球后不运球直接出手，投篮命中率+3%'),
('needle_threader', '传切利器', 'Playmaking',
 '在拥挤区域也能精准送出传球，个人失误率降低0.8%',
 '在拥挤区域也能精准送出传球，个人失误率降低1.5%',
 '在拥挤区域也能精准送出传球，个人失误率降低2%'),
('clamps', '封锁专家', 'Defense',
 '防守对手运球出手时横向移动迅捷，对手有效防守属性×1.12，迫使对手命中率下降',
 '防守对手运球出手时横向移动迅捷，对手有效防守属性×1.22，迫使对手命中率下降',
 '防守对手运球出手时横向移动迅捷，对手有效防守属性×1.32，迫使对手命中率下降'),
('intimidator', '硬汉防守', 'Defense',
 '强壮身躯令对手心理受压，防守篮下/低位/中距离时对手命中率上限降低2%',
 '强壮身躯令对手心理受压，防守篮下/低位/中距离时对手命中率上限降低3%',
 '强壮身躯令对手心理受压，防守篮下/低位/中距离时对手命中率上限降低4%'),
('volume_shooter', '量产型', 'Shooting',
 '出手越多越进入状态：本场出手达10次后，每多出手1次命中率+0.1%，上限+1%',
 '出手越多越进入状态：本场出手达8次后，每多出手1次命中率+0.2%，上限+2%',
 '出手越多越进入状态：本场出手达6次后，每多出手1次命中率+0.3%，上限+3%'),
('comeback_kid', '慢热型', 'Scoring',
 '球队落后5分以上时激发斗志，个人投篮命中率+1%',
 '球队落后5分以上时+1.5%，落后15分以上时+2.5%，越难越勇',
 '球队落后5分以上时+2%，落后15分以上时+3.5%，越难越勇');");
            transaction.Commit();
        }

        private static void EnsureGrowthColumns(SqliteConnection connection)
        {
            using var tx = connection.BeginTransaction();
            var addCols = new (string col, string def)[]
            {
                ("potential_min",  "0"),
                ("potential_max",  "0"),
                ("peak_age_start", "0"),
                ("peak_age_end",   "0"),
            };
            foreach (var (col, def) in addCols)
            {
                if (!HasColumn(connection, "players", col))
                    ExecuteNonQuery(connection, tx,
                        $"ALTER TABLE players ADD COLUMN {col} INTEGER NOT NULL DEFAULT {def};");
            }
            if (!HasColumn(connection, "seasons", "season_number"))
                ExecuteNonQuery(connection, tx,
                    "ALTER TABLE seasons ADD COLUMN season_number INTEGER NOT NULL DEFAULT 1;");
            tx.Commit();

            // 对默认值为 0 的球员做一次性自动校准
            using var tx2 = connection.BeginTransaction();
            ExecuteNonQuery(connection, tx2, @"
UPDATE players SET
  potential_min   = CASE WHEN age <= 22 THEN MAX(50, overall - 5)
                         WHEN age <= 28 THEN MAX(50, overall - 3)
                         ELSE MAX(40, overall - 8) END,
  potential_max   = CASE WHEN age <= 22 THEN MIN(99, overall + 20)
                         WHEN age <= 28 THEN MIN(99, overall + 8)
                         ELSE MIN(99, overall + 2) END,
  peak_age_start  = CASE WHEN age <= 22 THEN 25
                         WHEN age <= 28 THEN age
                         ELSE MAX(age - 2, 25) END,
  peak_age_end    = CASE WHEN age <= 22 THEN 30
                         WHEN age <= 28 THEN MIN(age + 4, 34)
                         ELSE MIN(age + 1, 36) END
WHERE potential_min = 0 AND potential_max = 0;");
            tx2.Commit();
        }

        public SqliteConnection OpenConnection()
        {
            Initialize();

            var connection = CreateConnection();
            connection.Open();
            SetForeignKeys(connection, true);
            return connection;
        }

        private void CreateEmptyManagedDatabase()
        {
            using var connection = CreateConnection();
            connection.Open();
            RebuildManagedSchema(connection);
        }

        private SqliteConnection CreateConnection()
        {
            return new SqliteConnection($"URI=file:{_persistentDatabasePath}");
        }

        private string BuildSchemaMismatchMessage()
        {
            return $"当前 game.db schema 与代码不匹配。请删除旧数据库后重新启动，或重新运行 tools/build_unity_database.py。路径：{_persistentDatabasePath}";
        }

        private static void SetForeignKeys(SqliteConnection connection, bool enabled)
        {
            using var command = connection.CreateCommand();
            command.CommandText = enabled ? "PRAGMA foreign_keys = ON;" : "PRAGMA foreign_keys = OFF;";
            command.ExecuteNonQuery();
        }

        private static bool HasManagedSchema(SqliteConnection connection)
        {
            return TableExists(connection, "teams")
                && TableExists(connection, "players")
                && TableExists(connection, "player_attributes")
                && TableExists(connection, "player_tendencies")
                && TableExists(connection, "player_simulation_profiles")
                && TableExists(connection, "seasons")
                && TableExists(connection, "season_teams")
                && TableExists(connection, "season_games")
                && TableExists(connection, "season_player_stats")
                && HasColumn(connection, "teams", "id")
                && HasColumn(connection, "teams", "era")
                && HasColumn(connection, "teams", "is_current")
                && HasColumn(connection, "players", "team_id")
                && HasColumn(connection, "players", "first_name")
                && HasColumn(connection, "players", "last_name")
                && HasColumn(connection, "players", "name_order")
                && HasColumn(connection, "players", "secondary_position")
                && HasColumn(connection, "players", "is_current")
                && !HasColumn(connection, "players", "display_name")
                && !HasColumn(connection, "players", "nationality")
                && !HasColumn(connection, "players", "region_type")
                && HasColumn(connection, "player_attributes", "draw_foul")
                && HasColumn(connection, "player_tendencies", "draw_foul_tendency")
                && HasColumn(connection, "player_simulation_profiles", "source_mpg");
        }

        private static void RebuildManagedSchema(SqliteConnection connection)
        {
            SetForeignKeys(connection, false);

            using var transaction = connection.BeginTransaction();
            // 先删依赖表（被引用方在前）。
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS player_traits;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS traits;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS playoff_zone_stats;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS season_zone_stats;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS game_zone_stats;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS playoff_player_stats;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS playoff_series;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS game_player_stats;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS season_player_stats;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS season_games;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS season_teams;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS seasons;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS player_simulation_profiles;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS player_tendencies;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS player_attributes;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS players;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS teams;");

            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE teams (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    city TEXT DEFAULT '',
    era INTEGER DEFAULT 0,
    is_current INTEGER DEFAULT 0,
    abbreviation TEXT NOT NULL DEFAULT ''
);");
            ExecuteNonQuery(connection, transaction, @"
INSERT INTO teams (id, name, city, era, is_current, abbreviation)
VALUES ('__FA__', '自由球员', '', 0, 0, 'FA'),
       ('__DRAFT_POOL__', '选秀池', '', 0, 0, 'DP');");

            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE players (
    id INTEGER PRIMARY KEY,
    team_id TEXT NOT NULL,
    first_name TEXT NOT NULL,
    last_name TEXT NOT NULL,
    name_order TEXT NOT NULL DEFAULT 'WESTERN',
    position TEXT NOT NULL,
    secondary_position TEXT,
    height_cm INTEGER NOT NULL,
    weight_kg INTEGER NOT NULL,
    age INTEGER NOT NULL,
    jersey_number INTEGER,
    overall INTEGER NOT NULL DEFAULT 70,
    is_current INTEGER NOT NULL DEFAULT 0,
    contract_years INTEGER NOT NULL DEFAULT 0,
    contract_salary INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (team_id) REFERENCES teams (id) ON UPDATE CASCADE ON DELETE CASCADE
);");

            ExecuteNonQuery(connection, transaction, "CREATE INDEX idx_players_team_id ON players (team_id);");

            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE player_attributes (
    player_id INTEGER PRIMARY KEY,
    two_point INTEGER DEFAULT 60,
    three_point INTEGER DEFAULT 60,
    layup INTEGER DEFAULT 60,
    close_shot INTEGER DEFAULT 60,
    post_scoring INTEGER DEFAULT 60,
    free_throw INTEGER DEFAULT 60,
    passing INTEGER DEFAULT 60,
    ball_handle INTEGER DEFAULT 60,
    drive INTEGER DEFAULT 60,
    draw_foul INTEGER DEFAULT 60,
    offensive_consistency INTEGER DEFAULT 70,
    perimeter_defense INTEGER DEFAULT 60,
    interior_defense INTEGER DEFAULT 60,
    steal INTEGER DEFAULT 60,
    block INTEGER DEFAULT 60,
    offensive_rebound INTEGER DEFAULT 60,
    defensive_rebound INTEGER DEFAULT 60,
    defensive_consistency INTEGER DEFAULT 70,
    speed INTEGER DEFAULT 60,
    strength INTEGER DEFAULT 60,
    stamina INTEGER DEFAULT 60,
    FOREIGN KEY (player_id) REFERENCES players (id) ON UPDATE CASCADE ON DELETE CASCADE
);");

            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE player_tendencies (
    player_id INTEGER PRIMARY KEY,
    shot_tendency INTEGER DEFAULT 60,
    three_tendency INTEGER DEFAULT 60,
    two_point_tendency INTEGER DEFAULT 60,
    drive_tendency INTEGER DEFAULT 60,
    post_tendency INTEGER DEFAULT 60,
    close_shot_tendency INTEGER DEFAULT 60,
    pass_tendency INTEGER DEFAULT 60,
    draw_foul_tendency INTEGER DEFAULT 60,
    steal_tendency INTEGER DEFAULT 60,
    block_tendency INTEGER DEFAULT 60,
    foul_tendency INTEGER DEFAULT 60,
    help_defense_tendency INTEGER DEFAULT 60,
    offensive_rebound_tendency INTEGER DEFAULT 60,
    defensive_rebound_tendency INTEGER DEFAULT 60,
    zone_three_left_corner INTEGER DEFAULT 50,
    zone_three_right_corner INTEGER DEFAULT 50,
    zone_three_left_wing INTEGER DEFAULT 60,
    zone_three_right_wing INTEGER DEFAULT 60,
    zone_three_top_key INTEGER DEFAULT 70,
    zone_mid_left_corner INTEGER DEFAULT 40,
    zone_mid_right_corner INTEGER DEFAULT 40,
    zone_mid_left_elbow INTEGER DEFAULT 60,
    zone_mid_right_elbow INTEGER DEFAULT 60,
    zone_mid_top_key INTEGER DEFAULT 50,
    zone_close_left INTEGER DEFAULT 50,
    zone_close_center INTEGER DEFAULT 70,
    zone_close_right INTEGER DEFAULT 50,
    FOREIGN KEY (player_id) REFERENCES players (id) ON UPDATE CASCADE ON DELETE CASCADE
);");

            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE player_simulation_profiles (
    player_id INTEGER PRIMARY KEY,
    team_id TEXT NOT NULL,
    source_mpg REAL NOT NULL,
    rotation_role TEXT NOT NULL,
    minute_floor REAL NOT NULL,
    minute_ceiling REAL NOT NULL,
    FOREIGN KEY (player_id) REFERENCES players (id) ON UPDATE CASCADE ON DELETE CASCADE
);");

            // 赛季容器表：name + 创建时间 + 状态 + 阶段。
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE seasons (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'IN_PROGRESS',
    phase TEXT NOT NULL DEFAULT 'REGULAR',
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);");

            // 赛季参赛队伍 + 实时累计战绩 / 得失分。
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE season_teams (
    season_id INTEGER NOT NULL,
    team_id TEXT NOT NULL,
    wins INTEGER NOT NULL DEFAULT 0,
    losses INTEGER NOT NULL DEFAULT 0,
    points_for INTEGER NOT NULL DEFAULT 0,
    points_against INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (season_id, team_id),
    FOREIGN KEY (season_id) REFERENCES seasons (id) ON UPDATE CASCADE ON DELETE CASCADE,
    FOREIGN KEY (team_id) REFERENCES teams (id) ON UPDATE CASCADE ON DELETE CASCADE
);");

            ExecuteNonQuery(connection, transaction, "CREATE INDEX idx_season_teams_season ON season_teams (season_id);");

            // 赛程表：每场比赛一行，含主客队、day、状态、最终比分、阶段与系列赛id。
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE season_games (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    season_id INTEGER NOT NULL,
    day INTEGER NOT NULL,
    home_team_id TEXT NOT NULL,
    away_team_id TEXT NOT NULL,
    home_score INTEGER NOT NULL DEFAULT 0,
    away_score INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'SCHEDULED',
    winner_team_id TEXT,
    played_at TEXT,
    phase TEXT NOT NULL DEFAULT 'REGULAR',
    series_id INTEGER,
    FOREIGN KEY (season_id) REFERENCES seasons (id) ON UPDATE CASCADE ON DELETE CASCADE,
    FOREIGN KEY (home_team_id) REFERENCES teams (id) ON UPDATE CASCADE ON DELETE CASCADE,
    FOREIGN KEY (away_team_id) REFERENCES teams (id) ON UPDATE CASCADE ON DELETE CASCADE
);");

            ExecuteNonQuery(connection, transaction, "CREATE INDEX idx_season_games_season_day ON season_games (season_id, day);");
            ExecuteNonQuery(connection, transaction, "CREATE INDEX idx_season_games_status ON season_games (season_id, status);");

            // 赛季球员累计数据（按 season × player 聚合）。
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE season_player_stats (
    season_id INTEGER NOT NULL,
    player_id INTEGER NOT NULL,
    team_id TEXT NOT NULL,
    games_played INTEGER NOT NULL DEFAULT 0,
    minutes INTEGER NOT NULL DEFAULT 0,
    points INTEGER NOT NULL DEFAULT 0,
    rebounds INTEGER NOT NULL DEFAULT 0,
    offensive_rebounds INTEGER NOT NULL DEFAULT 0,
    assists INTEGER NOT NULL DEFAULT 0,
    steals INTEGER NOT NULL DEFAULT 0,
    blocks INTEGER NOT NULL DEFAULT 0,
    turnovers INTEGER NOT NULL DEFAULT 0,
    fouls INTEGER NOT NULL DEFAULT 0,
    field_goals_made INTEGER NOT NULL DEFAULT 0,
    field_goals_attempted INTEGER NOT NULL DEFAULT 0,
    three_pointers_made INTEGER NOT NULL DEFAULT 0,
    three_pointers_attempted INTEGER NOT NULL DEFAULT 0,
    free_throws_made INTEGER NOT NULL DEFAULT 0,
    free_throws_attempted INTEGER NOT NULL DEFAULT 0,
    plus_minus INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (season_id, player_id),
    FOREIGN KEY (season_id) REFERENCES seasons (id) ON UPDATE CASCADE ON DELETE CASCADE,
    FOREIGN KEY (player_id) REFERENCES players (id) ON UPDATE CASCADE ON DELETE CASCADE
);");

            ExecuteNonQuery(connection, transaction, "CREATE INDEX idx_season_player_stats_team ON season_player_stats (season_id, team_id);");

            // 每场比赛的球员数据（per-game box score）。
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE game_player_stats (
    game_id INTEGER NOT NULL,
    player_id INTEGER NOT NULL,
    team_id TEXT NOT NULL,
    player_name TEXT NOT NULL DEFAULT '',
    position TEXT NOT NULL DEFAULT '',
    minutes INTEGER NOT NULL DEFAULT 0,
    points INTEGER NOT NULL DEFAULT 0,
    rebounds INTEGER NOT NULL DEFAULT 0,
    offensive_rebounds INTEGER NOT NULL DEFAULT 0,
    assists INTEGER NOT NULL DEFAULT 0,
    steals INTEGER NOT NULL DEFAULT 0,
    blocks INTEGER NOT NULL DEFAULT 0,
    turnovers INTEGER NOT NULL DEFAULT 0,
    fouls INTEGER NOT NULL DEFAULT 0,
    field_goals_made INTEGER NOT NULL DEFAULT 0,
    field_goals_attempted INTEGER NOT NULL DEFAULT 0,
    three_pointers_made INTEGER NOT NULL DEFAULT 0,
    three_pointers_attempted INTEGER NOT NULL DEFAULT 0,
    free_throws_made INTEGER NOT NULL DEFAULT 0,
    free_throws_attempted INTEGER NOT NULL DEFAULT 0,
    plus_minus INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (game_id, player_id),
    FOREIGN KEY (game_id) REFERENCES season_games (id) ON UPDATE CASCADE ON DELETE CASCADE
);");
            ExecuteNonQuery(connection, transaction, "CREATE INDEX idx_game_player_stats_team ON game_player_stats (game_id, team_id);");

            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE playoff_series (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    season_id INTEGER NOT NULL,
    round INTEGER NOT NULL,
    seed1 INTEGER NOT NULL,
    seed2 INTEGER NOT NULL,
    team1_id TEXT NOT NULL,
    team2_id TEXT NOT NULL,
    team1_wins INTEGER NOT NULL DEFAULT 0,
    team2_wins INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'IN_PROGRESS',
    winner_team_id TEXT,
    FOREIGN KEY (season_id) REFERENCES seasons (id) ON UPDATE CASCADE ON DELETE CASCADE
);");
            ExecuteNonQuery(connection, transaction, "CREATE INDEX idx_playoff_series_season_round ON playoff_series (season_id, round);");

            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE playoff_player_stats (
    season_id INTEGER NOT NULL,
    player_id INTEGER NOT NULL,
    team_id TEXT NOT NULL,
    games_played INTEGER NOT NULL DEFAULT 0,
    minutes INTEGER NOT NULL DEFAULT 0,
    points INTEGER NOT NULL DEFAULT 0,
    rebounds INTEGER NOT NULL DEFAULT 0,
    offensive_rebounds INTEGER NOT NULL DEFAULT 0,
    assists INTEGER NOT NULL DEFAULT 0,
    steals INTEGER NOT NULL DEFAULT 0,
    blocks INTEGER NOT NULL DEFAULT 0,
    turnovers INTEGER NOT NULL DEFAULT 0,
    fouls INTEGER NOT NULL DEFAULT 0,
    field_goals_made INTEGER NOT NULL DEFAULT 0,
    field_goals_attempted INTEGER NOT NULL DEFAULT 0,
    three_pointers_made INTEGER NOT NULL DEFAULT 0,
    three_pointers_attempted INTEGER NOT NULL DEFAULT 0,
    free_throws_made INTEGER NOT NULL DEFAULT 0,
    free_throws_attempted INTEGER NOT NULL DEFAULT 0,
    plus_minus INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (season_id, player_id),
    FOREIGN KEY (season_id) REFERENCES seasons (id) ON UPDATE CASCADE ON DELETE CASCADE,
    FOREIGN KEY (player_id) REFERENCES players (id) ON UPDATE CASCADE ON DELETE CASCADE
);");
            ExecuteNonQuery(connection, transaction, "CREATE INDEX idx_playoff_player_stats_team ON playoff_player_stats (season_id, team_id);");

            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE game_zone_stats (
    game_id   INTEGER NOT NULL,
    player_id INTEGER NOT NULL,
    team_id   TEXT NOT NULL,
    zone      INTEGER NOT NULL,
    fgm       INTEGER NOT NULL DEFAULT 0,
    fga       INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (game_id, player_id, zone),
    FOREIGN KEY (game_id) REFERENCES season_games (id) ON DELETE CASCADE
);");
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE season_zone_stats (
    season_id INTEGER NOT NULL,
    player_id INTEGER NOT NULL,
    team_id   TEXT NOT NULL,
    zone      INTEGER NOT NULL,
    fgm       INTEGER NOT NULL DEFAULT 0,
    fga       INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (season_id, player_id, zone),
    FOREIGN KEY (season_id) REFERENCES seasons (id) ON DELETE CASCADE
);");
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE playoff_zone_stats (
    season_id INTEGER NOT NULL,
    player_id INTEGER NOT NULL,
    team_id   TEXT NOT NULL,
    zone      INTEGER NOT NULL,
    fgm       INTEGER NOT NULL DEFAULT 0,
    fga       INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (season_id, player_id, zone),
    FOREIGN KEY (season_id) REFERENCES seasons (id) ON DELETE CASCADE
);");

            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE traits (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name_key TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    category TEXT NOT NULL DEFAULT '',
    description_1 TEXT NOT NULL DEFAULT '',
    description_2 TEXT NOT NULL DEFAULT '',
    description_3 TEXT NOT NULL DEFAULT ''
);");
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE player_traits (
    player_id INTEGER NOT NULL,
    trait_id INTEGER NOT NULL,
    star_level INTEGER NOT NULL DEFAULT 1,
    PRIMARY KEY (player_id, trait_id),
    FOREIGN KEY (player_id) REFERENCES players (id) ON UPDATE CASCADE ON DELETE CASCADE,
    FOREIGN KEY (trait_id) REFERENCES traits (id) ON UPDATE CASCADE ON DELETE CASCADE
);");
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE free_agents (
    player_id   INTEGER PRIMARY KEY,
    asking_salary INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (player_id) REFERENCES players (id) ON DELETE CASCADE
);");
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE draft_class (
    player_id   INTEGER PRIMARY KEY,
    draft_year  INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY (player_id) REFERENCES players (id) ON DELETE CASCADE
);");

            ExecuteNonQuery(connection, transaction, @"
INSERT INTO traits (name_key, display_name, category, description_1, description_2, description_3)
VALUES
('clutch_performer', '关键先生', 'Scoring',
 '第四节剩余5分钟内分差<5分时，投篮命中率+1.5%',
 '第四节剩余5分钟内分差<5分时，投篮命中率+2.5%',
 '第四节剩余5分钟内分差<5分时，投篮命中率+4%'),
('catch_and_shoot', '接球即投', 'Shooting',
 '接到队友传球后不运球直接出手，投篮命中率+1%',
 '接到队友传球后不运球直接出手，投篮命中率+2%',
 '接到队友传球后不运球直接出手，投篮命中率+3%'),
('needle_threader', '传切利器', 'Playmaking',
 '在拥挤区域也能精准送出传球，个人失误率降低0.8%',
 '在拥挤区域也能精准送出传球，个人失误率降低1.5%',
 '在拥挤区域也能精准送出传球，个人失误率降低2%'),
('clamps', '封锁专家', 'Defense',
 '防守对手运球出手时横向移动迅捷，对手有效防守属性×1.12，迫使对手命中率下降',
 '防守对手运球出手时横向移动迅捷，对手有效防守属性×1.22，迫使对手命中率下降',
 '防守对手运球出手时横向移动迅捷，对手有效防守属性×1.32，迫使对手命中率下降'),
('intimidator', '硬汉防守', 'Defense',
 '强壮身躯令对手心理受压，防守篮下/低位/中距离时对手命中率上限降低2%',
 '强壮身躯令对手心理受压，防守篮下/低位/中距离时对手命中率上限降低3%',
 '强壮身躯令对手心理受压，防守篮下/低位/中距离时对手命中率上限降低4%'),
('volume_shooter', '量产型', 'Shooting',
 '出手越多越进入状态：本场出手达10次后，每多出手1次命中率+0.1%，上限+1%',
 '出手越多越进入状态：本场出手达8次后，每多出手1次命中率+0.2%，上限+2%',
 '出手越多越进入状态：本场出手达6次后，每多出手1次命中率+0.3%，上限+3%'),
('comeback_kid', '慢热型', 'Scoring',
 '球队落后5分以上时激发斗志，个人投篮命中率+1%',
 '球队落后5分以上时+1.5%，落后15分以上时+2.5%，越难越勇',
 '球队落后5分以上时+2%，落后15分以上时+3.5%，越难越勇');");

            ExecuteNonQuery(connection, transaction, @"
CREATE VIEW IF NOT EXISTS player_attributes_named AS
SELECT p.id AS player_id, p.first_name, p.last_name, p.team_id,
       a.two_point, a.three_point, a.layup, a.close_shot, a.post_scoring, a.free_throw,
       a.passing, a.ball_handle, a.drive, a.draw_foul, a.offensive_consistency,
       a.perimeter_defense, a.interior_defense, a.steal, a.block,
       a.offensive_rebound, a.defensive_rebound, a.defensive_consistency,
       a.speed, a.strength, a.stamina
FROM player_attributes a
JOIN players p ON p.id = a.player_id;");
            ExecuteNonQuery(connection, transaction, @"
CREATE VIEW IF NOT EXISTS player_tendencies_named AS
SELECT p.id AS player_id, p.first_name, p.last_name, p.team_id,
       t.shot_tendency, t.three_tendency, t.two_point_tendency, t.drive_tendency,
       t.post_tendency, t.close_shot_tendency, t.pass_tendency, t.draw_foul_tendency,
       t.steal_tendency, t.block_tendency, t.foul_tendency, t.help_defense_tendency,
       t.offensive_rebound_tendency, t.defensive_rebound_tendency
FROM player_tendencies t
JOIN players p ON p.id = t.player_id;");
            ExecuteNonQuery(connection, transaction, @"
CREATE TRIGGER IF NOT EXISTS trg_attr_named_update
INSTEAD OF UPDATE ON player_attributes_named
BEGIN
    UPDATE player_attributes SET
        two_point = NEW.two_point, three_point = NEW.three_point,
        layup = NEW.layup, close_shot = NEW.close_shot, post_scoring = NEW.post_scoring,
        free_throw = NEW.free_throw, passing = NEW.passing, ball_handle = NEW.ball_handle,
        drive = NEW.drive, draw_foul = NEW.draw_foul, offensive_consistency = NEW.offensive_consistency,
        perimeter_defense = NEW.perimeter_defense, interior_defense = NEW.interior_defense,
        steal = NEW.steal, block = NEW.block,
        offensive_rebound = NEW.offensive_rebound, defensive_rebound = NEW.defensive_rebound,
        defensive_consistency = NEW.defensive_consistency,
        speed = NEW.speed, strength = NEW.strength, stamina = NEW.stamina
    WHERE player_id = OLD.player_id;
    UPDATE players SET first_name = NEW.first_name, last_name = NEW.last_name
    WHERE id = OLD.player_id;
END;");
            ExecuteNonQuery(connection, transaction, @"
CREATE TRIGGER IF NOT EXISTS trg_tend_named_update
INSTEAD OF UPDATE ON player_tendencies_named
BEGIN
    UPDATE player_tendencies SET
        shot_tendency = NEW.shot_tendency, three_tendency = NEW.three_tendency,
        two_point_tendency = NEW.two_point_tendency, drive_tendency = NEW.drive_tendency,
        post_tendency = NEW.post_tendency, close_shot_tendency = NEW.close_shot_tendency,
        pass_tendency = NEW.pass_tendency, draw_foul_tendency = NEW.draw_foul_tendency,
        steal_tendency = NEW.steal_tendency, block_tendency = NEW.block_tendency,
        foul_tendency = NEW.foul_tendency, help_defense_tendency = NEW.help_defense_tendency,
        offensive_rebound_tendency = NEW.offensive_rebound_tendency,
        defensive_rebound_tendency = NEW.defensive_rebound_tendency
    WHERE player_id = OLD.player_id;
    UPDATE players SET first_name = NEW.first_name, last_name = NEW.last_name
    WHERE id = OLD.player_id;
END;");

            transaction.Commit();
            SetForeignKeys(connection, true);
        }

        private static bool TableExists(SqliteConnection connection, string tableName)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
            command.Parameters.AddWithValue("@name", tableName);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }

        private static bool HasColumn(SqliteConnection connection, string tableName, string columnName)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
