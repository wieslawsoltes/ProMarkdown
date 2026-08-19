---
title: "Image Loading and Security"
---

# Image Loading and Security

`MarkdownTextBlock` loads Markdown images through an explicit source policy and an injectable loader. Choose the policy for the trust boundary of the document rather than relying on the compatibility default for untrusted content.

## Built-in policies

```csharp
using ProMarkdown.Services;

markdownView.ImageOptions = MarkdownImageOptions.BlockRemote;
```

| Policy | Data URI | Local file | HTTP/HTTPS |
| --- | --- | --- | --- |
| `MarkdownImageOptions.Default` | Allowed | Allowed | Allowed |
| `MarkdownImageOptions.BlockRemote` | Allowed | Allowed | Blocked |
| `MarkdownImageOptions.EmbeddedOnly` | Allowed | Blocked | Blocked |

`Default` preserves the original ProMarkdown behavior. Remote images can reveal the viewer's network address, request document-specific URLs, and delay rendering. Applications displaying untrusted or privacy-sensitive Markdown should normally select `BlockRemote` or `EmbeddedOnly`.

The policy is a styled property, so it can also be assigned in XAML or a shared style:

```xml
<markdown:MarkdownTextBlock
    xmlns:markdown="using:ProMarkdown.Controls"
    xmlns:services="using:ProMarkdown.Services"
    ImageOptions="{x:Static services:MarkdownImageOptions.BlockRemote}" />
```

## Resource limits

Every built-in policy uses bounded defaults:

- `MaximumBytes`: 8 MiB for the encoded data, local file, or remote download
- `MaximumPixelCount`: 64 × 1024 × 1024 decoded pixels
- `RemoteTimeout`: 15 seconds

Create a policy to apply stricter application limits:

```csharp
markdownView.ImageOptions = new MarkdownImageOptions
{
    AllowedSourceKinds = MarkdownImageSourceKinds.Data,
    MaximumBytes = 2 * 1024 * 1024,
    MaximumPixelCount = 16 * 1024 * 1024,
    RemoteTimeout = TimeSpan.FromSeconds(5)
};
```

`AllowedSourceKinds` is a flags enum with `None`, `Data`, `File`, `Remote`, and `All`. Invalid flags, non-positive limits, and invalid timeouts are rejected when rendering validates the policy.

## Custom loaders

Assign an `IMarkdownImageLoader` when images come from an application cache, authenticated store, virtual file system, or another host-controlled source:

```csharp
markdownView.ImageLoader = applicationImageLoader;
```

The loader receives a `MarkdownImageLoadRequest` containing the resolved `Source` URI and active `Options`, plus the render generation's `CancellationToken`. It must return an Avalonia `Bitmap` and should enforce the supplied policy and limits before allocating the decoded image.

`DefaultMarkdownImageLoader.Instance` is the standard implementation for data, file, HTTP, and HTTPS sources. It validates the source policy, encoded size, and pixel count before returning a bitmap.

## Cancellation and lifetime

Changing Markdown, image options, the image loader, layout, or theme starts a new render generation. The previous generation is canceled, so in-flight remote downloads and custom loader work should stop promptly. Detaching the control cancels the active generation and disposes its render resources.

Image loading participates in `MarkdownTextBlock.IsRendering`. The control remains busy until each image operation for the current generation completes or is canceled. Results from stale generations are discarded and cannot replace current content.

Continue with [Rendering Services](rendering-services/) when implementing asynchronous rendering plugins.
