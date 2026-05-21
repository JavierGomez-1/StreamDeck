using Ahsoka.Core;
using Ahsoka.Core.Drawing;
using Ahsoka.Core.Drawing.Base;
using Ahsoka.Core.Drawing.Utility;

namespace Ahsoka.CS.StreamDeck;

internal sealed class StreamDeckUi
{
    private readonly DeckConfigStore configStore;
    private readonly AppCatalogStore catalogStore;
    private readonly ConnectivityConfigStore connectivityStore;
    private readonly UiThemeStore themeStore;
    private readonly PcCompanionClient pcCompanionClient;
    private readonly WiFiConnectionManager wifiConnectionManager;
    private readonly BluetoothConnectionManager bluetoothConnectionManager;
    private readonly DeckActionRunner actionRunner;
    private readonly object stateLock = new();
    private readonly List<TouchArea> touchAreas = new();

    private readonly DrawingColor backgroundColor;
    private readonly DrawingColor headerColor;
    private readonly DrawingColor buttonColor;
    private readonly DrawingColor buttonAltColor;
    private readonly DrawingColor pressedColor;
    private readonly DrawingColor strokeColor;
    private readonly DrawingColor textColor;
    private readonly DrawingColor mutedTextColor;
    private readonly DrawingColor logoPlateColor;
    private readonly DrawingColor logoPlateStrokeColor;
    private readonly DrawingColor errorColor;
    private readonly DrawingColor okColor;

    private DrawingWindow? window;
    private DrawingTypeface? typeface;
    private string currentPageId;
    private string statusText = "Listo";
    private bool statusIsError;
    private int installedPageIndex;
    private int selectedProgramSlotIndex = -1;
    private bool deleteMode;
    private string keyboardTitle = "";
    private string keyboardValue = "";
    private string keyboardReturnPage = "settings";
    private bool keyboardMasked;
    private bool keyboardShift;
    private bool keyboardSymbols;
    private Action<string>? keyboardCompleted;

    public StreamDeckUi(
        DeckConfigStore configStore,
        AppCatalogStore catalogStore,
        ConnectivityConfigStore connectivityStore,
        UiThemeStore themeStore,
        PcCompanionClient pcCompanionClient,
        WiFiConnectionManager wifiConnectionManager,
        BluetoothConnectionManager bluetoothConnectionManager,
        DeckActionRunner actionRunner)
    {
        this.configStore = configStore;
        this.catalogStore = catalogStore;
        this.connectivityStore = connectivityStore;
        this.themeStore = themeStore;
        this.pcCompanionClient = pcCompanionClient;
        this.wifiConnectionManager = wifiConnectionManager;
        this.bluetoothConnectionManager = bluetoothConnectionManager;
        this.actionRunner = actionRunner;
        var colors = Theme.Colors ?? new UiThemeColors();
        backgroundColor = colors.Parse(colors.Background, 0xFF101318);
        headerColor = colors.Parse(colors.Header, 0xFF171D26);
        buttonColor = colors.Parse(colors.Button, 0xFF202735);
        buttonAltColor = colors.Parse(colors.ButtonAlt, 0xFF283241);
        pressedColor = colors.Parse(colors.ButtonPressed, 0xFF0B0F14);
        strokeColor = colors.Parse(colors.Stroke, 0xFF344153);
        textColor = colors.Parse(colors.Text, 0xFFF7F9FC);
        mutedTextColor = colors.Parse(colors.MutedText, 0xFFBAC4D4);
        logoPlateColor = colors.Parse(colors.LogoPlate, 0xFFF4F7FB);
        logoPlateStrokeColor = colors.Parse(colors.LogoPlateStroke, 0xFFD8E0EC);
        errorColor = colors.Parse(colors.Error, 0xFFD14B4B);
        okColor = colors.Parse(colors.Green, 0xFF22A06B);
        currentPageId = configStore.Config.HomePageId;
    }

    private UiTheme Theme => themeStore.Theme;

    private UiThemeTypography Typography => Theme.Typography ??= new UiThemeTypography();

    public DrawingWindow Start()
    {
        window = new DrawingWindow(OperatingSystem.IsLinux());
        var api = window.GetApi();
        typeface = LoadTypeface(api);

        window.PrepareFrame += (_, args) =>
        {
            foreach (var eventItem in args.Events)
            {
                if (eventItem.Event == TouchEvent.Pressed)
                {
                    foreach (var area in touchAreas)
                        area.IsTouched = area.Rect.Contains(eventItem.X, eventItem.Y);
                }
                else if (eventItem.Event == TouchEvent.Released)
                {
                    foreach (var area in touchAreas)
                    {
                        bool shouldRun = area.Rect.Contains(eventItem.X, eventItem.Y);
                        area.IsTouched = false;

                        if (shouldRun)
                            area.Command.Invoke();
                    }
                }
            }
        };

        window.RenderFrame += (_, _) => Draw(api);
        return window;
    }

    private DrawingTypeface? LoadTypeface(IDrawingApi api)
    {
        string fontFile = string.IsNullOrWhiteSpace(Theme.FontFile) ? "UiFont.ttf" : Theme.FontFile.Trim();
        string fontFamily = string.IsNullOrWhiteSpace(Theme.FontFamily) ? "Segoe UI" : Theme.FontFamily.Trim();

        try
        {
            return api.LoadTypeface(fontFile, fontFamily);
        }
        catch (Exception ex)
        {
            AhsokaLogging.LogMessage(AhsokaVerbosity.Low, $"Theme font load failed for {fontFile}: {ex.Message}");
            return api.LoadTypeface("UiFont.ttf", "Segoe UI");
        }
    }

