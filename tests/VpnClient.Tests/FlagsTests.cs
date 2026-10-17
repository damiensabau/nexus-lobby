using System.IO;
using VpnClient.App.Services;

namespace VpnClient.Tests;

public class FlagsTests
{
    [Fact]
    public void Every_catalog_country_has_a_png_flag()
    {
        var dir = Path.Combine(RepoRoot(), "src", "VpnClient.App", "Resources", "Flags");
        var missing = Locations.All
            .Select(l => l.CountryCode.ToLowerInvariant())
            .Distinct()
            .Where(code => !File.Exists(Path.Combine(dir, code + ".png")));
        Assert.Empty(missing);
    }

    [Fact]
    public void Flag_files_are_real_png_images()
    {
        var dir = Path.Combine(RepoRoot(), "src", "VpnClient.App", "Resources", "Flags");
        foreach (var file in Directory.GetFiles(dir, "*.png"))
        {
            var header = File.ReadAllBytes(file).Take(8).ToArray();
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, header);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VpnClient.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("VpnClient.sln introuvable");
    }
}
