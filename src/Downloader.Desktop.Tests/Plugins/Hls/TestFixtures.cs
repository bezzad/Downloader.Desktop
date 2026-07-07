namespace Downloader.Desktop.Tests.Plugins.Hls;

internal static class TestFixtures
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string Read(string name) => File.ReadAllText(Path.Combine(Dir, name));
}
