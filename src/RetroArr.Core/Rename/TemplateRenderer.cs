using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RetroArr.Core.Rename
{
    // Pure-function template engine. No IO, no DB. Renders a filename
    // template against a variable map, drops empty-variable artefacts,
    // sanitizes for filesystem, and clamps total length.
    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
    public sealed class TemplateRenderer
    {
        // Tokens callers may use. Anything else in a template stays literal
        // (so users typing {oops} get back "{oops}" rather than crashing).
        public static readonly IReadOnlyCollection<string> KnownTokens = new[]
        {
            "Title", "Year", "Platform", "Version", "ContentName",
            "ReleaseGroup", "Region", "Languages", "Revision", "Edition", "Disc"
        };

        private static readonly Regex TokenRegex = new(@"\{([A-Za-z]+)\}", RegexOptions.Compiled);

        // An empty token is marked, a filled one is fenced in, so the cleanup below only ever takes the
        // template's own separators next to an empty token and never a dash of the title ("-Zero-").
        private const char Empty = '\uE000';
        private const char Fence = '\uE001';

        private const string EmptyInside = @"[\s,;+\-\uE000]*\uE000[\s,;+\-\uE000]*";
        private static readonly Regex EmptyBracketRegex = new(
            @"\s*(\(" + EmptyInside + @"\)|\[" + EmptyInside + @"\]|\{" + EmptyInside + @"\})", RegexOptions.Compiled);

        private static readonly Regex EmptyRunRegex = new(@"[\s-]*(?:\uE000[\s-]*)+", RegexOptions.Compiled);

        private static readonly Regex DashRunRegex = new(@"-[\s-]*-", RegexOptions.Compiled);

        // Reserved filename stems on Windows. The renamer prefixes an
        // underscore if it would otherwise produce one of these.
        private static readonly HashSet<string> WindowsReservedStems = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
            "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
        };

        // Windows MAX_PATH minus a buffer for the directory portion the
        // caller still has to prepend. Picked to leave space for a typical
        // library root like "D:\Library\windows\".
        public const int MaxStemLength = 200;

        public string RenderStem(string template, IReadOnlyDictionary<string, string?> variables)
        {
            if (string.IsNullOrWhiteSpace(template)) template = "{Title}";

            // 1. Substitute tokens. Unknown tokens stay as-is so the user
            // can SEE them in the preview rather than getting a silent drop.
            var substituted = TokenRegex.Replace(template, m =>
            {
                var key = m.Groups[1].Value;
                if (variables.TryGetValue(key, out var v))
                {
                    return string.IsNullOrEmpty(v) ? Empty.ToString() : Fence + v + Fence;
                }
                return m.Value;
            });

            // An empty token leaves "()" or "[]" behind: "{Title} ({Region})" with no region is just the title.
            substituted = EmptyBracketRegex.Replace(substituted, Empty.ToString());

            // 2. The separators next to an empty token go at either end and collapse to one in
            // between: "{Title} - DLC - {ContentName}" with ContentName="" becomes
            // "Chrono Trigger - DLC", "{Title} - {Region} - {Version}" "Chrono Trigger - 1.02".
            var marked = substituted;
            substituted = EmptyRunRegex.Replace(marked, m =>
                m.Index == 0 || m.Index + m.Length == marked.Length ? "" : DashRunRegex.Replace(m.Value.Replace(Empty.ToString(), ""), "-"));
            substituted = substituted.Replace(Fence.ToString(), "");

            // 3. Collapse runs of whitespace, trim ends.
            substituted = Regex.Replace(substituted, @"\s{2,}", " ").Trim();

            // 4. Filesystem-safe sanitisation. Invalid chars get stripped,
            // path separators get replaced with " - " to keep the stem flat.
            substituted = SanitizeStem(substituted);

            if (string.IsNullOrWhiteSpace(substituted))
            {
                substituted = "untitled";
            }

            // 5. Avoid the Windows reserved-name trap.
            if (WindowsReservedStems.Contains(substituted))
            {
                substituted = "_" + substituted;
            }

            // 6. Length clamp.
            if (substituted.Length > MaxStemLength)
            {
                substituted = substituted.Substring(0, MaxStemLength).TrimEnd();
            }

            return substituted;
        }

        // Cheap preview pass that does NOT touch the filesystem and is
        // safe to call from a UI input handler on every keystroke.
        public string Preview(string template, IReadOnlyDictionary<string, string?> variables, string extension)
        {
            var stem = RenderStem(template, variables);
            var ext = string.IsNullOrWhiteSpace(extension) ? string.Empty :
                      (extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension);
            return stem + ext;
        }

        private static string SanitizeStem(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            // Cross-platform-safe: always strips the Windows-reserved set (: * ? " < > |
            // etc.), which Path.GetInvalidFileNameChars() does NOT cover on Linux.
            return RetroArr.Core.IO.FileNameSanitizer.Sanitize(input, string.Empty);
        }
    }
}
