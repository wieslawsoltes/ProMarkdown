---
title: "Build, Test, and Docs"
---

# Build, Test, and Docs

## Solution commands

Install the .NET 11 SDK and the .NET 10 runtime. The solution builds and tests both `net10.0` and `net11.0`, and each library package includes both targets.

Headless UI tests use [ProTest](https://github.com/wieslawsoltes/ProTest) with xUnit 4 and Microsoft Testing Platform. Update ProTest and the Avalonia packages together: ProTest depends on an exact Avalonia version because its headless platform uses private APIs.

```bash
dotnet restore ProMarkdown.slnx
dotnet build ProMarkdown.slnx
dotnet test --solution ProMarkdown.slnx
dotnet pack ProMarkdown.slnx -o artifacts/packages
```

## AOT and trimming analysis

`src/Directory.Build.props` enables `EnableAotAnalyzer` and `EnableTrimAnalyzer` for every library and the sample on both target frameworks. It imports the repository-wide build properties, including the runtime async opt-in for `net11.0`. Test projects are outside this configuration.

The source analyzers report no AOT or trimming warnings. No warning suppressions or assembly preservation descriptors are needed for the current source. Analyzer results do not certify the complete third-party dependency graph, so packages are not marked `IsAotCompatible` or `IsTrimmable`.

Validate a consuming application with its actual runtime identifier and publishing settings. For example:

```bash
dotnet publish src/ProMarkdown.Sample/ProMarkdown.Sample.csproj -c Release -f net11.0 -r win-arm64 --self-contained true -p:PublishTrimmed=true -p:TrimMode=full -p:TrimmerSingleWarn=false -o artifacts/trim-audit/net11.0
```

The .NET 11 Windows ARM64 sample publishes successfully with full trimming and no trimming warnings.

### Windows ARM64 Native AOT

Use Visual Studio 2026 Build Tools with the **Desktop development with C++** workload and **ARM64 C++ tools**. MSVC 2022 14.44.35207 fails with `LNK1322` for this application. Microsoft identifies the [same ARM64 linker issue as fixed in Visual Studio 2026](https://github.com/dotnet/runtime/issues/133835). No linker checks need to be disabled.

Run the publish script from the repository root:

```powershell
pwsh -File ./build/Publish-WindowsArm64NativeAot.ps1
```

The script uses the released Win32 backend. Avalonia 12.2.0 fixes the DirectComposition callback signatures for .NET 11 Native AOT. [Avalonia DirectComposition fix](https://github.com/AvaloniaUI/Avalonia/pull/22820)

The executable is written to `artifacts/aot-investigation/patched/ProMarkdown.Sample.exe`.

## Lunet docs commands

```bash
dotnet tool restore
bash ./build-docs.sh
bash ./check-docs.sh
bash ./serve-docs.sh
```

PowerShell equivalents are also available.

## CI layout

- `build.yml` validates the Markdown solution, tests, docs, and NuGet packages.
- `docs.yml` deploys the Lunet site.
- `release.yml` builds tagged releases and publishes the ProMarkdown packages.

Generated docs are written to `site/.lunet/build/www/` and are not committed.

## NuGet Trusted Publishing

The Release workflow uses the GitHub `nuget` environment. Its `NUGET_USER` variable contains the NuGet.org profile name `wieslawsoltes`. GitHub OIDC supplies a short-lived key for uploading packages and symbols.

On NuGet.org, create a GitHub Actions Trusted Publishing policy with package owner `wieslawsoltes`, repository owner `wieslawsoltes`, repository `ProMarkdown`, workflow file `release.yml`, and environment `nuget`. Select **Push only new package versions** and enter these patterns on separate lines. [NuGet.org Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)

```text
ProMarkdown
ProMarkdown.Plugin.*
```

Push a version tag such as `v0.2.3` to start a release. For a manual release, run **Release** from `main` and enter the package version, such as `0.2.3`.
