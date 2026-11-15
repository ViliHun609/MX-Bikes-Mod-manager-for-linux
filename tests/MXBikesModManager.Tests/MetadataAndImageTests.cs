using System.IO.Compression;
using System.Text;
using MXBikesModManager.Models;
using MXBikesModManager.Services;
using Xunit;

namespace MXBikesModManager.Tests;

public class MetadataAndImageTests
{
    private static byte[] MakeTga(int width, int height, params (byte B, byte G, byte R)[] pixels)
    {
        var data = new byte[18 + width * height * 3];
        data[2] = 2; // uncompressed true-colour
        data[12] = (byte)(width & 0xFF);
        data[13] = (byte)(width >> 8);
        data[14] = (byte)(height & 0xFF);
        data[15] = (byte)(height >> 8);
        data[16] = 24;
        data[17] = 0x20; // top-left origin

        var offset = 18;
        foreach (var (b, g, r) in pixels)
        {
            data[offset++] = b;
            data[offset++] = g;
            data[offset++] = r;
        }
        return data;
    }

    [Fact]
    public void Tga_decoder_reads_uncompressed_truecolour()
    {
        var tga = MakeTga(2, 2,
            (255, 0, 0), (0, 255, 0),
            (0, 0, 255), (255, 255, 255));

        var pixels = TgaDecoder.Decode(tga);

        Assert.NotNull(pixels);
        Assert.Equal(2, pixels!.Width);
        Assert.Equal(2, pixels.Height);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels.Bgra[..4]);          // blue
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixels.Bgra[4..8]);         // green
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels.Bgra[8..12]);        // red
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, pixels.Bgra[12..16]);   // white
    }

    [Fact]
    public void Tga_decoder_reads_rle_packets()
    {
        // 2x2: one raw packet (2 pixels) + one run packet (2 pixels).
        var data = new byte[18 + 1 + 6 + 1 + 3];
        data[2] = 10;
        data[12] = 2;
        data[14] = 2;
        data[16] = 24;
        data[17] = 0x20;

        var offset = 18;
        data[offset++] = 0x01; // raw, 2 pixels
        data[offset++] = 0; data[offset++] = 0; data[offset++] = 255;
        data[offset++] = 0; data[offset++] = 255; data[offset++] = 0;
        data[offset++] = 0x81; // run, 2 pixels
        data[offset++] = 255; data[offset++] = 0; data[offset] = 0;

        var pixels = TgaDecoder.Decode(data);

        Assert.NotNull(pixels);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels!.Bgra[..4]);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixels.Bgra[4..8]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels.Bgra[8..12]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels.Bgra[12..16]);
    }

    [Fact]
    public void Pkz_inspector_reads_track_ini_and_cover_image()
    {
        using var temp = new TempDir();
        var zipPath = Path.Combine(temp.Path, "Nice Track.pkz");
        var tga = MakeTga(1, 1, (10, 20, 30));
        var ini = """
            [info]
            name = Nice Track
            short_name = Nice
            [ui]
            author = Track Maker
            location = Somewhere
            pic = capa.tga
            """;

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "Nice Track/Nice Track.ini", ini);
            WriteEntry(zip, "Nice Track/capa.tga", tga);
        }

        var inspection = new PkzInspector().Inspect(zipPath);

        Assert.NotNull(inspection.Info);
        Assert.Equal("Nice Track", inspection.Info!.Name);
        Assert.Equal("Track Maker", inspection.Info.Author);
        Assert.NotNull(inspection.Preview);
        Assert.Equal("Nice Track/capa.tga", inspection.Preview!.EntryName);
    }

    [Fact]
    public void Pkz_inspector_returns_nothing_for_obfuscated_archives()
    {
        using var temp = new TempDir();
        var fake = Path.Combine(temp.Path, "bike.pkz");
        File.WriteAllText(fake, "PK\u0003\u0004 not really a zip");

        var inspection = new PkzInspector().Inspect(fake);

        Assert.Null(inspection.Info);
        Assert.Null(inspection.Preview);
    }

    [Fact]
    public void Ini_parser_handles_sections_comments_and_spacing()
    {
        var ini = IniFile.Parse("""
            ; a comment
            [info]
            name = My Bike
            short_name=Short
            [ui]
            author = Someone
            """);

        Assert.Equal("My Bike", IniFile.Get(ini, "info", "name"));
        Assert.Equal("Short", IniFile.Get(ini, "INFO", "SHORT_NAME"));
        Assert.Equal("Someone", IniFile.Get(ini, "ui", "author"));
        Assert.Null(IniFile.Get(ini, "missing", "name"));
    }

    [Fact]
    public void Metadata_cache_survives_a_reload()
    {
        using var temp = new TempDir();
        var log = new LogService(temp.Path);
        var cache = new MetadataCache(Path.Combine(temp.Path, "cache"), log);
        var written = DateTime.UtcNow;

        cache.Put("/mods/bikes/a.pkz", 1234, written,
            new ModInfo { Name = "Cached Bike", Author = "Me" },
            PreviewSource.FromArchive("/mods/bikes/a.pkz", "a/cover.tga"));
        cache.SaveIfDirty();

        var reloaded = new MetadataCache(Path.Combine(temp.Path, "cache"), log);
        var hit = reloaded.TryGet("/mods/bikes/a.pkz", 1234, written, out var info, out var preview);

        Assert.True(hit);
        Assert.Equal("Cached Bike", info!.Name);
        Assert.Equal("a/cover.tga", preview!.EntryName);

        var miss = reloaded.TryGet("/mods/bikes/a.pkz", 9999, written, out _, out _);
        Assert.False(miss);
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
        => WriteEntry(zip, name, Encoding.UTF8.GetBytes(content));

    private static void WriteEntry(ZipArchive zip, string name, byte[] content)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mxbikes-tests", Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
            }
            catch (IOException) { }
        }
    }
}
