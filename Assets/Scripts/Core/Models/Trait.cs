namespace BasketballManager.Core.Models
{
    public sealed class Trait
    {
        public int Id { get; set; }
        public string NameKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description1 { get; set; } = string.Empty;
        public string Description2 { get; set; } = string.Empty;
        public string Description3 { get; set; } = string.Empty;

        public string GetDescription(int starLevel) => starLevel switch
        {
            1 => Description1,
            2 => Description2,
            3 => Description3,
            _ => Description1
        };
    }
}
