using System.IO;

namespace CodexGui.Markdown.Services;

internal static class MarkdownUriUtilities
{
    public static Uri? ResolveUri(Uri? baseUri, string? url)
    {
        return TryResolveUri(baseUri, url, out var resolvedUri) ? resolvedUri : null;
    }

    public static bool TryResolveUri(Uri? baseUri, string? url, out Uri? resolvedUri)
    {
        resolvedUri = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var trimmed = url.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            resolvedUri = absoluteUri;
            return true;
        }

        if (baseUri is not null &&
            Uri.TryCreate(baseUri, trimmed, out var baseResolvedUri) &&
            baseResolvedUri.IsAbsoluteUri)
        {
            resolvedUri = baseResolvedUri;
            return true;
        }

        if (TryCreateFileUri(trimmed, out var fileUri))
        {
            resolvedUri = fileUri;
            return true;
        }

        return false;
    }

    private static bool TryCreateFileUri(string path, out Uri? fileUri)
    {
        fileUri = null;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                return false;
            }

            fileUri = new Uri(fullPath);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
