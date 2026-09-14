using Xunit;

namespace Tenon.Update.Tests;

/// <summary>Sanity checks for the .ico files shipped in the repository (the resource updater consumes them).</summary>
public class IconFileTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Tenon.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Theory]
    [InlineData("src/Tenon.Setup/setup.ico")]
    [InlineData("samples/HelloWpf/Assets/app.ico")]
    public void Icons_are_valid_multi_size_ico_files(string relative)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), relative));
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        var count = BitConverter.ToUInt16(bytes, 4);
        Assert.True(count >= 3, "expected at least three sizes");
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + i * 16;
            var size = BitConverter.ToUInt32(bytes, entry + 8);
            var offset = BitConverter.ToUInt32(bytes, entry + 12);
            Assert.True(offset + size <= bytes.Length, "image data out of range");
        }
    }
}