    private void Draw(IDrawingApi api)
    {
        if (string.Equals(currentPageId, DeckConstants.KeyboardPageId, StringComparison.OrdinalIgnoreCase))
        {
            touchAreas.Clear();
            DrawKeyboard(api);
            return;
        }

        DeckPage page;
        string status;
        bool isError;

        lock (stateLock)
        {
            page = GetCurrentPage();
            status = statusText;
            isError = statusIsError;
        }

        touchAreas.Clear();

        api.Clear(backgroundColor);
        DrawHeader(api, page);
        DrawButtons(api, page);
        DrawStatus(api, status, isError);
    }

    private void DrawHeader(IDrawingApi api, DeckPage page)
    {
        api.DrawRectangle(new DrawingRect(0, 0, api.ScreenWidth, 76), headerColor, headerColor, 0);
        api.DrawRectangle(new DrawingRect(0, 74, api.ScreenWidth, 2), GetButtonColor("blue"), GetButtonColor("blue"), 0);
        api.DrawRectangle(new DrawingRect(0, 76, api.ScreenWidth, 14), backgroundColor, backgroundColor, 0);

        var titleInfo = CreateTextInfo(Typography.TitleSize, textColor, DrawingTextAlignment.Left);
        titleInfo.TextWeight = DrawingTextWeight.Bold;
        api.DrawText(configStore.Config.Title, 24, 34, titleInfo);

        var pageInfo = CreateTextInfo(Typography.PageSize, mutedTextColor, DrawingTextAlignment.Left);
        api.DrawText(page.Title, 24, 61, pageInfo);

        var modeInfo = CreateTextInfo(18, mutedTextColor, DrawingTextAlignment.Right);
        api.DrawText(deleteMode ? "MODO ELIMINAR" : "S70 Deck", api.ScreenWidth - 24, 45, modeInfo);
        
        if (deleteMode)
        {
            var warningInfo = CreateTextInfo(14, errorColor, DrawingTextAlignment.Right);
            api.DrawText("Toca app para borrar", api.ScreenWidth - 24, 64, warningInfo);
        }
    }

    private void DrawButtons(IDrawingApi api, DeckPage page)
    {
        const int columns = 4;
        const int rows = 2;
        const int gap = 16;
        const int left = 22;
        const int top = 96;
        int bottom = 62;

        float buttonWidth = (api.ScreenWidth - (left * 2) - (gap * (columns - 1))) / (float)columns;
        float buttonHeight = (api.ScreenHeight - top - bottom - (gap * (rows - 1))) / (float)rows;

        int count = Math.Min(page.Buttons.Count, columns * rows);
        for (int index = 0; index < count; index++)
        {
            DeckButton button = page.Buttons[index];
            int column = index % columns;
            int row = index / columns;
            var rect = new DrawingRect(
                left + column * (buttonWidth + gap),
                top + row * (buttonHeight + gap),
                buttonWidth,
                buttonHeight);

            DrawButton(api, rect, button, index);
            touchAreas.Add(new TouchArea
            {
                Rect = rect,
                Command = () => PressButton(button),
                IsTouched = false
            });
        }
    }

    private void DrawButton(IDrawingApi api, DrawingRect rect, DeckButton button, int index)
    {
        bool isTouched = touchAreas.FirstOrDefault(area => area.Rect.Equals(rect))?.IsTouched == true;
        DrawingColor accentColor = GetButtonColor(button.CssClass);
        bool solidStyle = string.Equals(Theme.ButtonStyle, "solid", StringComparison.OrdinalIgnoreCase);
        DrawingColor baseFill = isTouched
            ? pressedColor
            : solidStyle && !IsMuted(button)
            ? accentColor
            : index % 2 == 0 ? buttonColor : buttonAltColor;
        DrawingColor border = isTouched ? accentColor : strokeColor;

        api.DrawRectangle(new DrawingRect(rect.X + 4, rect.Y + 5, rect.Width, rect.Height), new DrawingColor(0x99000000), new DrawingColor(0x99000000), 0);
        api.DrawRectangle(rect, border, baseFill, 2);
        if (Theme.ShowButtonAccent)
            api.DrawRectangle(new DrawingRect(rect.X, rect.Y, rect.Width, 7), accentColor, accentColor, 0);

        bool hasLogo = !string.IsNullOrWhiteSpace(button.LogoPath);
        var plateRect = new DrawingRect(rect.X + rect.Width / 2 - 43, rect.Y + 20, 86, 74);
        var iconRect = new DrawingRect(plateRect.X + 12, plateRect.Y + 8, plateRect.Width - 24, plateRect.Height - 16);

        if (hasLogo && Theme.UseLightLogoPlate)
            api.DrawRectangle(plateRect, logoPlateStrokeColor, logoPlateColor, 1);

        if (!DrawLogo(api, iconRect, button.LogoPath))
        {
            var fallbackRect = Theme.UseLightLogoPlate
                ? plateRect
                : new DrawingRect(rect.X + rect.Width / 2 - 40, rect.Y + 22, 80, 66);
            api.DrawRectangle(fallbackRect, accentColor, accentColor, 0);
            var iconInfo = CreateTextInfo(Typography.IconSize, textColor, DrawingTextAlignment.Center);
            iconInfo.TextWeight = DrawingTextWeight.Bold;
            api.DrawText(TrimText(button.Icon, 5), fallbackRect.X + fallbackRect.Width / 2, fallbackRect.Y + fallbackRect.Height / 2 + 9, iconInfo);
        }

        var labelInfo = CreateTextInfo(Typography.LabelSize, textColor, DrawingTextAlignment.Center);
        labelInfo.TextWeight = DrawingTextWeight.Bold;
        string[] lines = SplitLabel(button.Label, 18, 2);
        float startY = rect.Y + rect.Height - 44 - ((lines.Length - 1) * 12);
        for (int i = 0; i < lines.Length; i++)
            api.DrawText(lines[i], rect.X + rect.Width / 2, startY + (i * 25), labelInfo);

        if (deleteMode && (string.Equals(button.Action, "launch", StringComparison.OrdinalIgnoreCase) || string.Equals(button.Action, "pc-launch", StringComparison.OrdinalIgnoreCase)))
        {
            var xRect = new DrawingRect(rect.X + rect.Width - 28, rect.Y + 6, 22, 22);
            api.DrawRectangle(xRect, errorColor, errorColor, 0);
            var xInfo = CreateTextInfo(14, textColor, DrawingTextAlignment.Center);
            xInfo.TextWeight = DrawingTextWeight.Bold;
            api.DrawText("X", xRect.X + 11, xRect.Y + 16, xInfo);
        }
    }

