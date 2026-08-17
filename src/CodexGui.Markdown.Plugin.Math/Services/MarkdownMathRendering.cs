using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using CodexGui.Markdown.Services;
using SystemMath = System.Math;

namespace CodexGui.Markdown.Plugin.Math;

internal static class MarkdownMathRendering
{
    private static readonly FontFamily MathFontFamily = new("Cambria Math, STIX Two Math, Times New Roman");
    private static readonly FontFamily SansSerifFamily = new("Inter, Segoe UI, Arial");
    private static readonly FontFamily MonospaceFamily = new("Cascadia Mono, Consolas, Courier New");
    private static readonly IBrush LightFormulaForeground = new SolidColorBrush(Color.Parse("#312E81"));
    private static readonly IBrush DarkFormulaForeground = new SolidColorBrush(Color.Parse("#C7D2FE"));
    private static readonly IBrush LightFormulaBackground = new SolidColorBrush(Color.Parse("#F5F3FF"));
    private static readonly IBrush DarkFormulaBackground = new SolidColorBrush(Color.Parse("#111827"));
    private static readonly IBrush LightFormulaBorder = new SolidColorBrush(Color.Parse("#C4B5FD"));
    private static readonly IBrush DarkFormulaBorder = new SolidColorBrush(Color.Parse("#6366F1"));
    private static readonly IBrush LightDiagnosticForeground = new SolidColorBrush(Color.Parse("#B42318"));
    private static readonly IBrush DarkDiagnosticForeground = new SolidColorBrush(Color.Parse("#FCA5A5"));

    public static Control CreateInlineView(MarkdownMathDocument document, MarkdownRenderContext renderContext)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(renderContext);

