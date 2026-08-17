using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CodexGui.Markdown.Services;
using Markdig.Extensions.Mathematics;

namespace CodexGui.Markdown.Plugin.Math;

internal sealed class MathMarkdownBlockTemplateProvider : IMarkdownBlockTemplateProvider
{
    public int Order => 10;

    public IEnumerable<MarkdownBlockTemplate> GetTemplates(MarkdownBlockTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        yield return new MarkdownBlockTemplate(
            "built-in-block-math",
            "Math block",
            MarkdownEditorFeature.Math,
            MathMarkdownSyntax.BuildMathBlock(@"\frac{a}{b} = \sqrt{x_1 + x_2}"),
            "Insert a fenced block math expression.");

        yield return new MarkdownBlockTemplate(
            "built-in-block-matrix",
            "Matrix",
            MarkdownEditorFeature.Math,
            MathMarkdownSyntax.BuildMathBlock(@"\begin{bmatrix} a & b \\ c & d \end{bmatrix}"),
            "Insert a matrix math block.");
    }
}

internal sealed class MathBlockMarkdownEditorPlugin : IMarkdownEditorPlugin
{
    public string EditorId => MathMarkdownEditorIds.Block;

    public MarkdownEditorFeature Feature => MarkdownEditorFeature.Math;

    public int Order => -30;

    public bool TryResolveTarget(MarkdownEditorResolveContext context, out MarkdownEditorTarget? target)
    {
        ArgumentNullException.ThrowIfNull(context);

        target = null;
        if (!context.TryFindAncestor<MathBlock>(out var mathBlock, out var depth) || mathBlock is null)
        {
            return false;
        }

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(mathBlock.Span);
        if (sourceSpan.IsEmpty)
        {
            return false;
        }

        target = new MarkdownEditorTarget(
            Feature,
            new MarkdownAstNodeInfo(mathBlock, sourceSpan, mathBlock.Line, mathBlock.Column),
            depth,
            "Math block");
        return true;
    }

    public Control? CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Node is MathBlock mathBlock
            ? MarkdownMathEditorUiFactory.CreateBlockEditor(context, mathBlock.Lines.ToString())
            : null;
    }
}

internal sealed class MathInlineMarkdownEditorPlugin : IMarkdownEditorPlugin
{
    public string EditorId => MathMarkdownEditorIds.Inline;

    public MarkdownEditorFeature Feature => MarkdownEditorFeature.Math;

    public int Order => -30;

    public bool TryResolveTarget(MarkdownEditorResolveContext context, out MarkdownEditorTarget? target)
    {
        ArgumentNullException.ThrowIfNull(context);

        target = null;
        if (!context.TryFindAncestor<MathInline>(out var mathInline, out var depth) || mathInline is null)
        {
            return false;
        }

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(mathInline.Span);
        if (sourceSpan.IsEmpty)
        {
            return false;
        }

        target = new MarkdownEditorTarget(
            Feature,
            new MarkdownAstNodeInfo(mathInline, sourceSpan, mathInline.Line, mathInline.Column),
            depth,
            "Inline math");
        return true;
    }

    public Control? CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Node is MathInline mathInline
            ? MarkdownMathEditorUiFactory.CreateInlineEditor(context, mathInline.Content.ToString())
            : null;
    }
}

internal static class MarkdownMathEditorUiFactory
{
    public static Control CreateBlockEditor(MarkdownEditorPluginContext context, string source)
    {
        ArgumentNullException.ThrowIfNull(context);

        var editor = MarkdownEditorUiFactory.CreateCodeEditor(MathMarkdownSyntax.NormalizeBlockSource(source));
        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineCodeStyle(editor, context.RenderContext.FontSize);
        }

        var previewHost = CreatePreviewHost(context);
        void UpdatePreview()
        {
            previewHost.Child = MarkdownMathRendering.CreateBlockView(
                MarkdownMathParser.ParseBlock(editor.Text),
                context.RenderContext);
        }

