namespace Wmux.Config;

/// <summary>
/// Defines the key bindings for wmux.
/// </summary>
public class KeyBindings
{
    /// <summary>
    /// The activation string that enters prefix mode.
    /// Must be at least 2 printable characters, no control keys.
    /// Default is "za" — type z then a to activate, then the command key.
    /// </summary>
    public string ActivationString { get; set; } = "za";

    // After activation:
    public char SplitHorizontal { get; set; } = 's';
    public char SplitVertical { get; set; } = '|';
    public char NewWindow { get; set; } = 'c';
    public char NextWindow { get; set; } = 'n';
    public char PrevWindow { get; set; } = 'p';
    public char Detach { get; set; } = 'd';
    public char KillPane { get; set; } = 'x';
    public char CommandMode { get; set; } = ':';
    public char CopyMode { get; set; } = '[';
    public char RenameWindow { get; set; } = ',';
    public char KillWindow { get; set; } = '&';
    public char NextPane { get; set; } = 'o';
    public char CycleLayout { get; set; } = ' ';
    public char RenamePane { get; set; } = 'r';
    public char BreakPane { get; set; } = '!';
}
