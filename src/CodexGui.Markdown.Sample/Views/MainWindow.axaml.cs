using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.Folding;
using AvaloniaEdit.TextMate;
using CodexGui.Markdown.Controls;
using CodexGui.Markdown.Plugin.Alerts;
using CodexGui.Markdown.Plugin.CustomContainers;
using CodexGui.Markdown.Plugin.DefinitionLists;
using CodexGui.Markdown.Plugin.Figures;
using CodexGui.Markdown.Plugin.Footers;
using CodexGui.Markdown.Plugin.Math;
using CodexGui.Markdown.Plugin.Mermaid;
using CodexGui.Markdown.Plugin.SyntaxHighlighting;
using CodexGui.Markdown.Plugin.TextMate;
using CodexGui.Markdown.Sample.Controls;
using CodexGui.Markdown.Services;
using TextMateSharp.Grammars;

namespace CodexGui.Markdown.Sample.Views;

public partial class MainWindow : Window
{
    private static readonly IMarkdownPlugin[] PreviewPlugins =
    [
        new AlertsMarkdownPlugin(),
        new CustomContainersMarkdownPlugin(),
        new DefinitionListMarkdownPlugin(),
        new FiguresMarkdownPlugin(),
        new FootersMarkdownPlugin(),
        new MathMarkdownPlugin(),
        new MermaidMarkdownPlugin(),
        new SyntaxHighlightingMarkdownPlugin(),
        new TextMateMarkdownPlugin()
    ];

    private static readonly IMarkdownRenderController PreviewRenderController = MarkdownRenderingServices.CreateController(PreviewPlugins);
    private static readonly IMarkdownEditingService PreviewEditingService = MarkdownRenderingServices.CreateEditingService(PreviewPlugins);
    private static readonly IReadOnlyList<CodeEditorOption> CodeEditorOptions =
    [
        new("Built-in markdown code editor", MarkdownBuiltInEditorIds.Code),
        new("TextMate code editor", TextMateMarkdownPlugin.TextMateCodeEditorId)
    ];

    private static readonly FilePickerFileType MarkdownFileType = new("Markdown files")
    {
        Patterns = ["*.md", "*.markdown", "*.mdown", "*.mkd"],
        MimeTypes = ["text/markdown", "text/plain"]
    };

    private string? _currentFilePath;
    private readonly FoldingManager _foldingManager;
    private readonly DispatcherTimer _foldingTimer;
    private readonly TextMate.Installation _textMateInstallation;
    private readonly EditorHoverHighlightRenderer _editorHoverHighlightRenderer = new();
    private MarkdownSourceSpan _hoveredSourceSpan = MarkdownSourceSpan.Empty;
    private bool _isSyncingFromPreviewEdit;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureNativeMenu();

        Editor.TextArea.Options.IndentationSize = 2;
        Editor.TextArea.Options.ConvertTabsToSpaces = true;
        Editor.TextChanged += OnEditorTextChanged;
        Editor.TextArea.TextView.BackgroundRenderers.Add(_editorHoverHighlightRenderer);

        _textMateInstallation = ConfigureTextMate(Editor);
        _foldingManager = FoldingManager.Install(Editor.TextArea);
        _foldingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _foldingTimer.Tick += OnFoldingTick;

        Preview.RenderController = PreviewRenderController;
        Preview.EditingService = PreviewEditingService;
        Preview.EditorPreferences = new MarkdownEditorPreferences()
            .PreferEditor(MarkdownEditorFeature.Code, TextMateMarkdownPlugin.TextMateCodeEditorId);
        Preview.MarkdownEdited += OnPreviewMarkdownEdited;

        CodeEditorSelector.ItemsSource = CodeEditorOptions;
        CodeEditorSelector.SelectedItem = CodeEditorOptions[1];
        CodeEditorSelector.SelectionChanged += OnCodeEditorSelectionChanged;
        PreviewEditToggle.IsCheckedChanged += OnPreviewEditToggleChanged;

