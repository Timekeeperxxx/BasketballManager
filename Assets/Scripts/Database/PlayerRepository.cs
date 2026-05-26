using System;
using System.Collections.Generic;
using System.Linq;
using BasketballManager.Core.Enums;
using BasketballManager.Core.Models;
using BasketballManager.Core.Services;
using Mono.Data.Sqlite;

namespace BasketballManager.Database
{
    public sealed class PlayerRepository
    {
        private readonly DatabaseManager _databaseManager;
        private TraitRepository _traitRepository;

        public PlayerRepository(DatabaseManager databaseManager)
        {
            _databaseManager = databaseManager;
        }

        public void SetTraitRepository(TraitRepository traitRepository)
        {
            _traitRepository = traitRepository;
        }

        public IReadOnlyList<Player> GetAllPlayers()
        {
            var players = new List<Player>();
            using var connection = _databaseManager.OpenConnection();
            using var command = CreatePlayerSelectCommand(connection);
            command.CommandText += "WHERE COALESCE(p.is_current, 1) = 1 ORDER BY p.team_id, p.overall DESC;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) players.Add(MapPlayer(reader));
            LoadTraitsForPlayers(players);
            return players;
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

            LoadTraitsForPlayers(players);
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
            if (!reader.Read()) return null;
            var player = MapPlayer(reader);
            reader.Close();
            if (_traitRepository != null)
                player.Traits = _traitRepository.GetPlayerTraits(playerId).ToList();
            return player;
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
    name_order = @nameOrder,
    position = @position,
    secondary_position = @secondaryPosition,
    height_cm = @heightCm,
    weight_kg = @weightKg,
    age = @age,
    jersey_number = @jerseyNumber,
    overall = @overall,
    is_current = @isCurrent,
    potential_min   = @potentialMin,
    potential_max   = @potentialMax,
    peak_age_start  = @peakAgeStart,
    peak_age_end    = @peakAgeEnd,
    contract_years  = @contractYears,
    contract_salary = @contractSalary,
    injury_games_remaining = @injuryGamesRemaining
WHERE id = @id;";
                AddPlayerParameters(playerCommand, player);
                playerCommand.ExecuteNonQuery();
            }

            using (var attributeCommand = connection.CreateCommand())
            {
                attributeCommand.Transaction = transaction;
                attributeCommand.CommandText = @"
REPLACE INTO player_attributes (
    player_id, two_point, three_point, layup, close_shot, post_scoring, free_throw, passing,
    ball_handle, drive, draw_foul, offensive_consistency, perimeter_defense, interior_defense,
    steal, block, offensive_rebound, defensive_rebound, defensive_consistency, speed, strength, stamina
) VALUES (
    @playerId, @twoPoint, @threePoint, @layup, @closeShot, @postScoring, @freeThrow, @passing,
    @ballHandle, @drive, @drawFoul, @offensiveConsistency, @perimeterDefense, @interiorDefense,
    @steal, @block, @offensiveRebound, @defensiveRebound, @defensiveConsistency, @speed, @strength, @stamina
);";
                AddAttributeParameters(attributeCommand, player);
                attributeCommand.ExecuteNonQuery();
            }