        var view = new Border
        {
            Background = FormulaBackground,
            BorderBrush = FormulaBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 2),
            Child = RenderExpression(document.Root, CreateContext(renderContext, MarkdownMathDisplayMode.Inline))
        };

        if (document.HasDiagnostics)
        {
            ToolTip.SetTip(view, string.Join(Environment.NewLine, document.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        }

        return view;
    }

    public static Control CreateBlockView(MarkdownMathDocument document, MarkdownRenderContext renderContext)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(renderContext);

        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new Border
                {
                    Background = FormulaBackground,
                    BorderBrush = FormulaBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16, 12),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Child = new Viewbox
                    {
                        StretchDirection = StretchDirection.DownOnly,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Child = RenderExpression(document.Root, CreateContext(renderContext, MarkdownMathDisplayMode.Block))
                    }
                }
            }
        };

        if (document.HasDiagnostics)
        {
            content.Children.Add(CreateDiagnosticsPanel(document.Diagnostics));
        }

        return content;
    }

    private static Control CreateDiagnosticsPanel(IReadOnlyList<MarkdownMathDiagnostic> diagnostics)
    {
        var panel = new StackPanel
        {
            Spacing = 2
        };

        foreach (var diagnostic in diagnostics)
        {
            panel.Children.Add(new TextBlock
            {
                Text = diagnostic.Message,
                Foreground = DiagnosticForeground,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11
            });
        }

        return panel;
    }

    private static MathRenderContext CreateContext(MarkdownRenderContext renderContext, MarkdownMathDisplayMode displayMode)
    {
        return new MathRenderContext(
            renderContext,
            displayMode,
            SystemMath.Max(renderContext.FontSize + (displayMode == MarkdownMathDisplayMode.Block ? 2 : 0), 13),
            MarkdownMathTextStyle.Normal,
            scriptDepth: 0);
    }

    private static Control RenderExpression(MarkdownMathExpression expression, MathRenderContext context)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = context.DisplayMode == MarkdownMathDisplayMode.Block ? HorizontalAlignment.Center : HorizontalAlignment.Left
        };

        foreach (var child in expression.Children)
        {
            row.Children.Add(RenderNode(child, context));
        }

        return row;
    }

    private static Control RenderNode(MarkdownMathNode node, MathRenderContext context)
    {
        return node switch
        {
            MarkdownMathExpression expression => RenderExpression(expression, context),
            MarkdownMathGroupedExpression grouped => RenderExpression(grouped.Content, context),
            MarkdownMathIdentifier identifier => CreateText(identifier.Text, context, italic: true),
            MarkdownMathNumber number => CreateText(number.Text, context),
            MarkdownMathOperator op => CreateText(op.Text, context),
            MarkdownMathSpace space => CreateSpace(space, context),
            MarkdownMathTextRun textRun => CreateTextRun(textRun, context),
            MarkdownMathSymbol symbol => CreateText(
                symbol.RenderText,
                context,
                fontSize: symbol.IsLargeOperator && context.DisplayMode == MarkdownMathDisplayMode.Block
                    ? context.FontSize * 1.2
                    : context.FontSize,
                italic: !symbol.IsLargeOperator),
            MarkdownMathCommand command => CreateText($"\\{command.Name}", context, foreground: DiagnosticForeground),
            MarkdownMathStyledExpression styled => RenderExpression(styled.Content, context.WithStyle(styled.Style)),
            MarkdownMathFraction fraction => RenderFraction(fraction, context),
            MarkdownMathRoot root => RenderRoot(root, context),
            MarkdownMathScript script => RenderScript(script, context),
            MarkdownMathDelimited delimited => RenderDelimited(delimited, context),
            MarkdownMathAccent accent => RenderAccent(accent, context),
            MarkdownMathEnvironment environment => RenderEnvironment(environment, context),
            MarkdownMathError error => CreateText(error.Text, context, foreground: DiagnosticForeground),
            _ => CreateText(node.ToString() ?? string.Empty, context)
        };
    }

    private static Control RenderFraction(MarkdownMathFraction fraction, MathRenderContext context)
    {
        var numerator = RenderExpression(fraction.Numerator, context.ForScript());
        var denominator = RenderExpression(fraction.Denominator, context.ForScript());

        return new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = numerator
                },
                new Border
                {
                    Height = 1,
                    MinWidth = SystemMath.Max(context.FontSize * 1.8, 18),
                    Background = FormulaForeground
                },
                new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = denominator
                }
            }
        };
    }

    private static Control RenderRoot(MarkdownMathRoot root, MathRenderContext context)
    {
        var radicand = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = FormulaForeground,
            Padding = new Thickness(4, 3, 1, 0),
            Child = RenderExpression(root.Radicand, context)
        };

        var radical = new TextBlock
        {
            Text = "√",
            FontFamily = MathFontFamily,
            FontSize = context.FontSize * 1.25,
            Foreground = FormulaForeground,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        var body = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                radical,
                radicand
            }
        };

        if (root.Degree is null)
        {
            return body;
        }

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        var degree = new Border
        {
            Child = RenderExpression(root.Degree, context.ForScript()),
            Margin = new Thickness(0, 0, 2, 0)
        };
        grid.Children.Add(degree);
        Grid.SetRow(body, 1);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    private static Control RenderScript(MarkdownMathScript script, MathRenderContext context)
    {
        var baseControl = RenderNode(script.Base, context);
        var scriptContext = context.ForScript();

        if (script.Base is MarkdownMathSymbol { IsLargeOperator: true } && context.DisplayMode == MarkdownMathDisplayMode.Block)
        {
            var operatorGrid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto }
                },
                HorizontalAlignment = HorizontalAlignment.Center
            };

            if (script.Superscript is not null)
            {
                var superscript = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = RenderExpression(script.Superscript, scriptContext)
                };
                operatorGrid.Children.Add(superscript);
            }

            Grid.SetRow(baseControl, 1);
            operatorGrid.Children.Add(baseControl);

            if (script.Subscript is not null)
            {
                var subscript = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = RenderExpression(script.Subscript, scriptContext)
                };
                Grid.SetRow(subscript, 2);
                operatorGrid.Children.Add(subscript);
            }

            return operatorGrid;
        }

        var scriptStack = new StackPanel
        {
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (script.Superscript is not null)
        {
            scriptStack.Children.Add(new Border
            {
                Child = RenderExpression(script.Superscript, scriptContext),
                Margin = new Thickness(0, -2, 0, 0)
            });
        }

        if (script.Subscript is not null)
        {
            scriptStack.Children.Add(new Border
            {
                Child = RenderExpression(script.Subscript, scriptContext),
                Margin = new Thickness(0, -2, 0, 0)
            });
        }

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                baseControl,
                scriptStack
            }
        };
    }

    private static Control RenderDelimited(MarkdownMathDelimited delimited, MathRenderContext context)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (!string.Equals(delimited.LeftDelimiter, ".", StringComparison.Ordinal))
        {
            row.Children.Add(CreateText(delimited.LeftDelimiter, context, fontSize: context.FontSize * 1.1));
        }

        row.Children.Add(RenderExpression(delimited.Content, context));

        if (!string.Equals(delimited.RightDelimiter, ".", StringComparison.Ordinal))
        {
            row.Children.Add(CreateText(delimited.RightDelimiter, context, fontSize: context.FontSize * 1.1));
        }

        return row;
    }

    private static Control RenderAccent(MarkdownMathAccent accent, MathRenderContext context)
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            },
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var accentControl = accent.Underline
            ? new Border
            {
                Height = 1,
                MinWidth = SystemMath.Max(context.FontSize * 0.7, 10),
                Background = FormulaForeground,
                Margin = new Thickness(0, 1, 0, 0)
            }
            : (Control)new TextBlock
            {
                Text = accent.AccentText,
                FontFamily = MathFontFamily,
                FontSize = SystemMath.Max(context.FontSize - 1, 10),
                Foreground = FormulaForeground,
                HorizontalAlignment = HorizontalAlignment.Center
            };

        var baseControl = RenderNode(accent.Base, context);
        if (accent.Underline)
        {
            Grid.SetRow(baseControl, 0);
            Grid.SetRow(accentControl, 1);
        }
        else
        {
            Grid.SetRow(baseControl, 1);
        }

        grid.Children.Add(accentControl);
        grid.Children.Add(baseControl);
        return grid;
    }

    private static Control RenderEnvironment(MarkdownMathEnvironment environment, MathRenderContext context)
    {
        var grid = new Grid
        {
            RowSpacing = 4,
            ColumnSpacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var columnCount = environment.Rows.Count == 0 ? 0 : environment.Rows.Max(static row => row.Count);
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        for (var rowIndex = 0; rowIndex < environment.Rows.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var columnIndex = 0; columnIndex < environment.Rows[rowIndex].Count; columnIndex++)
            {
                var cell = new Border
                {
                    Child = RenderExpression(environment.Rows[rowIndex][columnIndex], context),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, columnIndex);
                grid.Children.Add(cell);
            }
        }

        var (leftDelimiter, rightDelimiter) = ResolveEnvironmentDelimiters(environment.Name);
        if (leftDelimiter is null && rightDelimiter is null)
        {
            return grid;
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (!string.IsNullOrEmpty(leftDelimiter))
        {
            row.Children.Add(CreateText(leftDelimiter, context, fontSize: context.FontSize * 1.15));
        }

        row.Children.Add(grid);

        if (!string.IsNullOrEmpty(rightDelimiter))
        {
            row.Children.Add(CreateText(rightDelimiter, context, fontSize: context.FontSize * 1.15));
        }

        return row;
    }

    private static (string? Left, string? Right) ResolveEnvironmentDelimiters(string environmentName)
    {
        return environmentName switch
        {
            "pmatrix" => ("(", ")"),
            "bmatrix" => ("[", "]"),
            "Bmatrix" => ("{", "}"),
            "vmatrix" => ("|", "|"),
            "Vmatrix" => ("‖", "‖"),
            "cases" => ("{", null),
            _ => (null, null)
        };
    }

    private static Control CreateSpace(MarkdownMathSpace space, MathRenderContext context)
    {
        return new Border
        {
            Width = SystemMath.Max(space.WidthEm * context.FontSize * 0.5, 2),
            Background = Brushes.Transparent
        };
    }

    private static Control CreateTextRun(MarkdownMathTextRun textRun, MathRenderContext context)
    {
        return CreateText(
            textRun.Text,
            context.WithStyle(textRun.Style),
            italic: textRun.Style is MarkdownMathTextStyle.Normal ? false : null);
    }

    private static TextBlock CreateText(
        string text,
        MathRenderContext context,
        double? fontSize = null,
        bool? italic = null,
        IBrush? foreground = null)
    {
        var (fontFamily, fontWeight, fontStyle) = ResolveTypography(context.Style);
        var textBlock = new TextBlock
        {
            Text = text,
            FontFamily = fontFamily,
            FontSize = fontSize ?? context.FontSize,
            FontWeight = fontWeight,
            FontStyle = italic.HasValue
                ? italic.Value ? Avalonia.Media.FontStyle.Italic : Avalonia.Media.FontStyle.Normal
                : fontStyle,
            Foreground = foreground ?? FormulaForeground,
            VerticalAlignment = VerticalAlignment.Center
        };

        return textBlock;
    }

    private static (FontFamily FontFamily, FontWeight FontWeight, Avalonia.Media.FontStyle FontStyle) ResolveTypography(MarkdownMathTextStyle style)
    {
        return style switch
        {
            MarkdownMathTextStyle.Bold => (MathFontFamily, FontWeight.Bold, Avalonia.Media.FontStyle.Normal),
            MarkdownMathTextStyle.Italic => (MathFontFamily, FontWeight.Normal, Avalonia.Media.FontStyle.Italic),
            MarkdownMathTextStyle.Roman => (MathFontFamily, FontWeight.Normal, Avalonia.Media.FontStyle.Normal),
            MarkdownMathTextStyle.SansSerif => (SansSerifFamily, FontWeight.Normal, Avalonia.Media.FontStyle.Normal),
            MarkdownMathTextStyle.Monospace => (MonospaceFamily, FontWeight.Normal, Avalonia.Media.FontStyle.Normal),
            MarkdownMathTextStyle.Operator => (MathFontFamily, FontWeight.SemiBold, Avalonia.Media.FontStyle.Normal),
            _ => (MathFontFamily, FontWeight.Normal, Avalonia.Media.FontStyle.Normal)
        };
    }

    private static IBrush FormulaForeground => SelectThemeBrush(LightFormulaForeground, DarkFormulaForeground);

    private static IBrush FormulaBackground => SelectThemeBrush(LightFormulaBackground, DarkFormulaBackground);

    private static IBrush FormulaBorder => SelectThemeBrush(LightFormulaBorder, DarkFormulaBorder);

    private static IBrush DiagnosticForeground => SelectThemeBrush(LightDiagnosticForeground, DarkDiagnosticForeground);

    private static IBrush SelectThemeBrush(IBrush light, IBrush dark)
    {
        return Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? dark : light;
    }

    private sealed class MathRenderContext(
        MarkdownRenderContext markdownContext,
        MarkdownMathDisplayMode displayMode,
        double fontSize,
        MarkdownMathTextStyle style,
        int scriptDepth)
    {
        public MarkdownRenderContext MarkdownContext { get; } = markdownContext;

        public MarkdownMathDisplayMode DisplayMode { get; } = displayMode;

        public double FontSize { get; } = fontSize;

        public MarkdownMathTextStyle Style { get; } = style;

        public int ScriptDepth { get; } = scriptDepth;

        public MathRenderContext ForScript()
        {
            return new MathRenderContext(
                MarkdownContext,
                DisplayMode,
                SystemMath.Max(FontSize * 0.78, 10),
                Style,
                ScriptDepth + 1);
        }

        public MathRenderContext WithStyle(MarkdownMathTextStyle value)
        {
            return new MathRenderContext(MarkdownContext, DisplayMode, FontSize, value, ScriptDepth);
        }
    }

}
