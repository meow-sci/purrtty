using System.Text.Json.Nodes;
using purrTTY.Kfs.Lsp.Protocol;

namespace purrTTY.Kfs.Lsp;

/// <summary>What a feature provider gets with each request.</summary>
/// <param name="Server">The connection's server: its host, documents, builds, encoding and notifications.</param>
/// <param name="Method">The LSP method.</param>
/// <param name="Params">The request's parameters (an object, or null).</param>
public sealed record LspRequestContext(KfsLanguageServer Server, string Method, JsonObject? Params);

/// <summary>Handles one LSP request method; the result is the response's <c>result</c>. Throw <see cref="JsonRpcException"/> for an error.</summary>
public delegate Task<JsonNode?> LspRequestHandler(LspRequestContext context, CancellationToken cancellationToken);

/// <summary>Handles one LSP notification method (on the read thread, in arrival order).</summary>
public delegate void LspNotificationHandler(LspRequestContext context);

/// <summary><c>textDocument/hover</c> (plan §6.5, task C3).</summary>
public interface IHoverProvider
{
    /// <summary>The hover, or null.</summary>
    Task<JsonNode?> Hover(LspRequestContext context, CancellationToken cancellationToken);
}

/// <summary><c>textDocument/completion</c> (plan §6.5, task C3).</summary>
public interface ICompletionProvider
{
    /// <summary>Characters that open completion (<c>.</c>, <c>#</c>, <c>&lt;</c>, …); advertised as <c>triggerCharacters</c>.</summary>
    IReadOnlyList<string> TriggerCharacters { get; }

    /// <summary>A completion list, or null.</summary>
    Task<JsonNode?> Complete(LspRequestContext context, CancellationToken cancellationToken);
}

/// <summary><c>textDocument/documentSymbol</c> (plan §6.6, task C5).</summary>
public interface IDocumentSymbolProvider
{
    /// <summary>The document's symbols, or null.</summary>
    Task<JsonNode?> Symbols(LspRequestContext context, CancellationToken cancellationToken);
}

/// <summary><c>textDocument/codeLens</c> (plan §6.6, task C5).</summary>
public interface ICodeLensProvider
{
    /// <summary>The document's lenses, or null.</summary>
    Task<JsonNode?> Lenses(LspRequestContext context, CancellationToken cancellationToken);
}

/// <summary><c>textDocument/inlayHint</c> (plan §6.6, task C5).</summary>
public interface IInlayHintProvider
{
    /// <summary>The hints in the requested range, or null.</summary>
    Task<JsonNode?> Hints(LspRequestContext context, CancellationToken cancellationToken);
}

/// <summary><c>workspace/executeCommand</c> for a set of commands (plan §6.7, task C4).</summary>
public interface IExecuteCommandHandler
{
    /// <summary>The command names it handles (advertised in <c>executeCommandProvider.commands</c>).</summary>
    IReadOnlyList<string> Commands { get; }

    /// <summary>Runs <paramref name="command"/> with its <paramref name="arguments"/>; the result is the response's.</summary>
    Task<JsonNode?> Execute(LspRequestContext context, string command, JsonArray? arguments, CancellationToken cancellationToken);
}

/// <summary>
/// The feature providers of one <see cref="KfsLanguageServer"/>. Register before the client's
/// <c>initialize</c> (a listener does it in its per-connection configure callback): the capabilities
/// are computed from what is registered — hover, completion, symbols, lenses, inlay hints and
/// <c>executeCommand</c> are advertised only when a provider is — and the set is frozen then.
/// </summary>
public sealed class LspFeatures
{
    private readonly Dictionary<string, (LspRequestHandler Handler, Action<JsonObject>? Advertise)> _requests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LspNotificationHandler> _notifications = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IExecuteCommandHandler> _commands = new(StringComparer.Ordinal);
    private bool _frozen;

    /// <summary>The hover provider, or null.</summary>
    public IHoverProvider? Hover { get; private set; }

    /// <summary>The completion provider, or null.</summary>
    public ICompletionProvider? Completion { get; private set; }

    /// <summary>The document symbol provider, or null.</summary>
    public IDocumentSymbolProvider? DocumentSymbols { get; private set; }

    /// <summary>The code lens provider, or null.</summary>
    public ICodeLensProvider? CodeLens { get; private set; }

    /// <summary>The inlay hint provider, or null.</summary>
    public IInlayHintProvider? InlayHints { get; private set; }

    /// <summary>Every registered command name.</summary>
    public IReadOnlyCollection<string> Commands => _commands.Keys;

    /// <summary>True once <c>initialize</c> froze the set.</summary>
    public bool IsFrozen => _frozen;

    /// <summary>Registers the hover provider (advertises <c>hoverProvider</c>).</summary>
    public LspFeatures Add(IHoverProvider provider)
    {
        ThrowIfFrozen();
        Hover = provider ?? throw new ArgumentNullException(nameof(provider));
        return SetRequest("textDocument/hover", (c, t) => provider.Hover(c, t), caps => caps["hoverProvider"] = true);
    }