    private void DrawRoundedRect(IDrawingApi api, DrawingRect rect, float radius, DrawingColor stroke, DrawingColor fill)
    {
        api.DrawRectangle(rect, stroke, fill, 1);
    }

    private void DrawStatus(IDrawingApi api, string status, bool isError)
    {
        var statusRect = new DrawingRect(18, api.ScreenHeight - 44, api.ScreenWidth - 36, 32);
        DrawingColor fill = isError ? errorColor : headerColor;
        api.DrawRectangle(statusRect, strokeColor, fill, 1);

        var statusInfo = CreateTextInfo(Typography.StatusSize, isError ? textColor : mutedTextColor, DrawingTextAlignment.Left);
        api.DrawText(TrimText(status, 90), statusRect.X + 12, statusRect.Y + 22, statusInfo);
    }

    private void PressButton(DeckButton button)
    {
        if (deleteMode && (string.Equals(button.Action, "launch", StringComparison.OrdinalIgnoreCase) || string.Equals(button.Action, "pc-launch", StringComparison.OrdinalIgnoreCase)))
        {
            configStore.RemoveButton(currentPageId, button.Id);
            lock (stateLock)
            {
                statusText = $"Eliminado: {button.Label}";
                statusIsError = false;
                deleteMode = false;
            }
            return;
        }

        if (string.Equals(button.Action, "toggle-delete-mode", StringComparison.OrdinalIgnoreCase))
        {
            lock (stateLock)
                deleteMode = !deleteMode;
            return;
        }

        if (string.Equals(button.Action, "navigate", StringComparison.OrdinalIgnoreCase))
        {
            bool scanWiFi = string.Equals(button.TargetPage, DeckConstants.WiFiPageId, StringComparison.OrdinalIgnoreCase);
            bool scanBluetooth = string.Equals(button.TargetPage, DeckConstants.BluetoothPageId, StringComparison.OrdinalIgnoreCase);

            lock (stateLock)
            {
                currentPageId = button.TargetPage;
                if (!string.Equals(currentPageId, DeckConstants.AppCatalogPageId, StringComparison.OrdinalIgnoreCase))
                    selectedProgramSlotIndex = -1;
                installedPageIndex = 0;
                deleteMode = false;
                statusText = $"Pagina: {GetCurrentPage().Title}";
                statusIsError = false;
            }

            if (scanWiFi)
                RunBackgroundResult(() => wifiConnectionManager.Discover());
            if (scanBluetooth)
                RunBackgroundResult(() => bluetoothConnectionManager.StartDiscovery());

            return;
        }

        if (string.Equals(button.Action, "remove-program-button", StringComparison.OrdinalIgnoreCase))
        {
            DeckActionResult result = configStore.ClearProgramButton(button.Command);
            lock (stateLock)
            {
                currentPageId = DeckConstants.RemoveAppsPageId;
                statusText = result.Message;
                statusIsError = !result.Success;
            }

            return;
        }

        if (string.Equals(button.Action, "add-app", StringComparison.OrdinalIgnoreCase))
        {
            var app = pcCompanionClient.GetInstalledApps().FirstOrDefault(item => string.Equals(item.Id, button.CatalogId, StringComparison.OrdinalIgnoreCase))
                ?? pcCompanionClient.GetCatalog().FirstOrDefault(item => string.Equals(item.Id, button.CatalogId, StringComparison.OrdinalIgnoreCase))
                ?? catalogStore.GetById(button.CatalogId);
            DeckActionResult result = app == null
                ? new DeckActionResult(false, "App no encontrada en catalogo.")
                : selectedProgramSlotIndex >= 0
                ? configStore.SetProgramButton(selectedProgramSlotIndex, app)
                : configStore.AddApplication(app);

            lock (stateLock)
            {
                statusText = result.Message;
                statusIsError = !result.Success;
                selectedProgramSlotIndex = -1;
                currentPageId = configStore.Config.ProgramsPageId;
            }

            return;
        }

        if (string.Equals(button.Action, "page-next", StringComparison.OrdinalIgnoreCase))
        {
            lock (stateLock)
                installedPageIndex++;
            return;
        }

        if (string.Equals(button.Action, "page-prev", StringComparison.OrdinalIgnoreCase))
        {
            lock (stateLock)
                installedPageIndex = Math.Max(0, installedPageIndex - 1);
            return;
        }

        if (string.Equals(button.Action, "select-program-slot", StringComparison.OrdinalIgnoreCase))
        {
            lock (stateLock)
            {
                selectedProgramSlotIndex = Math.Max(0, button.TimeoutMs - 1);
                installedPageIndex = 0;
                currentPageId = DeckConstants.AppCatalogPageId;
                statusText = $"Elige app para boton {selectedProgramSlotIndex + 1}";
                statusIsError = false;
            }

            return;
        }

        if (string.Equals(button.Action, "wifi-add", StringComparison.OrdinalIgnoreCase))
        {
            StartWiFiInput();
            return;
        }

        if (string.Equals(button.Action, "bt-add", StringComparison.OrdinalIgnoreCase))
        {
            StartBluetoothInput();
            return;
        }

        if (string.Equals(button.Action, "theme-style", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(themeStore.SetButtonStyle(button.Command));
            return;
        }

        if (string.Equals(button.Action, "theme-logo", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(themeStore.ToggleLogoPlate());
            return;
        }

        if (string.Equals(button.Action, "theme-text", StringComparison.OrdinalIgnoreCase))
        {
            float delta = string.Equals(button.Command, "plus", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
            SetStatus(themeStore.AdjustTextSize(delta));
            return;
        }

        if (string.Equals(button.Action, "theme-reset", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(themeStore.Reset());
            return;
        }

        if (string.Equals(button.Action, "wifi-connect", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(button.CatalogId, "scanned", StringComparison.OrdinalIgnoreCase))
        {
            StartWiFiPasswordInput(button.Label, button.Command);
            return;
        }

        lock (stateLock)
        {
            statusText = $"Ejecutando: {button.Label}";
            statusIsError = false;
        }

        Task.Run(async () =>
        {
            DeckActionResult result = await actionRunner.ExecuteAsync(button);
            SetStatus(result);
        });
    }

    private void RunBackgroundResult(Func<DeckActionResult> action)
    {
        Task.Run(() => SetStatus(action()));
    }

    private void RunBackgroundResult(Func<Task<DeckActionResult>> action)
    {
        Task.Run(async () => SetStatus(await action()));
    }

    private void SetStatus(DeckActionResult result)
    {
        lock (stateLock)
        {
            statusText = result.Message;
            statusIsError = !result.Success;
        }
    }

    private DrawingTextInfo CreateTextInfo(float size, DrawingColor color, DrawingTextAlignment alignment)
    {
        return new DrawingTextInfo
        {
            Alignment = alignment,
            Color = color,
            TextSize = size,
            Typeface = typeface
        };
    }

    private DeckPage GetCurrentPage()
    {
        if (string.Equals(currentPageId, DeckConstants.AppCatalogPageId, StringComparison.OrdinalIgnoreCase))
            return CreateCatalogPage();
        if (string.Equals(currentPageId, DeckConstants.ProgramSlotsPageId, StringComparison.OrdinalIgnoreCase))
            return CreateProgramSlotsPage();
        if (string.Equals(currentPageId, DeckConstants.RemoveAppsPageId, StringComparison.OrdinalIgnoreCase))
            return CreateRemoveAppsPage();
        if (string.Equals(currentPageId, DeckConstants.ThemePageId, StringComparison.OrdinalIgnoreCase))
            return CreateThemePage();
        if (string.Equals(currentPageId, DeckConstants.WiFiPageId, StringComparison.OrdinalIgnoreCase))
            return CreateWiFiPage();
        if (string.Equals(currentPageId, DeckConstants.BluetoothPageId, StringComparison.OrdinalIgnoreCase))
            return CreateBluetoothPage();

        return configStore.Config.GetPageOrHome(currentPageId);
    }

    private DeckPage CreateCatalogPage()
    {
        var page = new DeckPage
        {
            Id = DeckConstants.AppCatalogPageId,
            Title = selectedProgramSlotIndex >= 0 ? $"Boton {selectedProgramSlotIndex + 1}" : "Agregar Apps"
        };

        var apps = pcCompanionClient.GetInstalledApps();
        if (apps.Count == 0)
            apps = pcCompanionClient.GetCatalog();
        if (apps.Count == 0)
            apps = catalogStore.Apps;

        const int pageSize = 5;
        int totalPages = Math.Max(1, (int)Math.Ceiling(apps.Count / (double)pageSize));
        installedPageIndex = Math.Clamp(installedPageIndex, 0, totalPages - 1);

        foreach (var app in apps.Skip(installedPageIndex * pageSize).Take(pageSize))
        {
            page.Buttons.Add(new DeckButton
            {
                Id = app.Id,
                Label = app.Label,
                Icon = string.IsNullOrWhiteSpace(app.Icon) ? "APP" : app.Icon,
                LogoPath = app.LogoPath,
                Action = "add-app",
                CatalogId = app.Id,
                CssClass = app.CssClass
            });
        }

        if (installedPageIndex > 0)
            page.Buttons.Add(new DeckButton { Id = "prev", Label = "Anterior", Icon = "PREV", Action = "page-prev", CssClass = "muted" });
        if (installedPageIndex < totalPages - 1)
            page.Buttons.Add(new DeckButton { Id = "next", Label = "Siguiente", Icon = "NEXT", Action = "page-next", CssClass = "blue" });

        page.Buttons.Add(new DeckButton
        {
            Id = "back",
            Label = "Regresar",
            Icon = "BACK",
            Action = "navigate",
            TargetPage = selectedProgramSlotIndex >= 0 ? DeckConstants.ProgramSlotsPageId : configStore.Config.HomePageId,
            CssClass = "muted"
        });
        return page;
    }

    private DeckPage CreateProgramSlotsPage()
    {
        var page = new DeckPage { Id = DeckConstants.ProgramSlotsPageId, Title = "Programar Apps" };
        var programs = configStore.Config.GetPageOrHome(configStore.Config.ProgramsPageId)
            .Buttons
            .Where(button => !string.Equals(button.Id, "back", StringComparison.OrdinalIgnoreCase))
            .ToList();

        for (int i = 0; i < 7; i++)
        {
            var current = i < programs.Count ? programs[i] : null;
            page.Buttons.Add(new DeckButton
            {
                Id = $"slot-{i + 1}",
                Label = current == null || string.Equals(current.Action, "noop", StringComparison.OrdinalIgnoreCase)
                    ? $"Boton {i + 1}"
                    : current.Label,
                Icon = current?.Icon ?? "APP",
                LogoPath = current?.LogoPath ?? "",
                Action = "select-program-slot",
                TimeoutMs = i + 1,
                CssClass = current?.CssClass ?? "muted"
            });
        }

        page.Buttons.Add(new DeckButton { Id = "back", Label = "Regresar", Icon = "BACK", Action = "navigate", TargetPage = "settings", CssClass = "muted" });
        return page;
    }

    private DeckPage CreateRemoveAppsPage()
    {
        var page = new DeckPage { Id = DeckConstants.RemoveAppsPageId, Title = "Eliminar Apps" };
        var removableButtons = configStore.Config.GetPageOrHome(configStore.Config.ProgramsPageId)
            .Buttons
            .Where(button => !string.Equals(button.Id, "back", StringComparison.OrdinalIgnoreCase))
            .Where(button => string.Equals(button.Action, "launch", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(button.Action, "pc-launch", StringComparison.OrdinalIgnoreCase))
            .ToList();

        const int pageSize = 5;
        int totalPages = Math.Max(1, (int)Math.Ceiling(removableButtons.Count / (double)pageSize));
        installedPageIndex = Math.Clamp(installedPageIndex, 0, totalPages - 1);

        foreach (var button in removableButtons.Skip(installedPageIndex * pageSize).Take(pageSize))
        {
            page.Buttons.Add(new DeckButton
            {
                Id = $"remove-{button.Id}",
                Label = button.Label,
                Icon = string.IsNullOrWhiteSpace(button.Icon) ? "APP" : button.Icon,
                LogoPath = button.LogoPath,
                Action = "remove-program-button",
                Command = button.Id,
                CssClass = "error"
            });
        }

        if (removableButtons.Count == 0)
        {
            page.Buttons.Add(new DeckButton
            {
                Id = "empty",
                Label = "Sin Apps",
                Icon = "APP",
                Action = "noop",
                CssClass = "muted"
            });
        }

        if (installedPageIndex > 0)
            page.Buttons.Add(new DeckButton { Id = "prev", Label = "Anterior", Icon = "PREV", Action = "page-prev", CssClass = "muted" });
        if (installedPageIndex < totalPages - 1)
            page.Buttons.Add(new DeckButton { Id = "next", Label = "Siguiente", Icon = "NEXT", Action = "page-next", CssClass = "blue" });

        page.Buttons.Add(new DeckButton { Id = "back", Label = "Regresar", Icon = "BACK", Action = "navigate", TargetPage = "settings", CssClass = "muted" });
        return page;
    }

    private DeckPage CreateThemePage()
    {
        var page = new DeckPage { Id = DeckConstants.ThemePageId, Title = "Tema" };
        bool isSolid = string.Equals(Theme.ButtonStyle, "solid", StringComparison.OrdinalIgnoreCase);

        page.Buttons.Add(new DeckButton { Id = "theme-soft", Label = "Suave", Icon = "SOFT", Action = "theme-style", Command = "soft", CssClass = isSolid ? "muted" : "green" });
        page.Buttons.Add(new DeckButton { Id = "theme-solid", Label = "Solido", Icon = "FULL", Action = "theme-style", Command = "solid", CssClass = isSolid ? "green" : "muted" });
        page.Buttons.Add(new DeckButton { Id = "theme-logo", Label = Theme.UseLightLogoPlate ? "Logo Claro" : "Logo Plano", Icon = "LOGO", Action = "theme-logo", CssClass = Theme.UseLightLogoPlate ? "blue" : "muted" });
        page.Buttons.Add(new DeckButton { Id = "theme-text-plus", Label = "Texto +", Icon = "A+", Action = "theme-text", Command = "plus", CssClass = "blue" });
        page.Buttons.Add(new DeckButton { Id = "theme-text-minus", Label = "Texto -", Icon = "A-", Action = "theme-text", Command = "minus", CssClass = "purple" });
        page.Buttons.Add(new DeckButton { Id = "theme-reset", Label = "Reset", Icon = "RST", Action = "theme-reset", CssClass = "error" });
        page.Buttons.Add(new DeckButton { Id = "back", Label = "Regresar", Icon = "BACK", Action = "navigate", TargetPage = "settings", CssClass = "muted" });

        return page;
    }

    private DeckPage CreateWiFiPage()
    {
        var page = new DeckPage { Id = DeckConstants.WiFiPageId, Title = "Conectar WiFi" };

        var configuredNetworks = connectivityStore.Config.WiFiNetworks
            .Concat(pcCompanionClient.GetWiFiNetworks())
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Ssid))
            .Select(profile => (Profile: profile, IsConfigured: true));

        var scannedNetworks = wifiConnectionManager.GetAvailableProfiles()
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Ssid))
            .Select(profile => (Profile: profile, IsConfigured: false));

        var wifiNetworks = scannedNetworks
            .Concat(configuredNetworks)
            .GroupBy(item => item.Profile.Ssid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.IsConfigured).First())
            .ToList();

        const int pageSize = 3;
        int totalPages = Math.Max(1, (int)Math.Ceiling(wifiNetworks.Count / (double)pageSize));
        installedPageIndex = Math.Clamp(installedPageIndex, 0, totalPages - 1);

        foreach (var item in wifiNetworks
                     .Skip(installedPageIndex * pageSize)
                     .Take(pageSize))
        {
            var profile = item.Profile;
            string label = string.IsNullOrWhiteSpace(profile.Label) ? profile.Ssid : profile.Label;
            page.Buttons.Add(new DeckButton
            {
                Id = string.IsNullOrWhiteSpace(profile.Id) ? CreateSafeId(label) : profile.Id,
                Label = label,
                Icon = "WIFI",
                Action = "wifi-connect",
                Command = profile.Ssid,
                Arguments = item.IsConfigured ? profile.Password : "",
                CatalogId = item.IsConfigured ? "configured" : "scanned",
                CssClass = string.IsNullOrWhiteSpace(profile.CssClass) ? "blue" : profile.CssClass
            });
        }

        if (installedPageIndex > 0)
            page.Buttons.Add(new DeckButton { Id = "prev", Label = "Anterior", Icon = "PREV", Action = "page-prev", CssClass = "muted" });

        if (installedPageIndex < totalPages - 1)
            page.Buttons.Add(new DeckButton { Id = "next", Label = "Siguiente", Icon = "NEXT", Action = "page-next", CssClass = "blue" });

        page.Buttons.Add(new DeckButton { Id = "wifi-scan", Label = "Buscar WiFi", Icon = "SCAN", Action = "wifi-scan", CssClass = "blue" });

        page.Buttons.Add(new DeckButton { Id = "wifi-disconnect", Label = "Desconectar", Icon = "OFF", Action = "wifi-disconnect", CssClass = "muted" });
        page.Buttons.Add(new DeckButton { Id = "back", Label = "Regresar", Icon = "BACK", Action = "navigate", TargetPage = configStore.Config.HomePageId, CssClass = "muted" });
        return page;
    }

    private DeckPage CreateBluetoothPage()
    {
        var page = new DeckPage { Id = DeckConstants.BluetoothPageId, Title = "Conectar Bluetooth" };

        var configuredDevices = pcCompanionClient.GetBluetoothDevices()
            .Concat(connectivityStore.Config.BluetoothDevices)
            .Where(device => !string.IsNullOrWhiteSpace(device.MacAddress))
            .Select(device => (Profile: device, IsConfigured: true));
        var pairedDevices = bluetoothConnectionManager.GetPairedDevices()
            .Where(device => !string.IsNullOrWhiteSpace(device.MacAddress))
            .Select(device => (Profile: device, IsConfigured: true));
        var discoveredDevices = bluetoothConnectionManager.GetDiscoveredDevices()
            .Where(device => !string.IsNullOrWhiteSpace(device.MacAddress))
            .Select(device => (Profile: device, IsConfigured: false));

        var devices = discoveredDevices
            .Concat(pairedDevices)
            .Concat(configuredDevices)
            .GroupBy(device => device.Profile.MacAddress, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.IsConfigured).First())
            .ToList();

        const int pageSize = 3;
        int totalPages = Math.Max(1, (int)Math.Ceiling(devices.Count / (double)pageSize));
        installedPageIndex = Math.Clamp(installedPageIndex, 0, totalPages - 1);

        foreach (var item in devices.Skip(installedPageIndex * pageSize).Take(pageSize))
        {
            var device = item.Profile;
            string label = string.IsNullOrWhiteSpace(device.Label) ? device.MacAddress : device.Label;
            page.Buttons.Add(new DeckButton
            {
                Id = string.IsNullOrWhiteSpace(device.Id) ? CreateSafeId(label) : device.Id,
                Label = label,
                Icon = "BT",
                Action = "bt-connect",
                Command = device.MacAddress,
                CssClass = string.IsNullOrWhiteSpace(device.CssClass) ? "purple" : device.CssClass
            });
        }

        if (installedPageIndex > 0)
            page.Buttons.Add(new DeckButton { Id = "prev", Label = "Anterior", Icon = "PREV", Action = "page-prev", CssClass = "muted" });

        if (installedPageIndex < totalPages - 1)
            page.Buttons.Add(new DeckButton { Id = "next", Label = "Siguiente", Icon = "NEXT", Action = "page-next", CssClass = "blue" });

        page.Buttons.Add(new DeckButton { Id = "bt-scan", Label = "Buscar BT", Icon = "SCAN", Action = "bt-scan", CssClass = "purple" });

        page.Buttons.Add(new DeckButton { Id = "back", Label = "Regresar", Icon = "BACK", Action = "navigate", TargetPage = configStore.Config.HomePageId, CssClass = "muted" });
        return page;
    }

