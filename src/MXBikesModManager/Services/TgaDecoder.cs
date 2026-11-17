namespace MXBikesModManager.Services;

/// <summary>Raw BGRA32 pixels, ready to be turned into a bitmap on the UI thread.</summary>
public sealed record PixelBuffer(int Width, int Height, byte[] Bgra);

/// <summary>
/// Small TGA decoder (types 1/2/3 and RLE variants), enough for PiBoSo's capa.tga previews.
/// </summary>
public static class TgaDecoder
{
    public static PixelBuffer? Decode(byte[] data)
    {
        if (data.Length < 18) return null;

        var idLength = data[0];
        var colorMapType = data[1];
        var imageType = data[2];
        var colorMapLength = data[5] | (data[6] << 8);
        var colorMapEntrySize = data[7];
        var width = data[12] | (data[13] << 8);
        var height = data[14] | (data[15] << 8);
        var bpp = data[16];
        var descriptor = data[17];

        if (width <= 0 || height <= 0 || width > 8192 || height > 8192) return null;

        var offset = 18 + idLength;
        if (offset > data.Length) return null;

        byte[]? palette = null;
        if (colorMapType == 1)
        {
            var entryBytes = (colorMapEntrySize + 7) / 8;
            var paletteBytes = colorMapLength * entryBytes;
            if (offset + paletteBytes > data.Length) return null;
            palette = new byte[paletteBytes];
            Array.Copy(data, offset, palette, 0, paletteBytes);
            offset += paletteBytes;
        }

        var pixelBytes = (bpp + 7) / 8;
        if (pixelBytes <= 0) return null;

        var isRle = imageType is 9 or 10 or 11;
        var baseType = imageType switch
        {
            1 or 9 => 1,
            2 or 10 => 2,
            3 or 11 => 3,
            _ => -1
        };
        if (baseType < 0) return null;

        var pixelCount = width * height;
        var raw = new byte[pixelCount * pixelBytes];
        var rawIndex = 0;

        if (isRle)
        {
            var p = offset;
            while (rawIndex < raw.Length && p < data.Length)
            {
                var packet = data[p++];
                var count = (packet & 0x7F) + 1;
                if ((packet & 0x80) != 0)
                {
                    if (p + pixelBytes > data.Length) break;
                    for (var i = 0; i < count && rawIndex + pixelBytes <= raw.Length; i++)
                    {
                        Array.Copy(data, p, raw, rawIndex, pixelBytes);
                        rawIndex += pixelBytes;
                    }
                    p += pixelBytes;
                }
                else
                {
                    var run = count * pixelBytes;
                    if (p + run > data.Length) break;
                    if (rawIndex + run > raw.Length) run = raw.Length - rawIndex;
                    Array.Copy(data, p, raw, rawIndex, run);
                    rawIndex += run;
                    p += count * pixelBytes;
                }
            }
        }
        else
        {
            var available = Math.Min(raw.Length, data.Length - offset);
            Array.Copy(data, offset, raw, 0, available);
            rawIndex = available;
        }

        if (rawIndex <= 0) return null;

        var bgra = new byte[pixelCount * 4];
        var alphaBits = descriptor & 0x0F;
        var rightToLeft = (descriptor & 0x10) != 0;
        var topToBottom = (descriptor & 0x20) != 0;

        for (var y = 0; y < height; y++)
        {
            var srcY = topToBottom ? y : height - 1 - y;
            for (var x = 0; x < width; x++)
            {
                var srcX = rightToLeft ? width - 1 - x : x;
                var src = (srcY * width + srcX) * pixelBytes;
                var dst = (y * width + x) * 4;
                if (src + pixelBytes > raw.Length) continue;

                switch (baseType)
                {
                    case 1 when palette is not null && colorMapEntrySize is 15 or 16 or 24 or 32:
                        {
                            var index = pixelBytes == 1 ? raw[src] : raw[src] | (raw[src + 1] << 8);
                            var entryBytes = (colorMapEntrySize + 7) / 8;
                            var pi = index * entryBytes;
                            if (pi + entryBytes <= palette.Length)
                            {
                                WriteColor(bgra, dst, palette, pi, colorMapEntrySize, true);
                            }
                            break;
                        }
                    case 3:
                        bgra[dst] = raw[src];
                        bgra[dst + 1] = raw[src];
                        bgra[dst + 2] = raw[src];
                        bgra[dst + 3] = 255;
                        break;
                    case 2 when bpp is 15 or 16 or 24 or 32:
                        WriteColor(bgra, dst, raw, src, bpp, alphaBits > 0);
                        break;
                    default:
                        // Unsupported/malformed pixel depth: leave the pixel transparent
                        // instead of reading past the end of the raw buffer.
                        break;
                }
            }
        }

        return new PixelBuffer(width, height, bgra);
    }

    private static void WriteColor(byte[] destination, int dst, byte[] source, int src, int bits, bool hasAlpha)
    {
        if (bits is 16 or 15)
        {
            var value = source[src] | (source[src + 1] << 8);
            var r = (value >> 10) & 0x1F;
            var g = (value >> 5) & 0x1F;
            var b = value & 0x1F;
            destination[dst] = (byte)((b * 255) / 31);
            destination[dst + 1] = (byte)((g * 255) / 31);
            destination[dst + 2] = (byte)((r * 255) / 31);
            destination[dst + 3] = hasAlpha && (value & 0x8000) == 0 ? (byte)0 : (byte)255;
            return;
        }

        // TGA stores BGR(A)
        destination[dst] = source[src];
        destination[dst + 1] = source[src + 1];
        destination[dst + 2] = source[src + 2];
        destination[dst + 3] = bits == 32 && hasAlpha ? source[src + 3] : (byte)255;
    }
}
