using System.Net.Http;
using System.Runtime.CompilerServices;

namespace Downloader.Desktop.Plugins.SiteMedia;

/// <summary>
/// Calls SDK members that are newer than some apps this plugin gets installed into. An app older than
/// v2.13.0 has neither <c>IPluginContext.CreateHttpClient</c> nor <c>ProxyAddress</c>, and a direct call
/// made <c>Initialize</c> throw <see cref="MissingMethodException"/> — the app then reported "the
/// downloaded package contained no plugin". There the plugin now works without the proxy, instead of not
/// at all.
/// <para>Each call sits in its own non-inlined method because the exception is raised when THAT method is
/// compiled, i.e. at the call site, inside the try. (Each plugin keeps its own copy: two plugins must
/// never share source — see SKILL.md.)</para>
/// </summary>
internal static class HostCompat
{
    public static HttpClient CreateHttpClient(IPluginContext context)
    {
        try { return ClientFromHost(context); }
        catch (MissingMethodException) { return new HttpClient(); }
    }

    public static string? ProxyAddress(IPluginContext context)
    {
        try { return ProxyFromHost(context); }
        catch (MissingMethodException) { return null; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static HttpClient ClientFromHost(IPluginContext context) => context.CreateHttpClient();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? ProxyFromHost(IPluginContext context) => context.ProxyAddress;
}