        PreviewHost.AddHandler(InputElement.PointerMovedEvent, OnPreviewHostPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        PreviewHost.AddHandler(InputElement.PointerPressedEvent, OnPreviewHostPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        PreviewHost.PointerExited += OnPreviewHostPointerExited;
        Editor.Text = DefaultMarkdownDocument;
        UpdatePreviewBaseUri();
        Preview.Markdown = Editor.Text;
        RefreshFoldings();
        UpdateWindowTitle();

        Closed += OnWindowClosed;
    }

    private static TextMate.Installation ConfigureTextMate(AvaloniaEdit.TextEditor editor)
    {
        var registryOptions = new RegistryOptions(ThemeName.LightPlus);
        var installation = editor.InstallTextMate(registryOptions);
        var language = registryOptions.GetLanguageByExtension(".md");
        if (language is not null)
        {
            installation.SetGrammar(registryOptions.GetScopeByLanguageId(language.Id));
        }

        return installation;
    }

    private void ConfigureNativeMenu()
    {
        var commandKey = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        var newItem = new NativeMenuItem("New")
        {
            Gesture = new KeyGesture(Key.N, commandKey)
        };
        newItem.Click += (_, _) => NewDocument();

        var openItem = new NativeMenuItem("Open…")
        {
            Gesture = new KeyGesture(Key.O, commandKey)
        };
        openItem.Click += async (_, _) => await OpenDocumentAsync();

        var saveItem = new NativeMenuItem("Save")
        {
            Gesture = new KeyGesture(Key.S, commandKey)
        };
        saveItem.Click += async (_, _) => await SaveDocumentAsync(saveAs: false);

        var saveAsItem = new NativeMenuItem("Save As…")
        {
            Gesture = new KeyGesture(Key.S, commandKey | KeyModifiers.Shift)
        };
        saveAsItem.Click += async (_, _) => await SaveDocumentAsync(saveAs: true);

        var fileMenu = new NativeMenu();
        fileMenu.Add(newItem);
        fileMenu.Add(openItem);
        fileMenu.Add(saveItem);
        fileMenu.Add(saveAsItem);

        var fileRoot = new NativeMenuItem("File")
        {
            Menu = fileMenu
        };

        var rootMenu = new NativeMenu();
        rootMenu.Add(fileRoot);
        NativeMenu.SetMenu(this, rootMenu);
    }

    private void OnEditorTextChanged(object? sender, EventArgs eventArgs)
    {
        if (_isSyncingFromPreviewEdit)
        {
            _foldingTimer.Stop();
            _foldingTimer.Start();
            return;
        }

        Preview.Markdown = Editor.Text;
        ClearPreviewHover();
        _foldingTimer.Stop();
        _foldingTimer.Start();
    }

    private void OnPreviewHostPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        UpdatePreviewHover(eventArgs.GetPosition(Preview));
    }

    private void OnPreviewHostPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (!eventArgs.GetCurrentPoint(PreviewHost).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (Preview.ActiveEditorSession is not null)
        {
            return;
        }

        var point = eventArgs.GetPosition(Preview);
        if (Preview.IsEditingEnabled && Preview.TryBeginEdit(point))
        {
            ClearPreviewHover();
            eventArgs.Handled = true;
            return;
        }

        if (Preview.HitTestMarkdown(point) is { } hit)
        {
            RevealEditorSourceSpan(hit.SourceSpan);
        }
    }

    private void OnPreviewHostPointerExited(object? sender, PointerEventArgs eventArgs)
    {
        ClearPreviewHover();
    }

    private void OnFoldingTick(object? sender, EventArgs eventArgs)
    {
        _foldingTimer.Stop();
        RefreshFoldings();
    }

    private void RefreshFoldings()
    {
        var foldings = BuildFoldings(Editor.Text ?? string.Empty);
        _foldingManager.UpdateFoldings(foldings, -1);
    }

