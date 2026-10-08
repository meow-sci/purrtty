using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Imports = Brutal.ImGuiApi.ImGui.PInvoke;

// No resolver or native owner here. The caller installs exactly one resolver,
// stages imgui for Interop's explicit load, and owns the handle through Run.
internal static unsafe class FullManualAbiChecks
{
    private static Exception? callbackFailure;
    private static int formatCalls;
    private static int textVCalls;

    internal static Dictionary<string, object> Run(nint nativeHandle)
    {
        if (nativeHandle == 0) throw new ArgumentException("An already-owned native handle is required.");
        var identities = new Dictionary<string, string>();
        foreach (var entry in new (Assembly Assembly, string Hash)[] {
            (typeof(ImGui).Assembly, "b76777a4ef3399d6353b9dba1982e3afb84b5da2aa2500da1c062db0bfad827c"),
            (typeof(Brutal.Bool8).Assembly, "4cbae1473d6a3759345f1eec8e62e6c7da1e19f8d10db8d13c005203ec590de4"),
            (typeof(float2).Assembly, "a217a6098116a1895e965b3a1d4fdee931e59a7a55f326c7d710b0d7eeccd17a") })
        {
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry.Assembly.Location))).ToLowerInvariant();
            Require(hash == entry.Hash, "Loaded ABI contributor changed: " + entry.Assembly.FullName);
            identities.Add(entry.Assembly.Location, hash);
        }
        nint Export(string name) => NativeLibrary.GetExport(nativeHandle, name);
        var create = (delegate* unmanaged[Cdecl]<nint>)Export("purr_manual_fixture_create");
        var destroy = (delegate* unmanaged[Cdecl]<nint, void>)Export("purr_manual_fixture_destroy");
        var formats = (delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<int, byte*, void*, int, byte>, byte*>)Export("purr_manual_fixture_formats");
        var textv = (delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<byte*, byte*, void>, byte*>)Export("purr_manual_fixture_textv");
        callbackFailure = null;
        formatCalls = textVCalls = 0;
        nint context = create();
        Require(context != 0, "context");
        try
        {
            CheckDiagnostic(formats(&Exercise));
            CheckDiagnostic(textv(&ExerciseTextV));
            Require(callbackFailure is null, callbackFailure?.ToString() ?? "callback failure");
            Require(formatCalls == 23 && textVCalls == 2, "actual import invocation count");
            var viewport = (delegate* unmanaged[Cdecl]<nint>)Export("purr_manual_fixture_viewport");
            var invoke = (delegate* unmanaged[Cdecl]<nint, nint, float2>)Export("purr_manual_fixture_invoke");
            nint argument = viewport();
            foreach (string name in new[] { "GetWindowFramebufferScale", "GetWindowPos", "GetWindowSize" })
            {
                nint* slot = (nint*)Export("Platform_" + name + "_ManagedFunctionPointer");
                var getter = (delegate* unmanaged[Cdecl]<nint>)Export("Get_" + name + "_InteropPointer");
                nint trampoline = getter();
                Require(trampoline != 0 && *slot == 0, "inactive slot/getter");
                var first = new CallbackTarget(argument, -17.25f, 98.5f);
                var second = new CallbackTarget(argument, 35.125f, -0.75f);
                ViewportCallback firstDelegate = first.Invoke;
                ViewportCallback secondDelegate = second.Invoke;
                GCHandle firstRoot = GCHandle.Alloc(firstDelegate);
                GCHandle secondRoot = GCHandle.Alloc(secondDelegate);
                try
                {
                    nint pointer = Marshal.GetFunctionPointerForDelegate(firstDelegate);
                    nint installed = Interop.CreateTrampoline((delegate* unmanaged[Cdecl]<ImGuiViewportPtr, float2>)pointer, name);
                    // Interop performs NativeLibrary.Load itself: a write visible in
                    // THIS handle's slot proves the explicit load shares this image.
                    Require(installed == trampoline && *slot == pointer, "Interop loaded a different native artifact");
                    foreach (string other in new[] { "GetWindowFramebufferScale", "GetWindowPos", "GetWindowSize" })
                    {
                        if (other == name) continue;
                        nint* neighbor = (nint*)Export("Platform_" + other + "_ManagedFunctionPointer");
                        Require(neighbor != slot && *neighbor == 0, "callback slot alias/neighbor corruption");
                    }
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    float2 result = invoke(trampoline, argument);
                    Require(result.X == -17.25f && result.Y == 98.5f && first.Seen == argument && first.Calls == 1, "first callback identity/value/root");
                    pointer = Marshal.GetFunctionPointerForDelegate(secondDelegate);
                    Require(Interop.CreateTrampoline((delegate* unmanaged[Cdecl]<ImGuiViewportPtr, float2>)pointer, name) == trampoline, "stable trampoline");
                    Require(*slot == pointer, "replacement slot");
                    result = invoke(trampoline, argument);
                    Require(result.X == 35.125f && result.Y == -0.75f && second.Seen == argument && second.Calls == 1 && first.Calls == 1, "replacement identity/value");
                    *slot = 0;
                    Require(getter() == trampoline && *slot == 0, "unregister does not invalidate getter");
                    // Never call an inactive trampoline. Roots released only after
                    // clearing and completing synchronous UI-thread calls.
                }
                finally
                {
                    *slot = 0;
                    firstRoot.Free();
                    secondRoot.Free();
                    GC.KeepAlive(firstDelegate);
                    GC.KeepAlive(secondDelegate);
                }
            }
            return new Dictionary<string, object> {
                ["schema"] = "purr.manual.managed-checks.v1", ["formatCalls"] = formatCalls,
                ["textVCalls"] = textVCalls, ["callbackBridges"] = 3,
                ["loadedAbiContributors"] = identities, ["interopSameArtifact"] = true,
                ["runtimeRid"] = RuntimeInformation.RuntimeIdentifier
            };
        }
        finally { destroy(context); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float2 ViewportCallback(ImGuiViewportPtr viewport);
    private sealed class CallbackTarget(nint expected, float x, float y)
    {
        internal nint Seen;
        internal int Calls;
        internal float2 Invoke(ImGuiViewportPtr viewport)
        {
            Seen = (nint)viewport.Ptr;
            ++Calls;
            // No managed exception is allowed across the reverse P/Invoke edge.
            if (Seen != expected) callbackFailure = new InvalidOperationException("viewport identity");
            return new float2(x, y);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte Exercise(int id, byte* fmt, void* payload, int flags)
    {
        try
        {
            ++formatCalls;
            switch (id)
            {
                case 0: Imports.BulletText(fmt); break;
                case 1: Imports.DebugLog(fmt); break;
                case 2: fixed (byte* label = "label%%\0"u8) Imports.LabelText(label, fmt); break;
                case 3: Imports.LogText(fmt); break;
                case 4: Imports.SetItemTooltip(fmt); break;
                case 5: Imports.SetTooltip(fmt); break;
                case 6: Imports.Text(fmt); break;
                case 7: Imports.TextColored((float4*)payload, fmt); break;
                case 8: Imports.TextDisabled(fmt); break;
                case 9: Imports.TextWrapped(fmt); break;
                case 10: return Imports.TreeNode_1((byte*)payload, fmt) ? (byte)1 : (byte)0;
                case 11: return Imports.TreeNode_2(payload, fmt) ? (byte)1 : (byte)0;
                case 12: return Imports.TreeNodeEx_1((byte*)payload, (ImGuiTreeNodeFlags)flags, fmt) ? (byte)1 : (byte)0;
                case 13: return Imports.TreeNodeEx_2(payload, (ImGuiTreeNodeFlags)flags, fmt) ? (byte)1 : (byte)0;
                case 14: Brutal.ImGuiApi.Internal.PInvoke.TextAligned_internal(0.5f, 300.0f, fmt); break;
                default: throw new InvalidOperationException("Unknown native fixture case");
            }
        }
        catch (Exception error) { callbackFailure = error; }
        return 0;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ExerciseTextV(byte* fmt, byte* args)
    {
        try { ++textVCalls; Imports.TextV(fmt, args); }
        catch (Exception error) { callbackFailure = error; }
    }
    private static void CheckDiagnostic(byte* error)
    {
        if (callbackFailure is not null) throw new InvalidOperationException("Managed import callback failed", callbackFailure);
        if (error != null) throw new InvalidOperationException(Marshal.PtrToStringUTF8((nint)error));
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
