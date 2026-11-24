namespace NexaDock.UI.Services;

/// <summary>
/// Game mode: while a game or another app runs full screen and the bar has stepped aside, the
/// widgets stop sampling the system (CPU, network, audio, logs, clock) so NexaDock costs the game
/// nothing. Work that must not be missed (Focus and Timer rounds, reminders' data) keeps running;
/// everything else catches up on the first tick after the game.
/// </summary>
public static class QuietMode
{
    public static bool IsActive { get; private set; }

    public static event EventHandler? Changed;

    public static void Set(bool active)
    {
        if (IsActive == active)
        {
            return;
        }

        IsActive = active;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
