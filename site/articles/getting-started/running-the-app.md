---
title: "Running the Sample"
---

# Running the Sample

## Prerequisite

- .NET 10 SDK

## Build and test

```bash
dotnet restore CodexGui.slnx
dotnet build CodexGui.slnx
dotnet test --solution CodexGui.slnx
```

## Run the Markdown sample

```bash
dotnet run --project src/CodexGui.Markdown.Sample/CodexGui.Markdown.Sample.csproj
```

The sample provides an editor and preview surface with the Markdown plugin ecosystem registered together.

## Serve the documentation

```bash
dotnet tool restore
bash ./serve-docs.sh
```
