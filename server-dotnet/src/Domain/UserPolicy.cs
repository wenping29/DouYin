using System.Text.RegularExpressions;

namespace DouYing.Server.Domain;

public static partial class UserPolicy
{
    [GeneratedRegex("^[a-zA-Z0-9_]{3,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();

    public static string NormalizeUsername(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

    public static bool IsValidUsername(string username) =>
        UsernamePattern().IsMatch(username);

    public static bool IsValidPassword(string password) =>
        password.Length is >= 6 and <= 64;
}