    private static List<NewFolding> BuildFoldings(string text)
    {
        var foldings = new List<NewFolding>();
        var headingStack = new Stack<HeadingBlockState>();
        FenceBlockState? activeFence = null;

        foreach (var line in EnumerateLines(text))
        {
            var trimmed = line.Text.TrimStart();
            if (TryParseFence(trimmed, out var marker, out var markerLength))
            {
                if (activeFence is null)
                {
                    activeFence = new FenceBlockState(marker, markerLength, line.StartOffset);
                }
                else if (activeFence.Value.Marker == marker && markerLength >= activeFence.Value.MarkerLength)
                {
                    var startOffset = activeFence.Value.StartOffset;
                    var endOffset = line.EndOffset;
                    if (endOffset > startOffset + 1)
                    {
                        foldings.Add(new NewFolding(startOffset, endOffset) { Name = "``` code block" });
                    }

                    activeFence = null;
                }

                continue;
            }

            if (activeFence is not null)
            {
                continue;
            }

            if (!TryParseHeading(trimmed, out var level, out var headingTitle))
            {
                continue;
            }

            while (headingStack.TryPeek(out var previous) && previous.Level >= level)
            {
                headingStack.Pop();
                AddHeadingFolding(foldings, previous, line.StartOffset);
            }

            headingStack.Push(new HeadingBlockState(level, line.EndOffset, headingTitle));
        }

        if (activeFence is not null && text.Length > activeFence.Value.StartOffset + 1)
        {
            foldings.Add(new NewFolding(activeFence.Value.StartOffset, text.Length) { Name = "``` code block" });
        }

        while (headingStack.TryPop(out var pendingHeading))
        {
            AddHeadingFolding(foldings, pendingHeading, text.Length);
        }

        foldings.Sort(static (left, right) => left.StartOffset.CompareTo(right.StartOffset));
        return foldings;
    }

    private static void AddHeadingFolding(List<NewFolding> foldings, HeadingBlockState heading, int endOffset)
    {
        if (endOffset <= heading.ContentStartOffset + 1)
        {
            return;
        }

        foldings.Add(new NewFolding(heading.ContentStartOffset, endOffset)
        {
            Name = string.IsNullOrWhiteSpace(heading.Title) ? "# section" : heading.Title
        });
    }

    private static List<LineRange> EnumerateLines(string text)
    {
        var lines = new List<LineRange>();
        var lineStart = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch is not '\r' and not '\n')
            {
                continue;
            }

            lines.Add(new LineRange(lineStart, index, text[lineStart..index]));

            if (ch == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                index++;
            }

