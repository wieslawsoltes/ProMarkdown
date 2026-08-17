namespace CodexGui.Markdown.Services;

public static class MarkdownRenderingServices
{
    public static IMarkdownRenderController DefaultController { get; } = CreateDefaultController();

    public static IMarkdownHitTestingService DefaultHitTestingService { get; } = CreateHitTestingService();

    public static IMarkdownEditingService DefaultEditingService { get; } = CreateEditingService();

    public static IMarkdownRenderController CreateDefaultController()
    {
        return CreateController();
    }

    public static IMarkdownRenderController CreateController(params IMarkdownPlugin[] plugins)
    {
        return CreateController(plugins.AsEnumerable());
    }

    public static IMarkdownRenderController CreateController(IEnumerable<IMarkdownPlugin>? plugins)
    {
        var registry = CreateRegistry(plugins);

        return new MarkdownRenderController(
            new MarkdownParsingService(registry.ParserPlugins),
            new MarkdownInlineRenderingService(registry.BlockRenderingPlugins, registry.InlineRenderingPlugins));
    }

    public static IMarkdownHitTestingService CreateHitTestingService()
    {
        return new MarkdownHitTestingService();
    }

    public static IMarkdownEditingService CreateEditingService(params IMarkdownPlugin[] plugins)
    {
        return CreateEditingService(plugins.AsEnumerable());
    }

    public static IMarkdownEditingService CreateEditingService(IEnumerable<IMarkdownPlugin>? plugins)
    {
        var registry = CreateRegistry(plugins);
        return new MarkdownEditingService(registry.EditorPlugins, registry.BlockTemplateProviders);
    }

    private static MarkdownPluginRegistry CreateRegistry(IEnumerable<IMarkdownPlugin>? plugins)
    {
        var registry = new MarkdownPluginRegistry();
        MarkdownBuiltInEditorPlugins.Register(registry);
        registry.AddPlugin(new ThematicBreakMarkdownPlugin());

        foreach (var plugin in plugins ?? [])
        {
            registry.AddPlugin(plugin);
        }

        return registry;
    }
}
