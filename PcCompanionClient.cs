using System.Net.Http.Json;
using System.Text.Json;

namespace Ahsoka.CS.StreamDeck;

public sealed class PcCompanionClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan OfflineBackoff = TimeSpan.FromSeconds(20);

    private readonly object syncRoot = new();
    private readonly HttpClient httpClient;
    private readonly string settingsPath;
    private readonly string iconFolder;
    private PcCompanionSettings settings;
    private List<DeckAppTemplate> cachedCatalog = new();
    private DateTime lastCatalogRefresh = DateTime.MinValue;
    private bool catalogRefreshInProgress;
    private List<DeckAppTemplate> cachedInstalledApps = new();
    private DateTime lastInstalledRefresh = DateTime.MinValue;
    private bool installedRefreshInProgress;
    private ConnectivityConfig cachedConnections = new();
    private DateTime lastConnectionsRefresh = DateTime.MinValue;
    private bool connectionsRefreshInProgress;
    private DateTime companionOfflineUntil = DateTime.MinValue;

    public PcCompanionClient(string settingsPath)
    {
        httpClient = new HttpClient { Timeout = RequestTimeout };
        this.settingsPath = settingsPath;
        iconFolder = Path.Combine(Path.GetDirectoryName(settingsPath)!, "PcIcons");
        settings = LoadSettings(settingsPath);
    }

    public bool IsEnabled => settings.Enabled && !string.IsNullOrWhiteSpace(settings.BaseUrl);

    public IReadOnlyList<DeckAppTemplate> GetCatalog()
    {
        StartCatalogRefreshIfNeeded();

        lock (syncRoot)
            return cachedCatalog.ToList();
    }

    public IReadOnlyList<DeckAppTemplate> GetInstalledApps()
    {
        StartInstalledRefreshIfNeeded();

        lock (syncRoot)
            return cachedInstalledApps.ToList();
    }

    public async Task<DeckActionResult> LaunchAsync(string appId)
    {
        if (!IsEnabled)
            return new DeckActionResult(false, "Companion PC no configurado.");

        if (!CanContactCompanion())
            return new DeckActionResult(false, "Companion PC no responde.");

        try
        {
            using var response = await httpClient.PostAsync($"{settings.BaseUrl.TrimEnd('/')}/api/apps/{Uri.EscapeDataString(appId)}/launch", null);
            string text = await response.Content.ReadAsStringAsync();
            MarkOnline();
            if (response.IsSuccessStatusCode)
                return new DeckActionResult(true, string.IsNullOrWhiteSpace(text) ? "App enviada a PC." : Trim(text));

            return new DeckActionResult(false, string.IsNullOrWhiteSpace(text) ? $"PC fallo: {(int)response.StatusCode}" : Trim(text));
        }
        catch (Exception ex)
        {
            MarkOffline();
            return new DeckActionResult(false, $"PC companion no responde: {ex.Message}");
        }
    }

    public IReadOnlyList<WiFiConnectionProfile> GetWiFiNetworks()
    {
        StartConnectionsRefreshIfNeeded();

        lock (syncRoot)
            return cachedConnections.WiFiNetworks.ToList();
    }

    public IReadOnlyList<BluetoothConnectionProfile> GetBluetoothDevices()
    {
        StartConnectionsRefreshIfNeeded();

        lock (syncRoot)
            return cachedConnections.BluetoothDevices.ToList();
    }

    public async Task<DeckActionResult> RunStreamActionAsync(string actionId)
    {
        if (!IsEnabled)
            return new DeckActionResult(false, "Companion PC no configurado.");

        if (!CanContactCompanion())
            return new DeckActionResult(false, "Companion PC no responde.");

        if (string.IsNullOrWhiteSpace(actionId))
            return new DeckActionResult(false, "Accion de streaming vacia.");

        try
        {
            using var response = await httpClient.PostAsync($"{settings.BaseUrl.TrimEnd('/')}/api/stream/actions/{Uri.EscapeDataString(actionId)}", null);
            string text = await response.Content.ReadAsStringAsync();
            MarkOnline();
            if (response.IsSuccessStatusCode)
                return new DeckActionResult(true, string.IsNullOrWhiteSpace(text) ? "Accion enviada a PC." : Trim(text));

            return new DeckActionResult(false, string.IsNullOrWhiteSpace(text) ? $"PC fallo: {(int)response.StatusCode}" : Trim(text));
        }
        catch (Exception ex)
        {
            MarkOffline();
            return new DeckActionResult(false, $"PC companion no responde: {ex.Message}");
        }
    }

    private void StartCatalogRefreshIfNeeded()
    {
        if (!TryBeginRefresh(ref catalogRefreshInProgress, lastCatalogRefresh, TimeSpan.FromSeconds(5)))
            return;

        Task.Run(async () =>
        {
            try
            {
                var response = await httpClient.GetFromJsonAsync<List<DeckAppTemplate>>($"{settings.BaseUrl.TrimEnd('/')}/api/catalog")
                    ?? new List<DeckAppTemplate>();

                foreach (var app in response)
                {
                    app.Action = "pc-launch";
                    app.Command = app.Id;
                    app.LogoPath = await CacheLogoAsync(app);
                }

                lock (syncRoot)
                {
                    cachedCatalog = response;
                    lastCatalogRefresh = DateTime.UtcNow;
                    MarkOnlineLocked();
                }
            }
            catch
            {
                lock (syncRoot)
                {
                    lastCatalogRefresh = DateTime.UtcNow;
                    MarkOfflineLocked();
                }
            }
            finally
            {
                lock (syncRoot)
                    catalogRefreshInProgress = false;
            }
        });
    }

    private void StartInstalledRefreshIfNeeded()
    {
        if (!TryBeginRefresh(ref installedRefreshInProgress, lastInstalledRefresh, TimeSpan.FromSeconds(10)))
            return;

        Task.Run(async () =>
        {
            try
            {
                var response = await httpClient.GetFromJsonAsync<List<DeckAppTemplate>>($"{settings.BaseUrl.TrimEnd('/')}/api/installed/templates")
                    ?? new List<DeckAppTemplate>();

                foreach (var app in response)
                {
                    app.Action = "pc-launch";
                    app.Command = app.Id;
                    app.LogoPath = await CacheLogoAsync(app);
                }

                lock (syncRoot)
                {
                    cachedInstalledApps = response;
                    lastInstalledRefresh = DateTime.UtcNow;
                    MarkOnlineLocked();
                }
            }
            catch
            {
                lock (syncRoot)
                {
                    lastInstalledRefresh = DateTime.UtcNow;
                    MarkOfflineLocked();
                }
            }
            finally
            {
                lock (syncRoot)
                    installedRefreshInProgress = false;
            }
        });
    }

    private void StartConnectionsRefreshIfNeeded()
    {
        if (!TryBeginRefresh(ref connectionsRefreshInProgress, lastConnectionsRefresh, TimeSpan.FromSeconds(5)))
            return;

        Task.Run(async () =>
        {
            try
            {
                var response = await httpClient.GetFromJsonAsync<ConnectivityConfig>($"{settings.BaseUrl.TrimEnd('/')}/api/connections")
                    ?? new ConnectivityConfig();

                lock (syncRoot)
                {
                    cachedConnections = response;
                    lastConnectionsRefresh = DateTime.UtcNow;
                    MarkOnlineLocked();
                }
            }
            catch
            {
                lock (syncRoot)
                {
                    lastConnectionsRefresh = DateTime.UtcNow;
                    MarkOfflineLocked();
                }
            }
            finally
            {
                lock (syncRoot)
                    connectionsRefreshInProgress = false;
            }
        });
    }

    private bool TryBeginRefresh(ref bool inProgress, DateTime lastRefresh, TimeSpan refreshInterval)
    {
        lock (syncRoot)
        {
            if (!CanContactCompanionLocked())
                return false;

            if (inProgress)
                return false;

            if (DateTime.UtcNow - lastRefresh < refreshInterval)
                return false;

            inProgress = true;
            return true;
        }
    }

    private async Task<string> CacheLogoAsync(DeckAppTemplate app)
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
                byte[] bytes = await httpClient.GetByteArrayAsync(app.LogoUrl);
                await File.WriteAllBytesAsync(path, bytes);
            }

            return path;
        }
        catch
        {
            lock (syncRoot)
                MarkOfflineLocked();

            return app.LogoPath;
        }
    }

    private bool CanContactCompanion()
    {
        lock (syncRoot)
            return CanContactCompanionLocked();
    }

    private bool CanContactCompanionLocked()
    {
        return IsEnabled && DateTime.UtcNow >= companionOfflineUntil;
    }

    private void MarkOffline()
    {
        lock (syncRoot)
            MarkOfflineLocked();
    }

    private void MarkOnline()
    {
        lock (syncRoot)
            MarkOnlineLocked();
    }

    private void MarkOfflineLocked()
    {
        companionOfflineUntil = DateTime.UtcNow.Add(OfflineBackoff);
    }

    private void MarkOnlineLocked()
    {
        companionOfflineUntil = DateTime.MinValue;
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
