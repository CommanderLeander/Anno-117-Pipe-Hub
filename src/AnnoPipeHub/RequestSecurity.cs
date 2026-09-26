using Microsoft.AspNetCore.Http;

namespace AnnoPipeHub;

public static class RequestSecurity
{
    public const string DashboardOrigin = "http://127.0.0.1:8765";

    public static bool IsAllowedOrigin(string? origin) => string.IsNullOrWhiteSpace(origin) ||
        string.Equals(origin.TrimEnd('/'), DashboardOrigin, StringComparison.OrdinalIgnoreCase);

    public static bool IsAllowedDashboardGet(HttpRequest request) =>
        string.Equals(request.Host.Value, "127.0.0.1:8765", StringComparison.OrdinalIgnoreCase);

    public static bool IsAllowedDashboardPost(HttpRequest request) =>
        IsAllowedOrigin(request.Headers.Origin.FirstOrDefault());
}