    private void StartWiFiInput()
    {
        BeginKeyboard("SSID WiFi", "", false, DeckConstants.WiFiPageId, ssid =>
        {
            BeginKeyboard("Password WiFi", "", true, DeckConstants.WiFiPageId, password =>
            {
                DeckActionResult result = connectivityStore.AddWiFi(ssid, ssid, password);
                lock (stateLock)
                {
                    currentPageId = DeckConstants.WiFiPageId;
                    statusText = result.Message;
                    statusIsError = !result.Success;
                }
            });
        });
    }

    private void StartWiFiPasswordInput(string label, string ssid)
    {
        ssid = (ssid ?? "").Trim();
        if (string.IsNullOrWhiteSpace(ssid))
        {
            SetStatus(new DeckActionResult(false, "Red WiFi invalida."));
            return;
        }

        BeginKeyboard($"Password {TrimText(ssid, 18)}", "", true, DeckConstants.WiFiPageId, password =>
        {
            DeckActionResult saved = connectivityStore.AddWiFi(label, ssid, password);
            SetStatus(saved);
            if (saved.Success)
                RunBackgroundResult(() => wifiConnectionManager.ConnectAsync(ssid, password));
        });
    }

    private void StartBluetoothInput()
    {
        BeginKeyboard("Nombre BT", "", false, DeckConstants.BluetoothPageId, label =>
        {
            BeginKeyboard("MAC Bluetooth", "", false, DeckConstants.BluetoothPageId, mac =>
            {
                DeckActionResult result = connectivityStore.AddBluetooth(label, mac);
                lock (stateLock)
                {
                    currentPageId = DeckConstants.BluetoothPageId;
                    statusText = result.Message;
                    statusIsError = !result.Success;
                }
            });
        });
    }

