using Avalonia.Media;

namespace ProMarkdown.Controls;

internal static class MarkdownCodeCopyIcons
{
    internal const double Size = 16;

    // Microsoft Fluent UI System Icons (MIT); see Assets/FluentSystemIcons.LICENSE.txt.
    // https://github.com/microsoft/fluentui-system-icons/tree/main/assets/Copy/SVG
    internal static readonly Geometry Copy = StreamGeometry.Parse(
        "M5 6H3.5C2.67157 6 2 6.67157 2 7.5V12.5C2 13.3284 2.67157 14 3.5 14H7.5C8.32843 14 9 13.3284 9 12.5V12H10V12.5C10 13.8807 8.88071 15 7.5 15H3.5C2.11929 15 1 13.8807 1 12.5V7.5C1 6.11929 2.11929 5 3.5 5H5V6ZM12.5 1C13.8807 1 15 2.11929 15 3.5V8.5C15 9.88071 13.8807 11 12.5 11H8.5C7.11929 11 6 9.88071 6 8.5V3.5C6 2.11929 7.11929 1 8.5 1H12.5ZM8.5 2C7.67157 2 7 2.67157 7 3.5V8.5C7 9.32843 7.67157 10 8.5 10H12.5C13.3284 10 14 9.32843 14 8.5V3.5C14 2.67157 13.3284 2 12.5 2H8.5Z");

    // https://github.com/microsoft/fluentui-system-icons/tree/main/assets/Checkmark/SVG
    internal static readonly Geometry Checkmark = StreamGeometry.Parse(
        "M13.8639 3.65511C14.0533 3.85606 14.0439 4.17251 13.8429 4.36191L5.91309 11.8358C5.67573 12.0595 5.30311 12.0526 5.07417 11.8203L2.39384 9.09995C2.20003 8.90325 2.20237 8.58667 2.39907 8.39286C2.59578 8.19905 2.91235 8.2014 3.10616 8.3981L5.51192 10.8398L13.1571 3.63419C13.358 3.44479 13.6745 3.45416 13.8639 3.65511Z");
}
