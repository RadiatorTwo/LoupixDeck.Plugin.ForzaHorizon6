using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.ForzaHorizon6.Commands;

/// <summary>
/// Starts the Forza HUD: takes the device over via exclusive mode so incoming telemetry is shown.
/// The HUD is never entered automatically — it appears only when the user runs this command, and it
/// does not reappear on its own after an exit or a profile switch. The <c>CommandName</c> is kept as
/// <c>ForzaHorizon6.Activate</c> so existing button bindings keep working.
/// </summary>
public sealed class ActivateCommand : IPluginCommand
{
    private readonly Action _start;

    public ActivateCommand(Action start)
    {
        _start = start;
    }

    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = "ForzaHorizon6.Activate",
        DisplayName = "Show Forza HUD",
        Group = "ForzaHorizon6",
        Icon = "\U000F0450",
        Description = "Take over the display with the live Forza telemetry HUD"
    };

    public ButtonTargets SupportedTargets => ButtonTargets.All;

    public Task Execute(CommandContext ctx)
    {
        try { _start(); }
        catch (Exception ex) { ctx?.Host?.Logger?.Error("ActivateCommand failed", ex); }
        return Task.CompletedTask;
    }
}
