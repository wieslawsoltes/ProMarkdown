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
