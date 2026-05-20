using System;
using System.Collections.Generic;
using BasketballManager.Core.Models;
using Mono.Data.Sqlite;

namespace BasketballManager.Database
{
    /// <summary>
    /// 赛季数据访问：创建赛季、查询赛程/积分榜/球员累计、写入比赛结果与统计。
    /// </summary>
    public sealed class SeasonRepository
    {
        private readonly DatabaseManager _databaseManager;

        public SeasonRepository(DatabaseManager databaseManager)
        {
            _databaseManager = databaseManager;
        }

        /// <summary>
        /// 在一个事务里写入赛季元数据、参赛球队、初始赛程。返回新赛季 id。
        /// </summary>
        public int CreateSeason(string name, IReadOnlyList<Team> teams, IReadOnlyList<SeasonGame> games)
        {
            using var connection = _databaseManager.OpenConnection();
            using var transaction = connection.BeginTransaction();

            int seasonId;
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "INSERT INTO seasons (name, status) VALUES (@name, 'IN_PROGRESS');";
                cmd.Parameters.AddWithValue("@name", name);
                cmd.ExecuteNonQuery();
            }
            // Mono.Data.Sqlite 对"多语句单 cmd"支持不稳；拆成第二条 SELECT 取 rowid。
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT last_insert_rowid();";
                seasonId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            foreach (var team in teams)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
INSERT INTO season_teams (season_id, team_id, wins, losses, points_for, points_against)
VALUES (@season_id, @team_id, 0, 0, 0, 0);";
                cmd.Parameters.AddWithValue("@season_id", seasonId);
                cmd.Parameters.AddWithValue("@team_id", team.Id);
                cmd.ExecuteNonQuery();
            }

