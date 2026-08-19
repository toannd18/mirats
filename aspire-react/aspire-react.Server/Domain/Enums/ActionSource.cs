namespace aspire_react.Server.Domain.Enums;

public enum ActionSource
{
    /// <summary>GUI — browser with CSRF token</summary>
    Gui = 1,
    /// <summary>API — Bearer token + JSON</summary>
    Api = 2,
    /// <summary>CLI — script or command-line tool</summary>
    Cli = 3
}