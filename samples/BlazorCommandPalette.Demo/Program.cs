using BlazorCommandPalette;
using BlazorCommandPalette.Demo;
using BlazorCommandPalette.Demo.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<DemoState>();
builder.Services.AddScoped<FakeDirectory>();

builder.Services.AddCommandPalette(options =>
{
    // Ctrl+K / ⌘K is the default; also allow VS Code style Ctrl+Shift+P.
    options.AdditionalToggleShortcuts.Add("Mod+Shift+P");

    // Groups appear in this order on the empty query; unknown groups follow alphabetically.
    options.GroupOrder.AddRange(["Navigation", "Actions", "Preferences", "Danger zone", "Help"]);

    options.VimBindings = true;
    options.Virtualize = false;

    // Modes: VS Code / GitHub style prefixes. Each mode can scope registry commands and add async sources.
    options.Modes.Add(new CommandMode
    {
        Prefix = ">",
        Name = "Commands",
        Description = "Run any command",
        Placeholder = "Run a command…",
        Tags = ["command"],
    });
    options.Modes.Add(new CommandMode
    {
        Prefix = "#",
        Name = "Issues",
        Description = "Search issues by number or title",
        Placeholder = "Search issues…",
        HideRegistryCommands = true,
        Sources = [new IssueSource()],
    });
    options.Modes.Add(new CommandMode
    {
        Prefix = "@",
        Name = "People",
        Description = "Find a teammate",
        Placeholder = "Search people…",
        HideRegistryCommands = true,
        Sources = [new PeopleSource()],
    });
    options.Modes.Add(new CommandMode
    {
        Prefix = "?",
        Name = "Help",
        Description = "Keyboard shortcuts and docs",
        Groups = ["Help"],
    });

    // In the default mode, also search documentation asynchronously (debounced, cancellable).
    options.Sources.Add(new DocsSource());
});

await builder.Build().RunAsync();
