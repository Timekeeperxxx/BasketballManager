using System;
using System.IO;
using BasketballManager.Core.Models;
using Mono.Data.Sqlite;
using UnityEngine;

namespace BasketballManager.App
{
    public static class SaveManager
    {
        public const int MaxSlots = 5;

        public static int ActiveSlotId { get; private set; } = -1;

        public static string GetSlotFileName(int slotId) => $"save_{slotId}.db";

        public static string GetSlotPath(int slotId)
            => Path.Combine(Application.persistentDataPath, GetSlotFileName(slotId));

        public static string GetUserTeamIdKey(int slotId) => $"Save_{slotId}_UserTeamId";

        public static void SetActiveSlot(int slotId)
        {
            ActiveSlotId = slotId;
        }

        public static bool SlotExists(int slotId)
            => File.Exists(GetSlotPath(slotId));

        public static void DeleteSlot(int slotId)
        {
            string path = GetSlotPath(slotId);
            if (File.Exists(path))
                File.Delete(path);
            PlayerPrefs.DeleteKey(GetUserTeamIdKey(slotId));
            PlayerPrefs.Save();
        }

        private const string TemplateName = "game.db";

        // 从模板（persistentDataPath/game.db）复制到槽位文件
        public static bool CreateSlot(int slotId)
        {
            string templatePath = Path.Combine(Application.persistentDataPath, TemplateName);
            string destPath = GetSlotPath(slotId);

            // 非 Android 编辑器：模板在 StreamingAssets
            string streamingTemplate = Path.Combine(Application.streamingAssetsPath, TemplateName);

            if (!File.Exists(templatePath) && File.Exists(streamingTemplate))
                File.Copy(streamingTemplate, templatePath, true);

            if (!File.Exists(templatePath))
            {
                Debug.LogError($"[SaveManager] 无法找到模板数据库：{templatePath}");
                return false;
            }

            File.Copy(templatePath, destPath, true);
            Debug.Log($"[SaveManager] 创建存档 {slotId}：{destPath}");
            return true;
        }

        public static SaveSlotInfo ReadSlotInfo(int slotId)
        {
            string path = GetSlotPath(slotId);
            if (!File.Exists(path))
                return new SaveSlotInfo { SlotId = slotId, IsEmpty = true };

            var info = new SaveSlotInfo
            {
                SlotId       = slotId,
                IsEmpty      = false,
                LastModified = File.GetLastWriteTime(path),
            };

            try
            {
                string connStr = $"Data Source={path};Version=3;";
                using var conn = new SqliteConnection(connStr);
                conn.Open();

                // 读取最新赛季
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT name, COALESCE(season_number,1) " +
                        "FROM seasons ORDER BY id DESC LIMIT 1;";
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        info.LastSeasonName   = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        info.LastSeasonNumber = reader.IsDBNull(1) ? 1  : reader.GetInt32(1);
                    }
                }

                // 读取玩家球队名
                string teamId = PlayerPrefs.GetString(GetUserTeamIdKey(slotId), "");
                if (!string.IsNullOrEmpty(teamId))
                {
                    using var cmd2 = conn.CreateCommand();
                    cmd2.CommandText = "SELECT name FROM teams WHERE id = @id LIMIT 1;";
                    cmd2.Parameters.AddWithValue("@id", teamId);
                    var result = cmd2.ExecuteScalar();
                    if (result != null) info.UserTeamName = result.ToString();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveManager] 读取存档 {slotId} 信息失败：{e.Message}");
            }

            return info;
        }
    }
}
