using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MXBikesModManager.Models;

namespace MXBikesModManager.Services;

/// <summary>
/// Loads preview images from inside .pkz archives (zip entries) or from loose files.
/// Handles TGA itself; PNG/JPEG/BMP/WebP go through Avalonia/Skia.
/// </summary>
public sealed class ThumbnailService
{
    private const int MaxDimension = 640;

    private readonly LogService _log;
    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.Ordinal);

    public ThumbnailService(LogService log) => _log = log;

    public async Task<Bitmap?> LoadAsync(PreviewSource source, CancellationToken ct = default)
    {
        var key = source.IsArchive ? $"{source.ArchivePath}\u0001{source.EntryName}" : source.FilePath ?? string.Empty;
        if (key.Length == 0) return null;

        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
        }

        var bytes = await Task.Run(() => ReadBytes(source), ct).ConfigureAwait(false);
        Bitmap? bitmap = null;

        if (bytes is { Length: > 0 })
        {
            bitmap = await Dispatcher.UIThread.InvokeAsync(() => Decode(bytes));
        }

        lock (_cache)
        {
            _cache[key] = bitmap;
        }

        return bitmap;
    }

    private Bitmap? Decode(byte[] bytes)
    {
        try
        {
            if (LooksLikeTga(bytes))
            {
                var pixels = TgaDecoder.Decode(bytes);
                if (pixels is null) return null;
                var scaled = Downscale(pixels);
                return CreateBitmap(scaled);
            }

            using var stream = new MemoryStream(bytes, writable: false);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            _log.Warn($"Preview decode failed: {ex.Message}");
            return null;
        }
    }

    private static bool LooksLikeTga(byte[] bytes)
    {
        // The extension is not passed through to the decoder, so detect TGA by header sanity.
        if (bytes.Length < 18) return false;
        var imageType = bytes[2];
        if (imageType is not (1 or 2 or 3 or 9 or 10 or 11)) return false;
        if (bytes[1] > 1) return false;
        var width = bytes[12] | (bytes[13] << 8);
        var height = bytes[14] | (bytes[15] << 8);
        return width is > 0 and <= 8192 && height is > 0 and <= 8192;
    }

    private static PixelBuffer Downscale(PixelBuffer source)
    {
        var max = Math.Max(source.Width, source.Height);
        if (max <= MaxDimension) return source;

        var scale = (double)MaxDimension / max;
        var width = Math.Max(1, (int)(source.Width * scale));
        var height = Math.Max(1, (int)(source.Height * scale));
        var target = new byte[width * height * 4];
        var stepX = (double)source.Width / width;
        var stepY = (double)source.Height / height;

        for (var y = 0; y < height; y++)
        {
            var sy = Math.Min(source.Height - 1, (int)(y * stepY));
            for (var x = 0; x < width; x++)
            {
                var sx = Math.Min(source.Width - 1, (int)(x * stepX));
                var src = (sy * source.Width + sx) * 4;
                var dst = (y * width + x) * 4;
                target[dst] = source.Bgra[src];
                target[dst + 1] = source.Bgra[src + 1];
                target[dst + 2] = source.Bgra[src + 2];
                target[dst + 3] = source.Bgra[src + 3];
            }
        }

        return new PixelBuffer(width, height, target);
    }

    private static Bitmap CreateBitmap(PixelBuffer pixels)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(pixels.Width, pixels.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var buffer = bitmap.Lock();
        var rowBytes = pixels.Width * 4;
        if (buffer.RowBytes == rowBytes)
        {
            Marshal.Copy(pixels.Bgra, 0, buffer.Address, pixels.Bgra.Length);
        }
        else
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                Marshal.Copy(pixels.Bgra, y * rowBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
            }
        }

        return bitmap;
    }

    private byte[]? ReadBytes(PreviewSource source)
    {
        try
        {
            if (source.IsArchive)
            {
                using var stream = new FileStream(source.ArchivePath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
                var entry = zip.GetEntry(source.EntryName!);
                if (entry is null || entry.Length > 30 * 1024 * 1024) return null;
                using var entryStream = entry.Open();
                using var memory = new MemoryStream((int)entry.Length);
                entryStream.CopyTo(memory);
                return memory.ToArray();
            }

            if (source.IsFile)
            {
                var info = new FileInfo(source.FilePath!);
                if (!info.Exists || info.Length > 30 * 1024 * 1024) return null;
                return File.ReadAllBytes(source.FilePath!);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not read preview: {ex.Message}");
        }

        return null;
    }
}