            using (var tendencyCommand = connection.CreateCommand())
            {
                tendencyCommand.Transaction = transaction;
                tendencyCommand.CommandText = @"
REPLACE INTO player_tendencies (
    player_id, shot_tendency, three_tendency, two_point_tendency, drive_tendency, post_tendency,
    close_shot_tendency, pass_tendency, draw_foul_tendency, steal_tendency, block_tendency,
    foul_tendency, help_defense_tendency, offensive_rebound_tendency, defensive_rebound_tendency,
    zone_three_left_corner, zone_three_right_corner, zone_three_left_wing, zone_three_right_wing, zone_three_top_key,
    zone_mid_left_corner, zone_mid_right_corner, zone_mid_left_elbow, zone_mid_right_elbow, zone_mid_top_key,
    zone_close_left, zone_close_center, zone_close_right
) VALUES (
    @playerId, @shotTendency, @threeTendency, @twoPointTendency, @driveTendency, @postTendency,
    @closeShotTendency, @passTendency, @drawFoulTendency, @stealTendency, @blockTendency,
    @foulTendency, @helpDefenseTendency, @offensiveReboundTendency, @defensiveReboundTendency,
    @zoneThreeLeftCorner, @zoneThreeRightCorner, @zoneThreeLeftWing, @zoneThreeRightWing, @zoneThreeTopKey,
    @zoneMidLeftCorner, @zoneMidRightCorner, @zoneMidLeftElbow, @zoneMidRightElbow, @zoneMidTopKey,
    @zoneCloseLeft, @zoneCloseCenter, @zoneCloseRight
);";
                AddTendencyParameters(tendencyCommand, player);
                tendencyCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        private void LoadTraitsForPlayers(List<Player> players)
        {
            if (_traitRepository == null || players.Count == 0) return;
            var ids = players.Select(p => p.Id).ToList();
            var allTraits = _traitRepository.GetPlayerTraitsBatch(ids);
            var byPlayer = new Dictionary<int, List<PlayerTrait>>();
            foreach (var pt in allTraits)
            {
                if (!byPlayer.TryGetValue(pt.PlayerId, out var bucket))
                {
                    bucket = new List<PlayerTrait>();
                    byPlayer[pt.PlayerId] = bucket;
                }
                bucket.Add(pt);
            }
            foreach (var p in players)
                p.Traits = byPlayer.TryGetValue(p.Id, out var traits) ? traits : new List<PlayerTrait>();
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
    p.name_order,
    p.position,
    p.secondary_position,
    p.height_cm,
    p.weight_kg,
    p.age,
    COALESCE(p.jersey_number, 0) AS jersey_number,
    p.overall,
    COALESCE(p.is_current, 0)       AS is_current,
    COALESCE(p.potential_min, 0)    AS potential_min,
    COALESCE(p.potential_max, 0)    AS potential_max,
    COALESCE(p.peak_age_start, 25)  AS peak_age_start,
    COALESCE(p.peak_age_end,   30)  AS peak_age_end,
    COALESCE(p.contract_years,  0)  AS contract_years,
    COALESCE(p.contract_salary, 0)  AS contract_salary,
    COALESCE(p.injury_games_remaining, 0) AS injury_games_remaining,
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
    COALESCE(t.defensive_rebound_tendency, 60) AS defensive_rebound_tendency,
    COALESCE(t.zone_three_left_corner, 50)  AS zone_three_left_corner,
    COALESCE(t.zone_three_right_corner, 50) AS zone_three_right_corner,
    COALESCE(t.zone_three_left_wing, 60)    AS zone_three_left_wing,
    COALESCE(t.zone_three_right_wing, 60)   AS zone_three_right_wing,
    COALESCE(t.zone_three_top_key, 70)      AS zone_three_top_key,
    COALESCE(t.zone_mid_left_corner, 40)    AS zone_mid_left_corner,
    COALESCE(t.zone_mid_right_corner, 40)   AS zone_mid_right_corner,
    COALESCE(t.zone_mid_left_elbow, 60)     AS zone_mid_left_elbow,
    COALESCE(t.zone_mid_right_elbow, 60)    AS zone_mid_right_elbow,
    COALESCE(t.zone_mid_top_key, 50)        AS zone_mid_top_key,
    COALESCE(t.zone_close_left, 50)         AS zone_close_left,
    COALESCE(t.zone_close_center, 70)       AS zone_close_center,
    COALESCE(t.zone_close_right, 50)        AS zone_close_right
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
                NameOrder = ParseNameOrder(reader["name_order"]?.ToString()),
                Position = ParsePosition(reader["position"]?.ToString()),
                SecondaryPosition = ParseSecondaryPosition(reader["secondary_position"]),
                HeightCm = ReadInt(reader["height_cm"]),
                WeightKg = ReadInt(reader["weight_kg"]),
                Age = ReadInt(reader["age"]),
                JerseyNumber = ReadInt(reader["jersey_number"]),
                Overall = ReadInt(reader["overall"]),
                IsCurrent    = ReadInt(reader["is_current"]) != 0,
                PotentialMin   = ReadInt(reader["potential_min"]),
                PotentialMax   = ReadInt(reader["potential_max"]),
                PeakAgeStart   = ReadInt(reader["peak_age_start"]),
                PeakAgeEnd     = ReadInt(reader["peak_age_end"]),
                ContractYears  = ReadInt(reader["contract_years"]),
                ContractSalary = ReadInt(reader["contract_salary"]),
                InjuryGamesRemaining = ReadInt(reader["injury_games_remaining"]),
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
                    DefensiveReboundTendency = ReadInt(reader["defensive_rebound_tendency"]),
                    ZoneThreeLeftCorner  = ReadInt(reader["zone_three_left_corner"]),
                    ZoneThreeRightCorner = ReadInt(reader["zone_three_right_corner"]),
                    ZoneThreeLeftWing    = ReadInt(reader["zone_three_left_wing"]),
                    ZoneThreeRightWing   = ReadInt(reader["zone_three_right_wing"]),
                    ZoneThreeTopKey      = ReadInt(reader["zone_three_top_key"]),
                    ZoneMidLeftCorner    = ReadInt(reader["zone_mid_left_corner"]),
                    ZoneMidRightCorner   = ReadInt(reader["zone_mid_right_corner"]),
                    ZoneMidLeftElbow     = ReadInt(reader["zone_mid_left_elbow"]),
                    ZoneMidRightElbow    = ReadInt(reader["zone_mid_right_elbow"]),
                    ZoneMidTopKey        = ReadInt(reader["zone_mid_top_key"]),
                    ZoneCloseLeft        = ReadInt(reader["zone_close_left"]),
                    ZoneCloseCenter      = ReadInt(reader["zone_close_center"]),
                    ZoneCloseRight       = ReadInt(reader["zone_close_right"])
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
            command.Parameters.AddWithValue("@nameOrder", player.NameOrder.ToString());
            command.Parameters.AddWithValue("@position", player.Position.ToString());
            command.Parameters.AddWithValue("@secondaryPosition", player.SecondaryPosition.HasValue ? (object)player.SecondaryPosition.Value.ToString() : DBNull.Value);
            command.Parameters.AddWithValue("@heightCm", player.HeightCm);
            command.Parameters.AddWithValue("@weightKg", player.WeightKg);
            command.Parameters.AddWithValue("@age", player.Age);
            command.Parameters.AddWithValue("@jerseyNumber", player.JerseyNumber);
            command.Parameters.AddWithValue("@overall", player.Overall);
            command.Parameters.AddWithValue("@isCurrent", player.IsCurrent ? 1 : 0);
            command.Parameters.AddWithValue("@potentialMin",   player.PotentialMin);
            command.Parameters.AddWithValue("@potentialMax",   player.PotentialMax);
            command.Parameters.AddWithValue("@peakAgeStart",   player.PeakAgeStart);
            command.Parameters.AddWithValue("@peakAgeEnd",     player.PeakAgeEnd);
            command.Parameters.AddWithValue("@contractYears",  player.ContractYears);
            command.Parameters.AddWithValue("@contractSalary", player.ContractSalary);
            command.Parameters.AddWithValue("@injuryGamesRemaining", player.InjuryGamesRemaining);
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
            command.Parameters.AddWithValue("@zoneThreeLeftCorner",  tendencies.ZoneThreeLeftCorner);
            command.Parameters.AddWithValue("@zoneThreeRightCorner", tendencies.ZoneThreeRightCorner);
            command.Parameters.AddWithValue("@zoneThreeLeftWing",    tendencies.ZoneThreeLeftWing);
            command.Parameters.AddWithValue("@zoneThreeRightWing",   tendencies.ZoneThreeRightWing);
            command.Parameters.AddWithValue("@zoneThreeTopKey",      tendencies.ZoneThreeTopKey);
            command.Parameters.AddWithValue("@zoneMidLeftCorner",    tendencies.ZoneMidLeftCorner);
            command.Parameters.AddWithValue("@zoneMidRightCorner",   tendencies.ZoneMidRightCorner);
            command.Parameters.AddWithValue("@zoneMidLeftElbow",     tendencies.ZoneMidLeftElbow);
            command.Parameters.AddWithValue("@zoneMidRightElbow",    tendencies.ZoneMidRightElbow);
            command.Parameters.AddWithValue("@zoneMidTopKey",        tendencies.ZoneMidTopKey);
            command.Parameters.AddWithValue("@zoneCloseLeft",        tendencies.ZoneCloseLeft);
            command.Parameters.AddWithValue("@zoneCloseCenter",      tendencies.ZoneCloseCenter);
            command.Parameters.AddWithValue("@zoneCloseRight",       tendencies.ZoneCloseRight);
        }

        private static Position? ParseSecondaryPosition(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            var str = value.ToString();
            if (string.IsNullOrWhiteSpace(str)) return null;
            if (Enum.TryParse<Position>(str, out var result)) return result;
            return null;
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

        public int InsertPlayer(Player player)
        {
            player.Overall = RatingCalculator.CalculateOverall(player);

            using var connection = _databaseManager.OpenConnection();
            using var transaction = connection.BeginTransaction();

            int newId;
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
INSERT INTO players (team_id, first_name, last_name, name_order, position, secondary_position,
    height_cm, weight_kg, age, jersey_number, overall, is_current,
    potential_min, potential_max, peak_age_start, peak_age_end,
    contract_years, contract_salary)
VALUES (@teamId, @firstName, @lastName, @nameOrder, @position, @secondaryPosition,
    @heightCm, @weightKg, @age, @jerseyNumber, @overall, @isCurrent,
    @potentialMin, @potentialMax, @peakAgeStart, @peakAgeEnd,
    @contractYears, @contractSalary);";
                AddPlayerParameters(cmd, player);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT last_insert_rowid();";
                newId = Convert.ToInt32(cmd.ExecuteScalar());
            }
            player.Id = newId;

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
INSERT OR REPLACE INTO player_attributes (
    player_id, two_point, three_point, layup, close_shot, post_scoring, free_throw, passing,
    ball_handle, drive, draw_foul, offensive_consistency, perimeter_defense, interior_defense,
    steal, block, offensive_rebound, defensive_rebound, defensive_consistency, speed, strength, stamina
) VALUES (
    @playerId, @twoPoint, @threePoint, @layup, @closeShot, @postScoring, @freeThrow, @passing,
    @ballHandle, @drive, @drawFoul, @offensiveConsistency, @perimeterDefense, @interiorDefense,
    @steal, @block, @offensiveRebound, @defensiveRebound, @defensiveConsistency, @speed, @strength, @stamina
);";
                AddAttributeParameters(cmd, player);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
INSERT OR REPLACE INTO player_tendencies (
    player_id, shot_tendency, three_tendency, two_point_tendency, drive_tendency, post_tendency,
    close_shot_tendency, pass_tendency, draw_foul_tendency, steal_tendency, block_tendency,
    foul_tendency, help_defense_tendency, offensive_rebound_tendency, defensive_rebound_tendency,
    zone_three_left_corner, zone_three_right_corner, zone_three_left_wing, zone_three_right_wing, zone_three_top_key,
    zone_mid_left_corner, zone_mid_right_corner, zone_mid_left_elbow, zone_mid_right_elbow, zone_mid_top_key,
    zone_close_left, zone_close_center, zone_close_right
) VALUES (
    @playerId, @shotTendency, @threeTendency, @twoPointTendency, @driveTendency, @postTendency,
    @closeShotTendency, @passTendency, @drawFoulTendency, @stealTendency, @blockTendency,
    @foulTendency, @helpDefenseTendency, @offensiveReboundTendency, @defensiveReboundTendency,
    @zoneThreeLeftCorner, @zoneThreeRightCorner, @zoneThreeLeftWing, @zoneThreeRightWing, @zoneThreeTopKey,
    @zoneMidLeftCorner, @zoneMidRightCorner, @zoneMidLeftElbow, @zoneMidRightElbow, @zoneMidTopKey,
    @zoneCloseLeft, @zoneCloseCenter, @zoneCloseRight
);";
                AddTendencyParameters(cmd, player);
                cmd.ExecuteNonQuery();
            }

            // 默认模拟档案（上场时间按 OVR 粗估）
            float defaultMpg = player.Overall >= 80 ? 28f : player.Overall >= 70 ? 20f : 12f;
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
INSERT OR REPLACE INTO player_simulation_profiles (player_id, team_id, source_mpg, rotation_role, minute_floor, minute_ceiling)
VALUES (@pid, @tid, @mpg, @role, @floor, @ceiling);";
                cmd.Parameters.AddWithValue("@pid",     newId);
                cmd.Parameters.AddWithValue("@tid",     player.TeamId);
                cmd.Parameters.AddWithValue("@mpg",     defaultMpg);
                cmd.Parameters.AddWithValue("@role",    player.Overall >= 75 ? "STARTER" : "BENCH");
                cmd.Parameters.AddWithValue("@floor",   defaultMpg * 0.6f);
                cmd.Parameters.AddWithValue("@ceiling",  defaultMpg * 1.4f);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
            return newId;
        }

