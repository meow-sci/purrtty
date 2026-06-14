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
/// which runs inside the game's UI pass. LRU-evicts to stay well under the shared
/// 1000-slot ImGui descriptor pool.
/// </summary>
internal sealed class ImageTextureCache : IDisposable
{
    private sealed class Entry
    {
        public required SimpleVkTexture Texture;
        public ImTextureRef TexRef;
        public long ContentVersion;
        public long LastUsedFrame;
    }

    // Conservative cap: the ImGui descriptor pool holds 1000 sets shared with the
    // whole game; terminal image previews need far fewer.
    private const int MaxTextures = 64;

    private readonly Dictionary<int, Entry> _entries = new();
    private long _frame;
    private bool _disposed;

    /// <summary>Advances the frame counter used for LRU bookkeeping.</summary>
    public void BeginFrame() => _frame++;

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

    /// <summary>Looks up the texture for an image id, marking it used this frame.</summary>
    public bool TryGet(int imageId, out ImTextureRef texRef)
    {
        if (_entries.TryGetValue(imageId, out var entry))
        {
            entry.LastUsedFrame = _frame;
            texRef = entry.TexRef;
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

        // Already have this exact content — nothing to do.
        if (_entries.TryGetValue(img.ImageId, out var existing))
        {
            if (existing.ContentVersion == img.ContentVersion)
            {
                return;
            }
            Remove(img.ImageId); // content changed: replace
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
            _entries[img.ImageId] = new Entry
            {
                Texture = texture,
                TexRef = texRef,
                ContentVersion = img.ContentVersion,
                LastUsedFrame = _frame,
            };
        }
        catch
        {
            texture.Dispose();
            throw;
        }

        EvictIfNeeded();
    }

    private void EvictIfNeeded()
    {
        while (_entries.Count > MaxTextures)
        {
            int lruId = -1;
            long lru = long.MaxValue;
            foreach (var (id, entry) in _entries)
            {
                if (entry.LastUsedFrame < lru)
                {
                    lru = entry.LastUsedFrame;
                    lruId = id;
                }
            }

            if (lruId < 0)
            {
                break;
            }
            Remove(lruId);
        }
    }

    private void Remove(int imageId)
    {
        if (!_entries.Remove(imageId, out var entry))
        {
            return;
        }

        try
        {
            ImGuiBackend.Vulkan.RemoveTexture(entry.TexRef);
        }
        catch
        {
            // Best-effort descriptor release; still dispose the image below.
        }
        entry.Texture.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        foreach (var entry in _entries.Values)
        {
            try
            {
                ImGuiBackend.Vulkan.RemoveTexture(entry.TexRef);
            }
            catch
            {
                // ignore
            }
            entry.Texture.Dispose();
        }
        _entries.Clear();
    }
}
