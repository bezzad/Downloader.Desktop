# Writing a Downloader plugin

Downloader can be extended with **plugins** (add-ons) so the core app stays small. A plugin is a normal
.NET DLL that references one tiny package — **`Downloader.Desktop.Plugins.Abstractions`** — and implements
one or more of the pipeline interfaces. This guide walks you through it using the bundled example,
[`src/Downloader.Desktop.Plugins/Downloader.Desktop.Plugins.GitHub`](../src/Downloader.Desktop.Plugins/Downloader.Desktop.Plugins.GitHub) (a **GitHub
Releases** downloader that implements *every* interface).

## The model in 30 seconds
A download flows through three phases; your plugin hooks whichever it needs:

```
user input ──▶ RESOLVE ──▶ TRANSFER ──▶ POST-PROCESS ──▶ final file
              ILinkResolver  ITransfer    IPostProcessor
```
- **Resolve** (`ILinkResolver`) — turn a pasted input (a page/short-link/`github.com/...`) into a
  `DownloadPlan` of real URLs. **You don't download here** — the core engine does, keeping its
  multipart/pause/resume. *This is what most plugins need.*
- **Transfer** (`ITransferProvider`/`ITransfer`) — only if the bytes can't come over plain HTTP (e.g. a
  torrent). Your transfer *owns* the whole download and reports its own progress.
- **Post-process** (`IPostProcessor`) — combine/transform the downloaded files (mux, concat, checksum…).

You implement `IDownloaderPlugin` and register your contributions in `Initialize`.

## 1. Create the project
```xml
<!-- MyPlugin.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- REQUIRED: emits the deps.json the host needs to load your plugin's dependencies. -->
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <ItemGroup>
    <!-- Reference the SDK but DON'T ship it — the host provides it (shared type identity). -->
    <PackageReference Include="Downloader.Desktop.Plugins.Abstractions" Version="1.0.0">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```
(The sample uses a `ProjectReference` to the SDK in this repo; external authors use the package above.)

## 2. Implement the plugin
```csharp
using Downloader.Desktop.Plugins;

public sealed class MyPlugin : IDownloaderPlugin
{
    public string Id => "com.you.myplugin";   // stable, unique (used to remember enable/disable)
    public string Name => "My Plugin";
    public string Version => "1.0.0";
    public string Author => "you";
    public string Description => "What it does.";

    public void Initialize(IPluginContext ctx)
    {
        ctx.RegisterResolver(new MyResolver());          // claim some URLs
        // ctx.RegisterTransferProvider(new MyTransfer()); // own a protocol (torrent…)
        // ctx.RegisterPostProcessor(new MyPostProc());    // combine/transform after download
        // ctx.DataDirectory  → a per-plugin writable folder (download yt-dlp/ffmpeg here on first use)
        // ctx.Logger.LogInformation("…") → standard ILogger (Microsoft.Extensions.Logging) → app log
    }
}
```

### A resolver (the common case)
```csharp
internal sealed class MyResolver : ILinkResolver
{
    public bool CanResolve(string url) => url.Contains("example.com");   // fast, no network

    public async Task<DownloadPlan> ResolveAsync(string url, CancellationToken ct)
        => new DownloadPlan {
            SuggestedFileName = "video.mp4",
            Parts = new[] { new DownloadPart { Url = "https://cdn/real.mp4", Kind = PartKind.Combined } },
            PostProcess = PostProcess.None,
        };
}
```
For a video that comes as **separate streams**, return two `DownloadPart`s (`Video` + `Audio`) and set
`PostProcess.Kind = Mux`; for **HLS**, return the segment parts and `Concat`. The engine downloads every
part; your `IPostProcessor` combines them.

See the bundled **GitHub Releases** sample for a complete, working resolver (calls the GitHub API, picks
the asset for the user's OS) plus tiny `IPostProcessor` (writes a `.sha256` sidecar) and `ITransferProvider`
(a `file://` copier that owns its transfer — the shape a torrent plugin would take).

## 3. Build & install
```bash
dotnet build -c Release
```
Copy the built DLL (and any of its own dependencies) into the plugins folder, then enable it:
- **In the app:** Settings → **Plugins (add-ons)** → **Install plugin…** (pick the `.dll`), or **Open
  folder** and drop it in. Toggle it on/off there.
- **Folder:** `~/.config/Downloader/plugins` (Linux/macOS) · `%AppData%\Downloader\plugins` (Windows).

The app loads each plugin in an isolated `AssemblyLoadContext`, so a plugin's dependencies won't clash with
the app's. The `Abstractions` SDK is always supplied by the host, so your `IDownloaderPlugin` is the *same*
type the app expects.

> ⚠️ **Trust:** a plugin is normal code with full app permissions. Only install plugins you trust.

## Interface reference
| Interface | Purpose | Implement when |
|---|---|---|
| `IDownloaderPlugin` | Entry point; registers contributions in `Initialize`. | Always. |
| `ILinkResolver` | input → `DownloadPlan` (real URLs + recipe). | You turn pages/links into downloads. |
| `ITransferProvider` / `ITransfer` | Own a non-HTTP download (torrent…). | The engine can't fetch it over HTTP. |
| `IPostProcessor` | Combine/transform downloaded files. | You need mux/concat/decrypt/checksum. |
| `IPostDownloadAction` | A user-initiated action offered on a completed download your resolver produced (e.g. "Add to Ollama"): `Label`, `CanOffer(sourceUrl, filePath)`, `ExecuteAsync`. Shown as a button on the completion notification and the finished row; runs only on click; never modify the downloaded file. | You want a one-click follow-up on the finished file. |
| `IPluginContext` | Given to `Initialize`: register*, `DataDirectory`, `Logger` (`ILogger`). | — |

Types: `DownloadPlan { SuggestedFileName, Parts[], PostProcess }`, `DownloadPart { Url, Kind, Headers, ExpectedSize }`,
`PostProcess { Kind, Recipe }`, `TransferProgress { Percentage, BytesReceived, TotalBytes, BytesPerSecond }`.
Full architecture: [`docs/plugins-architecture.md`](plugins-architecture.md).
