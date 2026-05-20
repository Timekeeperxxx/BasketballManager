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

        public DatabaseManager()
        {
            _streamingDatabasePath = Path.Combine(UnityEngine.Application.streamingAssetsPath, DatabaseFileName);
            _persistentDatabasePath = Path.Combine(UnityEngine.Application.persistentDataPath, DatabaseFileName);
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
            EnsurePlayerIsCurrentColumn(connection);
            EnsurePlayerNamedViews(connection);

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
            // 先删赛季相关表（被引用方在前）。
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
    is_current INTEGER DEFAULT 0
);");

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

            // 赛季容器表：name + 创建时间 + 状态。
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE seasons (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'IN_PROGRESS',
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

            // 赛程表：每场比赛一行，含主客队、day、状态、最终比分。
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
