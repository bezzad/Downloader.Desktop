namespace Downloader.Desktop.Plugins.Hls.Tests;

internal static class TestFixtures
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string Read(string name) => File.ReadAllText(Path.Combine(Dir, name));
}
