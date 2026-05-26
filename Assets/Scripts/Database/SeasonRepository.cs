using System;
using System.Collections.Generic;
using System.Linq;
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
        public int CreateSeason(string name, IReadOnlyList<Team> teams, IReadOnlyList<SeasonGame> games, int seasonNumber = 1)
        {
            using var connection = _databaseManager.OpenConnection();
            using var transaction = connection.BeginTransaction();

            int seasonId;
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "INSERT INTO seasons (name, status, phase, season_number) VALUES (@name, 'IN_PROGRESS', 'REGULAR', @seasonNumber);";
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@seasonNumber", seasonNumber);
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
            cmd.CommandText = "SELECT id, name, status, COALESCE(phase, 'REGULAR') AS phase, created_at, COALESCE(season_number, 1) AS season_number FROM seasons ORDER BY id DESC LIMIT 1;";
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapSeason(reader) : null;
        }

        public Season GetSeasonById(int seasonId)
        {
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, name, status, COALESCE(phase, 'REGULAR') AS phase, created_at, COALESCE(season_number, 1) AS season_number FROM seasons WHERE id = @id LIMIT 1;";
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
       COALESCE(winner_team_id, '') AS winner_team_id, COALESCE(played_at, '') AS played_at,
       COALESCE(phase, 'REGULAR') AS phase, COALESCE(series_id, 0) AS series_id
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
       COALESCE(winner_team_id, '') AS winner_team_id, COALESCE(played_at, '') AS played_at,
       COALESCE(phase, 'REGULAR') AS phase, COALESCE(series_id, 0) AS series_id
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

        // -------- playoff --------

        public void InitializePlayoffs(int seasonId, IReadOnlyList<PlayoffSeries> seriesList)
        {
            using var connection = _databaseManager.OpenConnection();
            using var transaction = connection.BeginTransaction();

            // 取当前最大 day，季后赛从 day+1 开始编号
            int maxDay;
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT COALESCE(MAX(day), 0) FROM season_games WHERE season_id = @sid;";
                cmd.Parameters.AddWithValue("@sid", seasonId);
                maxDay = Convert.ToInt32(cmd.ExecuteScalar());
            }

            int dayCounter = maxDay + 1;

            foreach (var s in seriesList)
            {
                int seriesId;
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
INSERT INTO playoff_series (season_id, round, seed1, seed2, team1_id, team2_id)
VALUES (@season_id, @round, @seed1, @seed2, @team1_id, @team2_id);";
                    cmd.Parameters.AddWithValue("@season_id", seasonId);
                    cmd.Parameters.AddWithValue("@round", s.Round);
                    cmd.Parameters.AddWithValue("@seed1", s.Seed1);
                    cmd.Parameters.AddWithValue("@seed2", s.Seed2);
                    cmd.Parameters.AddWithValue("@team1_id", s.Team1Id);
                    cmd.Parameters.AddWithValue("@team2_id", s.Team2Id);
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "SELECT last_insert_rowid();";
                    seriesId = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // 7 场对局：1,2,5,7 主场=team1；3,4,6 主场=team2
                for (int gameNum = 1; gameNum <= 7; gameNum++)
                {
                    bool team1Home = gameNum == 1 || gameNum == 2 || gameNum == 5 || gameNum == 7;
                    string home = team1Home ? s.Team1Id : s.Team2Id;
                    string away = team1Home ? s.Team2Id : s.Team1Id;
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
INSERT INTO season_games (season_id, day, home_team_id, away_team_id, status, phase, series_id)
VALUES (@season_id, @day, @home, @away, 'SCHEDULED', 'PLAYOFF', @series_id);";
                    cmd.Parameters.AddWithValue("@season_id", seasonId);
                    cmd.Parameters.AddWithValue("@day", dayCounter++);
                    cmd.Parameters.AddWithValue("@home", home);
                    cmd.Parameters.AddWithValue("@away", away);
                    cmd.Parameters.AddWithValue("@series_id", seriesId);
                    cmd.ExecuteNonQuery();
                }
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "UPDATE seasons SET phase = 'PLAYOFF' WHERE id = @id;";
                cmd.Parameters.AddWithValue("@id", seasonId);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        public IReadOnlyList<PlayoffSeries> GetPlayoffSeries(int seasonId)
        {
            var result = new List<PlayoffSeries>();
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT ps.id, ps.season_id, ps.round, ps.seed1, ps.seed2,
       ps.team1_id, ps.team2_id,
       COALESCE(t1.name, ps.team1_id) AS team1_name,
       COALESCE(t2.name, ps.team2_id) AS team2_name,
       ps.team1_wins, ps.team2_wins, ps.status,
       COALESCE(ps.winner_team_id, '') AS winner_team_id
FROM playoff_series ps
LEFT JOIN teams t1 ON t1.id = ps.team1_id
LEFT JOIN teams t2 ON t2.id = ps.team2_id
WHERE ps.season_id = @season_id
ORDER BY ps.round ASC, ps.id ASC;";
            cmd.Parameters.AddWithValue("@season_id", seasonId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new PlayoffSeries
                {
                    Id = ReadInt(reader["id"]),
                    SeasonId = ReadInt(reader["season_id"]),
                    Round = ReadInt(reader["round"]),
                    Seed1 = ReadInt(reader["seed1"]),
                    Seed2 = ReadInt(reader["seed2"]),
                    Team1Id = reader["team1_id"].ToString() ?? string.Empty,
                    Team2Id = reader["team2_id"].ToString() ?? string.Empty,
                    Team1Name = reader["team1_name"].ToString() ?? string.Empty,
                    Team2Name = reader["team2_name"].ToString() ?? string.Empty,
                    Team1Wins = ReadInt(reader["team1_wins"]),
                    Team2Wins = ReadInt(reader["team2_wins"]),
                    Status = reader["status"].ToString() ?? "IN_PROGRESS",
                    WinnerTeamId = reader["winner_team_id"].ToString() ?? string.Empty,
                });
            }
            return result;
        }

        /// <summary>
        /// 更新系列赛胜场；若某队达到4胜则完成该系列赛并取消剩余对局。返回本次是否完成。
        /// </summary>
        public bool UpdatePlayoffSeries(int seriesId, MatchResult result)
        {
            using var connection = _databaseManager.OpenConnection();
            using var transaction = connection.BeginTransaction();

            // 读取当前系列赛信息
            string team1Id, team2Id;
            int team1Wins, team2Wins;
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT team1_id, team2_id, team1_wins, team2_wins FROM playoff_series WHERE id = @id;";
                cmd.Parameters.AddWithValue("@id", seriesId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) { transaction.Commit(); return false; }
                team1Id = r["team1_id"].ToString() ?? string.Empty;
                team2Id = r["team2_id"].ToString() ?? string.Empty;
                team1Wins = ReadInt(r["team1_wins"]);
                team2Wins = ReadInt(r["team2_wins"]);
            }

            bool homeWon = result.HomeScore > result.AwayScore;
            string winnerOfGame = homeWon ? result.HomeTeamId : result.AwayTeamId;
            if (winnerOfGame == team1Id) team1Wins++;
            else team2Wins++;

            bool seriesComplete = team1Wins >= 4 || team2Wins >= 4;
            string seriesWinner = team1Wins >= 4 ? team1Id : (team2Wins >= 4 ? team2Id : string.Empty);

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                if (seriesComplete)
                {
                    cmd.CommandText = @"
UPDATE playoff_series SET team1_wins = @w1, team2_wins = @w2,
    status = 'COMPLETE', winner_team_id = @winner WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@winner", seriesWinner);
                }
                else
                {
                    cmd.CommandText = "UPDATE playoff_series SET team1_wins = @w1, team2_wins = @w2 WHERE id = @id;";
                }
                cmd.Parameters.AddWithValue("@w1", team1Wins);
                cmd.Parameters.AddWithValue("@w2", team2Wins);
                cmd.Parameters.AddWithValue("@id", seriesId);
                cmd.ExecuteNonQuery();
            }

            if (seriesComplete)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "UPDATE season_games SET status = 'CANCELLED' WHERE series_id = @sid AND status = 'SCHEDULED';";
                cmd.Parameters.AddWithValue("@sid", seriesId);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
            return seriesComplete;
        }

        /// <summary>
        /// 检查当前轮是否全部完成，若是则推进到下一轮（或标记赛季结束）。
        /// </summary>
        public void TryAdvancePlayoffRound(int seasonId)
        {
            using var connection = _databaseManager.OpenConnection();

            // 当前最大轮次
            int currentRound;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COALESCE(MAX(round), 0) FROM playoff_series WHERE season_id = @sid;";
                cmd.Parameters.AddWithValue("@sid", seasonId);
                currentRound = Convert.ToInt32(cmd.ExecuteScalar());
            }
            if (currentRound == 0) return;

            // 检查当前轮是否全完成
            int notComplete;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM playoff_series WHERE season_id = @sid AND round = @round AND status != 'COMPLETE';";
                cmd.Parameters.AddWithValue("@sid", seasonId);
                cmd.Parameters.AddWithValue("@round", currentRound);
                notComplete = Convert.ToInt32(cmd.ExecuteScalar());
            }
            if (notComplete > 0) return;

            // 收集胜者（按 id 顺序保留原始配对信息）
            var winners = new List<(int seriesIndex, string winnerId, string winnerName, int winnerSeed)>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
