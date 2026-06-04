using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ScanGo.Api.Features.Tts;

/// <summary>
/// Real TTS via Google Cloud Text-to-Speech REST (`text:synthesize`). Picks a
/// neural/WaveNet voice per language so the read-aloud sounds natural in
/// whatever language the AI translated into. Used when Tts.Mock=false and a key
/// is set.
/// </summary>
public class GoogleTtsService(
    HttpClient http,
    IOptions<TtsOptions> options,
    ILogger<GoogleTtsService> log) : ITtsService
{
    private readonly TtsOptions _opts = options.Value;

    // Google's synthesize endpoint caps input at 5000 bytes; stay under it.
    private const int MaxBytes = 4800;

    // app 3-letter target code -> (Google languageCode, preferred voice).
    // NB: Google uses cmn-CN / cmn-TW for Chinese (not zh-*). If a voice name is
    // ever wrong for a locale, SynthesizeAsync retries with languageCode only.
    private static readonly Dictionary<string, (string Lang, string? Voice)> Voices = new()
    {
        ["ara"] = ("ar-XA", "ar-XA-Wavenet-A"),
        ["bul"] = ("bg-BG", null),
        ["chs"] = ("cmn-CN", "cmn-CN-Wavenet-A"),
        ["cht"] = ("cmn-TW", "cmn-TW-Wavenet-A"),
        ["hrv"] = ("hr-HR", null),
        ["cze"] = ("cs-CZ", "cs-CZ-Wavenet-A"),
        ["dan"] = ("da-DK", "da-DK-Wavenet-A"),
        ["dut"] = ("nl-NL", "nl-NL-Wavenet-A"),
        ["eng"] = ("en-US", "en-US-Wavenet-D"),
        ["fin"] = ("fi-FI", "fi-FI-Wavenet-A"),
        ["fre"] = ("fr-FR", "fr-FR-Wavenet-C"),
        ["ger"] = ("de-DE", "de-DE-Wavenet-F"),
        ["gre"] = ("el-GR", "el-GR-Wavenet-A"),
        ["hun"] = ("hu-HU", "hu-HU-Wavenet-A"),
        ["kor"] = ("ko-KR", "ko-KR-Wavenet-A"),
        ["ita"] = ("it-IT", "it-IT-Wavenet-A"),
        ["jpn"] = ("ja-JP", "ja-JP-Wavenet-B"),
        ["pol"] = ("pl-PL", "pl-PL-Wavenet-A"),
        ["por"] = ("pt-PT", "pt-PT-Wavenet-A"),
        ["rus"] = ("ru-RU", "ru-RU-Wavenet-C"),
        ["slv"] = ("sl-SI", null),
        ["spa"] = ("es-ES", "es-ES-Wavenet-C"),
        ["swe"] = ("sv-SE", "sv-SE-Wavenet-A"),
        ["tha"] = ("th-TH", "th-TH-Neural2-C"),
        ["tur"] = ("tr-TR", "tr-TR-Wavenet-A"),
        ["ukr"] = ("uk-UA", "uk-UA-Wavenet-A"),
        ["vnm"] = ("vi-VN", "vi-VN-Wavenet-A"),
    };

    public async Task<byte[]?> SynthesizeAsync(string text, string targetLang, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.GoogleApiKey)) return null;
        if (string.IsNullOrWhiteSpace(text)) return null;

        text = TruncateByBytes(text, MaxBytes);
        var (lang, voice) = Voices.TryGetValue(targetLang, out var v)
            ? v
            : ("vi-VN", "vi-VN-Wavenet-A");

        var audio = await TrySynthAsync(text, lang, voice, ct);
        // A mistyped/unavailable voice name 400s — retry letting Google pick the
        // default voice for the language so we still produce audio.
        if (audio is null && voice is not null)
            audio = await TrySynthAsync(text, lang, null, ct);
        return audio;
    }

    private async Task<byte[]?> TrySynthAsync(
        string text, string lang, string? voice, CancellationToken ct)
    {
        var url =
            $"https://texttospeech.googleapis.com/v1/text:synthesize?key={_opts.GoogleApiKey}";
        var payload = new
        {
            input = new { text },
            voice = voice is null
                ? (object)new { languageCode = lang }
                : new { languageCode = lang, name = voice },
            audioConfig = new { audioEncoding = "MP3", speakingRate = 1.0 },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            log.LogWarning("Google TTS {Status}: {Body}", resp.StatusCode, body);
            return null;
        }

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
        if (!doc.RootElement.TryGetProperty("audioContent", out var b64))
            return null;
        var s = b64.GetString();
        return string.IsNullOrEmpty(s) ? null : Convert.FromBase64String(s);
    }

    // Trim to at most maxBytes of UTF-8 (binary search on char length).
    private static string TruncateByBytes(string s, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(s) <= maxBytes) return s;
        int lo = 0, hi = s.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (Encoding.UTF8.GetByteCount(s[..mid]) <= maxBytes) lo = mid;
            else hi = mid - 1;
        }
        return s[..lo];
    }
}
