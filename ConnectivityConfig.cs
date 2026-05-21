using System.Text.Json;

namespace Ahsoka.CS.StreamDeck;

public sealed class ConnectivityConfigStore
{
    private readonly string path;
    private readonly JsonSerializerOptions options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public ConnectivityConfigStore(string path)
    {
        this.path = path;
        Config = Load();
    }

    public ConnectivityConfig Config { get; }

    public DeckActionResult AddWiFi(string label, string ssid, string password)
    {
        ssid = (ssid ?? "").Trim();
        if (string.IsNullOrWhiteSpace(ssid))
            return new DeckActionResult(false, "SSID requerido.");

        lock (Config)
        {
            string id = SafeId(ssid);
            Config.WiFiNetworks.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            Config.WiFiNetworks.Add(new WiFiConnectionProfile
            {
                Id = id,
                Label = string.IsNullOrWhiteSpace(label) ? ssid : label.Trim(),
                Ssid = ssid,
                Password = password ?? "",
                CssClass = "blue"
            });
            Save(Config);
        }

        return new DeckActionResult(true, $"WiFi guardado: {ssid}");
    }

    public DeckActionResult AddBluetooth(string label, string macAddress)
    {
        macAddress = (macAddress ?? "").Trim();
        if (string.IsNullOrWhiteSpace(macAddress))
            return new DeckActionResult(false, "MAC requerida.");

        lock (Config)
        {
            string id = SafeId(macAddress);
            Config.BluetoothDevices.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            Config.BluetoothDevices.Add(new BluetoothConnectionProfile
            {
                Id = id,
                Label = string.IsNullOrWhiteSpace(label) ? macAddress : label.Trim(),
                MacAddress = macAddress,
                CssClass = "purple"
            });
            Save(Config);
        }

        return new DeckActionResult(true, $"BT guardado: {macAddress}");
    }

    private ConnectivityConfig Load()
    {
        if (!File.Exists(path))
        {
            var config = new ConnectivityConfig();
            Save(config);
            return config;
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ConnectivityConfig>(json, options) ?? new ConnectivityConfig();
    }

    private void Save(ConnectivityConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, options));
    }

    private static string SafeId(string value)
    {
        string safe = new string((value ?? "item").Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "item" : safe.ToLowerInvariant();
    }
}

public sealed class ConnectivityConfig
{
    public List<WiFiConnectionProfile> WiFiNetworks { get; set; } = new();
    public List<BluetoothConnectionProfile> BluetoothDevices { get; set; } = new();
}

public sealed class WiFiConnectionProfile
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Ssid { get; set; } = "";
    public string Password { get; set; } = "";
    public string CssClass { get; set; } = "blue";
}

public sealed class BluetoothConnectionProfile
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string CssClass { get; set; } = "purple";
}
