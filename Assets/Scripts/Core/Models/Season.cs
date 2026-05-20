namespace BasketballManager.Core.Models
{
    /// <summary>
    /// 赛季容器。一个 season 包含 N 支球队、双循环赛程、累计球员/球队数据。
    /// </summary>
    public sealed class Season
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = "IN_PROGRESS";   // IN_PROGRESS / FINISHED
        public string Phase { get; set; } = "REGULAR";        // REGULAR | PLAYOFF
        public string CreatedAt { get; set; } = string.Empty;
    }
}