        editor.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };
        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit TeX-style math source and review the native formula preview below."));
        }

        body.Children.Add(CreateSnippetToolbar(editor, includeBlockSnippets: true));
        body.Children.Add(editor);
        body.Children.Add(previewHost);

        string BuildMarkdown() => MathMarkdownSyntax.BuildMathBlock(editor.Text);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit math block",
            "Math editor",
            body,
            () => context.CommitReplacement(BuildMarkdown()),
            context.CancelEdit,
            editor,
            buildBlockMarkdownForActions: BuildMarkdown);
    }

    public static Control CreateInlineEditor(MarkdownEditorPluginContext context, string source)
    {
        ArgumentNullException.ThrowIfNull(context);

        var editor = MarkdownEditorUiFactory.CreateTextEditor(MathMarkdownSyntax.NormalizeInlineSource(source), acceptsReturn: false, minHeight: 40);
        MarkdownEditorUiFactory.ApplyInlineMetadataStyle(editor, context.RenderContext.FontSize);

        var previewHost = CreatePreviewHost(context);
        void UpdatePreview()
        {
            previewHost.Child = MarkdownMathRendering.CreateInlineView(
                MarkdownMathParser.ParseInline(editor.Text),
                context.RenderContext);
        }

        editor.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        var body = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                CreateSnippetToolbar(editor, includeBlockSnippets: false),
                editor,
                previewHost
            }
        };

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit inline math",
            "Inline math editor",
            body,
            () => context.CommitReplacement(MathMarkdownSyntax.BuildInlineMath(editor.Text)),
            context.CancelEdit,
            editor,
            preferInlineTextLayout: true,
            buildBlockMarkdownForActions: null);
    }

    private static Border CreatePreviewHost(MarkdownEditorPluginContext context)
    {
        return new Border
        {
            Background = MarkdownEditorUiFactory.SectionBackground,
            BorderBrush = MarkdownEditorUiFactory.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? new Thickness(8, 6) : new Thickness(10)
        };
    }

    private static Control CreateSnippetToolbar(TextBox textBox, bool includeBlockSnippets)
    {
        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        panel.Children.Add(CreateSnippetButton("frac", () => AppendSnippet(textBox, @"\frac{a}{b}")));
        panel.Children.Add(CreateSnippetButton("sqrt", () => AppendSnippet(textBox, @"\sqrt{x}")));
        panel.Children.Add(CreateSnippetButton("sum", () => AppendSnippet(textBox, @"\sum_{i=0}^{n}")));
        panel.Children.Add(CreateSnippetButton("int", () => AppendSnippet(textBox, @"\int_{a}^{b}")));
        panel.Children.Add(CreateSnippetButton("hat", () => AppendSnippet(textBox, @"\hat{x}")));
        panel.Children.Add(CreateSnippetButton("text", () => AppendSnippet(textBox, @"\text{label}")));

        if (includeBlockSnippets)
        {
            panel.Children.Add(CreateSnippetButton("matrix", () => AppendSnippet(textBox, @"\begin{bmatrix} a & b \\ c & d \end{bmatrix}")));
            panel.Children.Add(CreateSnippetButton("cases", () => AppendSnippet(textBox, @"\begin{cases} x & x > 0 \\ 0 & x = 0 \end{cases}")));
            panel.Children.Add(CreateSnippetButton("aligned", () => AppendSnippet(textBox, @"\begin{aligned} a &= b + c \\ d &= e - f \end{aligned}")));
        }

        return panel;
    }

    private static Button CreateSnippetButton(string text, Action onClick)
    {
        return MarkdownEditorUiFactory.CreateSecondaryButton(text, onClick);
    }

    private static void AppendSnippet(TextBox textBox, string snippet)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(snippet);

        var current = textBox.Text ?? string.Empty;
        if (current.Length > 0 && !char.IsWhiteSpace(current[^1]))
        {
            current += " ";
        }

        current += snippet;
        textBox.Text = current;
        textBox.CaretIndex = current.Length;
        textBox.Focus();
    }
}