SELECT ps.id, ps.winner_team_id, COALESCE(t.name, ps.winner_team_id) AS winner_name,
       CASE WHEN ps.winner_team_id = ps.team1_id THEN ps.seed1 ELSE ps.seed2 END AS winner_seed
FROM playoff_series ps
LEFT JOIN teams t ON t.id = ps.winner_team_id
WHERE ps.season_id = @sid AND ps.round = @round
ORDER BY ps.id ASC;";
                cmd.Parameters.AddWithValue("@sid", seasonId);
                cmd.Parameters.AddWithValue("@round", currentRound);
                using var r = cmd.ExecuteReader();
                int idx = 0;
                while (r.Read())
                    winners.Add((idx++, r["winner_team_id"].ToString() ?? string.Empty,
                                 r["winner_name"].ToString() ?? string.Empty,
                                 ReadInt(r["winner_seed"])));
            }

            if (winners.Count == 1)
            {
                // 总决赛结束，赛季完成
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE seasons SET status = 'FINISHED' WHERE id = @id;";
                cmd.Parameters.AddWithValue("@id", seasonId);
                cmd.ExecuteNonQuery();
                return;
            }

            // 生成下一轮系列赛：配对规则：[0]v[n-1], [1]v[n-2], ...
            int nextRound = currentRound + 1;
            int maxDay;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COALESCE(MAX(day), 0) FROM season_games WHERE season_id = @sid;";
                cmd.Parameters.AddWithValue("@sid", seasonId);
                maxDay = Convert.ToInt32(cmd.ExecuteScalar());
            }

            using var transaction = connection.BeginTransaction();
            int dayCounter = maxDay + 1;
            int n = winners.Count;
            for (int i = 0; i < n / 2; i++)
            {
                var w1 = winners[i];
                var w2 = winners[n - 1 - i];
                int newSeriesId;
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
INSERT INTO playoff_series (season_id, round, seed1, seed2, team1_id, team2_id)
VALUES (@sid, @round, @s1, @s2, @t1, @t2);";
                    cmd.Parameters.AddWithValue("@sid", seasonId);
                    cmd.Parameters.AddWithValue("@round", nextRound);
                    cmd.Parameters.AddWithValue("@s1", w1.winnerSeed);
                    cmd.Parameters.AddWithValue("@s2", w2.winnerSeed);
                    cmd.Parameters.AddWithValue("@t1", w1.winnerId);
                    cmd.Parameters.AddWithValue("@t2", w2.winnerId);
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "SELECT last_insert_rowid();";
                    newSeriesId = Convert.ToInt32(cmd.ExecuteScalar());
                }
                for (int gameNum = 1; gameNum <= 7; gameNum++)
                {
                    bool team1Home = gameNum == 1 || gameNum == 2 || gameNum == 5 || gameNum == 7;
                    string home = team1Home ? w1.winnerId : w2.winnerId;
                    string away = team1Home ? w2.winnerId : w1.winnerId;
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
INSERT INTO season_games (season_id, day, home_team_id, away_team_id, status, phase, series_id)
VALUES (@sid, @day, @home, @away, 'SCHEDULED', 'PLAYOFF', @series_id);";
                    cmd.Parameters.AddWithValue("@sid", seasonId);
                    cmd.Parameters.AddWithValue("@day", dayCounter++);
                    cmd.Parameters.AddWithValue("@home", home);
                    cmd.Parameters.AddWithValue("@away", away);
                    cmd.Parameters.AddWithValue("@series_id", newSeriesId);
                    cmd.ExecuteNonQuery();
                }
            }
            transaction.Commit();
        }

        public void UpdatePlayoffPlayerStats(int seasonId, MatchResult result)
        {
            using var connection = _databaseManager.OpenConnection();
            using var transaction = connection.BeginTransaction();

            UpsertTeamPlayoffStats(connection, transaction, seasonId, result.HomeTeamId, result.HomePlayerStats);
            UpsertTeamPlayoffStats(connection, transaction, seasonId, result.AwayTeamId, result.AwayPlayerStats);

            transaction.Commit();
        }

        public IReadOnlyList<SeasonPlayerStat> GetPlayerPlayoffStats(int seasonId, string teamId)
        {
            var stats = new List<SeasonPlayerStat>();
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT pps.season_id, pps.player_id, pps.team_id,
       p.first_name || ' ' || p.last_name AS player_name,
       pps.games_played, pps.minutes, pps.points, pps.rebounds, pps.offensive_rebounds,
       pps.assists, pps.steals, pps.blocks, pps.turnovers, pps.fouls,
       pps.field_goals_made, pps.field_goals_attempted,
       pps.three_pointers_made, pps.three_pointers_attempted,
       pps.free_throws_made, pps.free_throws_attempted,
       pps.plus_minus
FROM playoff_player_stats pps
JOIN players p ON p.id = pps.player_id
WHERE pps.season_id = @season_id AND pps.team_id = @team_id
ORDER BY pps.points DESC, pps.player_id ASC;";
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

        // -------- zone stats --------

        public void SaveGameZoneStats(int gameId, MatchResult result)
        {
            using var connection = _databaseManager.OpenConnection();
            using var transaction = connection.BeginTransaction();
            SaveTeamGameZoneStats(connection, transaction, gameId, result.HomeTeamId, result.HomePlayerStats);
            SaveTeamGameZoneStats(connection, transaction, gameId, result.AwayTeamId, result.AwayPlayerStats);
            transaction.Commit();
        }

        public void UpdateSeasonZoneStats(int seasonId, MatchResult result)
        {
            using var connection = _databaseManager.OpenConnection();
            using var transaction = connection.BeginTransaction();
            UpsertTeamZoneStats(connection, transaction, "season_zone_stats", "season_id", seasonId, result.HomeTeamId, result.HomePlayerStats);
            UpsertTeamZoneStats(connection, transaction, "season_zone_stats", "season_id", seasonId, result.AwayTeamId, result.AwayPlayerStats);
            transaction.Commit();
        }

        public void UpdatePlayoffZoneStats(int seasonId, MatchResult result)
        {
            using var connection = _databaseManager.OpenConnection();
            using var transaction = connection.BeginTransaction();
            UpsertTeamZoneStats(connection, transaction, "playoff_zone_stats", "season_id", seasonId, result.HomeTeamId, result.HomePlayerStats);
            UpsertTeamZoneStats(connection, transaction, "playoff_zone_stats", "season_id", seasonId, result.AwayTeamId, result.AwayPlayerStats);
            transaction.Commit();
        }

        public List<BasketballManager.Core.Models.PlayerZoneStat> GetPlayerSeasonZoneStats(int seasonId, int playerId)
        {
            var stats = new List<BasketballManager.Core.Models.PlayerZoneStat>();
            using var connection = _databaseManager.OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT zone, fgm, fga FROM season_zone_stats
WHERE season_id = @season_id AND player_id = @player_id;";
            cmd.Parameters.AddWithValue("@season_id", seasonId);
            cmd.Parameters.AddWithValue("@player_id", playerId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                stats.Add(new BasketballManager.Core.Models.PlayerZoneStat
                {
                    Zone = (BasketballManager.Core.Models.ShotZone)ReadInt(reader["zone"]),
                    Fgm = ReadInt(reader["fgm"]),
                    Fga = ReadInt(reader["fga"]),
                });
            }
            return stats;
        }

        private static void SaveTeamGameZoneStats(SqliteConnection connection, SqliteTransaction transaction,
            int gameId, string teamId, List<BasketballManager.Core.Models.PlayerBoxScore> players)
        {
            foreach (var p in players)
            {
                if (p.Minutes <= 0) continue;
                for (int z = 0; z < 14; z++)
                {
                    if (p.ZoneFga[z] == 0) continue;
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
INSERT OR REPLACE INTO game_zone_stats (game_id, player_id, team_id, zone, fgm, fga)
VALUES (@game_id, @player_id, @team_id, @zone, @fgm, @fga);";
                    cmd.Parameters.AddWithValue("@game_id", gameId);
                    cmd.Parameters.AddWithValue("@player_id", p.PlayerId);
                    cmd.Parameters.AddWithValue("@team_id", teamId);
                    cmd.Parameters.AddWithValue("@zone", z);
                    cmd.Parameters.AddWithValue("@fgm", p.ZoneFgm[z]);
                    cmd.Parameters.AddWithValue("@fga", p.ZoneFga[z]);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static void UpsertTeamZoneStats(SqliteConnection connection, SqliteTransaction transaction,
            string table, string idCol, int id, string teamId, List<BasketballManager.Core.Models.PlayerBoxScore> players)
        {
            foreach (var p in players)
            {
                if (p.Minutes <= 0) continue;
                for (int z = 0; z < 14; z++)
                {
                    if (p.ZoneFga[z] == 0) continue;
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = $@"
INSERT INTO {table} ({idCol}, player_id, team_id, zone, fgm, fga)
VALUES (@id, @player_id, @team_id, @zone, @fgm, @fga)
ON CONFLICT({idCol}, player_id, zone) DO UPDATE SET
    fgm = fgm + excluded.fgm,
    fga = fga + excluded.fga,
    team_id = excluded.team_id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@player_id", p.PlayerId);
                    cmd.Parameters.AddWithValue("@team_id", teamId);
                    cmd.Parameters.AddWithValue("@zone", z);
                    cmd.Parameters.AddWithValue("@fgm", p.ZoneFgm[z]);
                    cmd.Parameters.AddWithValue("@fga", p.ZoneFga[z]);
                    cmd.ExecuteNonQuery();
                }
            }
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

        private static void UpsertTeamPlayoffStats(SqliteConnection connection, SqliteTransaction transaction, int seasonId, string teamId, List<PlayerBoxScore> players)
        {
            foreach (var p in players)
            {
                if (p.Minutes <= 0) continue;

                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
INSERT INTO playoff_player_stats (
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
                Id           = ReadInt(reader["id"]),
                Name         = reader["name"].ToString() ?? string.Empty,
                Status       = reader["status"].ToString() ?? "IN_PROGRESS",
                Phase        = reader["phase"].ToString() ?? "REGULAR",
                CreatedAt    = reader["created_at"].ToString() ?? string.Empty,
                SeasonNumber = ReadInt(reader["season_number"]),
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
                Phase = reader["phase"].ToString() ?? "REGULAR",
                SeriesId = ReadInt(reader["series_id"]),
            };
        }

        private static int ReadInt(object value)
        {
            return int.TryParse(value?.ToString(), out var parsed) ? parsed : 0;
        }
    }
}
