using System;
using System.Collections.Generic;
using BasketballManager.Core.Enums;
using BasketballManager.Core.Models;
using BasketballManager.Core.Services;
using Mono.Data.Sqlite;

namespace BasketballManager.Database
{
    public sealed class PlayerRepository
    {
        private readonly DatabaseManager _databaseManager;

        public PlayerRepository(DatabaseManager databaseManager)
        {
            _databaseManager = databaseManager;
        }

        public IReadOnlyList<Player> GetPlayersByTeamId(string teamId)
        {
            var players = new List<Player>();

            using var connection = _databaseManager.OpenConnection();
            using var command = CreatePlayerSelectCommand(connection);
            command.CommandText += @"
WHERE p.team_id = @teamId
ORDER BY p.overall DESC, p.id ASC;";
            command.Parameters.AddWithValue("@teamId", teamId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                players.Add(MapPlayer(reader));
            }

            return players;
        }

        public Player GetPlayerById(int playerId)
        {
            using var connection = _databaseManager.OpenConnection();
            using var command = CreatePlayerSelectCommand(connection);
            command.CommandText += @"
WHERE p.id = @playerId
LIMIT 1;";
            command.Parameters.AddWithValue("@playerId", playerId);

            using var reader = command.ExecuteReader();
            return reader.Read() ? MapPlayer(reader) : null;
        }

