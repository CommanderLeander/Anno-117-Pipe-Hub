using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace AnnoPipeHub;

public sealed record ListenerSettings(int Port = 8766, bool LanEnabled = false, string? LanAddress = null);
public sealed record ListenerStatus(bool LanEnabled, string BindAddress, int Port, string State, int ConnectedClients, string? Error)
{
    public static ListenerStatus Default => new(false, "127.0.0.1", 8766, "Stopped", 0, null);
}
public sealed record NetworkInterfaceOption(string Address, string Label);

public sealed class SettingsStore
{
    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "config", "hub-settings.json");
    public ListenerSettings Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<ListenerSettings>(File.ReadAllText(_path)) ?? new() : new(); }
        catch { return new(); }
    }
    public void Save(ListenerSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
}

public static class NetworkSettings
{
    public static IReadOnlyList<NetworkInterfaceOption> PrivateAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(i => i.OperationalStatus == OperationalStatus.Up)
        .SelectMany(i => i.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(a.Address))
        .Select(a => new NetworkInterfaceOption(a.Address.ToString(), a.Address.ToString()))
        .DistinctBy(a => a.Address).OrderBy(a => a.Address).ToArray();

    public static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 172 && b[1] >= 16 && b[1] <= 31 || b[0] == 192 && b[1] == 168;
    }
    public static bool IsValidPort(int port) => port is >= 1024 and <= 65535;
}
