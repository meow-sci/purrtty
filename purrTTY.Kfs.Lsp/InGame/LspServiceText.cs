using System.Globalization;
using purrTTY.Kfs.Lsp.Features;

namespace purrTTY.Kfs.Lsp.InGame;

/// <summary>The text of <c>purr lsp</c> and of the purrTTY menu's server line (never the token).</summary>
public static class LspServiceText
{
    /// <summary>The menu line: <c>Language server: 127.0.0.1:52344, 1 client</c>, or why it is not running.</summary>
    public static string MenuLine(LspServiceCore? core, string? offReason)
    {
        if (core is null || !core.IsRunning) return "Language server: " + (offReason ?? core?.Status ?? "not running");
        int clients = core.Clients;
        return string.Create(CultureInfo.InvariantCulture, $"Language server: {core.Address}, {clients} client{(clients == 1 ? "" : "s")}");
    }

    /// <summary>
    /// The lines of <c>purr lsp</c>: the status, the connected clients, the workspace root, the build
    /// target, what the editor may run, the info file and how to get the token.
    /// </summary>
    public static IReadOnlyList<string> Describe(LspServiceCore? core, IKfsLanguageHost? host, bool allowVerb, LiveFeed? feed, string? offReason)
    {
        var lines = new List<string>();
        if (core is null || !core.IsRunning)
        {
            lines.Add("purr lsp: " + (offReason ?? core?.Status ?? "not running"));
            if (core is not null) lines.Add("  purr lsp restart starts it again (kfs.toml is read again)");
            return lines;
        }
        int clients = core.Clients;
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"purr lsp: listening on {core.Address}, {clients} client{(clients == 1 ? "" : "s")} connected"));
        if (host is not null)
        {
            lines.Add("  workspace:  " + host.WorkspaceRoot);
            lines.Add("  target:     " + host.CurrentTarget + (host.CurrentTarget.IsVehicle ? " (kfs.load adds --vehicle)" : ""));
        }
        lines.Add("  commands:   kfs.build kfs.load kfs.start kfs.stop kfs.reset kfs.target kfs.manual kfs.parts; kfs.verb "
            + (allowVerb ? "on (lsp_allow_vehicle_commands = true)" : "off (lsp_allow_vehicle_commands = false in kfs.toml)"));
        if (feed is not null)
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  live data:  {feed.Subscribers} subscribed client(s), {(feed.IsPolling ? "polling" : "idle")}"));
        (string? path, string? problem) = core.InfoFile;
        if (path is not null) lines.Add("  info file:  " + (problem is null ? path : $"not written ({problem})"));
        lines.Add("  token:      purr lsp token prints it (it changes at every start)");
        return lines;
    }
}
