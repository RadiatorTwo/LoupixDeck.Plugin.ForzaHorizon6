using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.ForzaHorizon6.Commands;

/// <summary>
/// Re-arms automatic entry into exclusive mode after the user manually exited
/// it. The command does not force-enter; the next valid telemetry packet
/// triggers the transition. This keeps the trigger source (UDP) singular.
/// </summary>
public sealed class ActivateCommand : IPluginCommand
{
    private readonly Action _reArm;

    public ActivateCommand(Action reArm)
    {
        _reArm = reArm;
    }

    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = "ForzaHorizon6.Activate",
        DisplayName = "Re-arm Forza HUD",
        Group = "ForzaHorizon6"
    };

    public ButtonTargets SupportedTargets => ButtonTargets.All;

    public Task Execute(CommandContext ctx)
    {
        try { _reArm(); }
        catch (Exception ex) { ctx?.Host?.Logger?.Error("ActivateCommand failed", ex); }
        return Task.CompletedTask;
    }
}
