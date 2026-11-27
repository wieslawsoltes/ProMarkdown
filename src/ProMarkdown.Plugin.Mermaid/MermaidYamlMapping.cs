using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using Anchor = YamlDotNet.Core.Tokens.Anchor;
using FlowEntry = YamlDotNet.Core.Tokens.FlowEntry;
using ScalarToken = YamlDotNet.Core.Tokens.Scalar;
using Tag = YamlDotNet.Core.Tokens.Tag;
using Token = YamlDotNet.Core.Tokens.Token;

namespace ProMarkdown.Plugin.Mermaid;

internal readonly struct MermaidYamlMapping
{
    private readonly Document? _document;
    private readonly int _index;

    private MermaidYamlMapping(Document document, int index)
    {
        _document = document;
        _index = index;
    }

    public static MermaidYamlMapping Parse(string yaml)
    {
        using var reader = new StringReader(yaml);
        var parser = yaml.Contains('&') ? new Parser(new AnchoredScalarScanner(reader)) : new Parser(reader);
        var document = new Document(parser);
        return new MermaidYamlMapping(document, document.Root);
    }

    public MermaidYamlMapping ReadMapping(string name) => _document is { } document
        ? new MermaidYamlMapping(document, document.FindValue(_index, name))
        : default;

    public Scalar? FindScalar(string name) => _document?.FindScalar(_index, name);

    public bool TryReadNullableString(string name, out string? value)
    {
        value = null;
        if (FindScalar(name) is not { } scalar)
            return false;

        if ((IsImplicitPlain(scalar) || HasTag(scalar, "tag:yaml.org,2002:null")) &&
            (scalar.Value is "~" or "null" or "Null" or "NULL" ||
             scalar is { Style: ScalarStyle.Plain, Value.Length: 0 }))
            return true;

        if (!scalar.Tag.IsEmpty && !HasTag(scalar, "tag:yaml.org,2002:str"))
            return false;

        value = scalar.Value;
        return true;
    }

    public bool? ReadBoolean(string name)
    {
        var scalar = FindScalar(name);
        if (scalar is null || (!IsImplicitPlain(scalar) && !HasTag(scalar, "tag:yaml.org,2002:bool")))
            return null;

        return scalar.Value switch
        {
            "true" or "True" or "TRUE" => true,
            "false" or "False" or "FALSE" => false,
            _ => null
        };
    }

    public double? ReadNumber(string name)
    {
        var scalar = FindScalar(name);
        if (scalar is null)
            return null;

        var implicitPlain = IsImplicitPlain(scalar);
        var explicitlyInteger = HasTag(scalar, "tag:yaml.org,2002:int");
        if (!implicitPlain && !explicitlyInteger && !HasTag(scalar, "tag:yaml.org,2002:float"))
            return null;

        var numberStyle = explicitlyInteger ? NumberStyles.Integer : NumberStyles.Float;
        var parsed = double.TryParse(scalar.Value, numberStyle, CultureInfo.InvariantCulture, out var number) ||
            ((implicitPlain || explicitlyInteger) && TryReadRadixInteger(scalar.Value, out number));
        return parsed && double.IsFinite(number) ? number : null;
    }

    private static bool TryReadRadixInteger(ReadOnlySpan<char> value, out double result)
    {
        result = 0;
        if (value.StartsWith('+'))
            value = value[1..];
        if (value.Length < 3 || value[0] != '0')
            return false;

        var radix = value[1] switch
        {
            'b' => 2,
            'o' => 8,
            'x' => 16,
            _ => 0
        };
        if (radix == 0)
            return false;

        foreach (var character in value[2..])
        {
            var digit = character switch
            {
                >= '0' and <= '9' => character - '0',
                >= 'a' and <= 'f' => character - 'a' + 10,
                >= 'A' and <= 'F' => character - 'A' + 10,
                _ => radix
            };
            if (digit >= radix)
                return false;
            result = result * radix + digit;
        }
        return true;
    }

    private static bool IsImplicitPlain(Scalar scalar) => scalar.Tag.IsEmpty && scalar.Style == ScalarStyle.Plain;

    private static bool HasTag(Scalar scalar, string tag) => !scalar.Tag.IsEmpty && scalar.Tag.Value == tag;

    private sealed class AnchoredScalarScanner(TextReader reader) : IScanner
    {
        private readonly Scanner _scanner = new(reader);
        private bool _hasAnchor;
        private bool _consumedAnchoredScalar;
        private bool _repeatDelimiter;

        public Token? Current => _scanner.Current;
        public Mark CurrentPosition => _scanner.CurrentPosition;

        public bool MoveNext()
        {
            if (Current is not null)
                ConsumeCurrent();
            return MoveNextWithoutConsuming();
        }

        public bool MoveNextWithoutConsuming()
        {
            // YamlDotNet 18.1 checks the next token twice after an anchored scalar in a flow mapping.
            // Both checks need to see the same comma until the parser consumes it.
            if (_repeatDelimiter)
            {
                _repeatDelimiter = false;
                return true;
            }

            var advanced = _scanner.MoveNextWithoutConsuming();
            _repeatDelimiter = advanced && _consumedAnchoredScalar && Current is FlowEntry;
            return advanced;
        }

        public void ConsumeCurrent()
        {
            _consumedAnchoredScalar = _hasAnchor && Current is ScalarToken;
            _hasAnchor = Current is Anchor || (_hasAnchor && Current is Tag);
            _repeatDelimiter = false;
            _scanner.ConsumeCurrent();
        }
    }

    private sealed class Document
    {
        private readonly List<Entry> _events = [];

        public Document(IParser parser)
        {
            parser.Consume<StreamStart>();
            if (!parser.TryConsume<DocumentStart>(out _))
                return;

            // Index references and collection boundaries without expanding aliases or recursively loading YAML.
            var anchors = new Dictionary<AnchorName, int>();
            var collections = new Stack<int>();
            while (parser.Current is { } item and not DocumentEnd)
            {
                var index = _events.Count;
                var resolvedIndex = index;
                if (item is NodeEvent node && !node.Anchor.IsEmpty)
                    anchors[node.Anchor] = index;
                else if (item is AnchorAlias alias && !anchors.TryGetValue(alias.Value, out resolvedIndex))
                    throw new AnchorNotFoundException(alias.Start, alias.End, "Undefined YAML anchor.");

                if (item.NestingIncrease > 0)
                    collections.Push(index);
                else if (item.NestingIncrease < 0)
                {
                    var start = collections.Pop();
                    _events[start] = _events[start] with { NextIndex = index + 1 };
                }

                _events.Add(new Entry(item, index + 1, resolvedIndex));
                parser.MoveNext();
            }
        }

        public int Root => _events.Count > 0 ? 0 : -1;

        public int FindValue(int mapping, string name)
        {
            if (mapping < 0)
                return -1;

            var start = _events[mapping].ResolvedIndex;
            var entry = _events[start];
            if (entry.Event is not MappingStart)
                return -1;

            for (var key = start + 1; key < entry.NextIndex - 1;)
            {
                var keyEntry = _events[key];
                var value = keyEntry.NextIndex;
                if (_events[keyEntry.ResolvedIndex].Event is Scalar scalar && scalar.Value == name)
                    return value;
                key = _events[value].NextIndex;
            }
            return -1;
        }

        public Scalar? FindScalar(int mapping, string name)
        {
            var value = FindValue(mapping, name);
            return value >= 0 ? _events[_events[value].ResolvedIndex].Event as Scalar : null;
        }

        private readonly record struct Entry(ParsingEvent Event, int NextIndex, int ResolvedIndex);
    }
}
