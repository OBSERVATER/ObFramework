namespace ObFramework.Cli.Core;

internal sealed class CliSessionState
{
    public bool ExitRequested { get; private set; }
    private string[]? _helpPath;
    public void RequestExit()
    {
        ExitRequested = true;
    }

    public void RequestHelp(string[] path)
    {
        _helpPath = path;
    }

    public string[]? TakeHelpRequest()
    {
        var path = _helpPath;
        _helpPath = null;
        return path;
    }
}