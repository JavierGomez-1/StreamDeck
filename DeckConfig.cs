using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ahsoka.CS.StreamDeck;

public sealed class DeckConfig
{
    public string Title { get; set; } = "S70 Stream Deck";
    public string HomePageId { get; set; } = "home";
    public string ProgramsPageId { get; set; } = "programs";
    public List<DeckPage> Pages { get; set; } = new();

    public static DeckConfig Load(string path)
    {
        if (!File.Exists(path))
            return CreateDefault();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        string json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<DeckConfig>(json, options) ?? CreateDefault();
        return config.Pages.Count == 0 ? CreateDefault() : config;
    }

    public DeckPage GetPageOrHome(string? pageId)
    {
        return Pages.FirstOrDefault(p => string.Equals(p.Id, pageId, StringComparison.OrdinalIgnoreCase))
            ?? Pages.FirstOrDefault(p => string.Equals(p.Id, HomePageId, StringComparison.OrdinalIgnoreCase))
            ?? Pages.First();
    }

    public DeckButton? GetButton(string pageId, string buttonId)
    {
        return GetPageOrHome(pageId).Buttons.FirstOrDefault(b =>
            string.Equals(b.Id, buttonId, StringComparison.OrdinalIgnoreCase));
    }

    private static DeckConfig CreateDefault()
    {
        return new DeckConfig
        {
            Pages =
            {
                new DeckPage
                {
                    Id = "home",
                    Title = "Principal",
                    Buttons =
                    {
                        new DeckButton { Id = "programs", Label = "Programas", Icon = "APP", Action = "navigate", TargetPage = "programs", CssClass = "blue" },
                        new DeckButton { Id = "tools", Label = "Herramientas", Icon = "TOOL", Action = "navigate", TargetPage = "tools", CssClass = "green" }
                    }
                }
            }
        };
    }
}

public sealed class DeckConfigStore
{
    private readonly object syncRoot = new();
    private readonly string path;

    public DeckConfigStore(string path)
    {
        this.path = path;
        Config = DeckConfig.Load(path);
        if (EnsureBuiltInPagesLocked())
            SaveLocked();
    }

    public DeckConfig Config { get; private set; }

