using Avalonia.Controls;

namespace CodexGui.Markdown.Services;

public sealed class MarkdownEditingService : IMarkdownEditingService
{
    private readonly IReadOnlyList<IMarkdownEditorPlugin> _editorPlugins;
    private readonly IReadOnlyList<IMarkdownBlockTemplateProvider> _blockTemplateProviders;

    public MarkdownEditingService(
        IEnumerable<IMarkdownEditorPlugin>? editorPlugins = null,
        IEnumerable<IMarkdownBlockTemplateProvider>? blockTemplateProviders = null)
    {
        _editorPlugins = (editorPlugins ?? [])
            .OrderBy(static plugin => plugin.Order)
            .ToArray();
        _blockTemplateProviders = (blockTemplateProviders ?? [])
            .OrderBy(static provider => provider.Order)
            .ToArray();
    }

    public MarkdownEditorSession? ResolveSession(MarkdownEditorResolveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.HitTestResult);
        ArgumentNullException.ThrowIfNull(request.Preferences);

        if (!request.HitTestResult.ParseResult.UsesOriginalSourceSpans)
        {
            return null;
        }

        var resolveContext = new MarkdownEditorResolveContext
        {
            HitTestResult = request.HitTestResult,
            Preferences = request.Preferences
        };

        List<(IMarkdownEditorPlugin Plugin, MarkdownEditorTarget Target)> candidates = [];
        foreach (var plugin in _editorPlugins)
        {
            if (plugin.TryResolveTarget(resolveContext, out var target) && target is not null && !target.SourceSpan.IsEmpty)
            {
                candidates.Add((plugin, target));
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var minimumDepth = candidates.Min(static candidate => candidate.Target.MatchDepth);
        var selected = candidates
            .Where(candidate => candidate.Target.MatchDepth == minimumDepth)
            .OrderByDescending(candidate => IsPreferredEditor(candidate.Plugin, candidate.Target.Feature, request.Preferences))
            .ThenBy(candidate => candidate.Plugin.Order)
            .First();

        return new MarkdownEditorSession(
            selected.Plugin.EditorId,
            selected.Target.Feature,
            selected.Target.SourceSpan,
            selected.Target.Title,
            selected.Target.AstNode.NodeKind);
    }

    public Control? CreateEditor(MarkdownEditorRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plugin = _editorPlugins.FirstOrDefault(candidate => string.Equals(candidate.EditorId, request.Session.EditorId, StringComparison.Ordinal));
        if (plugin is null)
        {
            return null;
        }

        return plugin.CreateEditor(new MarkdownEditorPluginContext(request, GetBlockTemplates(request)));
    }

    private static bool IsPreferredEditor(IMarkdownEditorPlugin plugin, MarkdownEditorFeature feature, MarkdownEditorPreferences preferences)
    {
        return preferences.TryGetPreferredEditor(feature, out var preferredEditorId) &&
               string.Equals(preferredEditorId, plugin.EditorId, StringComparison.Ordinal);
    }

    private IReadOnlyList<MarkdownBlockTemplate> GetBlockTemplates(MarkdownEditorRenderRequest request)
    {
        if (_blockTemplateProviders.Count == 0)
        {
            return [];
        }

        var context = new MarkdownBlockTemplateContext
        {
            Node = request.Node,
            ParseResult = request.ParseResult,
            RenderContext = request.RenderContext,
            Session = request.Session,
            Preferences = request.Preferences
        };

        List<MarkdownBlockTemplate> templates = [];
        foreach (var provider in _blockTemplateProviders)
        {
            foreach (var template in provider.GetTemplates(context) ?? [])
            {
                if (string.IsNullOrWhiteSpace(template.TemplateId) ||
                    string.IsNullOrWhiteSpace(template.Label) ||
                    string.IsNullOrWhiteSpace(template.Markdown))
                {
                    continue;
                }

                templates.Add(template);
            }
        }

        return templates;
    }
}
