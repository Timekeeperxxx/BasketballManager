namespace BasketballManager.Core.Models
{
    public sealed class PlayerTrait
    {
        public int PlayerId { get; set; }
        public int TraitId { get; set; }
        public int StarLevel { get; set; } = 1;
        public string NameKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
