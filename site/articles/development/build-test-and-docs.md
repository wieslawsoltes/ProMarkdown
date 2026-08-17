---
title: "Build, Test, and Docs"
---

# Build, Test, and Docs

## Solution commands

```bash
dotnet restore CodexGui.slnx
dotnet build CodexGui.slnx
dotnet test --solution CodexGui.slnx
dotnet pack CodexGui.slnx -o artifacts/packages
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
- `release.yml` builds tagged releases and publishes the existing Markdown packages.

Generated docs are written to `site/.lunet/build/www/` and are not committed.