    private void BeginKeyboard(string title, string value, bool masked, string returnPage, Action<string> completed)
    {
        lock (stateLock)
        {
            keyboardTitle = title;
            keyboardValue = value ?? "";
            keyboardMasked = masked;
            keyboardReturnPage = returnPage;
            keyboardCompleted = completed;
            keyboardShift = false;
            keyboardSymbols = false;
            currentPageId = DeckConstants.KeyboardPageId;
            statusText = title;
            statusIsError = false;
        }
    }

    private void DrawKeyboard(IDrawingApi api)
    {
        string title;
        string value;
        bool masked;
        bool shift;
        bool symbols;

        lock (stateLock)
        {
            title = keyboardTitle;
            value = keyboardValue;
            masked = keyboardMasked;
            shift = keyboardShift;
            symbols = keyboardSymbols;
        }

        api.Clear(backgroundColor);
        api.DrawRectangle(new DrawingRect(0, 0, api.ScreenWidth, 74), headerColor, headerColor, 0);

        var titleInfo = CreateTextInfo(Typography.KeyboardTitleSize, textColor, DrawingTextAlignment.Left);
        titleInfo.TextWeight = DrawingTextWeight.Bold;
        api.DrawText(title, 20, 45, titleInfo);

        var inputRect = new DrawingRect(20, 88, api.ScreenWidth - 40, 54);
        api.DrawRectangle(inputRect, strokeColor, pressedColor, 2);
        var inputInfo = CreateTextInfo(Typography.KeyboardTextSize, textColor, DrawingTextAlignment.Left);
        string displayValue = masked ? new string('*', Math.Min(value.Length, 28)) : value;
        api.DrawText(TrimText(displayValue, 42), inputRect.X + 14, inputRect.Y + 36, inputInfo);

        string[] rows = symbols
            ? new[] { "1234567890", "!@#$%&*()", ".-_:/\\+=?" }
            : shift
            ? new[] { "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" }
            : new[] { "qwertyuiop", "asdfghjkl", "zxcvbnm" };

        float keyGap = 7;
        float keyHeight = 56;
        float top = 160;

        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            string row = rows[rowIndex];
            float keyWidth = (api.ScreenWidth - 40 - (keyGap * (row.Length - 1))) / row.Length;
            float x = 20;
            if (rowIndex == 1 && !symbols)
                x += keyWidth / 2;
            if (rowIndex == 2 && !symbols)
                x += keyWidth * 1.5f;

            for (int i = 0; i < row.Length; i++)
            {
                string key = row[i].ToString();
                AddKeyboardButton(api, new DrawingRect(x + i * (keyWidth + keyGap), top + rowIndex * (keyHeight + keyGap), keyWidth, keyHeight), key, key, buttonColor);
            }
        }

