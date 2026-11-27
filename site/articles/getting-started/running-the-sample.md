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

## Try the Mermaid animations

The **Native Mermaid edge animation** and **Mermaid classDef edge animation** examples also animate in Mermaid Live. Use the Play button or drag the timeline in the sample. In Mermaid Live, paste the diagram source without the Markdown fence. [Native Mermaid edge animation](../markdown/plugin-ecosystem/#native-mermaid-edge-animation)

**Animated Mermaid** and **Animated Mermaid chart** use the `Mermaider.Animate` extension. The request flow lets you select an ending and jump between steps. The chart's timeline follows the preview as you scroll. [Mermaider animation scores](../markdown/plugin-ecosystem/#mermaider-animation)

Use the theme toggle to try the diagrams in light and dark mode. The preview eases whole mouse-wheel steps. Fractional wheel input and scrolling over controls such as the timeline and ending selector keep their usual behavior.

## Serve the documentation

```bash
dotnet tool restore
bash ./serve-docs.sh
```
