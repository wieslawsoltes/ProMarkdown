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

Avalonia 12.1.5 and 12.1.6 contain two generated DirectComposition callbacks with `bool` parameters: `CreateTargetForHwnd` on the desktop-device and device vtables. [.NET 11 Native AOT rejects these non-blittable callback signatures](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop). Setting `DisableRuntimeMarshalling` on the sample cannot repair signatures in the separate Avalonia assembly.

Until Avalonia ships a fix, use the explicit source-patched publishing workflow:

```powershell
pwsh -File ./build/Publish-WindowsArm64NativeAot.ps1
```

The script downloads the exact Avalonia 12.1.5 source commit, verifies its SHA-256 hash, changes the two IDL parameters to the existing `BOOL` mapping (`int`), and passes `0` at the managed call site. MicroCom regenerates the interop code from the corrected declarations. The backend builds against the matching released implementation assemblies with its original assembly identity.

`build/WindowsArm64NativeAot.targets` substitutes that backend only for Native AOT compilation of the sample, after checking the target platform and assembly identity. Ordinary builds, package dependencies, and the NuGet cache keep the released backend. The workflow introduces no warning suppressions, marshalling bypasses, or preservation descriptors. The patch is pinned to Avalonia 12.1.5 and must be reaudited when updating that dependency.

Source and output are under `artifacts/aot-investigation/`; the executable is `artifacts/aot-investigation/patched/ProMarkdown.Sample.exe`.

Validated with .NET 11.0.0 and MSVC 14.52.37025 (Visual Studio Build Tools 2026 18.12): Native AOT publishing completes without the interop diagnostics or `LNK1322`. The ARM64 native executable starts successfully and renders Markdown, TextMate highlighting, and Mermaid diagrams, including theme refresh. Existing NuGet source-mapping warnings are unrelated to AOT and remain visible.

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