    /// <summary>Registers the completion provider (advertises <c>completionProvider</c> with its trigger characters).</summary>
    public LspFeatures Add(ICompletionProvider provider)
    {
        ThrowIfFrozen();
        Completion = provider ?? throw new ArgumentNullException(nameof(provider));
        return SetRequest("textDocument/completion", (c, t) => provider.Complete(c, t), caps =>
            caps["completionProvider"] = new JsonObject
            {
                ["triggerCharacters"] = new JsonArray(provider.TriggerCharacters.Select(s => (JsonNode)JsonValue.Create(s)).ToArray()),
                ["resolveProvider"] = false,
            });
    }

    /// <summary>Registers the document symbol provider (advertises <c>documentSymbolProvider</c>).</summary>
    public LspFeatures Add(IDocumentSymbolProvider provider)
    {
        ThrowIfFrozen();
        DocumentSymbols = provider ?? throw new ArgumentNullException(nameof(provider));
        return SetRequest("textDocument/documentSymbol", (c, t) => provider.Symbols(c, t), caps => caps["documentSymbolProvider"] = true);
    }

    /// <summary>Registers the code lens provider (advertises <c>codeLensProvider</c>).</summary>
    public LspFeatures Add(ICodeLensProvider provider)
    {
        ThrowIfFrozen();
        CodeLens = provider ?? throw new ArgumentNullException(nameof(provider));
        return SetRequest("textDocument/codeLens", (c, t) => provider.Lenses(c, t), caps => caps["codeLensProvider"] = new JsonObject { ["resolveProvider"] = false });
    }

    /// <summary>Registers the inlay hint provider (advertises <c>inlayHintProvider</c>).</summary>
    public LspFeatures Add(IInlayHintProvider provider)
    {
        ThrowIfFrozen();
        InlayHints = provider ?? throw new ArgumentNullException(nameof(provider));
        return SetRequest("textDocument/inlayHint", (c, t) => provider.Hints(c, t), caps => caps["inlayHintProvider"] = true);
    }

    /// <summary>Registers a command handler for its <see cref="IExecuteCommandHandler.Commands"/> (advertises <c>executeCommandProvider</c>).</summary>
    public LspFeatures Add(IExecuteCommandHandler handler)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(handler);
        foreach (string command in handler.Commands)
        {
            if (!_commands.TryAdd(command, handler)) throw new InvalidOperationException($"the command '{command}' is already registered");
        }
        return this;
    }

    /// <summary>
    /// Registers any other request method, with an optional contribution to the server capabilities.
    /// The lifecycle and document-sync methods are the server's own and cannot be replaced.
    /// </summary>
    public LspFeatures AddRequest(string method, LspRequestHandler handler, Action<JsonObject>? advertise = null)
    {
        ThrowIfFrozen();
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentNullException.ThrowIfNull(handler);
        if (KfsLanguageServer.IsBuiltIn(method)) throw new ArgumentException($"'{method}' is handled by the server itself", nameof(method));
        return SetRequest(method, handler, advertise);
    }

    /// <summary>Registers a handler for a client notification the server does not handle itself (custom <c>purrtty/…</c> subscriptions).</summary>
    public LspFeatures AddNotification(string method, LspNotificationHandler handler)
    {
        ThrowIfFrozen();
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentNullException.ThrowIfNull(handler);
        if (KfsLanguageServer.IsBuiltIn(method)) throw new ArgumentException($"'{method}' is handled by the server itself", nameof(method));
        _notifications[method] = handler;
        return this;
    }

    internal LspRequestHandler? Request(string method) => _requests.TryGetValue(method, out var r) ? r.Handler : null;

    internal LspNotificationHandler? Notification(string method) => _notifications.GetValueOrDefault(method);

    internal IExecuteCommandHandler? Command(string command) => _commands.GetValueOrDefault(command);

    /// <summary>Adds every registered feature's capability to <paramref name="capabilities"/> and freezes the set.</summary>
    internal void Advertise(JsonObject capabilities)
    {
        _frozen = true;
        foreach ((_, (_, Action<JsonObject>? advertise)) in _requests) advertise?.Invoke(capabilities);
        if (_commands.Count > 0)
        {
            capabilities["executeCommandProvider"] = new JsonObject
            {
                ["commands"] = new JsonArray(_commands.Keys.Order(StringComparer.Ordinal).Select(c => (JsonNode)JsonValue.Create(c)).ToArray()),
            };
        }
    }

    private LspFeatures SetRequest(string method, LspRequestHandler handler, Action<JsonObject>? advertise)
    {
        _requests[method] = (handler, advertise);
        return this;
    }

    private void ThrowIfFrozen()
    {
        if (_frozen) throw new InvalidOperationException("features must be registered before the client's initialize");
    }
}
