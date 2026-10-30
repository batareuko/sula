using System.Text.Json;

namespace AionMeter.Core.OneHp;

/// <summary>
/// 1 HP: the world maps (data/world_maps.json, from Aion2Data's interactive maps) — each map's picture address and the
/// places on it by layer. Points are in the map's own units: x from 0 to 10000 across the picture's width, y in the
/// same units down from its top.
/// </summary>
public sealed class WorldMaps
{
    public const double Units = 10_000;

    /// <param name="Name">What stands there (boss, dungeon entrance, pet, ore …).</param>
    /// <param name="Sub">A line under the name (level, where the rift leads, soul monster …).</param>
    /// <param name="Kind">The layer's own grouping (gathering type, pet family, cube "on land").</param>
    public sealed record Place(string Name, string? Sub, string? Kind, IReadOnlyList<(double X, double Y)> Points)
    {
        /// <summary>Its own game icon (a gathering node's item, a boss's or pet's portrait), by resource name.</summary>
        public string? Icon { get; init; }
    }

    /// <summary>The game's map marker of each layer (resource names on the game's CDN).</summary>
    public static string LayerIcon(string key) => key switch
    {
        "bosses" => "UT_Marker_Monster_Boss",
        "dungeons" => "UT_Marker_Dungeon",
        "rifts" => "UT_Marker_Rift",
        "kibelisks" => "UT_Marker_TeleportArtifact_Light_Unengraved",
        "monoliths" => "UT_Marker_Monolith_Light",
        "gather" => "UT_Marker_Gatherl_Jewelry",
        "pets" or "pets-fera" or "pets-natura" or "pets-varian" or "pets-cogni" => "Icon_BMShop_Soulcrystal_All",
        "cubes" => "Icon_Item_Currency_Treasure_Key_A_s_001",
        _ => "UT_Marker_Pin",
    };

    /// <summary>The icon a place is drawn with: gathering nodes by what they give, the rest by their layer's marker.</summary>
    public static string IconOf(string layer, Place place) => layer == "gather" && place.Icon is { Length: > 0 } own ? own : LayerIcon(layer);

    public sealed record Area(string Name, double X, double Y, int Rank);

    /// <summary>Game position (centimetres) → map units: u = Scale·x + OffsetX, v = Scale·y + OffsetY (data/map_calibration.json).</summary>
    public sealed record Calibration(int MapId, double Scale, double OffsetX, double OffsetY);

    public sealed record Map(string Slug, string Name, string Faction, double Metres, string ImageUrl, int Width, int Height,
        IReadOnlyList<Area> Areas, IReadOnlyDictionary<string, IReadOnlyList<Place>> Layers)
    {
        /// <summary>How game positions land on this map; null until a recording has measured it.</summary>
        public Calibration? Fit { get; init; }

        public (double X, double Y)? ToMap(float x, float y) =>
            Fit is { } f ? (f.Scale * x + f.OffsetX, f.Scale * y + f.OffsetY) : null;

        /// <summary>Height of the map in its units (x runs to 10000).</summary>
        public double UnitsHigh => Units * Height / Width;

        /// <summary>Metres between two points (the map is <see cref="Metres"/> wide).</summary>
        public double MetresBetween((double X, double Y) a, (double X, double Y) b) =>
            Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)) * Metres / Units;
    }

    /// <summary>Layers in the order the map window offers them.</summary>
    public static readonly string[] LayerOrder =
        ["bosses", "dungeons", "rifts", "kibelisks", "monoliths", "gather", "pets-fera", "pets-natura", "pets-varian", "pets-cogni", "cubes"];

    /// <summary>Pet souls come as one layer per family (the place's kind): "pets-fera", "pets-natura" …</summary>
    public static string PetLayer(string? family) => "pets-" + (family ?? "other").ToLowerInvariant();

    public IReadOnlyList<Map> All { get; }

    public WorldMaps(IReadOnlyList<Map> maps) => All = maps;

    public Map? Find(string? slug) => All.FirstOrDefault(m => m.Slug == slug);

    /// <summary>The map the game calls <paramref name="mapId"/> (zone change), when its position is calibrated.</summary>
    public Map? ForGameMap(int mapId) => All.FirstOrDefault(m => m.Fit?.MapId == mapId);

    public static WorldMaps Load(string path, string? calibrationPath = null)
    {
        var fits = new Dictionary<string, Calibration>();
        try
        {
            if (calibrationPath is not null && File.Exists(calibrationPath))
            {
                using var cal = JsonDocument.Parse(File.ReadAllText(calibrationPath));
                foreach (var c in cal.RootElement.GetProperty("maps").EnumerateObject())
                    fits[c.Name] = new Calibration(c.Value.GetProperty("mapId").GetInt32(), c.Value.GetProperty("scale").GetDouble(),
                        c.Value.GetProperty("offsetX").GetDouble(), c.Value.GetProperty("offsetY").GetDouble());
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no calibration: the maps work without "you are here"
        }
        var maps = new List<Map>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var m in doc.RootElement.GetProperty("maps").EnumerateArray())
            {
                var image = m.GetProperty("image");
                var areas = m.GetProperty("areas").EnumerateArray()
                    .Select(a => new Area(a.GetProperty("n").GetString() ?? "", a.GetProperty("x").GetDouble(), a.GetProperty("y").GetDouble(),
                        a.GetProperty("r").GetInt32()))
                    .ToList();
                var layers = new Dictionary<string, IReadOnlyList<Place>>();
                foreach (var layer in m.GetProperty("layers").EnumerateObject())
                    layers[layer.Name] = layer.Value.EnumerateArray().Select(p => new Place(
                        p.GetProperty("n").GetString() ?? "",
                        p.TryGetProperty("s", out var s) ? s.GetString() : null,
                        p.TryGetProperty("f", out var f) ? f.GetString() : null,
                        p.GetProperty("pts").EnumerateArray().Select(pt => (pt[0].GetDouble(), pt[1].GetDouble())).ToList())
                    {
                        Icon = p.TryGetProperty("i", out var i) ? i.GetString() : null,
                    }).ToList();
                // pet souls by family, each its own layer
                if (layers.Remove("pets", out var pets))
                    foreach (var family in pets.GroupBy(p => PetLayer(p.Kind)))
                        layers[family.Key] = family.ToList();
                maps.Add(new Map(m.GetProperty("slug").GetString() ?? "", m.GetProperty("name").GetString() ?? "",
                    m.GetProperty("faction").GetString() ?? "", m.GetProperty("metres").GetDouble(), image.GetProperty("src").GetString() ?? "",
                    image.GetProperty("w").GetInt32(), image.GetProperty("h").GetInt32(), areas, layers)
                {
                    Fit = fits.GetValueOrDefault(m.GetProperty("slug").GetString() ?? ""),
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no map table: the map window says so
        }
        return new WorldMaps(maps);
    }
}
