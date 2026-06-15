using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using PurrTTY.Terminal.Rendering;
using purrTTY.Logging;
using RenderCore;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// Per-window GPU texture cache for kitty-graphics images. One <see cref="SimpleVkTexture"/>
/// + ImGui descriptor (<see cref="ImTextureRef"/>) per image id, created from the
/// backend's decoded RGBA and drawn via the window draw list. Render-thread only
/// (Vulkan upload + ImGui registration): driven from <c>TerminalWindow.Render</c>,
/// which runs inside the game's UI pass. O(1) LRU-evicts to stay well under the shared
/// 1000-slot ImGui descriptor pool. Evicted textures are deferred for
/// <see cref="DeferredDeleteFrames"/> frames so in-flight GPU draw commands finish before
/// the Vulkan object is freed.
/// </summary>
internal sealed class ImageTextureCache : IDisposable
{
    private sealed class Entry
    {
        public required SimpleVkTexture Texture;
        public ImTextureRef TexRef;
        public long ContentVersion;
        public int Width;
        public int Height;
    }

    // Tunable cap shared across all instances: the ImGui descriptor pool has 1000 slots
    // (whole game). Terminal-doom cycles ~1 image per frame, so 32 is ample and saves VRAM.
    public static int MaxTextures { get; set; } = 32;

    // Hold evicted textures for this many frames before calling Dispose so in-flight
    // GPU draw commands (from the previous 1–2 frames) finish before the Vulkan object
    // is freed. 3 covers double- and triple-buffered swap chains with one frame of margin.
    private const int DeferredDeleteFrames = 3;

    // O(1) LRU: _lruOrder front = least-recently-used, back = most-recently-used.
    // Storing the LinkedListNode inside the dictionary enables O(1) promote on TryGet
    // (remove + re-add-last) and O(1) evict (take First, look up bucket, remove both).
    private readonly Dictionary<int, (Entry Entry, LinkedListNode<int> Node)> _entries = new();
    private readonly LinkedList<int> _lruOrder = new();
    private readonly Queue<(Entry Entry, long DeleteAtFrame)> _pendingDelete = new();
    private long _frame;
    private long _estimatedVramBytes;
    private bool _disposed;

    /// <summary>Number of GPU textures currently cached (for the perf HUD).</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Approximate GPU VRAM used by cached textures in bytes (Width × Height × 4 per entry).
    /// </summary>
    public long EstimatedVramBytes => _estimatedVramBytes;

    /// <summary>
    /// Advances the frame counter and releases any GPU textures whose deferred-delete
    /// deadline has passed. Must be called once at the start of each render pass.
    /// </summary>
    public void BeginFrame()
    {
        _frame++;
        while (_pendingDelete.Count > 0 && _pendingDelete.Peek().DeleteAtFrame <= _frame)
        {
            try { _pendingDelete.Dequeue().Entry.Texture.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// Uploads each newly-decoded image to the GPU (idempotent per id+version).
    /// Must be called on the render thread before drawing. Failures are logged and
    /// skipped — a missing texture just means that image won't draw this frame.
    /// </summary>
    public void Upload(IReadOnlyList<TerminalImage> newImages)
    {
        if (_disposed || newImages.Count == 0)
        {
            return;
        }

        var renderer = Program.GetRenderer();
        if (renderer is null)
        {
            return;
        }

        foreach (var img in newImages)
        {
            try
            {
                UploadOne(renderer, img);
            }
            catch (Exception ex)
            {
                ModLog.Log.Debug($"purrTTY: kitty texture upload failed for image {img.ImageId}: {ex.Message}");
            }
        }
    }

    /// <summary>Looks up the texture for an image id, promoting it to MRU position.</summary>
    public bool TryGet(int imageId, out ImTextureRef texRef)
    {
        if (_entries.TryGetValue(imageId, out var bucket))
        {
            _lruOrder.Remove(bucket.Node);
            _lruOrder.AddLast(bucket.Node);
            texRef = bucket.Entry.TexRef;
            return true;
        }

        texRef = default;
        return false;
    }

    private void UploadOne(Renderer renderer, TerminalImage img)
    {
        if (img.Width <= 0 || img.Height <= 0 ||
            img.Rgba.Length < (long)img.Width * img.Height * 4)
        {
            return;
        }

        if (_entries.TryGetValue(img.ImageId, out var existing))
        {
            if (existing.Entry.ContentVersion == img.ContentVersion)
            {
                return;
            }
            Evict(img.ImageId, existing); // content changed: replace
        }

        var texture = new SimpleVkTexture(
            $"purrtty-kitty-{img.ImageId}",
            renderer.Allocator,
            img.Width,
            img.Height,
            depth: 1,
            VkFormat.R8G8B8A8UNorm,
            mipLevels: 1,
            flags: VkImageUsageFlags.TransferDstBit | VkImageUsageFlags.SampledBit);

        try
        {
            using (var pool = renderer.Allocator.CreateStagingPool(renderer.Graphics, 1))
            {
                var cmd = pool.NextCommandBuffer();
                cmd.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
                texture.UploadData(pool, cmd, img.Rgba, new[] { img.Rgba.Length });
                cmd.End();
                pool.Submit().Wait();
            }

            var texRef = ImGuiBackend.Vulkan.AddTexture(renderer.LinearSampler, texture.ImageView);
            // GPU upload and ImGui registration both succeeded — release the CPU buffer now.
            img.ClearPixelData();

            var entry = new Entry
            {
                Texture = texture,
                TexRef = texRef,
                ContentVersion = img.ContentVersion,
                Width = img.Width,
                Height = img.Height,
            };
            var node = _lruOrder.AddLast(img.ImageId);
            _entries[img.ImageId] = (entry, node);
            _estimatedVramBytes += (long)img.Width * img.Height * 4;
        }
        catch
        {
            texture.Dispose();
            throw;
        }

        EvictLruIfNeeded();
    }

    private void EvictLruIfNeeded()
    {
        while (_entries.Count > MaxTextures)
        {
            var lruNode = _lruOrder.First;
            if (lruNode is null)
            {
                break;
            }

            if (_entries.TryGetValue(lruNode.Value, out var bucket))
            {
                Evict(lruNode.Value, bucket);
            }
            else
            {
                _lruOrder.RemoveFirst(); // state desync recovery; should not happen
            }
        }
    }

    private void Evict(int imageId, (Entry Entry, LinkedListNode<int> Node) bucket)
    {
        _entries.Remove(imageId);
        _lruOrder.Remove(bucket.Node);
        _estimatedVramBytes = Math.Max(0, _estimatedVramBytes - (long)bucket.Entry.Width * bucket.Entry.Height * 4);

        // Release the ImGui descriptor slot immediately (prevents new draw calls from
        // referencing this texture). The Vulkan image itself is deferred: in-flight
        // commands from prior frames may still be sampling it via the old descriptor.
        try { ImGuiBackend.Vulkan.RemoveTexture(bucket.Entry.TexRef); } catch { }

        _pendingDelete.Enqueue((bucket.Entry, _frame + DeferredDeleteFrames));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        while (_pendingDelete.Count > 0)
        {
            try { _pendingDelete.Dequeue().Entry.Texture.Dispose(); } catch { }
        }

        foreach (var (entry, _) in _entries.Values)
        {
            try { ImGuiBackend.Vulkan.RemoveTexture(entry.TexRef); } catch { }
            try { entry.Texture.Dispose(); } catch { }
        }
        _entries.Clear();
        _lruOrder.Clear();
        _estimatedVramBytes = 0;
    }
}
