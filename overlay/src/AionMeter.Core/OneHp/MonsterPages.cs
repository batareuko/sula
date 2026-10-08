using System.Text;
using System.Text.RegularExpressions;

namespace AionMeter.Core.OneHp;

/// <summary>
/// 1 HP: MetaBot.GG has a page for most AION 2 monsters at metabot.gg/en/aion-2/monsters/&lt;name&gt;, and its head
/// names the monster's portrait (og:image). The overlay reads only the start of the page to find it.
/// </summary>
public static partial class MonsterPages
{
    public const string PageUrl = "https://metabot.gg/en/aion-2/monsters/";

    /// <summary>"Marsh Moss Potcrab" → "marsh-moss-potcrab"; "Mau Sentry's Soul" → "mau-sentrys-soul"; null for "???".</summary>
    public static string? Slug(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.ToLowerInvariant())
        {
            if (ch is '\'' or '’') continue;
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().TrimEnd('-');
        return slug.Length >= 2 ? slug : null;
    }

    /// <summary>
    /// The portrait file named in a monster page ("f/mob_bigman_01", the path under metabot.gg/web/aion2/npcs/ without
    /// .webp; a large variant maps to the same file), or null when the page shows the site's generic card.
    /// </summary>
    public static string? PortraitFile(string html) => OgImage().Match(html) is { Success: true } m ? m.Groups[1].Value : null;

    [GeneratedRegex(@"og:image""\s+content=""https://metabot\.gg/web/aion2/npcs(?:-large)?/([A-Za-z0-9_/]+)\.webp""")]
    private static partial Regex OgImage();
}