        public IReadOnlyList<Player> GetFreeAgents()
        {
            var players = new List<Player>();
            using var connection = _databaseManager.OpenConnection();
            using var command = CreatePlayerSelectCommand(connection);
            command.CommandText += "INNER JOIN free_agents fa ON fa.player_id = p.id ORDER BY p.overall DESC;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) players.Add(MapPlayer(reader));
            return players;
        }

        public IReadOnlyList<Player> GetDraftPool()
        {
            var players = new List<Player>();
            using var connection = _databaseManager.OpenConnection();
            using var command = CreatePlayerSelectCommand(connection);
            command.CommandText += "INNER JOIN draft_class dc ON dc.player_id = p.id ORDER BY p.overall DESC;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) players.Add(MapPlayer(reader));
            return players;
        }

        public void AddToFreeAgents(int playerId, int askingSalary)
        {
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO free_agents (player_id, asking_salary) VALUES (@pid, @sal);";
            cmd.Parameters.AddWithValue("@pid", playerId);
            cmd.Parameters.AddWithValue("@sal", askingSalary);
            cmd.ExecuteNonQuery();
        }

        public void RemoveFromFreeAgents(int playerId)
        {
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM free_agents WHERE player_id = @pid;";
            cmd.Parameters.AddWithValue("@pid", playerId);
            cmd.ExecuteNonQuery();
        }

