using purrTTY.Kfs.Lsp.Index;

namespace purrTTY.Kfs.Lsp.Features;

/// <summary>
/// The language features every kFS language server offers whatever its host is: hover and
/// completion (plan §6.5). Hosts that can act on the game add their own on top (commands and live
/// data, plan §6.7).
/// </summary>
public static class StandardFeatures
{
    /// <summary>
    /// Registers the standard feature providers on <paramref name="server"/>: a
    /// <see cref="HoverProvider"/> and a <see cref="CompletionProvider"/> (trigger characters
    /// <see cref="CompletionProvider.Triggers"/>). Call it before the server reads the client's
    /// <c>initialize</c>: capabilities are computed from what is registered and then frozen.
    /// </summary>
    /// <remarks>
    /// The index of the host's current target is built in the background now and again after every
    /// <see cref="IKfsLanguageHost.TargetChanged"/>, so the first request after either rarely builds
    /// it on the read thread (it would if it came first: <see cref="KfsIndexCache"/> builds on demand).
    /// The subscription ends when the server closes.
    /// </remarks>
    public static void AddTo(KfsLanguageServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        server.Features.Add(new HoverProvider());
        server.Features.Add(new CompletionProvider());

        IKfsLanguageHost host = server.Host;
        void Warm() => Task.Run(() =>
        {
            try { KfsIndexCache.For(host); }
            catch (Exception) { /* the request that needs it builds it again and reports what fails */ }
        });
        host.TargetChanged += Warm;
        server.Closed += _ => host.TargetChanged -= Warm;
        Warm();
    }
}
