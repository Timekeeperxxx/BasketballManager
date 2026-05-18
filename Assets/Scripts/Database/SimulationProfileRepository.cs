using System;
using System.Collections.Generic;
using BasketballManager.Core.Models;
using Mono.Data.Sqlite;

namespace BasketballManager.Database
{
    public sealed class SimulationProfileRepository
    {
        private readonly DatabaseManager _databaseManager;

        public SimulationProfileRepository(DatabaseManager databaseManager)
        {
            _databaseManager = databaseManager;
        }

        public IReadOnlyDictionary<int, SimulationPlayerProfile> GetAllProfiles()
        {
            var profiles = new Dictionary<int, SimulationPlayerProfile>();

            using var connection = _databaseManager.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    player_id,
    team_id,
    source_mpg,
    rotation_role,
    minute_floor,
    minute_ceiling
FROM player_simulation_profiles;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var profile = new SimulationPlayerProfile
                {
                    PlayerId = Convert.ToInt32(reader["player_id"]),
                    TeamId = reader["team_id"].ToString(),
                    SourceMpg = Convert.ToSingle(reader["source_mpg"]),
                    RotationRole = reader["rotation_role"].ToString(),
                    MinuteFloor = Convert.ToSingle(reader["minute_floor"]),
                    MinuteCeiling = Convert.ToSingle(reader["minute_ceiling"])
                };
                profiles[profile.PlayerId] = profile;
            }

            return profiles;
        }
    }
}
