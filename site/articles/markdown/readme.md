---
title: "Markdown Stack"
---

# Markdown Stack

The repository contains a reusable Markdown subsystem for Avalonia applications.

## Base library

`src/CodexGui.Markdown` contains the core control, rendering, editing, parsing, selection, layout, theme, and hit-testing services.

## Separate plugin projects

Optional features live in dedicated plugin packages instead of growing the core library indefinitely. Registration remains explicit, and consumers can choose only the features they need.

## Sample

`src/CodexGui.Markdown.Sample` demonstrates editor integration, preview rendering, and plugin registration in one isolated executable.

Continue with [Plugin Ecosystem](plugin-ecosystem/) for the project breakdown.
