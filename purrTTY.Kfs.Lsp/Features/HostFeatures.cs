using System.Text.Json.Nodes;
using purrTTY.Kfs.Lsp.Protocol;

namespace purrTTY.Kfs.Lsp.Features;

/// <summary>What <see cref="HostFeatures.AddTo"/> wires to a server: one instance per service, shared by its connections.</summary>
public sealed class HostFeatureOptions
{
    /// <summary>Whether <c>kfs.verb</c> runs (<c>lsp_allow_vehicle_commands</c>; default false).</summary>
    public bool AllowVerb { get; init; }

    /// <summary>Runs a <c>purr</c> line where the game allows it (<c>kfs.parts</c>); null: refused.</summary>
    public Func<string, CancellationToken, Task<CommandResult>>? RunPurr { get; init; }

    /// <summary>The live feeds; null: <c>purrtty/subscribe</c> is accepted and nothing is ever sent.</summary>
    public LiveFeed? Feed { get; init; }
}

/// <summary>
/// The features of a language server whose host can act on the game (plan §6.7): the
/// <see cref="KfsCommands"/> (<see cref="KfsCommandProvider"/>), the <c>purrtty/subscribe</c>
/// notification (<see cref="LiveFeed"/>), a <c>purrtty/status</c> request and the
/// <c>capabilities.experimental.purrtty</c> entry of the <c>initialize</c> result:
/// <c>{ "subscribe": Feed != null, "vehicleCommands": host.CanControlVehicle, "verb": vehicleCommands &amp;&amp; AllowVerb }</c>.
/// <c>subscribe</c> says whether live data will ever be sent (only the in-game service has a feed); a
/// <c>purrtty/subscribe</c> is accepted either way. <c>vehicleCommands</c> false means every command
/// but <c>kfs.manual</c> answers <c>ok: false</c> ("needs the game"), so a client can say so without sending.
/// Call <see cref="AddTo"/> after <c>StandardFeatures.AddTo(server)</c>, before the server reads
/// <c>initialize</c>.
/// </summary>
public static class HostFeatures
{
    /// <summary>The subscription notification (client → server).</summary>
    public const string SubscribeMethod = "purrtty/subscribe";

    /// <summary>A request answering where the server runs and what it may do (client → server).</summary>
    public const string StatusMethod = "purrtty/status";

    /// <summary>Registers the commands, the subscription and the capability on <paramref name="server"/>.</summary>
    public static void AddTo(KfsLanguageServer server, HostFeatureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        HostFeatureOptions o = options ?? new HostFeatureOptions();
        server.Features.Add(new KfsCommandProvider(o));
        server.Features.AddNotification(SubscribeMethod, context => Subscribe(context, o));
        server.Features.AddRequest(StatusMethod, (context, _) => Task.FromResult<JsonNode?>(Status(context.Server, o)),
            capabilities => Advertise(capabilities, server.Host, o));
        o.Feed?.Attach(server);
    }

    /// <summary>The topics of a <c>purrtty/subscribe</c> params object (unknown names ignored), or null when it has no <c>topics</c> array.</summary>
    public static LiveTopics? ParseTopics(JsonObject? @params)
    {
        if (LspJson.Get(@params, "topics") is not JsonArray topics) return null;
        LiveTopics result = LiveTopics.None;
        foreach (JsonNode? topic in topics)
        {
            result |= LspJson.String(topic) switch
            {
                "log" => LiveTopics.Log,
                "output" => LiveTopics.Output,
                "state" => LiveTopics.State,
                _ => LiveTopics.None,
            };
        }
        return result;
    }

    private static void Subscribe(LspRequestContext context, HostFeatureOptions options)
    {
        // Without a topics array the notification is malformed and ignored (a notification has no answer).
        if (ParseTopics(context.Params) is { } topics) options.Feed?.Subscribe(context.Server, topics);
    }

    private static void Advertise(JsonObject capabilities, IKfsLanguageHost host, HostFeatureOptions options)
    {
        bool vehicle = SafeCanControl(host);
        if (capabilities["experimental"] is not JsonObject experimental)
        {
            experimental = new JsonObject();
            capabilities["experimental"] = experimental;
        }
        experimental["purrtty"] = new JsonObject
        {
            ["subscribe"] = options.Feed is not null,
            ["vehicleCommands"] = vehicle,
            ["verb"] = vehicle && options.AllowVerb,
        };
    }

    private static JsonObject Status(KfsLanguageServer server, HostFeatureOptions options)
    {
        IKfsLanguageHost host = server.Host;
        LiveTopics topics = options.Feed?.TopicsOf(server) ?? LiveTopics.None;
        var subscribed = new JsonArray();
        if ((topics & LiveTopics.Log) != 0) subscribed.Add("log");
        if ((topics & LiveTopics.Output) != 0) subscribed.Add("output");
        if ((topics & LiveTopics.State) != 0) subscribed.Add("state");
        bool vehicle = SafeCanControl(host);
        return new JsonObject
        {
            ["workspaceRoot"] = host.WorkspaceRoot,
            ["target"] = host.CurrentTarget.ToString(),
            ["targetIsVehicle"] = host.CurrentTarget.IsVehicle,
            ["vehicleCommands"] = vehicle,
            ["verb"] = vehicle && options.AllowVerb,
            ["subscribed"] = subscribed,
        };
    }

    private static bool SafeCanControl(IKfsLanguageHost host)
    {
        try { return host.CanControlVehicle; }
        catch { return false; }
    }
}
