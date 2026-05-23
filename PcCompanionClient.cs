using System.Net.Http.Json;
using System.Text.Json;

namespace Ahsoka.CS.StreamDeck;

public sealed class PcCompanionClient
{
    private readonly HttpClient httpClient = new();
    private readonly string settingsPath;
    private readonly string iconFolder;
    private PcCompanionSettings settings;
    private List<DeckAppTemplate> cachedCatalog = new();
    private DateTime lastCatalogRefresh = DateTime.MinValue;
    private List<DeckAppTemplate> cachedInstalledApps = new();
    private DateTime lastInstalledRefresh = DateTime.MinValue;
    private ConnectivityConfig cachedConnections = new();
    private DateTime lastConnectionsRefresh = DateTime.MinValue;

    public PcCompanionClient(string settingsPath)
    {
        this.settingsPath = settingsPath;
        iconFolder = Path.Combine(Path.GetDirectoryName(settingsPath)!, "PcIcons");
        settings = LoadSettings(settingsPath);
    }

    public bool IsEnabled => settings.Enabled && !string.IsNullOrWhiteSpace(settings.BaseUrl);

    public IReadOnlyList<DeckAppTemplate> GetCatalog()
    {
        if (!IsEnabled)
            return cachedCatalog;

        if ((DateTime.UtcNow - lastCatalogRefresh).TotalSeconds < 5)
            return cachedCatalog;

        try
        {
            var response = httpClient.GetFromJsonAsync<List<DeckAppTemplate>>($"{settings.BaseUrl.TrimEnd('/')}/api/catalog").GetAwaiter().GetResult();
            cachedCatalog = response ?? new List<DeckAppTemplate>();
            foreach (var app in cachedCatalog)
            {
                app.Action = "pc-launch";
                app.Command = app.Id;
                app.LogoPath = CacheLogo(app);
            }

            lastCatalogRefresh = DateTime.UtcNow;
        }
        catch
        {
            lastCatalogRefresh = DateTime.UtcNow;
        }

        return cachedCatalog;
    }

    public IReadOnlyList<DeckAppTemplate> GetInstalledApps()
    {
        if (!IsEnabled)
            return cachedInstalledApps;

        if ((DateTime.UtcNow - lastInstalledRefresh).TotalSeconds < 10)
            return cachedInstalledApps;

        try
        {
            var response = httpClient.GetFromJsonAsync<List<DeckAppTemplate>>($"{settings.BaseUrl.TrimEnd('/')}/api/installed/templates").GetAwaiter().GetResult();
            cachedInstalledApps = response ?? new List<DeckAppTemplate>();
            foreach (var app in cachedInstalledApps)
            {
                app.Action = "pc-launch";
                app.Command = app.Id;
                app.LogoPath = CacheLogo(app);
            }

            lastInstalledRefresh = DateTime.UtcNow;
        }
        catch
        {
            lastInstalledRefresh = DateTime.UtcNow;
        }

        return cachedInstalledApps;
    }

    public async Task<DeckActionResult> LaunchAsync(string appId)
    {
        if (!IsEnabled)
            return new DeckActionResult(false, "Companion PC no configurado.");

        try
        {
            using var response = await httpClient.PostAsync($"{settings.BaseUrl.TrimEnd('/')}/api/apps/{Uri.EscapeDataString(appId)}/launch", null);
            string text = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
                return new DeckActionResult(true, string.IsNullOrWhiteSpace(text) ? "App enviada a PC." : Trim(text));

            return new DeckActionResult(false, string.IsNullOrWhiteSpace(text) ? $"PC fallo: {(int)response.StatusCode}" : Trim(text));
        }
        catch (Exception ex)
        {
            return new DeckActionResult(false, $"PC companion no responde: {ex.Message}");
        }
    }

    public IReadOnlyList<WiFiConnectionProfile> GetWiFiNetworks()
    {
        RefreshConnections();
        return cachedConnections.WiFiNetworks;
    }

    public IReadOnlyList<BluetoothConnectionProfile> GetBluetoothDevices()
    {
        RefreshConnections();
        return cachedConnections.BluetoothDevices;
    }

    public async Task<DeckActionResult> RunStreamActionAsync(string actionId)
    {
        if (!IsEnabled)
            return new DeckActionResult(false, "Companion PC no configurado.");

        if (string.IsNullOrWhiteSpace(actionId))
            return new DeckActionResult(false, "Accion de streaming vacia.");

        try
        {
            using var response = await httpClient.PostAsync($"{settings.BaseUrl.TrimEnd('/')}/api/stream/actions/{Uri.EscapeDataString(actionId)}", null);
            string text = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
                return new DeckActionResult(true, string.IsNullOrWhiteSpace(text) ? "Accion enviada a PC." : Trim(text));

            return new DeckActionResult(false, string.IsNullOrWhiteSpace(text) ? $"PC fallo: {(int)response.StatusCode}" : Trim(text));
        }
        catch (Exception ex)
        {
            return new DeckActionResult(false, $"PC companion no responde: {ex.Message}");
        }
    }

    private void RefreshConnections()
    {
        if (!IsEnabled)
            return;

        if ((DateTime.UtcNow - lastConnectionsRefresh).TotalSeconds < 5)
            return;

        try
        {
            cachedConnections = httpClient.GetFromJsonAsync<ConnectivityConfig>($"{settings.BaseUrl.TrimEnd('/')}/api/connections")
                .GetAwaiter()
                .GetResult() ?? new ConnectivityConfig();
            lastConnectionsRefresh = DateTime.UtcNow;
        }
        catch
        {
            lastConnectionsRefresh = DateTime.UtcNow;
        }
    }

    private string CacheLogo(DeckAppTemplate app)
    {
        if (string.IsNullOrWhiteSpace(app.LogoUrl))
            return app.LogoPath;

        try
        {
            Directory.CreateDirectory(iconFolder);
            string fileName = $"{Sanitize(app.Id)}.png";
            string path = Path.Combine(iconFolder, fileName);

            if (!File.Exists(path) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalHours > 12)
            {
                byte[] bytes = httpClient.GetByteArrayAsync(app.LogoUrl).GetAwaiter().GetResult();
                File.WriteAllBytes(path, bytes);
            }

            return path;
        }
        catch
        {
            return app.LogoPath;
        }
    }

    private static PcCompanionSettings LoadSettings(string path)
    {
        PcCompanionSettings settings;

        if (!File.Exists(path))
        {
            settings = new PcCompanionSettings();
        }
        else
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            settings = JsonSerializer.Deserialize<PcCompanionSettings>(File.ReadAllText(path), options) ?? new PcCompanionSettings();
        }

        var baseUrl = Environment.GetEnvironmentVariable("S70_PC_COMPANION_URL");
        if (!string.IsNullOrWhiteSpace(baseUrl))
            settings.BaseUrl = baseUrl.Trim();

        return settings;
    }

    private static string Sanitize(string value)
    {
        return new string((value ?? "app").Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
    }

    private static string Trim(string value)
    {
        value = value.Replace("\r", "").Replace("\n", " ").Trim();
        return value.Length <= 100 ? value : value[..99] + ".";
    }
}

public sealed class PcCompanionSettings
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "";
}
