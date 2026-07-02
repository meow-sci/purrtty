// Standalone replication of ghostty's LoadingImage.decompressZlib
// (src/terminal/kitty/graphics_image.zig, pinned 7092b394 == current main),
// fed the exact zlib payloads the gatOS screen stream produces (.NET
// ZLibStream, CompressionLevel.Fastest). Run in Debug (safety on) and
// ReleaseFast (the vendored native's build mode).
const std = @import("std");

const max_size = 400 * 1024 * 1024; // ghostty's image-data cap

pub fn main() !void {
    var gpa: std.heap.GeneralPurposeAllocator(.{}) = .{};
    defer _ = gpa.deinit();
    const alloc = gpa.allocator();

    var args = try std.process.argsWithAllocator(alloc);
    defer args.deinit();
    _ = args.next(); // exe
    const path = args.next() orelse "zlib-zeros.bin";

    const file = try std.fs.cwd().openFile(path, .{});
    defer file.close();
    const data = try file.readToEndAlloc(alloc, 1 << 30);
    defer alloc.free(data);
    std.debug.print("{s}: {} compressed bytes\n", .{ path, data.len });

    // --- ghostty decompressZlib, verbatim ---
    var buf: [std.compress.flate.max_window_len]u8 = undefined;
    var reader: std.Io.Reader = .fixed(data);
    var stream: std.compress.flate.Decompress = .init(&reader, .zlib, &buf);

    var list: std.ArrayList(u8) = .empty;
    defer list.deinit(alloc);
    stream.reader.appendRemaining(alloc, &list, .limited(max_size)) catch {
        std.debug.print("decompress FAILED: {?}\n", .{stream.err});
        return error.DecompressionFailed;
    };
    // --- end ---

    std.debug.print("decompressed {} bytes\n", .{list.items.len});

    // Integrity: zlib-zeros must inflate to exactly 230400 zero bytes;
    // for the real payload just report length (expected 230400).
    var nonzero: usize = 0;
    for (list.items) |b| {
        if (b != 0) nonzero += 1;
    }
    std.debug.print("nonzero bytes: {}\n", .{nonzero});
}
