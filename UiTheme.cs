using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ahsoka.Core.Drawing.Base;

namespace Ahsoka.CS.StreamDeck;

public sealed class UiThemeStore
{
    private readonly string path;

    public UiThemeStore(string path)
    {
        this.path = path;
        Theme = Load(path);
    }

    public UiTheme Theme { get; private set; }

    public DeckActionResult SetButtonStyle(string style)
    {
        style = string.Equals(style, "solid", StringComparison.OrdinalIgnoreCase) ? "solid" : "soft";
        Theme.ButtonStyle = style;
        Save();
        return new DeckActionResult(true, $"Tema: botones {style}");
    }

    public DeckActionResult ToggleLogoPlate()
    {
        Theme.UseLightLogoPlate = !Theme.UseLightLogoPlate;
        Save();
        return new DeckActionResult(true, Theme.UseLightLogoPlate ? "Logos claros activados." : "Logos claros apagados.");
    }

    public DeckActionResult AdjustTextSize(float delta)
    {
        Theme.Typography ??= new UiThemeTypography();
        Theme.Typography.TitleSize = Clamp(Theme.Typography.TitleSize + delta, 22, 34);
        Theme.Typography.PageSize = Clamp(Theme.Typography.PageSize + delta * .5f, 13, 22);
        Theme.Typography.IconSize = Clamp(Theme.Typography.IconSize + delta, 20, 32);
        Theme.Typography.LabelSize = Clamp(Theme.Typography.LabelSize + delta, 16, 26);
        Theme.Typography.StatusSize = Clamp(Theme.Typography.StatusSize + delta * .5f, 12, 19);
        Theme.Typography.KeyboardTitleSize = Clamp(Theme.Typography.KeyboardTitleSize + delta, 22, 34);
        Theme.Typography.KeyboardTextSize = Clamp(Theme.Typography.KeyboardTextSize + delta, 18, 28);
        Save();
        return new DeckActionResult(true, $"Texto: {Theme.Typography.LabelSize:0}");
    }

    public DeckActionResult Reset()
    {
        Theme = UiTheme.CreateDefault();
        Save();
        return new DeckActionResult(true, "Tema reiniciado.");
    }

    private static UiTheme Load(string path)
    {
        if (!File.Exists(path))
            return UiTheme.CreateDefault();

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            return JsonSerializer.Deserialize<UiTheme>(File.ReadAllText(path), options) ?? UiTheme.CreateDefault();
        }
        catch
        {
            return UiTheme.CreateDefault();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(Theme, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        }));
    }

    private static float Clamp(float value, float min, float max)
    {
        return Math.Min(max, Math.Max(min, value));
    }
}

public sealed class UiTheme
{
    public string FontFile { get; set; } = "UiFont.ttf";
    public string FontFamily { get; set; } = "Segoe UI";
    public bool UseLightLogoPlate { get; set; } = true;
    public bool ShowButtonAccent { get; set; } = true;
    public string ButtonStyle { get; set; } = "soft";
    public UiThemeColors Colors { get; set; } = new();
    public UiThemeTypography? Typography { get; set; } = new();

    public static UiTheme CreateDefault()
    {
        return new UiTheme();
    }
}

public sealed class UiThemeColors
{
    public string Background { get; set; } = "#101318";
    public string Header { get; set; } = "#171D26";
    public string Button { get; set; } = "#202735";
    public string ButtonAlt { get; set; } = "#283241";
    public string ButtonPressed { get; set; } = "#0B0F14";
    public string Stroke { get; set; } = "#344153";
    public string Text { get; set; } = "#F7F9FC";
    public string MutedText { get; set; } = "#BAC4D4";
    public string LogoPlate { get; set; } = "#F4F7FB";
    public string LogoPlateStroke { get; set; } = "#D8E0EC";
    public string Blue { get; set; } = "#2D7DFF";
    public string Green { get; set; } = "#22A06B";
    public string Purple { get; set; } = "#8B6DFF";
    public string Error { get; set; } = "#D14B4B";
    public string Muted { get; set; } = "#505B6E";

    public DrawingColor Parse(string value, uint fallback)
    {
        if (TryParse(value, out var color))
            return color;

        return new DrawingColor(fallback);
    }

    private static bool TryParse(string value, out DrawingColor color)
    {
        color = new DrawingColor(0xFFFFFFFF);
        value = (value ?? "").Trim().TrimStart('#');

        if (value.Length == 6 &&
            uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            color = new DrawingColor(0xFF000000 | rgb);
            return true;
        }

        if (value.Length == 8 &&
            uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
        {
            color = new DrawingColor(argb);
            return true;
        }

        return false;
    }
}

public sealed class UiThemeTypography
{
    public float TitleSize { get; set; } = 26;
    public float PageSize { get; set; } = 16;
    public float IconSize { get; set; } = 24;
    public float LabelSize { get; set; } = 20;
    public float StatusSize { get; set; } = 15;
    public float KeyboardTitleSize { get; set; } = 26;
    public float KeyboardTextSize { get; set; } = 22;
}