        float commandTop = top + 3 * (keyHeight + keyGap) + 8;
        float commandWidth = (api.ScreenWidth - 40 - (keyGap * 5)) / 6;
        AddKeyboardButton(api, new DrawingRect(20, commandTop, commandWidth, keyHeight), shift ? "min" : "SHIFT", "shift", GetButtonColor("muted"));
        AddKeyboardButton(api, new DrawingRect(20 + (commandWidth + keyGap), commandTop, commandWidth, keyHeight), symbols ? "ABC" : "SYM", "symbols", GetButtonColor("muted"));
        AddKeyboardButton(api, new DrawingRect(20 + 2 * (commandWidth + keyGap), commandTop, commandWidth * 1.5f, keyHeight), "Espacio", "space", GetButtonColor("muted"));
        AddKeyboardButton(api, new DrawingRect(20 + 3.5f * (commandWidth + keyGap), commandTop, commandWidth, keyHeight), "Borrar", "backspace", errorColor);
        AddKeyboardButton(api, new DrawingRect(20 + 4.5f * (commandWidth + keyGap), commandTop, commandWidth, keyHeight), "Cancelar", "cancel", GetButtonColor("muted"));
        AddKeyboardButton(api, new DrawingRect(20 + 5.5f * (commandWidth + keyGap), commandTop, commandWidth * .5f, keyHeight), "OK", "ok", okColor);
    }

    private void AddKeyboardButton(IDrawingApi api, DrawingRect rect, string label, string command, DrawingColor fill)
    {
        api.DrawRectangle(rect, strokeColor, fill, 2);
        var info = CreateTextInfo(label.Length > 5 ? Typography.KeyboardTextSize - 5 : Typography.KeyboardTextSize, textColor, DrawingTextAlignment.Center);
        info.TextWeight = DrawingTextWeight.Bold;
        api.DrawText(label, rect.X + rect.Width / 2, rect.Y + rect.Height / 2 + 8, info);

        touchAreas.Add(new TouchArea
        {
            Rect = rect,
            Command = () => PressKeyboard(command)
        });
    }

    private void PressKeyboard(string command)
    {
        Action<string>? completed = null;
        string completedValue = "";

        lock (stateLock)
        {
            switch (command)
            {
                case "shift":
                    keyboardShift = !keyboardShift;
                    keyboardSymbols = false;
                    return;
                case "symbols":
                    keyboardSymbols = !keyboardSymbols;
                    return;
                case "space":
                    keyboardValue += " ";
                    return;
                case "backspace":
                    if (keyboardValue.Length > 0)
                        keyboardValue = keyboardValue[..^1];
                    return;
                case "cancel":
                    currentPageId = keyboardReturnPage;
                    keyboardCompleted = null;
                    statusText = "Cancelado";
                    statusIsError = false;
                    return;
                case "ok":
                    completed = keyboardCompleted;
                    completedValue = keyboardValue;
                    keyboardCompleted = null;
                    currentPageId = keyboardReturnPage;
                    break;
                default:
                    keyboardValue += command;
                    if (keyboardShift)
                        keyboardShift = false;
                    return;
            }
        }

        completed?.Invoke(completedValue);
    }

    private bool DrawLogo(IDrawingApi api, DrawingRect rect, string logoPath)
    {
        string resolvedPath = ResolveAssetPath(logoPath);
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            return false;

        try
        {
            var logo = api.CreateImage(resolvedPath);
            api.DrawBitmap(logo, rect);
            return true;
        }
        catch (Exception ex)
        {
            AhsokaLogging.LogMessage(AhsokaVerbosity.Low, $"Logo load failed for {resolvedPath}: {ex.Message}");
            return false;
        }
    }

    private static string ResolveAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        path = path.Trim();
        if (Path.IsPathRooted(path))
            return path;

        return Path.Combine(AppContext.BaseDirectory, path.Replace('/', Path.DirectorySeparatorChar));
    }

    private DrawingColor GetButtonColor(string cssClass)
    {
        var colors = Theme.Colors ?? new UiThemeColors();
        return (cssClass ?? "").Trim().ToLowerInvariant() switch
        {
            "blue" => colors.Parse(colors.Blue, 0xFF2D7DFF),
            "green" => okColor,
            "purple" => colors.Parse(colors.Purple, 0xFF8B6DFF),
            "error" => errorColor,
            "muted" => colors.Parse(colors.Muted, 0xFF505B6E),
            _ => buttonAltColor
        };
    }

    private static bool IsMuted(DeckButton button)
    {
        return string.Equals(button.CssClass, "muted", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(button.Action, "noop", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimText(string? value, int maxLength)
    {
        value = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + ".";
    }

    private static string[] SplitLabel(string? value, int maxLineLength, int maxLines)
    {
        value = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        if (value.Length <= maxLineLength)
            return new[] { value };

        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        string current = "";

        foreach (string word in words)
        {
            string candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
            if (candidate.Length <= maxLineLength)
            {
                current = candidate;
                continue;
            }

            if (!string.IsNullOrEmpty(current))
                lines.Add(current);

            current = word.Length <= maxLineLength ? word : TrimText(word, maxLineLength);
            if (lines.Count == maxLines - 1)
                break;
        }

        if (lines.Count < maxLines && !string.IsNullOrEmpty(current))
            lines.Add(current);

        if (lines.Count == 0)
            lines.Add(TrimText(value, maxLineLength));

        return lines.Take(maxLines).ToArray();
    }

    private static string CreateSafeId(string value)
    {
        string safe = new string((value ?? "item").Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "item" : safe.ToLowerInvariant();
    }

    private sealed class TouchArea
    {
        public DrawingRect Rect { get; init; } = DrawingRect.Empty;
        public bool IsTouched { get; set; }
        public Action Command { get; init; } = () => { };
    }
}
