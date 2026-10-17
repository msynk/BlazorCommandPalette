namespace BlazorCommandPalette;

/// <summary>Tells the palette what to do after a handler ran.</summary>
public abstract record CommandResult
{
    /// <summary>Close the palette (default).</summary>
    public static CommandResult Close { get; } = new CloseResult();

    /// <summary>Keep the palette open on the current page (e.g. toggles).</summary>
    public static CommandResult KeepOpen { get; } = new KeepOpenResult();

    /// <summary>Clear the query and return to the root page, keeping the palette open.</summary>
    public static CommandResult Reset { get; } = new ResetResult();

    /// <summary>Go back one page, keeping the palette open.</summary>
    public static CommandResult Back { get; } = new BackResult();

    /// <summary>Push a page listing the given commands.</summary>
    public static CommandResult Page(string title, IReadOnlyList<Command> commands, string? placeholder = null, bool multiSelect = false, Func<CommandContext, Task<CommandResult>>? onSubmit = null)
        => new PageResult(title, commands, placeholder, multiSelect, onSubmit);

    /// <summary>Ask the user for free text, then run <paramref name="onSubmit"/> with <see cref="CommandContext.PromptValue"/> set.</summary>
    public static CommandResult Prompt(string title, Func<CommandContext, Task<CommandResult>> onSubmit, string? placeholder = null, string? initialValue = null)
        => new PromptResult(title, onSubmit, placeholder, initialValue);

    /// <summary>Show a transient message in the palette footer.</summary>
    public static CommandResult Toast(string message, bool keepOpen = false) => new ToastResult(message, keepOpen);

    public sealed record CloseResult : CommandResult;

    public sealed record KeepOpenResult : CommandResult;

    public sealed record ResetResult : CommandResult;

    public sealed record BackResult : CommandResult;

    /// <summary>A page of commands. <c>OnSubmit</c> is invoked with <see cref="CommandContext.SelectedCommands"/> on <c>Ctrl+Enter</c> for multi-select pages.</summary>
    public sealed record PageResult(string Title, IReadOnlyList<Command> Commands, string? Placeholder, bool MultiSelect, Func<CommandContext, Task<CommandResult>>? OnSubmit = null) : CommandResult;

    public sealed record PromptResult(string Title, Func<CommandContext, Task<CommandResult>> OnSubmit, string? Placeholder, string? InitialValue) : CommandResult;

    public sealed record ToastResult(string Message, bool StayOpen) : CommandResult;
}
