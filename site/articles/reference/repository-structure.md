---
title: "Repository Structure"
---

# Repository Structure

## Top-level layout

- `src/` — the core Markdown library, optional plugins, and sample
- `tests/` — headless Markdown tests
- `docs/` — Markdown implementation and design notes
- `site/` — Lunet documentation content and configuration
- `.github/workflows/` — build, docs, package, and release automation
- `build/` — shared package metadata and SourceLink configuration

## Solution projects

- `CodexGui.Markdown` — core Markdown control and services
- `CodexGui.Markdown.Plugin.*` — optional feature packages
- `CodexGui.Markdown.Sample` — standalone sample application
- `CodexGui.Markdown.Tests` — headless tests

The CodexGui desktop application and app-server transport projects are intentionally not part of this repository.
