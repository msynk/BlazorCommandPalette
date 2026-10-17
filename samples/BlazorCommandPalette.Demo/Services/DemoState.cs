using Microsoft.JSInterop;

namespace BlazorCommandPalette.Demo.Services;

/// <summary>App state the demo commands manipulate (theme, toggles, an activity log).</summary>
public sealed class DemoState
{
    private readonly IJSRuntime _js;
    private readonly List<string> _log = [];

    public DemoState(IJSRuntime js)
    {
        _js = js;
    }

    public event Action? Changed;

    public string Theme { get; private set; } = "auto";

    public bool WordWrap { get; private set; } = true;

    public bool Notifications { get; private set; }

    public bool ShowMinimap { get; private set; } = true;

    public string ProjectName { get; private set; } = "Acme Dashboard";

    public IReadOnlyList<string> Labels { get; private set; } = ["bug"];

    public IReadOnlyList<string> Log => _log;

    public async Task SetThemeAsync(string theme)
    {
        Theme = theme;
        await _js.InvokeVoidAsync("document.documentElement.setAttribute", "data-theme", theme);
        Record($"Theme set to {theme}");
    }

    public void ToggleWordWrap() { WordWrap = !WordWrap; Record($"Word wrap {(WordWrap ? "on" : "off")}"); }

    public void ToggleNotifications() { Notifications = !Notifications; Record($"Notifications {(Notifications ? "on" : "off")}"); }

    public void ToggleMinimap() { ShowMinimap = !ShowMinimap; Record($"Minimap {(ShowMinimap ? "shown" : "hidden")}"); }

    public void Rename(string name) { ProjectName = name; Record($"Project renamed to “{name}”"); }

    public void SetLabels(IEnumerable<string> labels) { Labels = labels.ToList(); Record($"Labels: {string.Join(", ", Labels)}"); }

    public void Record(string message)
    {
        _log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        if (_log.Count > 30)
        {
            _log.RemoveAt(_log.Count - 1);
        }

        Changed?.Invoke();
    }
}
