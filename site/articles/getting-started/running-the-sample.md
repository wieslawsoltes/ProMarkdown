---
title: "Running the Sample"
---

# Running the Sample

## Prerequisite

- .NET 10 SDK

## Build and test

```bash
dotnet restore ProMarkdown.slnx
dotnet build ProMarkdown.slnx
dotnet test --solution ProMarkdown.slnx
```

## Run the Markdown sample

```bash
dotnet run --project src/ProMarkdown.Sample/ProMarkdown.Sample.csproj
```

The sample provides an editor and preview surface with the Markdown plugin ecosystem registered together.

## Serve the documentation

```bash
dotnet tool restore
bash ./serve-docs.sh
```
