---
title: "Running the Sample"
---

# Running the Sample

## Prerequisite

- .NET 11 SDK and .NET 10 runtime (to run tests on both targets)

## Build and test

```bash
dotnet restore ProMarkdown.slnx
dotnet build ProMarkdown.slnx
dotnet test --solution ProMarkdown.slnx
```

## Run the Markdown sample

```bash
dotnet run --project src/ProMarkdown.Sample/ProMarkdown.Sample.csproj --framework net11.0
```

Use `--framework net10.0` to run the sample on .NET 10.

The sample provides an editor and preview surface with the Markdown plugin ecosystem registered together.

## Serve the documentation

```bash
dotnet tool restore
bash ./serve-docs.sh
```
