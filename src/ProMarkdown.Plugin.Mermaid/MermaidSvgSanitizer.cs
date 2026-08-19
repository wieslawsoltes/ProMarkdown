using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace ProMarkdown.Plugin.Mermaid;

internal static class MermaidSvgSanitizer
{
    private const int MaximumSvgCharacters = 8 * 1024 * 1024;

    public static string Sanitize(string svg)
    {
        ArgumentNullException.ThrowIfNull(svg);
        if (svg.Length > MaximumSvgCharacters)
            throw new InvalidOperationException("The generated Mermaid SVG exceeds the sanitization limit.");

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersInDocument = MaximumSvgCharacters
        };
        using var textReader = new StringReader(svg);
        using var xmlReader = XmlReader.Create(textReader, settings);
        var document = XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace);
        if (document.Root is null ||
            !string.Equals(document.Root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase) ||
            document.Root.Name.NamespaceName is not ("" or "http://www.w3.org/2000/svg"))
            throw new InvalidOperationException("Mermaid did not return an SVG document.");

        foreach (var element in document.Root.DescendantsAndSelf().ToArray())
        {
            if (IsBlockedElement(element.Name.LocalName))
            {
                element.Remove();
                continue;
            }

            if (string.Equals(element.Name.LocalName, "style", StringComparison.OrdinalIgnoreCase) &&
                ContainsExternalCss(element.Value))
                throw new InvalidOperationException("The Mermaid SVG contains external CSS.");

            foreach (var attribute in element.Attributes().ToArray())
            {
                if (attribute.Name == XNamespace.Xml + "base")
                {
                    attribute.Remove();
                    continue;
                }

                var name = attribute.Name.LocalName;
                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                {
                    attribute.Remove();
                    continue;
                }

                if (ContainsExternalCss(attribute.Value))
                    throw new InvalidOperationException("The Mermaid SVG contains an external style reference.");

                if (!string.Equals(name, "href", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "src", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(element.Name.LocalName, "a", StringComparison.OrdinalIgnoreCase) &&
                    IsSafeHttpsUri(attribute.Value))
                    continue;
                if (attribute.Value.StartsWith('#'))
                    continue;
                if (string.Equals(element.Name.LocalName, "image", StringComparison.OrdinalIgnoreCase) &&
                    IsSafeEmbeddedRasterImage(attribute.Value))
                    continue;
                attribute.Remove();
            }
        }

        document.Declaration = null;
        return document.ToString(SaveOptions.DisableFormatting);
    }

    internal static bool IsSafeHttpsUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockedElement(string localName) =>
        localName.Equals("script", StringComparison.OrdinalIgnoreCase) ||
        localName.Equals("foreignObject", StringComparison.OrdinalIgnoreCase) ||
        localName.Equals("iframe", StringComparison.OrdinalIgnoreCase) ||
        localName.Equals("object", StringComparison.OrdinalIgnoreCase) ||
        localName.Equals("embed", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeEmbeddedRasterImage(string value) =>
        (value.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase) ||
         value.StartsWith("data:image/jpeg;base64,", StringComparison.OrdinalIgnoreCase) ||
         value.StartsWith("data:image/gif;base64,", StringComparison.OrdinalIgnoreCase) ||
         value.StartsWith("data:image/webp;base64,", StringComparison.OrdinalIgnoreCase)) &&
        value.Length > value.IndexOf(',') + 1;

    private static bool ContainsExternalCss(string value)
    {
        if (value.Contains("@import", StringComparison.OrdinalIgnoreCase))
            return true;

        var searchStart = 0;
        while (value.IndexOf("url(", searchStart, StringComparison.OrdinalIgnoreCase) is var index && index >= 0)
        {
            var target = value.AsSpan(index + 4).TrimStart();
            if (target.IsEmpty)
                return true;
            if (target[0] != '#' &&
                (target[0] is not ('\'' or '"') || target.Length < 2 || target[1] != '#'))
            {
                return true;
            }
            searchStart = index + 4;
        }
        return false;
    }
}
