using System.Globalization;
using System.Text;

namespace AionMeter.App.Services;

/// <summary>
/// 1 HP: alerts spoken by the Windows voice (SAPI, built into Windows, no download): the installed voice of the
/// overlay's language when there is one, the default voice otherwise. Never throws: no voice, no speech.
/// </summary>
public static class Voice
{
    private static dynamic? _voice;
    private static string? _language;
    private static bool _broken;

    public static void Speak(string text, string languageCode)
    {
        if (_broken || string.IsNullOrWhiteSpace(text)) return;
        try
        {
            if (_voice is null)
            {
                var type = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (type is null)
                {
                    _broken = true;
                    return;
                }
                _voice = Activator.CreateInstance(type);
            }
            if (_language != languageCode) PickVoice(languageCode);
            _voice!.Speak(Clean(text), 1 | 2); // async, purge what is still being said
        }
        catch (Exception ex)
        {
            _broken = true;
            Log.Error("Voice", ex);
        }
    }

    /// <summary>The first installed voice whose language matches (SAPI tokens carry the LCID in hex).</summary>
    private static void PickVoice(string languageCode)
    {
        _language = languageCode;
        try
        {
            var lcid = new CultureInfo(languageCode).LCID.ToString("X");
            var voices = _voice!.GetVoices($"Language={lcid}");
            if (voices.Count > 0) _voice.Voice = voices.Item(0);
        }
        catch (Exception ex) when (ex is CultureNotFoundException or System.Runtime.InteropServices.COMException)
        {
        }
    }

    /// <summary>Emoji and symbols out — the voice would spell them.</summary>
    private static string Clean(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
            if (Rune.IsLetterOrDigit(rune) || Rune.IsWhiteSpace(rune) || rune.Value is ',' or '.' or ':' or '-' or '(' or ')') sb.Append(rune.ToString());
        return sb.ToString().Trim();
    }
}
