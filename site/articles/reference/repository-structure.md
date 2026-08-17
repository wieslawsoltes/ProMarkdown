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

- `ProMarkdown` — core Markdown control and services
- `ProMarkdown.Plugin.*` — optional feature packages
- `ProMarkdown.Sample` — standalone sample application
- `ProMarkdown.Tests` — headless tests