            foreach (var g in games)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
INSERT INTO season_games (season_id, day, home_team_id, away_team_id, status)
VALUES (@season_id, @day, @home, @away, 'SCHEDULED');";
                cmd.Parameters.AddWithValue("@season_id", seasonId);
                cmd.Parameters.AddWithValue("@day", g.Day);
                cmd.Parameters.AddWithValue("@home", g.HomeTeamId);
                cmd.Parameters.AddWithValue("@away", g.AwayTeamId);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
            return seasonId;
        }

        public Season GetLatestSeason()
        {
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, name, status, created_at FROM seasons ORDER BY id DESC LIMIT 1;";
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapSeason(reader) : null;
        }

        public Season GetSeasonById(int seasonId)
        {
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, name, status, created_at FROM seasons WHERE id = @id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", seasonId);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapSeason(reader) : null;
        }

        public IReadOnlyList<Team> GetSeasonTeams(int seasonId)
        {
            var teams = new List<Team>();
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT t.id, t.name, COALESCE(t.city, '') AS city, COALESCE(t.era, 0) AS era, COALESCE(t.is_current, 0) AS is_current
FROM season_teams st
JOIN teams t ON t.id = st.team_id
WHERE st.season_id = @season_id
ORDER BY t.era ASC, t.id ASC;";
            cmd.Parameters.AddWithValue("@season_id", seasonId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                teams.Add(new Team
                {
                    Id = reader["id"].ToString() ?? string.Empty,
                    Name = reader["name"].ToString() ?? string.Empty,
                    City = reader["city"].ToString() ?? string.Empty,
                    Era = ReadInt(reader["era"]),
                    IsCurrent = ReadInt(reader["is_current"]) != 0,
                });
            }
            return teams;
        }

        public IReadOnlyList<SeasonGame> GetSeasonGames(int seasonId)
        {
            var games = new List<SeasonGame>();
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT id, season_id, day, home_team_id, away_team_id, home_score, away_score, status,
       COALESCE(winner_team_id, '') AS winner_team_id, COALESCE(played_at, '') AS played_at
FROM season_games
WHERE season_id = @season_id
ORDER BY day ASC, id ASC;";
            cmd.Parameters.AddWithValue("@season_id", seasonId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                games.Add(MapGame(reader));
            }
            return games;
        }

        public SeasonGame GetNextScheduledGame(int seasonId)
        {
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT id, season_id, day, home_team_id, away_team_id, home_score, away_score, status,
       COALESCE(winner_team_id, '') AS winner_team_id, COALESCE(played_at, '') AS played_at
FROM season_games
WHERE season_id = @season_id AND status = 'SCHEDULED'
ORDER BY day ASC, id ASC
LIMIT 1;";
            cmd.Parameters.AddWithValue("@season_id", seasonId);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapGame(reader) : null;
        }

        public void SaveGameResult(int gameId, MatchResult result)
        {
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
UPDATE season_games SET
    home_score = @home_score,
    away_score = @away_score,
    status = 'PLAYED',
    winner_team_id = @winner,
    played_at = CURRENT_TIMESTAMP
WHERE id = @id;";
            cmd.Parameters.AddWithValue("@home_score", result.HomeScore);
            cmd.Parameters.AddWithValue("@away_score", result.AwayScore);
            cmd.Parameters.AddWithValue("@winner", result.WinnerTeamId ?? string.Empty);
            cmd.Parameters.AddWithValue("@id", gameId);
            cmd.ExecuteNonQuery();
        }

        public void UpdateStandings(int seasonId, MatchResult result)
        {
            using var connection = _databaseManager.OpenConnection();
            using var transaction = connection.BeginTransaction();

            bool homeWon = result.HomeScore > result.AwayScore;

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
UPDATE season_teams SET
    wins = wins + @win,
    losses = losses + @loss,
    points_for = points_for + @pf,
    points_against = points_against + @pa
WHERE season_id = @season_id AND team_id = @team_id;";
                cmd.Parameters.AddWithValue("@season_id", seasonId);
                cmd.Parameters.AddWithValue("@team_id", result.HomeTeamId);
                cmd.Parameters.AddWithValue("@win", homeWon ? 1 : 0);
                cmd.Parameters.AddWithValue("@loss", homeWon ? 0 : 1);
                cmd.Parameters.AddWithValue("@pf", result.HomeScore);
                cmd.Parameters.AddWithValue("@pa", result.AwayScore);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
UPDATE season_teams SET
    wins = wins + @win,
    losses = losses + @loss,
    points_for = points_for + @pf,
    points_against = points_against + @pa
WHERE season_id = @season_id AND team_id = @team_id;";
                cmd.Parameters.AddWithValue("@season_id", seasonId);
                cmd.Parameters.AddWithValue("@team_id", result.AwayTeamId);
                cmd.Parameters.AddWithValue("@win", homeWon ? 0 : 1);
                cmd.Parameters.AddWithValue("@loss", homeWon ? 1 : 0);
                cmd.Parameters.AddWithValue("@pf", result.AwayScore);
                cmd.Parameters.AddWithValue("@pa", result.HomeScore);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        public void UpdatePlayerStats(int seasonId, MatchResult result)
        {
            using var connection = _databaseManager.OpenConnection();
            using var transaction = connection.BeginTransaction();

            UpsertTeamPlayerStats(connection, transaction, seasonId, result.HomeTeamId, result.HomePlayerStats);
            UpsertTeamPlayerStats(connection, transaction, seasonId, result.AwayTeamId, result.AwayPlayerStats);

            transaction.Commit();
        }

        public IReadOnlyList<SeasonTeamStanding> GetStandings(int seasonId)
        {
            var standings = new List<SeasonTeamStanding>();
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            // SQLite 不支持 NULLS LAST；用 (wins+losses)=0 时排在最后的等价表达。
            cmd.CommandText = @"
SELECT st.season_id, st.team_id, t.name AS team_name,
       st.wins, st.losses, st.points_for, st.points_against
FROM season_teams st
JOIN teams t ON t.id = st.team_id
WHERE st.season_id = @season_id
ORDER BY CASE WHEN (st.wins + st.losses) = 0 THEN 1 ELSE 0 END ASC,
         CASE WHEN (st.wins + st.losses) = 0
              THEN 0
              ELSE CAST(st.wins AS REAL) / (st.wins + st.losses)
         END DESC,
         st.wins DESC,
         (st.points_for - st.points_against) DESC,
         t.id ASC;";
            cmd.Parameters.AddWithValue("@season_id", seasonId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                standings.Add(new SeasonTeamStanding
                {
                    SeasonId = ReadInt(reader["season_id"]),
                    TeamId = reader["team_id"].ToString() ?? string.Empty,
                    TeamName = reader["team_name"].ToString() ?? string.Empty,
                    Wins = ReadInt(reader["wins"]),
                    Losses = ReadInt(reader["losses"]),
                    PointsFor = ReadInt(reader["points_for"]),
                    PointsAgainst = ReadInt(reader["points_against"]),
                });
            }
            return standings;
        }

        public IReadOnlyList<SeasonPlayerStat> GetPlayerSeasonStats(int seasonId, string teamId)
        {
            var stats = new List<SeasonPlayerStat>();
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT sps.season_id, sps.player_id, sps.team_id,
       p.first_name || ' ' || p.last_name AS player_name,
       sps.games_played, sps.minutes, sps.points, sps.rebounds, sps.offensive_rebounds,
       sps.assists, sps.steals, sps.blocks, sps.turnovers, sps.fouls,
       sps.field_goals_made, sps.field_goals_attempted,
       sps.three_pointers_made, sps.three_pointers_attempted,
       sps.free_throws_made, sps.free_throws_attempted,
       sps.plus_minus
FROM season_player_stats sps
JOIN players p ON p.id = sps.player_id
WHERE sps.season_id = @season_id AND sps.team_id = @team_id
ORDER BY sps.points DESC, sps.player_id ASC;";
            cmd.Parameters.AddWithValue("@season_id", seasonId);
            cmd.Parameters.AddWithValue("@team_id", teamId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                stats.Add(new SeasonPlayerStat
                {
                    SeasonId = ReadInt(reader["season_id"]),
                    PlayerId = ReadInt(reader["player_id"]),
                    TeamId = reader["team_id"].ToString() ?? string.Empty,
                    PlayerName = reader["player_name"].ToString() ?? string.Empty,
                    GamesPlayed = ReadInt(reader["games_played"]),
                    Minutes = ReadInt(reader["minutes"]),
                    Points = ReadInt(reader["points"]),
                    Rebounds = ReadInt(reader["rebounds"]),
                    OffensiveRebounds = ReadInt(reader["offensive_rebounds"]),
                    Assists = ReadInt(reader["assists"]),
                    Steals = ReadInt(reader["steals"]),
                    Blocks = ReadInt(reader["blocks"]),
                    Turnovers = ReadInt(reader["turnovers"]),
                    Fouls = ReadInt(reader["fouls"]),
                    FieldGoalsMade = ReadInt(reader["field_goals_made"]),
                    FieldGoalsAttempted = ReadInt(reader["field_goals_attempted"]),
                    ThreePointersMade = ReadInt(reader["three_pointers_made"]),
                    ThreePointersAttempted = ReadInt(reader["three_pointers_attempted"]),
                    FreeThrowsMade = ReadInt(reader["free_throws_made"]),
                    FreeThrowsAttempted = ReadInt(reader["free_throws_attempted"]),
                    PlusMinus = ReadInt(reader["plus_minus"]),
                });
            }
            return stats;
        }

        /// <summary>
        /// 保存单场比赛的球员 box score（写入 game_player_stats）。
        /// </summary>
        public void SaveGamePlayerStats(int gameId, MatchResult result)
        {
            using var connection = _databaseManager.OpenConnection();
            using var transaction = connection.BeginTransaction();

            SaveTeamGameStats(connection, transaction, gameId, result.HomeTeamId, result.HomePlayerStats);
            SaveTeamGameStats(connection, transaction, gameId, result.AwayTeamId, result.AwayPlayerStats);

            transaction.Commit();
        }

        /// <summary>
        /// 查询指定比赛中某支球队的球员 box score。
        /// </summary>
        public List<PlayerBoxScore> GetGamePlayerStats(int gameId, string teamId)
        {
            var stats = new List<PlayerBoxScore>();
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT player_id, team_id, player_name, position,
       minutes, points, rebounds, offensive_rebounds,
       assists, steals, blocks, turnovers, fouls,
       field_goals_made, field_goals_attempted,
       three_pointers_made, three_pointers_attempted,
       free_throws_made, free_throws_attempted,
       plus_minus
FROM game_player_stats
WHERE game_id = @game_id AND team_id = @team_id
ORDER BY points DESC, player_id ASC;";
            cmd.Parameters.AddWithValue("@game_id", gameId);
            cmd.Parameters.AddWithValue("@team_id", teamId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var orb = ReadInt(reader["offensive_rebounds"]);
                var reb = ReadInt(reader["rebounds"]);
                stats.Add(new PlayerBoxScore
                {
                    PlayerId              = ReadInt(reader["player_id"]),
                    TeamId                = reader["team_id"].ToString() ?? string.Empty,
                    PlayerName            = reader["player_name"].ToString() ?? string.Empty,
                    Position              = reader["position"].ToString() ?? string.Empty,
                    Minutes               = ReadInt(reader["minutes"]),
                    Points                = ReadInt(reader["points"]),
                    OffensiveRebounds     = orb,
                    DefensiveRebounds     = reb - orb,
                    Assists               = ReadInt(reader["assists"]),
                    Steals                = ReadInt(reader["steals"]),
                    Blocks                = ReadInt(reader["blocks"]),
                    Turnovers             = ReadInt(reader["turnovers"]),
                    Fouls                 = ReadInt(reader["fouls"]),
                    FieldGoalsMade        = ReadInt(reader["field_goals_made"]),
                    FieldGoalsAttempted   = ReadInt(reader["field_goals_attempted"]),
                    ThreePointersMade     = ReadInt(reader["three_pointers_made"]),
                    ThreePointersAttempted = ReadInt(reader["three_pointers_attempted"]),
                    FreeThrowsMade        = ReadInt(reader["free_throws_made"]),
                    FreeThrowsAttempted   = ReadInt(reader["free_throws_attempted"]),
                    PlusMinus             = ReadInt(reader["plus_minus"]),
                });
            }
            return stats;
        }

        // -------- helpers --------

        private static void SaveTeamGameStats(SqliteConnection connection, SqliteTransaction transaction, int gameId, string teamId, List<PlayerBoxScore> players)
        {
            foreach (var p in players)
            {
                if (p.Minutes <= 0) continue;
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
INSERT OR REPLACE INTO game_player_stats (
    game_id, player_id, team_id, player_name, position,
    minutes, points, rebounds, offensive_rebounds,
    assists, steals, blocks, turnovers, fouls,
    field_goals_made, field_goals_attempted,
    three_pointers_made, three_pointers_attempted,
    free_throws_made, free_throws_attempted,
    plus_minus
) VALUES (
    @game_id, @player_id, @team_id, @player_name, @position,
    @minutes, @points, @rebounds, @oreb,
    @assists, @steals, @blocks, @turnovers, @fouls,
    @fgm, @fga, @tpm, @tpa, @ftm, @fta, @pm
);";
                cmd.Parameters.AddWithValue("@game_id", gameId);
                cmd.Parameters.AddWithValue("@player_id", p.PlayerId);
                cmd.Parameters.AddWithValue("@team_id", teamId);
                cmd.Parameters.AddWithValue("@player_name", p.PlayerName);
                cmd.Parameters.AddWithValue("@position", p.Position);
                cmd.Parameters.AddWithValue("@minutes", p.Minutes);
                cmd.Parameters.AddWithValue("@points", p.Points);
                cmd.Parameters.AddWithValue("@rebounds", p.Rebounds);
                cmd.Parameters.AddWithValue("@oreb", p.OffensiveRebounds);
                cmd.Parameters.AddWithValue("@assists", p.Assists);
                cmd.Parameters.AddWithValue("@steals", p.Steals);
                cmd.Parameters.AddWithValue("@blocks", p.Blocks);
                cmd.Parameters.AddWithValue("@turnovers", p.Turnovers);
                cmd.Parameters.AddWithValue("@fouls", p.Fouls);
                cmd.Parameters.AddWithValue("@fgm", p.FieldGoalsMade);
                cmd.Parameters.AddWithValue("@fga", p.FieldGoalsAttempted);
                cmd.Parameters.AddWithValue("@tpm", p.ThreePointersMade);
                cmd.Parameters.AddWithValue("@tpa", p.ThreePointersAttempted);
                cmd.Parameters.AddWithValue("@ftm", p.FreeThrowsMade);
                cmd.Parameters.AddWithValue("@fta", p.FreeThrowsAttempted);
                cmd.Parameters.AddWithValue("@pm", p.PlusMinus);
                cmd.ExecuteNonQuery();
            }
        }

        private static void UpsertTeamPlayerStats(SqliteConnection connection, SqliteTransaction transaction, int seasonId, string teamId, List<PlayerBoxScore> players)
        {
            foreach (var p in players)
            {
                // 只累计真正上场的球员（避免给 0 分钟替补虚增一场 games_played）。
                if (p.Minutes <= 0) continue;

                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
INSERT INTO season_player_stats (
    season_id, player_id, team_id,
    games_played, minutes, points, rebounds, offensive_rebounds,
    assists, steals, blocks, turnovers, fouls,
    field_goals_made, field_goals_attempted,
    three_pointers_made, three_pointers_attempted,
    free_throws_made, free_throws_attempted,
    plus_minus
) VALUES (
    @season_id, @player_id, @team_id,
    1, @minutes, @points, @rebounds, @oreb,
    @assists, @steals, @blocks, @turnovers, @fouls,
    @fgm, @fga, @tpm, @tpa, @ftm, @fta, @pm
)
ON CONFLICT(season_id, player_id) DO UPDATE SET
    games_played = games_played + 1,
    minutes = minutes + excluded.minutes,
    points = points + excluded.points,
    rebounds = rebounds + excluded.rebounds,
    offensive_rebounds = offensive_rebounds + excluded.offensive_rebounds,
    assists = assists + excluded.assists,
    steals = steals + excluded.steals,
    blocks = blocks + excluded.blocks,
    turnovers = turnovers + excluded.turnovers,
    fouls = fouls + excluded.fouls,
    field_goals_made = field_goals_made + excluded.field_goals_made,
    field_goals_attempted = field_goals_attempted + excluded.field_goals_attempted,
    three_pointers_made = three_pointers_made + excluded.three_pointers_made,
    three_pointers_attempted = three_pointers_attempted + excluded.three_pointers_attempted,
    free_throws_made = free_throws_made + excluded.free_throws_made,
    free_throws_attempted = free_throws_attempted + excluded.free_throws_attempted,
    plus_minus = plus_minus + excluded.plus_minus;";
                cmd.Parameters.AddWithValue("@season_id", seasonId);
                cmd.Parameters.AddWithValue("@player_id", p.PlayerId);
                cmd.Parameters.AddWithValue("@team_id", teamId);
                cmd.Parameters.AddWithValue("@minutes", p.Minutes);
                cmd.Parameters.AddWithValue("@points", p.Points);
                cmd.Parameters.AddWithValue("@rebounds", p.Rebounds);
                cmd.Parameters.AddWithValue("@oreb", p.OffensiveRebounds);
                cmd.Parameters.AddWithValue("@assists", p.Assists);
                cmd.Parameters.AddWithValue("@steals", p.Steals);
                cmd.Parameters.AddWithValue("@blocks", p.Blocks);
                cmd.Parameters.AddWithValue("@turnovers", p.Turnovers);
                cmd.Parameters.AddWithValue("@fouls", p.Fouls);
                cmd.Parameters.AddWithValue("@fgm", p.FieldGoalsMade);
                cmd.Parameters.AddWithValue("@fga", p.FieldGoalsAttempted);
                cmd.Parameters.AddWithValue("@tpm", p.ThreePointersMade);
                cmd.Parameters.AddWithValue("@tpa", p.ThreePointersAttempted);
                cmd.Parameters.AddWithValue("@ftm", p.FreeThrowsMade);
                cmd.Parameters.AddWithValue("@fta", p.FreeThrowsAttempted);
                cmd.Parameters.AddWithValue("@pm", p.PlusMinus);
                cmd.ExecuteNonQuery();
            }
        }

        private static Season MapSeason(SqliteDataReader reader)
        {
            return new Season
            {
                Id = ReadInt(reader["id"]),
                Name = reader["name"].ToString() ?? string.Empty,
                Status = reader["status"].ToString() ?? "IN_PROGRESS",
                CreatedAt = reader["created_at"].ToString() ?? string.Empty,
            };
        }

        private static SeasonGame MapGame(SqliteDataReader reader)
        {
            return new SeasonGame
            {
                Id = ReadInt(reader["id"]),
                SeasonId = ReadInt(reader["season_id"]),
                Day = ReadInt(reader["day"]),
                HomeTeamId = reader["home_team_id"].ToString() ?? string.Empty,
                AwayTeamId = reader["away_team_id"].ToString() ?? string.Empty,
                HomeScore = ReadInt(reader["home_score"]),
                AwayScore = ReadInt(reader["away_score"]),
                Status = reader["status"].ToString() ?? "SCHEDULED",
                WinnerTeamId = reader["winner_team_id"].ToString() ?? string.Empty,
                PlayedAt = reader["played_at"].ToString() ?? string.Empty,
            };
        }

        private static int ReadInt(object value)
        {
            return int.TryParse(value?.ToString(), out var parsed) ? parsed : 0;
        }
    }
}
