using System.Collections.Generic;
using BasketballManager.Core.Models;

namespace BasketballManager.Database
{
    public sealed class TeamRepository
    {
        private readonly DatabaseManager _databaseManager;

        public TeamRepository(DatabaseManager databaseManager)
        {
            _databaseManager = databaseManager;
        }

        public IReadOnlyList<Team> GetAllTeams()
        {
            var teams = new List<Team>();

            using var connection = _databaseManager.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    id,
    name,
    COALESCE(city, '') AS city,
    COALESCE(era, 0) AS era,
    COALESCE(is_current, 0) AS is_current
FROM teams
ORDER BY era ASC, id ASC;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                teams.Add(new Team
                {
                    Id = reader["id"].ToString() ?? string.Empty,
                    Name = reader["name"].ToString() ?? string.Empty,
                    City = reader["city"].ToString() ?? string.Empty,
                    Era = ReadInt(reader["era"]),
                    IsCurrent = ReadInt(reader["is_current"]) != 0
                });
            }

            return teams;
        }

        private static int ReadInt(object value)
        {
            return int.TryParse(value?.ToString(), out var parsed) ? parsed : 0;
        }
    }
}
