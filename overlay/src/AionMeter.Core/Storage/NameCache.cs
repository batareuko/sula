using System.Text.Json;
using System.Text.Json.Serialization;
using AionMeter.Core.Game;

namespace AionMeter.Core.Storage;

/// <summary>
/// Who is who in the current zone, kept on disk. AION 2 only sends a player's name when they come into view or on a
/// loading screen, and a boss's template and max HP only when it comes into view, so without this a meter restart
/// mid-zone leaves everyone as "#id" and a boss already in view nameless, with a max HP guessed from its current HP.
/// Entries are only trusted for a limited time and are dropped as soon as the game shows the zone was reloaded.
/// </summary>
public sealed class NameCache(string path)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(45);

    /// <summary>Bosses are kept for a shorter time: a stale one would put a wrong name and HP on whatever reuses its id.</summary>
    public static readonly TimeSpan NpcMaxAge = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    /// <param name="Party">1 HP: the party roster's names (as old as the rest).</param>
    /// <param name="Powers">1 HP: combat power by name — kept whatever the age.</param>
    /// <param name="Gear">1 HP: gear score by name (roster or inspection) — kept whatever the age.</param>
    private sealed record FileData(DateTimeOffset SavedAt, uint? SelfId, int MapId, List<CachedPlayer> Players, List<CachedNpc>? Npcs = null,
        List<string>? Party = null, Dictionary<string, long>? Powers = null, Dictionary<string, int>? Gear = null);

    public void Save(SessionState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            var data = new FileData(DateTimeOffset.UtcNow, state.SelfId, state.MapId, state.Players.ToList(), state.Npcs.ToList(),
                state.Party?.ToList(), state.Powers?.ToDictionary(), state.Gear?.ToDictionary());
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>The saved state, or nothing when the file is missing, unreadable or older than <see cref="MaxAge"/>.</summary>
    public SessionState? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var data = JsonSerializer.Deserialize<FileData>(File.ReadAllText(path), Json);
            var age = DateTimeOffset.UtcNow - data?.SavedAt;
            if (data is null) return null;
            // Combat power and gear score do not go stale with the zone: they come back even from an old file.
            if (age > MaxAge)
                return data.Powers is null && data.Gear is null ? null : new SessionState(null, 0, [], [], null, data.Powers, data.Gear);
            return new SessionState(data.SelfId, data.MapId, data.Players, age <= NpcMaxAge ? data.Npcs ?? [] : [],
                data.Party, data.Powers, data.Gear);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }
}
