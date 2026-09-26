using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
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
    private readonly string _path = Path.Combine(Directory.GetCurrentDirectory(), "config", "hub-settings.json");
    private readonly string _tokenPath = Path.Combine(Directory.GetCurrentDirectory(), "config", "hub-token.bin");
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
    public string? LoadToken()
    {
        try
        {
            if (!OperatingSystem.IsWindows() || !File.Exists(_tokenPath)) return null;
            var encrypted = File.ReadAllBytes(_tokenPath);
            var token = Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
            return token.Length > 0 ? token : null;
        }
        catch { return null; }
    }
    public void SaveToken(string token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("LAN-Token-Speicherung benötigt Windows-DPAPI.");
        Directory.CreateDirectory(Path.GetDirectoryName(_tokenPath)!);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_tokenPath, encrypted);
    }
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
}

internal static class ProtectedData
{
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Size; public IntPtr Data; }
    public static byte[] Protect(byte[] data, byte[]? entropy, DataProtectionScope scope)
    {
        var input = new DataBlob { Size = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        try { Marshal.Copy(data, 0, input.Data, data.Length); if (!CryptProtectData(ref input, "Anno 117 Pipe Hub LAN Token", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output)) throw new CryptographicException(Marshal.GetLastWin32Error()); return CopyAndFree(output); }
        finally { Marshal.FreeHGlobal(input.Data); }
    }
    public static byte[] Unprotect(byte[] data, byte[]? entropy, DataProtectionScope scope)
    {
        var input = new DataBlob { Size = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        try { Marshal.Copy(data, 0, input.Data, data.Length); if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output)) throw new CryptographicException(Marshal.GetLastWin32Error()); return CopyAndFree(output); }
        finally { Marshal.FreeHGlobal(input.Data); }
    }
    private static byte[] CopyAndFree(DataBlob blob) { try { var result = new byte[blob.Size]; Marshal.Copy(blob.Data, result, 0, blob.Size); return result; } finally { LocalFree(blob.Data); } }
}

internal enum DataProtectionScope { CurrentUser }

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
        return b[0] == 10 || b[0] == 127 || b[0] == 172 && b[1] >= 16 && b[1] <= 31 || b[0] == 192 && b[1] == 168;
    }
    public static bool IsValidPort(int port) => port is >= 1024 and <= 65535;
}
