namespace ScanGo.Api.Common;

public record SettingsSnapshot(string GeminiModel, bool AiMock, bool OcrMock);

/// <summary>
/// Process-wide cache of admin-editable runtime settings (AI model + mock
/// toggles). Seeded from the DB at startup, then updated in place when an admin
/// changes them via the admin API — so changes apply WITHOUT a restart.
/// Single-instance friendly (Render Starter). Multi-instance would need a
/// pub/sub refresh; out of scope here.
/// </summary>
public class RuntimeSettings
{
    private volatile SettingsSnapshot _current =
        new("gemini-2.5-flash-lite", AiMock: true, OcrMock: true);

    public SettingsSnapshot Current => _current;

    public void Set(SettingsSnapshot snapshot) => _current = snapshot;
}
