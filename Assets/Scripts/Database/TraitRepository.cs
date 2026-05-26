using System.Collections.Generic;
using BasketballManager.Core.Models;
using Mono.Data.Sqlite;

namespace BasketballManager.Database
{
    public sealed class TraitRepository
    {
        private readonly DatabaseManager _db;

        public TraitRepository(DatabaseManager db) { _db = db; }

        public IReadOnlyList<Trait> GetAllTraits()
        {
            var list = new List<Trait>();
            using var connection = _db.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, name_key, display_name, category, description_1, description_2, description_3 FROM traits ORDER BY id ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(MapTrait(reader));
            return list;
        }

        public IReadOnlyList<PlayerTrait> GetPlayerTraits(int playerId)
        {
            var list = new List<PlayerTrait>();
            using var connection = _db.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT pt.player_id, pt.trait_id, pt.star_level,
       t.name_key, t.display_name,
       CASE pt.star_level
           WHEN 1 THEN t.description_1
           WHEN 2 THEN t.description_2
           ELSE t.description_3 END AS description
FROM player_traits pt
JOIN traits t ON t.id = pt.trait_id
WHERE pt.player_id = @playerId
ORDER BY pt.trait_id ASC;";
            cmd.Parameters.AddWithValue("@playerId", playerId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(MapPlayerTrait(reader));
            return list;
        }

        public IReadOnlyList<PlayerTrait> GetPlayerTraitsBatch(IReadOnlyList<int> playerIds)
        {
            var list = new List<PlayerTrait>();
            if (playerIds == null || playerIds.Count == 0) return list;

            var ids = string.Join(",", playerIds);
            using var connection = _db.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $@"
SELECT pt.player_id, pt.trait_id, pt.star_level,
       t.name_key, t.display_name,
       CASE pt.star_level
           WHEN 1 THEN t.description_1
           WHEN 2 THEN t.description_2
           ELSE t.description_3 END AS description
FROM player_traits pt
JOIN traits t ON t.id = pt.trait_id
WHERE pt.player_id IN ({ids})
ORDER BY pt.player_id ASC, pt.trait_id ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(MapPlayerTrait(reader));
            return list;
        }

        public void AddPlayerTrait(int playerId, int traitId, int starLevel)
        {
            using var connection = _db.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO player_traits (player_id, trait_id, star_level) VALUES (@playerId, @traitId, @starLevel);";
            cmd.Parameters.AddWithValue("@playerId", playerId);
            cmd.Parameters.AddWithValue("@traitId", traitId);
            cmd.Parameters.AddWithValue("@starLevel", starLevel);
            cmd.ExecuteNonQuery();
        }

        public void RemovePlayerTrait(int playerId, int traitId)
        {
            using var connection = _db.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM player_traits WHERE player_id = @playerId AND trait_id = @traitId;";
            cmd.Parameters.AddWithValue("@playerId", playerId);
            cmd.Parameters.AddWithValue("@traitId", traitId);
            cmd.ExecuteNonQuery();
        }

        public void UpdateStarLevel(int playerId, int traitId, int starLevel)
        {
            using var connection = _db.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE player_traits SET star_level = @starLevel WHERE player_id = @playerId AND trait_id = @traitId;";
            cmd.Parameters.AddWithValue("@starLevel", starLevel);
            cmd.Parameters.AddWithValue("@playerId", playerId);
            cmd.Parameters.AddWithValue("@traitId", traitId);
            cmd.ExecuteNonQuery();
        }

        private static Trait MapTrait(SqliteDataReader reader) => new Trait
        {
            Id = int.TryParse(reader["id"]?.ToString(), out var id) ? id : 0,
            NameKey = reader["name_key"]?.ToString() ?? string.Empty,
            DisplayName = reader["display_name"]?.ToString() ?? string.Empty,
            Category = reader["category"]?.ToString() ?? string.Empty,
            Description1 = reader["description_1"]?.ToString() ?? string.Empty,
            Description2 = reader["description_2"]?.ToString() ?? string.Empty,
            Description3 = reader["description_3"]?.ToString() ?? string.Empty,
        };

        private static PlayerTrait MapPlayerTrait(SqliteDataReader reader) => new PlayerTrait
        {
            PlayerId = int.TryParse(reader["player_id"]?.ToString(), out var pid) ? pid : 0,
            TraitId = int.TryParse(reader["trait_id"]?.ToString(), out var tid) ? tid : 0,
            StarLevel = int.TryParse(reader["star_level"]?.ToString(), out var sl) ? sl : 1,
            NameKey = reader["name_key"]?.ToString() ?? string.Empty,
            DisplayName = reader["display_name"]?.ToString() ?? string.Empty,
            Description = reader["description"]?.ToString() ?? string.Empty,
        };
    }
}
