using System.Diagnostics.CodeAnalysis;
using BlazorCommandPalette.History;
using BlazorCommandPalette.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorCommandPalette;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the command palette services. Add <c>&lt;CommandPalette /&gt;</c> once to your layout
    /// and inject <see cref="ICommandPaletteService"/> to register commands.
    /// </summary>
    public static IServiceCollection AddCommandPalette(this IServiceCollection services, Action<CommandPaletteOptions>? configure = null)
    {
        var options = new CommandPaletteOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(sp => new CommandRanker(sp.GetRequiredService<CommandPaletteOptions>()));
        services.TryAddScoped<CommandPaletteService>();
        services.TryAddScoped<ICommandPaletteService>(sp => sp.GetRequiredService<CommandPaletteService>());
        services.TryAddScoped<ICommandHistoryStore, BrowserStorageCommandHistoryStore>();
        services.TryAddScoped<CommandHistory>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>Use a custom history store (e.g. server-side per-user storage).</summary>
    public static IServiceCollection AddCommandPaletteHistoryStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>(this IServiceCollection services) where TStore : class, ICommandHistoryStore
    {
        services.Replace(ServiceDescriptor.Scoped<ICommandHistoryStore, TStore>());
        return services;
    }

    /// <summary>Registers an <see cref="ICommandSource"/> that is resolved from DI and added to the default mode.</summary>
    public static IServiceCollection AddCommandSource<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSource>(this IServiceCollection services) where TSource : class, ICommandSource
    {
        services.AddScoped<ICommandSource, TSource>();
        return services;
    }
}
