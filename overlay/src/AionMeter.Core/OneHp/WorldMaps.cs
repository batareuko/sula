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
    public sealed record Place(string Name, string? Sub, string? Kind, IReadOnlyList<(double X, double Y)> Points);

    public sealed record Area(string Name, double X, double Y, int Rank);

    public sealed record Map(string Slug, string Name, string Faction, double Metres, string ImageUrl, int Width, int Height,
        IReadOnlyList<Area> Areas, IReadOnlyDictionary<string, IReadOnlyList<Place>> Layers)
    {
        /// <summary>Height of the map in its units (x runs to 10000).</summary>
        public double UnitsHigh => Units * Height / Width;

        /// <summary>Metres between two points (the map is <see cref="Metres"/> wide).</summary>
        public double MetresBetween((double X, double Y) a, (double X, double Y) b) =>
            Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)) * Metres / Units;
    }

    /// <summary>Layers in the order the map window offers them.</summary>
    public static readonly string[] LayerOrder = ["bosses", "dungeons", "rifts", "kibelisks", "monoliths", "gather", "pets", "cubes"];

    public IReadOnlyList<Map> All { get; }

    public WorldMaps(IReadOnlyList<Map> maps) => All = maps;

    public Map? Find(string? slug) => All.FirstOrDefault(m => m.Slug == slug);

    public static WorldMaps Load(string path)
    {
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
                        p.GetProperty("pts").EnumerateArray().Select(pt => (pt[0].GetDouble(), pt[1].GetDouble())).ToList())).ToList();
                maps.Add(new Map(m.GetProperty("slug").GetString() ?? "", m.GetProperty("name").GetString() ?? "",
                    m.GetProperty("faction").GetString() ?? "", m.GetProperty("metres").GetDouble(), image.GetProperty("src").GetString() ?? "",
                    image.GetProperty("w").GetInt32(), image.GetProperty("h").GetInt32(), areas, layers));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no map table: the map window says so
        }
        return new WorldMaps(maps);
    }
}