    public void AddPage(string title)
    {
        title = (title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Escribe el nombre de la pagina.");

        lock (syncRoot)
        {
            string id = CreateUniqueId(title, Config.Pages.Select(p => p.Id));
            Config.Pages.Add(new DeckPage { Id = id, Title = title });
            SaveLocked();
        }
    }

    public void AddLaunchButton(string pageId, string label, string command, string arguments, string icon, string cssClass)
    {
        AddLaunchButton(pageId, label, command, arguments, icon, cssClass, "");
    }

    public void AddLaunchButton(string pageId, string label, string command, string arguments, string icon, string cssClass, string logoPath)
    {
        label = (label ?? "").Trim();
        command = (command ?? "").Trim();

        if (string.IsNullOrWhiteSpace(label))
            throw new InvalidOperationException("Escribe el nombre del boton.");
        if (string.IsNullOrWhiteSpace(command))
            throw new InvalidOperationException("Escribe el programa o comando.");

        lock (syncRoot)
        {
            var page = Config.GetPageOrHome(pageId);
            string id = CreateUniqueId(label, page.Buttons.Select(b => b.Id));

            page.Buttons.Add(new DeckButton
            {
                Id = id,
                Label = label,
                Icon = string.IsNullOrWhiteSpace(icon) ? "APP" : icon.Trim().ToUpperInvariant(),
                Action = "launch",
                Command = command,
                Arguments = arguments?.Trim() ?? "",
                CssClass = cssClass?.Trim() ?? "blue",
                LogoPath = logoPath?.Trim() ?? ""
            });

            SaveLocked();
        }
    }

    public DeckActionResult AddApplication(DeckAppTemplate app)
    {
        if (string.IsNullOrWhiteSpace(app.Label))
            return new DeckActionResult(false, "La app no tiene nombre.");
        if (string.IsNullOrWhiteSpace(app.Command))
            return new DeckActionResult(false, "La app no tiene comando.");

        lock (syncRoot)
        {
            var page = Config.GetPageOrHome(string.IsNullOrWhiteSpace(app.PageId) ? Config.ProgramsPageId : app.PageId);
            bool alreadyExists = page.Buttons.Any(button =>
                string.Equals(button.SourceTemplateId, app.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(button.Label, app.Label, StringComparison.OrdinalIgnoreCase));

            if (alreadyExists)
                return new DeckActionResult(true, $"{app.Label} ya estaba agregada.");

            string id = CreateUniqueId(app.Label, page.Buttons.Select(b => b.Id));
            page.Buttons.Insert(Math.Max(0, page.Buttons.Count - 1), new DeckButton
            {
                Id = id,
                Label = app.Label.Trim(),
                Icon = string.IsNullOrWhiteSpace(app.Icon) ? "APP" : app.Icon.Trim().ToUpperInvariant(),
                LogoPath = app.LogoPath?.Trim() ?? "",
                Action = string.IsNullOrWhiteSpace(app.Action) ? "launch" : app.Action.Trim(),
                Command = app.Command.Trim(),
                Arguments = app.Arguments?.Trim() ?? "",
                WorkingDirectory = app.WorkingDirectory?.Trim() ?? "",
                CssClass = app.CssClass?.Trim() ?? "blue",
                WaitForExit = app.WaitForExit,
                TimeoutMs = app.TimeoutMs,
                SourceTemplateId = app.Id?.Trim() ?? ""
            });

            SaveLocked();
            return new DeckActionResult(true, $"{app.Label} agregada.");
        }
    }

    public DeckActionResult SetProgramButton(int slotIndex, DeckAppTemplate app)
    {
        if (slotIndex < 0 || slotIndex > 6)
            return new DeckActionResult(false, "Slot de boton invalido.");
        if (string.IsNullOrWhiteSpace(app.Label))
            return new DeckActionResult(false, "La app no tiene nombre.");
        if (string.IsNullOrWhiteSpace(app.Command))
            return new DeckActionResult(false, "La app no tiene comando.");

        lock (syncRoot)
        {
            var page = Config.GetPageOrHome(Config.ProgramsPageId);
            page.Buttons.RemoveAll(button => string.Equals(button.Id, "back", StringComparison.OrdinalIgnoreCase));

            while (page.Buttons.Count <= slotIndex)
            {
                int number = page.Buttons.Count + 1;
                page.Buttons.Add(new DeckButton
                {
                    Id = $"slot-{number}",
                    Label = $"Boton {number}",
                    Icon = "APP",
                    Action = "noop",
                    CssClass = "muted"
                });
            }

            page.Buttons[slotIndex] = new DeckButton
            {
                Id = $"slot-{slotIndex + 1}",
                Label = app.Label.Trim(),
                Icon = string.IsNullOrWhiteSpace(app.Icon) ? "APP" : app.Icon.Trim().ToUpperInvariant(),
                LogoPath = app.LogoPath?.Trim() ?? "",
                Action = string.IsNullOrWhiteSpace(app.Action) ? "pc-launch" : app.Action.Trim(),
                Command = app.Command.Trim(),
                Arguments = app.Arguments?.Trim() ?? "",
                WorkingDirectory = app.WorkingDirectory?.Trim() ?? "",
                CssClass = app.CssClass?.Trim() ?? "blue",
                WaitForExit = app.WaitForExit,
                TimeoutMs = app.TimeoutMs,
                SourceTemplateId = app.Id?.Trim() ?? ""
            };

            EnsureBackButton(page);
            SaveLocked();
            return new DeckActionResult(true, $"Boton {slotIndex + 1}: {app.Label}");
        }
    }

    public void RemoveButton(string pageId, string buttonId)
    {
        lock (syncRoot)
        {
            var page = Config.GetPageOrHome(pageId);
            page.Buttons.RemoveAll(b => string.Equals(b.Id, buttonId, StringComparison.OrdinalIgnoreCase));
            SaveLocked();
        }
    }

    public DeckActionResult ClearProgramButton(string buttonId)
    {
        buttonId = (buttonId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(buttonId))
            return new DeckActionResult(false, "Boton invalido.");

        lock (syncRoot)
        {
            var page = Config.GetPageOrHome(Config.ProgramsPageId);
            int index = page.Buttons.FindIndex(button => string.Equals(button.Id, buttonId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return new DeckActionResult(false, "No encontre ese boton.");

            var removed = page.Buttons[index];
            if (TryGetSlotIndex(removed.Id, out int slotIndex) || index < 7)
                page.Buttons[index] = CreateEmptySlotButton(slotIndex >= 0 ? slotIndex : index);
            else
                page.Buttons.RemoveAt(index);

            EnsureBackButton(page);
            SaveLocked();
            return new DeckActionResult(true, $"Eliminado: {removed.Label}");
        }
    }

    private void SaveLocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };

        File.WriteAllText(path, JsonSerializer.Serialize(Config, options));
    }

    private bool EnsureBuiltInPagesLocked()
    {
        bool changed = false;

        Config.HomePageId = string.IsNullOrWhiteSpace(Config.HomePageId) ? "home" : Config.HomePageId;
        Config.ProgramsPageId = string.IsNullOrWhiteSpace(Config.ProgramsPageId) ? "programs" : Config.ProgramsPageId;

        var home = EnsurePage(Config.HomePageId, "Principal", ref changed);
        var programs = EnsurePage(Config.ProgramsPageId, "Programas", ref changed);
        var settings = EnsurePage("settings", "Ajustes", ref changed);
        var streaming = EnsurePage("streaming", "Streaming", ref changed);
        var streamScenes = EnsurePage("stream-scenes", "Escenas", ref changed);
        var streamAudio = EnsurePage("stream-audio", "Audio", ref changed);
        var streamLive = EnsurePage("stream-live", "Live", ref changed);
        var streamApps = EnsurePage("stream-apps", "Apps PC", ref changed);
        var streamAlerts = EnsurePage("stream-alerts", "Alertas", ref changed);
        var tools = EnsurePage("tools", "Herramientas", ref changed);
        var wifi = EnsurePage("wifi", "WiFi", ref changed);
        var bluetooth = EnsurePage("bluetooth", "Bluetooth", ref changed);

        changed |= EnsureButton(home, new DeckButton { Id = "streaming", Label = "Streaming", Icon = "LIVE", Action = "navigate", TargetPage = "streaming", CssClass = "purple" });
        changed |= EnsureButton(home, new DeckButton { Id = "programs", Label = "Programas", Icon = "APP", Action = "navigate", TargetPage = Config.ProgramsPageId, CssClass = "blue" });
        changed |= EnsureButton(home, new DeckButton { Id = "add-apps", Label = "Agregar Apps", Icon = "+", Action = "navigate", TargetPage = DeckConstants.AppCatalogPageId, CssClass = "green" });
        changed |= EnsureButton(home, new DeckButton { Id = "wifi", Label = "WiFi", Icon = "WIFI", Action = "navigate", TargetPage = "wifi", CssClass = "blue" });
        changed |= EnsureButton(home, new DeckButton { Id = "bluetooth", Label = "Bluetooth", Icon = "BT", Action = "navigate", TargetPage = "bluetooth", CssClass = "purple" });
        changed |= EnsureButton(home, new DeckButton { Id = "settings", Label = "Ajustes", Icon = "SET", Action = "navigate", TargetPage = "settings", CssClass = "muted" });

        changed |= EnsureButton(streaming, new DeckButton { Id = "stream-scenes", Label = "Escenas", Icon = "SCN", Action = "navigate", TargetPage = "stream-scenes", CssClass = "blue" });
        changed |= EnsureButton(streaming, new DeckButton { Id = "stream-audio", Label = "Audio", Icon = "AUD", Action = "navigate", TargetPage = "stream-audio", CssClass = "green" });
        changed |= EnsureButton(streaming, new DeckButton { Id = "stream-live", Label = "Live", Icon = "REC", Action = "navigate", TargetPage = "stream-live", CssClass = "purple" });
        changed |= EnsureButton(streaming, new DeckButton { Id = "stream-apps", Label = "Apps PC", Icon = "APP", Action = "navigate", TargetPage = "stream-apps", CssClass = "blue" });
        changed |= EnsureButton(streaming, new DeckButton { Id = "stream-alerts", Label = "Alertas", Icon = "ALT", Action = "navigate", TargetPage = "stream-alerts", CssClass = "green" });
        changed |= EnsureBackButton(streaming);

        changed |= EnsureButton(streamScenes, new DeckButton { Id = "starting", Label = "Starting", Icon = "STRT", Action = "pc-stream", Command = "starting", CssClass = "blue" });
        changed |= EnsureButton(streamScenes, new DeckButton { Id = "juego", Label = "Juego", Icon = "GAME", Action = "pc-stream", Command = "juego", CssClass = "green" });
        changed |= EnsureButton(streamScenes, new DeckButton { Id = "camara", Label = "Camara", Icon = "CAM", Action = "pc-stream", Command = "camara", CssClass = "purple" });
        changed |= EnsureButton(streamScenes, new DeckButton { Id = "brb", Label = "BRB", Icon = "BRB", Action = "pc-stream", Command = "brb", CssClass = "blue" });
        changed |= EnsureButton(streamScenes, new DeckButton { Id = "ending", Label = "Ending", Icon = "END", Action = "pc-stream", Command = "ending", CssClass = "muted" });
        changed |= EnsureButton(streamScenes, new DeckButton { Id = "back", Label = "Regresar", Icon = "BACK", Action = "navigate", TargetPage = "streaming", CssClass = "muted" });

        changed |= EnsureButton(streamAudio, new DeckButton { Id = "mute-mic", Label = "Mute Mic", Icon = "MIC", Action = "pc-stream", Command = "mute mic", CssClass = "purple" });
        changed |= EnsureButton(streamAudio, new DeckButton { Id = "mute-desktop", Label = "Mute Desktop", Icon = "DESK", Action = "pc-stream", Command = "mute desktop", CssClass = "purple" });
        changed |= EnsureButton(streamAudio, new DeckButton { Id = "musica", Label = "Musica", Icon = "PLAY", Action = "pc-stream", Command = "musica", CssClass = "green" });
        changed |= EnsureButton(streamAudio, new DeckButton { Id = "back", Label = "Regresar", Icon = "BACK", Action = "navigate", TargetPage = "streaming", CssClass = "muted" });

        changed |= EnsureButton(streamLive, new DeckButton { Id = "stream-on", Label = "Stream On", Icon = "ON", Action = "pc-stream", Command = "stream on", CssClass = "green" });
        changed |= EnsureButton(streamLive, new DeckButton { Id = "stream-off", Label = "Stream Off", Icon = "OFF", Action = "pc-stream", Command = "stream off", CssClass = "muted" });
        changed |= EnsureButton(streamLive, new DeckButton { Id = "grabar", Label = "Grabar", Icon = "REC", Action = "pc-stream", Command = "grabar", CssClass = "purple" });
        changed |= EnsureButton(streamLive, new DeckButton { Id = "clip", Label = "Clip", Icon = "CLIP", Action = "pc-stream", Command = "clip", CssClass = "blue" });
        changed |= EnsureButton(streamLive, new DeckButton { Id = "back", Label = "Regresar", Icon = "BACK", Action = "navigate", TargetPage = "streaming", CssClass = "muted" });

        changed |= EnsureButton(streamApps, new DeckButton { Id = "abrir-obs", Label = "Abrir OBS", Icon = "OBS", Action = "pc-stream", Command = "abrir obs", CssClass = "blue" });
        changed |= EnsureButton(streamApps, new DeckButton { Id = "abrir-discord", Label = "Discord", Icon = "DISC", Action = "pc-stream", Command = "abrir discord", CssClass = "purple" });
        changed |= EnsureButton(streamApps, new DeckButton { Id = "abrir-spotify", Label = "Spotify", Icon = "SPOT", Action = "pc-stream", Command = "abrir spotify", CssClass = "green" });
        changed |= EnsureButton(streamApps, new DeckButton { Id = "chat", Label = "Chat", Icon = "CHAT", Action = "pc-stream", Command = "chat", CssClass = "blue" });
        changed |= EnsureButton(streamApps, new DeckButton { Id = "dashboard", Label = "Dashboard", Icon = "DASH", Action = "pc-stream", Command = "dashboard", CssClass = "green" });
        changed |= EnsureButton(streamApps, new DeckButton { Id = "browser", Label = "Navegador", Icon = "WEB", Action = "pc-stream", Command = "navegador", CssClass = "blue" });
        changed |= EnsureButton(streamApps, new DeckButton { Id = "back", Label = "Regresar", Icon = "BACK", Action = "navigate", TargetPage = "streaming", CssClass = "muted" });

        changed |= EnsureButton(streamAlerts, new DeckButton { Id = "alerta-follow", Label = "Alerta Follow", Icon = "FOL", Action = "pc-stream", Command = "alerta follow", CssClass = "green" });
        changed |= EnsureButton(streamAlerts, new DeckButton { Id = "alerta-dono", Label = "Alerta Dono", Icon = "DONO", Action = "pc-stream", Command = "alerta dono", CssClass = "green" });
        changed |= EnsureButton(streamAlerts, new DeckButton { Id = "back", Label = "Regresar", Icon = "BACK", Action = "navigate", TargetPage = "streaming", CssClass = "muted" });

        changed |= EnsureBackButton(programs);
        RemoveBuiltInButton(settings, "add-wifi", ref changed);
        RemoveBuiltInButton(settings, "add-bt", ref changed);
        changed |= EnsureButton(settings, new DeckButton { Id = "program-button", Label = "Programar App", Icon = "APP", Action = "navigate", TargetPage = DeckConstants.ProgramSlotsPageId, CssClass = "blue" });
        changed |= EnsureButton(settings, new DeckButton { Id = "remove-apps", Label = "Eliminar Apps", Icon = "DEL", Action = "navigate", TargetPage = DeckConstants.RemoveAppsPageId, CssClass = "error" });
        changed |= EnsureButton(settings, new DeckButton { Id = "settings-wifi", Label = "WiFi", Icon = "WIFI", Action = "navigate", TargetPage = "wifi", CssClass = "blue" });
        changed |= EnsureButton(settings, new DeckButton { Id = "settings-bt", Label = "Bluetooth", Icon = "BT", Action = "navigate", TargetPage = "bluetooth", CssClass = "purple" });
        changed |= EnsureButton(settings, new DeckButton { Id = "theme", Label = "Tema", Icon = "UI", Action = "navigate", TargetPage = DeckConstants.ThemePageId, CssClass = "green" });
        changed |= EnsureBackButton(settings);

        changed |= EnsureButton(tools, new DeckButton { Id = "ping", Label = "Ping Local", Icon = "PING", Action = "shell", Command = "ping -c 1 127.0.0.1", WaitForExit = true, CssClass = "green" });
        changed |= EnsureBackButton(tools);

        RemoveBuiltInButton(wifi, "wifi-status", ref changed);
        RemoveBuiltInButton(wifi, "wifi-restart", ref changed);
        changed |= EnsureBackButton(wifi);

        RemoveBuiltInButton(bluetooth, "bt-status", ref changed);
        RemoveBuiltInButton(bluetooth, "bt-scan", ref changed);
        changed |= EnsureBackButton(bluetooth);

        return changed;
    }

    private DeckPage EnsurePage(string id, string title, ref bool changed)
    {
        var page = Config.Pages.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (page != null)
            return page;

        page = new DeckPage { Id = id, Title = title };
        Config.Pages.Add(page);
        changed = true;
        return page;
    }

    private static bool EnsureButton(DeckPage page, DeckButton button)
    {
        if (page.Buttons.Any(existing => string.Equals(existing.Id, button.Id, StringComparison.OrdinalIgnoreCase)))
            return false;

        page.Buttons.Add(button);
        return true;
    }

    private static void RemoveBuiltInButton(DeckPage page, string buttonId, ref bool changed)
    {
        int removed = page.Buttons.RemoveAll(button => string.Equals(button.Id, buttonId, StringComparison.OrdinalIgnoreCase));
        changed |= removed > 0;
    }

    private static bool EnsureBackButton(DeckPage page)
    {
        return EnsureButton(page, new DeckButton { Id = "back", Label = "Regresar", Icon = "BACK", Action = "navigate", TargetPage = "home", CssClass = "muted" });
    }

    private static DeckButton CreateEmptySlotButton(int slotIndex)
    {
        int number = Math.Clamp(slotIndex, 0, 6) + 1;
        return new DeckButton
        {
            Id = $"slot-{number}",
            Label = $"Boton {number}",
            Icon = "APP",
            Action = "noop",
            CssClass = "muted"
        };
    }

    private static bool TryGetSlotIndex(string id, out int slotIndex)
    {
        slotIndex = -1;
        if (!id.StartsWith("slot-", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!int.TryParse(id[5..], out int slotNumber))
            return false;

        slotIndex = slotNumber - 1;
        return slotIndex >= 0;
    }

    private static string CreateUniqueId(string value, IEnumerable<string> existingIds)
    {
        string id = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());

        id = string.Join('-', id.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(id))
            id = "item";

        var existing = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string candidate = id;
        int index = 2;

        while (existing.Contains(candidate))
        {
            candidate = $"{id}-{index}";
            index++;
        }

        return candidate;
    }
}

public sealed class DeckPage
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<DeckButton> Buttons { get; set; } = new();
}

public sealed class DeckButton
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "";
    public string LogoPath { get; set; } = "";
    public string Action { get; set; } = "noop";
    public string TargetPage { get; set; } = "";
    public string CatalogId { get; set; } = "";
    public string Command { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string CssClass { get; set; } = "";
    public string SourceTemplateId { get; set; } = "";
    public bool WaitForExit { get; set; }
    public int TimeoutMs { get; set; } = 10000;
}

public sealed record DeckActionResult(bool Success, string Message, string Output = "");

public sealed class DeckAppTemplate
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "";
    public string LogoPath { get; set; } = "";
    public string LogoUrl { get; set; } = "";
    public string Action { get; set; } = "launch";
    public string PageId { get; set; } = "";
    public string Command { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string CssClass { get; set; } = "blue";
    public bool WaitForExit { get; set; }
    public int TimeoutMs { get; set; } = 10000;
}

public static class DeckConstants
{
    public const string AppCatalogPageId = "add-apps";
    public const string ProgramSlotsPageId = "program-slots";
    public const string RemoveAppsPageId = "remove-apps";
    public const string ThemePageId = "theme";
    public const string WiFiPageId = "wifi";
    public const string BluetoothPageId = "bluetooth";
    public const string KeyboardPageId = "keyboard";
}
