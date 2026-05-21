using System.Text.Json;

namespace Ahsoka.CS.StreamDeck;

public sealed class AppCatalogStore
{
    private readonly string path;

    public AppCatalogStore(string path)
    {
        this.path = path;
        Apps = Load(path);
    }

    public List<DeckAppTemplate> Apps { get; }

    public DeckAppTemplate? GetById(string id)
    {
        return Apps.FirstOrDefault(app => string.Equals(app.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static List<DeckAppTemplate> Load(string path)
    {
        if (!File.Exists(path))
            return new List<DeckAppTemplate>();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        string json = File.ReadAllText(path);
        var catalog = JsonSerializer.Deserialize<AppCatalog>(json, options);
        return catalog?.Applications ?? new List<DeckAppTemplate>();
    }
}

public sealed class AppCatalog
{
    public List<DeckAppTemplate> Applications { get; set; } = new();
}
