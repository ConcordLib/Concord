namespace Concord.HarmonyX;

/// <summary>What one <see cref="HarmonyXEntryPoint.Install" /> call did, for the host to log.</summary>
public sealed class HarmonyXInstallResult {
    internal HarmonyXInstallResult(bool installed, string message) {
        Installed = installed;
        Message = message;
    }

    /// <summary>Whether the backend is now the one Concord puts composed bodies through.</summary>
    public bool Installed { get; }

    /// <summary>The one line the host logs for this install, whether it took or not.</summary>
    public string Message { get; }
}
