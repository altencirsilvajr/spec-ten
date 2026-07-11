namespace SpecTen.Web.Infrastructure;

public static class ConfigurationHelpers
{
    public static string FirstConfiguredValue(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    public static string NormalizePostgresConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString) ||
            !Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("postgres" or "postgresql"))
        {
            return connectionString;
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var username = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(0) ?? "");
        var password = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? "");
        var database = uri.AbsolutePath.TrimStart('/');

        return string.Join(';', new[]
        {
            $"Host={uri.Host}",
            $"Port={uri.Port}",
            $"Database={database}",
            $"Username={username}",
            $"Password={password}",
            "SSL Mode=Require",
            "Trust Server Certificate=true",
        });
    }
}