            lineStart = index + 1;
        }

        lines.Add(new LineRange(lineStart, text.Length, text[lineStart..]));
        return lines;
    }

    private static bool TryParseHeading(string line, out int level, out string title)
    {
        level = 0;
        title = string.Empty;
        if (string.IsNullOrWhiteSpace(line) || line[0] != '#')
        {
            return false;
        }

        var markerCount = 0;
        while (markerCount < line.Length && line[markerCount] == '#')
        {
            markerCount++;
        }

        if (markerCount is <= 0 or > 6 || markerCount >= line.Length || !char.IsWhiteSpace(line[markerCount]))
        {
            return false;
        }

        level = markerCount;
        title = line[markerCount..].Trim();
        return true;
    }

    private static bool TryParseFence(string line, out char marker, out int markerLength)
    {
        marker = default;
        markerLength = 0;

        if (line.Length < 3)
        {
            return false;
        }

        var first = line[0];
        if (first is not '`' and not '~')
        {
            return false;
        }

        var count = 0;
        while (count < line.Length && line[count] == first)
        {
            count++;
        }

        if (count < 3)
        {
            return false;
        }

        marker = first;
        markerLength = count;
        return true;
    }

    private void NewDocument()
    {
        _currentFilePath = null;
        UpdatePreviewBaseUri();
        Editor.Text = DefaultMarkdownDocument;
        UpdateWindowTitle();
    }

    private async Task OpenDocumentAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Open markdown file",
            FileTypeFilter = [MarkdownFileType]
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        var path = file.Path.LocalPath;
        var markdown = await File.ReadAllTextAsync(path);
        _currentFilePath = path;
        UpdatePreviewBaseUri();
        Editor.Text = markdown;
        UpdateWindowTitle();
    }

    private async Task SaveDocumentAsync(bool saveAs)
    {
        if (!saveAs && !string.IsNullOrWhiteSpace(_currentFilePath))
        {
            await File.WriteAllTextAsync(_currentFilePath, Editor.Text ?? string.Empty);
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save markdown file",
            SuggestedFileName = string.IsNullOrWhiteSpace(_currentFilePath) ? "untitled.md" : Path.GetFileName(_currentFilePath),
            DefaultExtension = "md",
            ShowOverwritePrompt = true,
            FileTypeChoices = [MarkdownFileType]
        });

        if (file is null)
        {
            return;
        }

        var path = file.Path.LocalPath;
        await File.WriteAllTextAsync(path, Editor.Text ?? string.Empty);
        _currentFilePath = path;
        UpdatePreviewBaseUri();
        UpdateWindowTitle();
    }

    private void UpdatePreviewBaseUri()
    {
        Preview.BaseUri = string.IsNullOrWhiteSpace(_currentFilePath)
            ? null
            : CreateDirectoryUri(Path.GetDirectoryName(_currentFilePath));
    }

    private static Uri? CreateDirectoryUri(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        var normalizedPath =
            directoryPath.EndsWith(Path.DirectorySeparatorChar) || directoryPath.EndsWith(Path.AltDirectorySeparatorChar)
                ? directoryPath
                : directoryPath + Path.DirectorySeparatorChar;

        return new Uri(normalizedPath);
    }

    private void UpdateWindowTitle()
    {
        var fileLabel = string.IsNullOrWhiteSpace(_currentFilePath) ? "Untitled.md" : Path.GetFileName(_currentFilePath);
        Title = $"CodexGui Markdown Sample — {fileLabel}";
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        _foldingTimer.Stop();
        ClearPreviewHover();
        Editor.TextArea.TextView.BackgroundRenderers.Remove(_editorHoverHighlightRenderer);
        _textMateInstallation.Dispose();
        FoldingManager.Uninstall(_foldingManager);
        Closed -= OnWindowClosed;
    }

    private void UpdatePreviewHover(Point point)
    {
        if (Preview.ActiveEditorSession is not null)
        {
            ClearPreviewHover();
            return;
        }

        if (!new Rect(Preview.Bounds.Size).Contains(point))
        {
            ClearPreviewHover();
            return;
        }

        if (Preview.HitTestMarkdown(point) is not { } hit)
        {
            ClearPreviewHover();
            return;
        }

        PreviewOverlay.HighlightRects = hit.HighlightRects;
        ApplyEditorHover(hit.SourceSpan);
    }

    private void ClearPreviewHover()
    {
        PreviewOverlay.HighlightRects = Array.Empty<Rect>();
        ApplyEditorHover(MarkdownSourceSpan.Empty);
    }

    private void ApplyEditorHover(MarkdownSourceSpan sourceSpan)
    {
        if (_hoveredSourceSpan == sourceSpan)
        {
            return;
        }

        _hoveredSourceSpan = sourceSpan;
        if (!TryResolveEditorRange(sourceSpan, out var start, out var length))
        {
            _editorHoverHighlightRenderer.Clear(Editor.TextArea.TextView);
            return;
        }

        _editorHoverHighlightRenderer.SetHighlight(Editor.TextArea.TextView, start, length);
    }

    private void OnPreviewEditToggleChanged(object? sender, RoutedEventArgs eventArgs)
    {
        Preview.IsEditingEnabled = PreviewEditToggle.IsChecked == true;
        if (!Preview.IsEditingEnabled)
        {
            ClearPreviewHover();
        }
    }

    private void OnCodeEditorSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (CodeEditorSelector.SelectedItem is not CodeEditorOption option)
        {
            return;
        }

        Preview.EditorPreferences = new MarkdownEditorPreferences()
            .PreferEditor(MarkdownEditorFeature.Code, option.EditorId);
    }

    private void OnPreviewMarkdownEdited(object? sender, MarkdownEditedEventArgs eventArgs)
    {
        if (string.Equals(Editor.Text, eventArgs.UpdatedMarkdown, StringComparison.Ordinal))
        {
            return;
        }

        _isSyncingFromPreviewEdit = true;
        try
        {
            Editor.Text = eventArgs.UpdatedMarkdown;
        }
        finally
        {
            _isSyncingFromPreviewEdit = false;
        }

        ClearPreviewHover();
        RefreshFoldings();
        RevealEditorRange(eventArgs.RevealStart, eventArgs.RevealLength);
    }

    private void RevealEditorSourceSpan(MarkdownSourceSpan sourceSpan)
    {
        if (!TryResolveEditorRange(sourceSpan, out var start, out var length))
        {
            return;
        }

        RevealEditorRange(start, length);
    }

    private void RevealEditorRange(int start, int length)
    {
        var documentLength = Editor.Document?.TextLength ?? (Editor.Text?.Length ?? 0);
        if (documentLength < 0)
        {
            return;
        }

        start = Math.Clamp(start, 0, documentLength);
        length = Math.Clamp(length, 0, Math.Max(documentLength - start, 0));

        Editor.Select(start, length);
        Editor.CaretOffset = start;

        if (Editor.Document is null || documentLength == 0)
        {
            return;
        }

        var location = Editor.Document.GetLocation(Math.Min(start, documentLength));
        Editor.ScrollTo(location.Line, location.Column);
    }

    private bool TryResolveEditorRange(MarkdownSourceSpan sourceSpan, out int start, out int length)
    {
        start = 0;
        length = 0;

        var documentLength = Editor.Document?.TextLength ?? (Editor.Text?.Length ?? 0);
        if (sourceSpan.IsEmpty || documentLength <= 0)
        {
            return false;
        }

        if (sourceSpan.Start >= documentLength)
        {
            return false;
        }

        start = Math.Max(sourceSpan.Start, 0);
        length = Math.Min(sourceSpan.Length, documentLength - start);
        return length > 0;
    }

    private readonly record struct LineRange(int StartOffset, int EndOffset, string Text);
    private readonly record struct HeadingBlockState(int Level, int ContentStartOffset, string Title);
    private readonly record struct FenceBlockState(char Marker, int MarkerLength, int StartOffset);
    private sealed record CodeEditorOption(string Label, string EditorId)
    {
        public override string ToString() => Label;
    }

    private const string DefaultMarkdownDocument =
        """
        ---
        title: CodexGui Markdown Control Showcase
        owner: preview-team
        summary: Rich markdown rendering, editing, and metadata support in Avalonia.
        ---

        # CodexGui Markdown Control Showcase

        Edit markdown on the **left** and preview the rendered output on the **right**.

        ## Features in this sample

        - Live markdown preview updates as you type.
        - TextMate-based syntax highlighting in AvaloniaEdit.
        - Markdown foldings for headings and fenced code blocks.
        - Plugin-based preview rendering for Mermaid diagrams, alert callouts, built-in syntax highlighting, and code fences.
        - AST-backed rendering for alerts, figures, custom containers, definition lists, footers, and math plus native abbreviations.
        - Optional in-place preview editing for alerts, figures, custom containers, footers, paragraphs, headings, lists, tables, code blocks, Mermaid diagrams, math nodes, footnotes, abbreviations, link references, and YAML front matter.
        - In-place text editing for paragraphs, headings, and list items with one editor surface per block plus formatting actions.
        - Emoji shortcodes, smart punctuation, marked text, and inserted text now render with dedicated inline styling.
        - Insert new markdown blocks before or after the active block while editing in the preview.
        - Native `File` menu with New/Open/Save/Save As.

        ## Links and footnotes

        Read the [Avalonia documentation][avalonia-docs] or the [Markdig repository](https://github.com/xoofx/markdig) for parser details.[^rendering]

        [^rendering]: Footnotes render as numbered references and are collected at the end of the document.

        [avalonia-docs]: https://docs.avaloniaui.net "Avalonia documentation"

        ## Images

        Images render inline when their source can be resolved from an absolute URL, a relative path backed by `BaseUri`, or a base64 `data:` URI.

        ## Figures

        ^^^ Preview surface
        Supporting markdown can live inside a figure body without getting mixed into the opening or closing fence captions.

        - lead captions behave like a figure title
        - closing captions behave like supporting notes

        ```json
        {
          "feature": "figures",
          "mode": "plugin-backed"
        }
        ```
        ^^^ Figure captions now edit independently from the body without manually rewriting the `^^^` fence lines.

        ## Alerts and custom containers

        > [!IMPORTANT]
        > Alert blocks now flow through a dedicated AST plugin that keeps the alert kind and quoted body markdown in sync with preview editing.
        >
        > - switch between note, tip, warning, or danger in the structured editor
        > - keep nested markdown body content intact

        > [!TIP]
        > Use preview editing on this callout to change the alert kind without manually rewriting the `> [!...]` header or each quote marker.

        :::info Renderer status
        Custom containers now flow through a dedicated plugin that keeps the type token, optional arguments, and markdown body separated in the editor surface.
        :::

        :::warning Migration note
        Mermaid-specific custom containers still belong to the Mermaid plugin, while generic documentation callouts can stay here.
        :::

        ## Footer

        ^^ Footer blocks now route through a dedicated plugin and keep the `^^` source prefix out of the editor surface.
        ^^ Use them for closing notes, provenance, or lightweight release context.

        ## Definitions, abbreviations, and math

        Rendering engine
        :   The renderer maps `Markdig` extension nodes to native Avalonia surfaces instead of losing their semantics.
        :   Definitions can span multiple paragraphs or nested blocks.

            - glossary layouts now come from a dedicated definition-list AST plugin
            - preview editing can add or remove full entries

        AST
        Abstract syntax tree
        :   A structured model used for rich rendering and editing.

        Fluent glossary
        Reference rail
        :   Definition-list entries render as a two-column glossary surface instead of plain indented text.

            ```json
            {
              "feature": "definition-lists",
              "mode": "plugin-backed"
            }
            ```

        *[AST]: Abstract syntax tree

        The markdown AST keeps inline abbreviations like AST, superscript like x^^2^^, subscript like H~2~O, and inline math such as $a^2 + b^2 = c^2$, $\frac{\alpha + \beta}{2}$, and $\hat{x}_{n+1} = \sqrt{1 + x_n^2}$ readable in-flow.

        ## Typography and inline extras

        Emoji shortcodes like :sparkles: and smileys like :-) now render inline.

        Smart punctuation turns "quoted copy" into typographic quotes and converts long-form dashes -- like this -- when SmartyPants is enabled.

        ==Marked text== and ++inserted text++ now keep distinct inline styling instead of falling back to plain bold emphasis.

        $$
        \int_0^1 x^2\,dx = \frac{1}{3}
        $$

        $$
        \begin{aligned}
        f(x) &= \frac{1}{\sqrt{2\pi\sigma^2}} e^{-\frac{(x - \mu)^2}{2\sigma^2}} \\
        \nabla \cdot \vec{F} &= \frac{\partial P}{\partial x} + \frac{\partial Q}{\partial y} + \frac{\partial R}{\partial z}
        \end{aligned}
        $$

        $$
        \begin{bmatrix}
        1 & 2 & 3 \\
        4 & 5 & 6 \\
        7 & 8 & 9
        \end{bmatrix}
        \cdot
        \begin{bmatrix}
        x \\
        y \\
        z
        \end{bmatrix}
        =
        \begin{bmatrix}
        14 \\
        32 \\
        50
        \end{bmatrix}
        $$

        ## Mermaid diagram

        :::mermaid
        flowchart LR
            Editor[Editor surface]
            Parser[Parser plugins]
            Renderer[Renderer plugins]
            Preview[Rich preview]

            Editor --> Parser --> Renderer --> Preview
        :::

        ## Mermaid fenced syntax

        ```mermaid
        sequenceDiagram
            participant User
            participant Parser
            participant Preview

            User->>Parser: Edit markdown
            Parser->>Preview: Emit Mermaid block
            Preview-->>User: Render diagram surface
        ```

        ## Mermaid state diagram

        ```mermaid
        stateDiagram-v2
            [*] --> Idle
            Idle --> Editing: Type markdown
            Editing --> Previewing: Rebuild preview
            Previewing --> Idle: Await next change
            Previewing --> [*]: Close sample
        ```

        ## Mermaid class diagram

        ```mermaid
        classDiagram
            class MarkdownTextBlock {
                +Markdown: string
                +BaseUri: Uri
                +RenderController
                +RebuildMarkdown()
            }

            class MarkdownRenderController {
                +Render(request)
            }

            class MermaidMarkdownPlugin {
                +Register(registry)
            }

            MarkdownTextBlock --> MarkdownRenderController : delegates rendering
            MermaidMarkdownPlugin ..> MarkdownRenderController : extends pipeline
        ```

        ## Mermaid ER diagram

        ```mermaid
        erDiagram
            CUSTOMER ||--o{ ORDER : places
            ORDER ||--|{ ORDER_ITEM : contains
            PRODUCT ||--o{ ORDER_ITEM : appears_in

            CUSTOMER {
                string id PK
                string email UK
            }

            ORDER {
                string id PK
                date created_at
                string status
            }

            PRODUCT {
                string sku PK
                string display_name
            }
        ```

        ## Mermaid pie chart

        ```mermaid
        pie showData
            title Native Mermaid coverage
            "Core diagrams" : 4
            "New native diagrams" : 6
            "Markdown editors" : 2
        ```

        ## Mermaid user journey

        ```mermaid
        journey
            title Preview editing workflow
            section Compose
              Write markdown: 5: User
              Insert diagram block: 4: User, Editor
            section Review
              Regenerate preview: 5: Parser, Preview
              Apply inline edit: 4: User, Preview
        ```

        ## Mermaid timeline

        ```mermaid
        timeline
            title Mermaid native renderer milestones
            section Existing support
              Initial rollout : Flowchart : Sequence
              Native states : State diagram
              Native relationships : Class diagram
            section Current parity work
              Parser preprocessing : Directives : Frontmatter
              Diagram expansion : ER : Pie : Journey : Timeline : Quadrant : Mindmap
        ```

        ## Mermaid quadrant chart

        ```mermaid
        quadrantChart
            title Mermaid implementation priorities
            x-axis Low impact --> High impact
            y-axis Low effort --> High effort
            quadrant-1 Ship next
            quadrant-2 Prototype
            quadrant-3 Skip
            quadrant-4 Plan carefully
            Parser preprocessing: [0.78, 0.82]
            Pie chart: [0.72, 0.70]
            Gantt chart: [0.66, 0.24]
            Mind map: [0.58, 0.64]
        ```

        ## Mermaid mind map

        ```mermaid
        mindmap
            Mermaid
                Native parser
                    Flowchart
                    Sequence
                    State
                    Class
                    ER
                    Pie
                    Journey
                    Timeline
                    Quadrant
                    Mind map
                Preview editor
                    Live updates
                    Inline editing
        ```

        ## Code fence

        ```csharp
        // Rich code fences now render through the TextMate plugin when a grammar is available.
        var state = new Dictionary<string, int>
        {
            ["mermaid-diagrams"] = 1,
            ["preview-surface"] = 1,
            ["tables"] = 2,
            ["code-fences"] = 3
        };

        Console.WriteLine($"Active markdown features: {string.Join(", ", state.Keys)}");
        ```

        ```postgresql
        -- When TextMate does not claim the alias, the built-in syntax-highlighting plugin still renders semantic tokens.
        select feature_name, status
        from markdown_features
        where renderer = 'plugin-backed'
        order by feature_name;
        ```

        ## Table

        | Feature         | Status | Notes |
        | :-------------- | :----: | ----: |
        | Preview surface | Ready  | Rich blocks stay constrained to the available width. |
        | Table layout    | Ready  | Alignment follows markdown while cell content wraps cleanly. |
        | Advanced nodes  | Ready  | Alerts, figures, containers, footers, definitions, abbreviations, footnotes, link references, YAML metadata, math, superscript, subscript, emoji, smart punctuation, mark, and insert now render natively. |
        | Mermaid plugin  | Ready  | Flowchart, sequence, state, class, ER, pie, journey, timeline, quadrant, and mind map diagrams render natively. |
        | Code fences     | Ready  | TextMate grammars are used before the built-in highlighter fallback. |
        """;
}
