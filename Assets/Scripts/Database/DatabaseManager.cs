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

            if (!File.Exists(_persistentDatabasePath) && File.Exists(_streamingDatabasePath))
            {
                File.Copy(_streamingDatabasePath, _persistentDatabasePath, false);
            }

            if (!File.Exists(_persistentDatabasePath))
            {
                using var createConnection = CreateConnection();
                createConnection.Open();
            }

            using var connection = CreateConnection();
            connection.Open();
            SetForeignKeys(connection, true);
            EnsureManagedSchema(connection);
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

        private SqliteConnection CreateConnection()
        {
            return new SqliteConnection($"URI=file:{_persistentDatabasePath}");
        }

        private static void SetForeignKeys(SqliteConnection connection, bool enabled)
        {
            using var command = connection.CreateCommand();
            command.CommandText = enabled ? "PRAGMA foreign_keys = ON;" : "PRAGMA foreign_keys = OFF;";
            command.ExecuteNonQuery();
        }

        private void EnsureManagedSchema(SqliteConnection connection)
        {
            if (!HasManagedSchema(connection))
            {
                RebuildManagedSchema(connection);
            }

            SeedExampleDataIfNeeded(connection);
        }

        private static bool HasManagedSchema(SqliteConnection connection)
        {
            return TableExists(connection, "teams")
                && TableExists(connection, "players")
                && TableExists(connection, "player_attributes")
                && TableExists(connection, "player_tendencies")
                && HasColumn(connection, "players", "first_name")
                && HasColumn(connection, "players", "last_name")
                && HasColumn(connection, "players", "display_name")
                && HasColumn(connection, "players", "name_order")
                && HasColumn(connection, "players", "nationality")
                && HasColumn(connection, "players", "region_type");
        }

        private static void RebuildManagedSchema(SqliteConnection connection)
        {
            SetForeignKeys(connection, false);

            using var transaction = connection.BeginTransaction();

            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS player_tendencies;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS player_attributes;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS players;");
            ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS teams;");

            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE teams (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    city TEXT DEFAULT ''
);");

            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE players (
    id INTEGER PRIMARY KEY,
    team_id INTEGER NOT NULL,
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

            transaction.Commit();
            SetForeignKeys(connection, true);
        }

        private static void SeedExampleDataIfNeeded(SqliteConnection connection)
        {
            if (GetScalarInt(connection, "SELECT COUNT(*) FROM teams;") > 0)
            {
                return;
            }

            using var transaction = connection.BeginTransaction();

            InsertTeam(connection, transaction, 1, "\u4e0a\u6d77\u9ca8\u9c7c", "\u4e0a\u6d77");
            InsertTeam(connection, transaction, 2, "Los Angeles Stars", "Los Angeles");

            InsertPlayer(connection, transaction, 1, 1, "\u660e", "\u59da", null, "EASTERN", "China", "CN", "C", 229, 141, 28, 11, 96);
            InsertPlayer(connection, transaction, 2, 1, "\u963f\u4e0d\u90fd\u6c99\u62c9\u6728", "\u963f\u4e0d\u90fd\u70ed\u897f\u63d0", "\u963f\u4e0d\u90fd\u6c99\u62c9\u6728", "CUSTOM", "China", "CN", "SF", 203, 105, 27, 23, 84);
            InsertPlayer(connection, transaction, 3, 2, "LeBron", "James", null, "WESTERN", "USA", "US", "SF", 206, 113, 39, 23, 97);
            InsertPlayer(connection, transaction, 4, 2, "Stephen", "Curry", null, "WESTERN", "USA", "US", "PG", 188, 84, 36, 30, 96);

            InsertAttributes(connection, transaction, 1, 92, 58, 85, 95, 97, 88, 72, 55, 68, 84, 94, 70, 95, 62, 96, 93, 98, 95, 58, 96, 90);
            InsertAttributes(connection, transaction, 2, 80, 73, 78, 79, 72, 76, 74, 72, 76, 70, 78, 74, 72, 68, 60, 79, 83, 76, 74, 78, 84);
            InsertAttributes(connection, transaction, 3, 92, 79, 94, 90, 82, 76, 91, 89, 94, 90, 95, 84, 75, 78, 70, 72, 80, 88, 86, 90, 94);
            InsertAttributes(connection, transaction, 4, 88, 99, 89, 87, 52, 92, 93, 96, 90, 79, 98, 78, 55, 82, 42, 50, 61, 84, 92, 60, 93);

            InsertTendencies(connection, transaction, 1, 82, 25, 84, 52, 90, 92, 58, 76, 45, 94, 44, 70, 90, 96);
            InsertTendencies(connection, transaction, 2, 73, 58, 72, 70, 54, 66, 72, 55, 54, 38, 48, 64, 70, 78);
            InsertTendencies(connection, transaction, 3, 96, 62, 88, 92, 48, 76, 88, 86, 64, 48, 52, 74, 58, 70);
            InsertTendencies(connection, transaction, 4, 97, 98, 60, 76, 12, 55, 90, 58, 65, 18, 42, 60, 28, 42);

            transaction.Commit();
        }

        private static void InsertTeam(SqliteConnection connection, SqliteTransaction transaction, int id, string name, string city)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO teams (id, name, city) VALUES (@id, @name, @city);";
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@city", city);
            command.ExecuteNonQuery();
        }

        private static void InsertPlayer(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int id,
            int teamId,
            string firstName,
            string lastName,
            string displayName,
            string nameOrder,
            string nationality,
            string regionType,
            string position,
            int heightCm,
            int weightKg,
            int age,
            int jerseyNumber,
            int overall)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO players (
    id, team_id, first_name, last_name, display_name, name_order, nationality, region_type,
    position, height_cm, weight_kg, age, jersey_number, overall
) VALUES (
    @id, @teamId, @firstName, @lastName, @displayName, @nameOrder, @nationality, @regionType,
    @position, @heightCm, @weightKg, @age, @jerseyNumber, @overall
);";
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@teamId", teamId);
            command.Parameters.AddWithValue("@firstName", firstName);
            command.Parameters.AddWithValue("@lastName", lastName);
            command.Parameters.AddWithValue("@displayName", string.IsNullOrWhiteSpace(displayName) ? (object)DBNull.Value : displayName);
            command.Parameters.AddWithValue("@nameOrder", nameOrder);
            command.Parameters.AddWithValue("@nationality", nationality);
            command.Parameters.AddWithValue("@regionType", regionType);
            command.Parameters.AddWithValue("@position", position);
            command.Parameters.AddWithValue("@heightCm", heightCm);
            command.Parameters.AddWithValue("@weightKg", weightKg);
            command.Parameters.AddWithValue("@age", age);
            command.Parameters.AddWithValue("@jerseyNumber", jerseyNumber);
            command.Parameters.AddWithValue("@overall", overall);
            command.ExecuteNonQuery();
        }

        private static void InsertAttributes(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int playerId,
            int twoPoint,
            int threePoint,
            int layup,
            int closeShot,
            int postScoring,
            int freeThrow,
            int passing,
            int ballHandle,
            int drive,
            int drawFoul,
            int offensiveConsistency,
            int perimeterDefense,
            int interiorDefense,
            int steal,
            int block,
            int offensiveRebound,
            int defensiveRebound,
            int defensiveConsistency,
            int speed,
            int strength,
            int stamina)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO player_attributes (
    player_id, two_point, three_point, layup, close_shot, post_scoring, free_throw, passing,
    ball_handle, drive, draw_foul, offensive_consistency, perimeter_defense, interior_defense,
    steal, block, offensive_rebound, defensive_rebound, defensive_consistency, speed, strength, stamina
) VALUES (
    @playerId, @twoPoint, @threePoint, @layup, @closeShot, @postScoring, @freeThrow, @passing,
    @ballHandle, @drive, @drawFoul, @offensiveConsistency, @perimeterDefense, @interiorDefense,
    @steal, @block, @offensiveRebound, @defensiveRebound, @defensiveConsistency, @speed, @strength, @stamina
);";
            command.Parameters.AddWithValue("@playerId", playerId);
            command.Parameters.AddWithValue("@twoPoint", twoPoint);
            command.Parameters.AddWithValue("@threePoint", threePoint);
            command.Parameters.AddWithValue("@layup", layup);
            command.Parameters.AddWithValue("@closeShot", closeShot);
            command.Parameters.AddWithValue("@postScoring", postScoring);
            command.Parameters.AddWithValue("@freeThrow", freeThrow);
            command.Parameters.AddWithValue("@passing", passing);
            command.Parameters.AddWithValue("@ballHandle", ballHandle);
            command.Parameters.AddWithValue("@drive", drive);
            command.Parameters.AddWithValue("@drawFoul", drawFoul);
            command.Parameters.AddWithValue("@offensiveConsistency", offensiveConsistency);
            command.Parameters.AddWithValue("@perimeterDefense", perimeterDefense);
            command.Parameters.AddWithValue("@interiorDefense", interiorDefense);
            command.Parameters.AddWithValue("@steal", steal);
            command.Parameters.AddWithValue("@block", block);
            command.Parameters.AddWithValue("@offensiveRebound", offensiveRebound);
            command.Parameters.AddWithValue("@defensiveRebound", defensiveRebound);
            command.Parameters.AddWithValue("@defensiveConsistency", defensiveConsistency);
            command.Parameters.AddWithValue("@speed", speed);
            command.Parameters.AddWithValue("@strength", strength);
            command.Parameters.AddWithValue("@stamina", stamina);
            command.ExecuteNonQuery();
        }

        private static void InsertTendencies(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int playerId,
            int shotTendency,
            int threeTendency,
            int twoPointTendency,
            int driveTendency,
            int postTendency,
            int closeShotTendency,
            int passTendency,
            int drawFoulTendency,
            int stealTendency,
            int blockTendency,
            int foulTendency,
            int helpDefenseTendency,
            int offensiveReboundTendency,
            int defensiveReboundTendency)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO player_tendencies (
    player_id, shot_tendency, three_tendency, two_point_tendency, drive_tendency, post_tendency,
    close_shot_tendency, pass_tendency, draw_foul_tendency, steal_tendency, block_tendency,
    foul_tendency, help_defense_tendency, offensive_rebound_tendency, defensive_rebound_tendency
) VALUES (
    @playerId, @shotTendency, @threeTendency, @twoPointTendency, @driveTendency, @postTendency,
    @closeShotTendency, @passTendency, @drawFoulTendency, @stealTendency, @blockTendency,
    @foulTendency, @helpDefenseTendency, @offensiveReboundTendency, @defensiveReboundTendency
);";
            command.Parameters.AddWithValue("@playerId", playerId);
            command.Parameters.AddWithValue("@shotTendency", shotTendency);
            command.Parameters.AddWithValue("@threeTendency", threeTendency);
            command.Parameters.AddWithValue("@twoPointTendency", twoPointTendency);
            command.Parameters.AddWithValue("@driveTendency", driveTendency);
            command.Parameters.AddWithValue("@postTendency", postTendency);
            command.Parameters.AddWithValue("@closeShotTendency", closeShotTendency);
            command.Parameters.AddWithValue("@passTendency", passTendency);
            command.Parameters.AddWithValue("@drawFoulTendency", drawFoulTendency);
            command.Parameters.AddWithValue("@stealTendency", stealTendency);
            command.Parameters.AddWithValue("@blockTendency", blockTendency);
            command.Parameters.AddWithValue("@foulTendency", foulTendency);
            command.Parameters.AddWithValue("@helpDefenseTendency", helpDefenseTendency);
            command.Parameters.AddWithValue("@offensiveReboundTendency", offensiveReboundTendency);
            command.Parameters.AddWithValue("@defensiveReboundTendency", defensiveReboundTendency);
            command.ExecuteNonQuery();
        }

        private static int GetScalarInt(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar());
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
