using Ahsoka.Core;
using Ahsoka.Core.Dispatch;
using Ahsoka.Core.Utility.WiFi;

namespace Ahsoka.CS.StreamDeck;

public sealed class WiFiConnectionManager
{
    private readonly object syncRoot = new();
    private readonly WiFiNetworkManager manager = new();
    private bool started;

    public void Start()
    {
        if (OperatingSystem.IsWindows())
            return;

        lock (syncRoot)
        {
            if (started)
                return;

            manager.Start(null, null, Dispatcher.Default);
            started = true;
        }
    }

    public List<WiFiConnectionProfile> GetAvailableProfiles()
    {
        if (OperatingSystem.IsWindows())
            return new List<WiFiConnectionProfile>();

        try
        {
            Start();
            return (manager.AvailableNetworks?.Cast<object>() ?? Enumerable.Empty<object>())
                .Select(network => ReadProfile(network))
                .Where(profile => !string.IsNullOrWhiteSpace(profile.Ssid))
                .GroupBy(profile => profile.Ssid, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }
        catch (Exception ex)
        {
            AhsokaLogging.LogMessage(AhsokaVerbosity.Low, $"WiFi list failed: {ex.Message}");
            return new List<WiFiConnectionProfile>();
        }
    }

    public DeckActionResult Discover()
    {
        try
        {
            Start();
            manager.DiscoverNetworks();
            return new DeckActionResult(true, "Buscando redes WiFi.");
        }
        catch (Exception ex)
        {
            AhsokaLogging.LogMessage(AhsokaVerbosity.Low, $"WiFi scan failed: {ex.Message}");
            return new DeckActionResult(false, $"WiFi: {ex.Message}");
        }
    }

    public Task<DeckActionResult> ConnectAsync(string ssid, string password)
    {
        return Task.Run(() => Connect(ssid, password));
    }

    public Task<DeckActionResult> DisconnectAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                Start();
                manager.Disconnect();
                return new DeckActionResult(true, "WiFi desconectado.");
            }
            catch (Exception ex)
            {
                AhsokaLogging.LogMessage(AhsokaVerbosity.Low, $"WiFi disconnect failed: {ex.Message}");
                return new DeckActionResult(false, $"WiFi: {ex.Message}");
            }
        });
    }

    private DeckActionResult Connect(string ssid, string password)
    {
        ssid = (ssid ?? "").Trim();
        if (string.IsNullOrWhiteSpace(ssid))
            return new DeckActionResult(false, "Configura el SSID de WiFi.");

        try
        {
            Start();
            if (!SelectNetwork(ssid))
            {
                manager.DiscoverNetworks();
                Thread.Sleep(1500);
            }

            if (!SelectNetwork(ssid))
                return new DeckActionResult(false, $"No encontre la red {ssid}.");

            manager.ConnectNetwork(password ?? "", 60000, 30000, 10000);
            return new DeckActionResult(true, $"Conectando WiFi: {ssid}");
        }
        catch (Exception ex)
        {
            AhsokaLogging.LogMessage(AhsokaVerbosity.Low, $"WiFi connect failed for {ssid}: {ex.Message}");
            return new DeckActionResult(false, $"WiFi: {ex.Message}");
        }
    }

    private bool SelectNetwork(string ssid)
    {
        var networks = manager.AvailableNetworks?.Cast<object>().ToList() ?? new List<object>();
        int targetIndex = networks.FindIndex(network => string.Equals(ReadStringProperty(network, "Ssid", "SSID"), ssid, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0)
            return false;

        string selectedSsid = ReadStringProperty(manager.SelectedNetwork, "Ssid", "SSID");
        int currentIndex = networks.FindIndex(network => string.Equals(ReadStringProperty(network, "Ssid", "SSID"), selectedSsid, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
            currentIndex = 0;

        while (currentIndex < targetIndex)
        {
            manager.MoveDown();
            currentIndex++;
        }

        while (currentIndex > targetIndex)
        {
            manager.MoveUp();
            currentIndex--;
        }

        return string.Equals(ReadStringProperty(manager.SelectedNetwork, "Ssid", "SSID"), ssid, StringComparison.OrdinalIgnoreCase);
    }

    private static WiFiConnectionProfile ReadProfile(object network)
    {
        string ssid = ReadStringProperty(network, "Ssid", "SSID");
        string security = ReadStringProperty(network, "SecurityType", "Security", "Authentication");
        string signal = ReadStringProperty(network, "SignalStrength", "SignalLevel", "Signal");
        string suffix = string.IsNullOrWhiteSpace(signal) ? "" : $" {signal}";

        return new WiFiConnectionProfile
        {
            Id = SafeId(ssid),
            Label = string.IsNullOrWhiteSpace(security) ? $"{ssid}{suffix}" : $"{ssid}{suffix}",
            Ssid = ssid,
            Password = "",
            CssClass = "blue"
        };
    }

    private static string ReadStringProperty(object? value, params string[] propertyNames)
    {
        if (value == null)
            return "";

        var type = value.GetType();
        foreach (string propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName);
            property ??= type.GetProperties().FirstOrDefault(item => string.Equals(item.Name, propertyName, StringComparison.OrdinalIgnoreCase));
            object? propertyValue = property?.GetValue(value);
            if (propertyValue != null)
                return propertyValue.ToString() ?? "";
        }

        return "";
    }

    private static string SafeId(string value)
    {
        string safe = new string((value ?? "wifi").Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "wifi" : safe.ToLowerInvariant();
    }
}