        public void AddToDraftClass(int playerId, int draftYear)
        {
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO draft_class (player_id, draft_year) VALUES (@pid, @yr);";
            cmd.Parameters.AddWithValue("@pid", playerId);
            cmd.Parameters.AddWithValue("@yr",  draftYear);
            cmd.ExecuteNonQuery();
        }

        public void ClearDraftClass()
        {
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM draft_class;";
            cmd.ExecuteNonQuery();
        }

        public int GetFreeAgentAskingSalary(int playerId)
        {
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT asking_salary FROM free_agents WHERE player_id = @pid;";
            cmd.Parameters.AddWithValue("@pid", playerId);
            var result = cmd.ExecuteScalar();
            return result == null ? 0 : Convert.ToInt32(result);
        }

        public void SetInjury(int playerId, int gamesRemaining)
        {
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE players SET injury_games_remaining = @g WHERE id = @id;";
            cmd.Parameters.AddWithValue("@g",  gamesRemaining);
            cmd.Parameters.AddWithValue("@id", playerId);
            cmd.ExecuteNonQuery();
        }

        public void DecrementInjuries(string teamId)
        {
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
UPDATE players
SET injury_games_remaining = MAX(0, injury_games_remaining - 1)
WHERE team_id = @teamId AND injury_games_remaining > 0;";
            cmd.Parameters.AddWithValue("@teamId", teamId);
            cmd.ExecuteNonQuery();
        }

        public void UpdateSimProfileTeam(int playerId, string teamId)
        {
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE player_simulation_profiles SET team_id = @tid WHERE player_id = @pid;";
            cmd.Parameters.AddWithValue("@tid", teamId);
            cmd.Parameters.AddWithValue("@pid", playerId);
            cmd.ExecuteNonQuery();
        }
    }
}
