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

            if (!File.Exists(_persistentDatabasePath))
            {
                if (File.Exists(_streamingDatabasePath))
                {
                    File.Copy(_streamingDatabasePath, _persistentDatabasePath, false);
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

            if (!HasManagedSchema(connection))
            {
                throw new InvalidOperationException(BuildSchemaMismatchMessage());
            }

            _initialized = true;
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
            return
                "数据库结构不匹配，请删除 persistentDataPath 下旧 game.db，或重新生成 Assets/StreamingAssets/game.db。\n"
                + $"Persistent DB: {_persistentDatabasePath}\n"
                + $"Streaming DB: {_streamingDatabasePath}\n"
                + "数据库结构不匹配，请删除持久化目录下的旧 game.db 后重试。";
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
                && HasColumn(connection, "teams", "id")
                && HasColumn(connection, "teams", "era")
                && HasColumn(connection, "teams", "is_current")
                && HasColumn(connection, "players", "team_id")
                && HasColumn(connection, "players", "first_name")
                && HasColumn(connection, "players", "last_name")
                && HasColumn(connection, "players", "display_name")
                && HasColumn(connection, "players", "name_order")
                && HasColumn(connection, "players", "nationality")
                && HasColumn(connection, "players", "region_type")
                && HasColumn(connection, "player_attributes", "draw_foul")
                && HasColumn(connection, "player_tendencies", "draw_foul_tendency")
                && HasColumn(connection, "player_simulation_profiles", "source_mpg");
        }

        private static void RebuildManagedSchema(SqliteConnection connection)
        {
            SetForeignKeys(connection, false);

            using var transaction = connection.BeginTransaction();
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
    display_name TEXT,
    name_order TEXT NOT NULL DEFAULT 'WESTERN',
    nationality TEXT,
    region_type TEXT,
    position TEXT NOT NULL,
    height_cm INTEGER NOT NULL,
    weight_kg INTEGER NOT NULL,
    age INTEGER NOT NULL,
    jersey_number INTEGER,
    overall INTEGER NOT NULL DEFAULT 70,
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
