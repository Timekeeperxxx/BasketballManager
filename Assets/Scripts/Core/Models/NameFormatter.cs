using BasketballManager.Core.Enums;

namespace BasketballManager.Core.Models
{
    public static class NameFormatter
    {
        public static string GetDisplayName(Player player)
        {
            if (player == null)
            {
                return string.Empty;
            }

            var firstName = player.FirstName?.Trim() ?? string.Empty;
            var lastName = player.LastName?.Trim() ?? string.Empty;

            if (player.NameOrder == NameOrder.EASTERN)
            {
                return $"{lastName}{firstName}".Trim();
            }

            if (string.IsNullOrWhiteSpace(firstName))
            {
                return lastName;
            }

            if (string.IsNullOrWhiteSpace(lastName))
            {
                return firstName;
            }

            return $"{firstName} {lastName}";
        }
    }
}