        public void UpdatePlayer(Player player)
        {
            player.Overall = RatingCalculator.CalculateOverall(player);

            using var connection = _databaseManager.OpenConnection();
            using var transaction = connection.BeginTransaction();

            using (var playerCommand = connection.CreateCommand())
            {
                playerCommand.Transaction = transaction;
                playerCommand.CommandText = @"
UPDATE players
SET
    team_id = @teamId,
    first_name = @firstName,
    last_name = @lastName,
    display_name = @displayName,
    name_order = @nameOrder,
    nationality = @nationality,
    region_type = @regionType,
    position = @position,
    height_cm = @heightCm,
    weight_kg = @weightKg,
    age = @age,
    jersey_number = @jerseyNumber,
    overall = @overall
WHERE id = @id;";
                AddPlayerParameters(playerCommand, player);
                playerCommand.ExecuteNonQuery();
            }

            using (var attributeCommand = connection.CreateCommand())
            {
                attributeCommand.Transaction = transaction;
                attributeCommand.CommandText = @"
INSERT INTO player_attributes (
    player_id, two_point, three_point, layup, close_shot, post_scoring, free_throw, passing,
    ball_handle, drive, draw_foul, offensive_consistency, perimeter_defense, interior_defense,
    steal, block, offensive_rebound, defensive_rebound, defensive_consistency, speed, strength, stamina
) VALUES (
    @playerId, @twoPoint, @threePoint, @layup, @closeShot, @postScoring, @freeThrow, @passing,
    @ballHandle, @drive, @drawFoul, @offensiveConsistency, @perimeterDefense, @interiorDefense,
    @steal, @block, @offensiveRebound, @defensiveRebound, @defensiveConsistency, @speed, @strength, @stamina
)
ON CONFLICT(player_id) DO UPDATE SET
    two_point = excluded.two_point,
    three_point = excluded.three_point,
    layup = excluded.layup,
    close_shot = excluded.close_shot,
    post_scoring = excluded.post_scoring,
    free_throw = excluded.free_throw,
    passing = excluded.passing,
    ball_handle = excluded.ball_handle,
    drive = excluded.drive,
    draw_foul = excluded.draw_foul,
    offensive_consistency = excluded.offensive_consistency,
    perimeter_defense = excluded.perimeter_defense,
    interior_defense = excluded.interior_defense,
    steal = excluded.steal,
    block = excluded.block,
    offensive_rebound = excluded.offensive_rebound,
    defensive_rebound = excluded.defensive_rebound,
    defensive_consistency = excluded.defensive_consistency,
    speed = excluded.speed,
    strength = excluded.strength,
    stamina = excluded.stamina;";
                AddAttributeParameters(attributeCommand, player);
                attributeCommand.ExecuteNonQuery();
            }

            using (var tendencyCommand = connection.CreateCommand())
            {
                tendencyCommand.Transaction = transaction;
                tendencyCommand.CommandText = @"
INSERT INTO player_tendencies (
    player_id, shot_tendency, three_tendency, two_point_tendency, drive_tendency, post_tendency,
    close_shot_tendency, pass_tendency, draw_foul_tendency, steal_tendency, block_tendency,
    foul_tendency, help_defense_tendency, offensive_rebound_tendency, defensive_rebound_tendency
) VALUES (
    @playerId, @shotTendency, @threeTendency, @twoPointTendency, @driveTendency, @postTendency,
    @closeShotTendency, @passTendency, @drawFoulTendency, @stealTendency, @blockTendency,
    @foulTendency, @helpDefenseTendency, @offensiveReboundTendency, @defensiveReboundTendency
)
ON CONFLICT(player_id) DO UPDATE SET
    shot_tendency = excluded.shot_tendency,
    three_tendency = excluded.three_tendency,
    two_point_tendency = excluded.two_point_tendency,
    drive_tendency = excluded.drive_tendency,
    post_tendency = excluded.post_tendency,
    close_shot_tendency = excluded.close_shot_tendency,
    pass_tendency = excluded.pass_tendency,
    draw_foul_tendency = excluded.draw_foul_tendency,
    steal_tendency = excluded.steal_tendency,
    block_tendency = excluded.block_tendency,
    foul_tendency = excluded.foul_tendency,
    help_defense_tendency = excluded.help_defense_tendency,
    offensive_rebound_tendency = excluded.offensive_rebound_tendency,
    defensive_rebound_tendency = excluded.defensive_rebound_tendency;";
                AddTendencyParameters(tendencyCommand, player);
                tendencyCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        private static SqliteCommand CreatePlayerSelectCommand(SqliteConnection connection)
        {
            var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    p.id,
    p.team_id,
    p.first_name,
    p.last_name,
    COALESCE(p.display_name, '') AS display_name,
    COALESCE(p.name_order, 'WESTERN') AS name_order,
    COALESCE(p.nationality, '') AS nationality,
    COALESCE(p.region_type, '') AS region_type,
    COALESCE(p.position, 'PG') AS position,
    p.height_cm,
    p.weight_kg,
    p.age,
    COALESCE(p.jersey_number, 0) AS jersey_number,
    p.overall,
    COALESCE(a.two_point, 60) AS two_point,
    COALESCE(a.three_point, 60) AS three_point,
    COALESCE(a.layup, 60) AS layup,
    COALESCE(a.close_shot, 60) AS close_shot,
    COALESCE(a.post_scoring, 60) AS post_scoring,
    COALESCE(a.free_throw, 60) AS free_throw,
    COALESCE(a.passing, 60) AS passing,
    COALESCE(a.ball_handle, 60) AS ball_handle,
    COALESCE(a.drive, 60) AS drive,
    COALESCE(a.draw_foul, 60) AS draw_foul,
    COALESCE(a.offensive_consistency, 70) AS offensive_consistency,
    COALESCE(a.perimeter_defense, 60) AS perimeter_defense,
    COALESCE(a.interior_defense, 60) AS interior_defense,
    COALESCE(a.steal, 60) AS steal,
    COALESCE(a.block, 60) AS block,
    COALESCE(a.offensive_rebound, 60) AS offensive_rebound,
    COALESCE(a.defensive_rebound, 60) AS defensive_rebound,
    COALESCE(a.defensive_consistency, 70) AS defensive_consistency,
    COALESCE(a.speed, 60) AS speed,
    COALESCE(a.strength, 60) AS strength,
    COALESCE(a.stamina, 60) AS stamina,
    COALESCE(t.shot_tendency, 60) AS shot_tendency,
    COALESCE(t.three_tendency, 60) AS three_tendency,
    COALESCE(t.two_point_tendency, 60) AS two_point_tendency,
    COALESCE(t.drive_tendency, 60) AS drive_tendency,
    COALESCE(t.post_tendency, 60) AS post_tendency,
    COALESCE(t.close_shot_tendency, 60) AS close_shot_tendency,
    COALESCE(t.pass_tendency, 60) AS pass_tendency,
    COALESCE(t.draw_foul_tendency, 60) AS draw_foul_tendency,
    COALESCE(t.steal_tendency, 60) AS steal_tendency,
    COALESCE(t.block_tendency, 60) AS block_tendency,
    COALESCE(t.foul_tendency, 60) AS foul_tendency,
    COALESCE(t.help_defense_tendency, 60) AS help_defense_tendency,
    COALESCE(t.offensive_rebound_tendency, 60) AS offensive_rebound_tendency,
    COALESCE(t.defensive_rebound_tendency, 60) AS defensive_rebound_tendency
FROM players p
LEFT JOIN player_attributes a ON a.player_id = p.id
LEFT JOIN player_tendencies t ON t.player_id = p.id
";
            return command;
        }

        private static Player MapPlayer(SqliteDataReader reader)
        {
            var player = new Player
            {
                Id = ReadInt(reader["id"]),
                TeamId = reader["team_id"].ToString() ?? string.Empty,
                FirstName = reader["first_name"].ToString() ?? string.Empty,
                LastName = reader["last_name"].ToString() ?? string.Empty,
                DisplayName = reader["display_name"].ToString() ?? string.Empty,
                NameOrder = ParseNameOrder(reader["name_order"].ToString()),
                Nationality = reader["nationality"].ToString() ?? string.Empty,
                RegionType = reader["region_type"].ToString() ?? string.Empty,
                Position = ParsePosition(reader["position"].ToString()),
                HeightCm = ReadInt(reader["height_cm"]),
                WeightKg = ReadInt(reader["weight_kg"]),
                Age = ReadInt(reader["age"]),
                JerseyNumber = ReadInt(reader["jersey_number"]),
                Overall = ReadInt(reader["overall"]),
                Attributes = new PlayerAttributes
                {
                    TwoPoint = ReadInt(reader["two_point"]),
                    ThreePoint = ReadInt(reader["three_point"]),
                    Layup = ReadInt(reader["layup"]),
                    CloseShot = ReadInt(reader["close_shot"]),
                    PostScoring = ReadInt(reader["post_scoring"]),
                    FreeThrow = ReadInt(reader["free_throw"]),
                    Passing = ReadInt(reader["passing"]),
                    BallHandle = ReadInt(reader["ball_handle"]),
                    Drive = ReadInt(reader["drive"]),
                    DrawFoul = ReadInt(reader["draw_foul"]),
                    OffensiveConsistency = ReadInt(reader["offensive_consistency"]),
                    PerimeterDefense = ReadInt(reader["perimeter_defense"]),
                    InteriorDefense = ReadInt(reader["interior_defense"]),
                    Steal = ReadInt(reader["steal"]),
                    Block = ReadInt(reader["block"]),
                    OffensiveRebound = ReadInt(reader["offensive_rebound"]),
                    DefensiveRebound = ReadInt(reader["defensive_rebound"]),
                    DefensiveConsistency = ReadInt(reader["defensive_consistency"]),
                    Speed = ReadInt(reader["speed"]),
                    Strength = ReadInt(reader["strength"]),
                    Stamina = ReadInt(reader["stamina"])
                },
                Tendencies = new PlayerTendencies
                {
                    ShotTendency = ReadInt(reader["shot_tendency"]),
                    ThreeTendency = ReadInt(reader["three_tendency"]),
                    TwoPointTendency = ReadInt(reader["two_point_tendency"]),
                    DriveTendency = ReadInt(reader["drive_tendency"]),
                    PostTendency = ReadInt(reader["post_tendency"]),
                    CloseShotTendency = ReadInt(reader["close_shot_tendency"]),
                    PassTendency = ReadInt(reader["pass_tendency"]),
                    DrawFoulTendency = ReadInt(reader["draw_foul_tendency"]),
                    StealTendency = ReadInt(reader["steal_tendency"]),
                    BlockTendency = ReadInt(reader["block_tendency"]),
                    FoulTendency = ReadInt(reader["foul_tendency"]),
                    HelpDefenseTendency = ReadInt(reader["help_defense_tendency"]),
                    OffensiveReboundTendency = ReadInt(reader["offensive_rebound_tendency"]),
                    DefensiveReboundTendency = ReadInt(reader["defensive_rebound_tendency"])
                }
            };

            player.Overall = RatingCalculator.CalculateOverall(player);
            return player;
        }

        private static void AddPlayerParameters(SqliteCommand command, Player player)
        {
            command.Parameters.AddWithValue("@id", player.Id);
            command.Parameters.AddWithValue("@teamId", player.TeamId);
            command.Parameters.AddWithValue("@firstName", player.FirstName);
            command.Parameters.AddWithValue("@lastName", player.LastName);
            command.Parameters.AddWithValue("@displayName", string.IsNullOrWhiteSpace(player.DisplayName) ? (object)DBNull.Value : player.DisplayName);
            command.Parameters.AddWithValue("@nameOrder", player.NameOrder.ToString());
            command.Parameters.AddWithValue("@nationality", player.Nationality);
            command.Parameters.AddWithValue("@regionType", player.RegionType);
            command.Parameters.AddWithValue("@position", player.Position.ToString());
            command.Parameters.AddWithValue("@heightCm", player.HeightCm);
            command.Parameters.AddWithValue("@weightKg", player.WeightKg);
            command.Parameters.AddWithValue("@age", player.Age);
            command.Parameters.AddWithValue("@jerseyNumber", player.JerseyNumber);
            command.Parameters.AddWithValue("@overall", player.Overall);
        }

        private static void AddAttributeParameters(SqliteCommand command, Player player)
        {
            var attributes = player.Attributes;
            command.Parameters.AddWithValue("@playerId", player.Id);
            command.Parameters.AddWithValue("@twoPoint", attributes.TwoPoint);
            command.Parameters.AddWithValue("@threePoint", attributes.ThreePoint);
            command.Parameters.AddWithValue("@layup", attributes.Layup);
            command.Parameters.AddWithValue("@closeShot", attributes.CloseShot);
            command.Parameters.AddWithValue("@postScoring", attributes.PostScoring);
            command.Parameters.AddWithValue("@freeThrow", attributes.FreeThrow);
            command.Parameters.AddWithValue("@passing", attributes.Passing);
            command.Parameters.AddWithValue("@ballHandle", attributes.BallHandle);
            command.Parameters.AddWithValue("@drive", attributes.Drive);
            command.Parameters.AddWithValue("@drawFoul", attributes.DrawFoul);
            command.Parameters.AddWithValue("@offensiveConsistency", attributes.OffensiveConsistency);
            command.Parameters.AddWithValue("@perimeterDefense", attributes.PerimeterDefense);
            command.Parameters.AddWithValue("@interiorDefense", attributes.InteriorDefense);
            command.Parameters.AddWithValue("@steal", attributes.Steal);
            command.Parameters.AddWithValue("@block", attributes.Block);
            command.Parameters.AddWithValue("@offensiveRebound", attributes.OffensiveRebound);
            command.Parameters.AddWithValue("@defensiveRebound", attributes.DefensiveRebound);
            command.Parameters.AddWithValue("@defensiveConsistency", attributes.DefensiveConsistency);
            command.Parameters.AddWithValue("@speed", attributes.Speed);
            command.Parameters.AddWithValue("@strength", attributes.Strength);
            command.Parameters.AddWithValue("@stamina", attributes.Stamina);
        }

        private static void AddTendencyParameters(SqliteCommand command, Player player)
        {
            var tendencies = player.Tendencies;
            command.Parameters.AddWithValue("@playerId", player.Id);
            command.Parameters.AddWithValue("@shotTendency", tendencies.ShotTendency);
            command.Parameters.AddWithValue("@threeTendency", tendencies.ThreeTendency);
            command.Parameters.AddWithValue("@twoPointTendency", tendencies.TwoPointTendency);
            command.Parameters.AddWithValue("@driveTendency", tendencies.DriveTendency);
            command.Parameters.AddWithValue("@postTendency", tendencies.PostTendency);
            command.Parameters.AddWithValue("@closeShotTendency", tendencies.CloseShotTendency);
            command.Parameters.AddWithValue("@passTendency", tendencies.PassTendency);
            command.Parameters.AddWithValue("@drawFoulTendency", tendencies.DrawFoulTendency);
            command.Parameters.AddWithValue("@stealTendency", tendencies.StealTendency);
            command.Parameters.AddWithValue("@blockTendency", tendencies.BlockTendency);
            command.Parameters.AddWithValue("@foulTendency", tendencies.FoulTendency);
            command.Parameters.AddWithValue("@helpDefenseTendency", tendencies.HelpDefenseTendency);
            command.Parameters.AddWithValue("@offensiveReboundTendency", tendencies.OffensiveReboundTendency);
            command.Parameters.AddWithValue("@defensiveReboundTendency", tendencies.DefensiveReboundTendency);
        }

        private static int ReadInt(object value)
        {
            return int.TryParse(value?.ToString(), out var parsed) ? parsed : 0;
        }

        private static Position ParsePosition(string value)
        {
            return Enum.TryParse(value, true, out Position position) ? position : Position.PG;
        }

        private static NameOrder ParseNameOrder(string value)
        {
            return Enum.TryParse(value, true, out NameOrder nameOrder) ? nameOrder : NameOrder.WESTERN;
        }
    }
}
