#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ObFramework.Editor.Core
{
    /// <summary>One generated binding attribute + serialized field declaration found in an existing window source.</summary>
    internal sealed class BindingDeclaration
    {
        public string FieldName = string.Empty;
        public string ViewTypeText = string.Empty;
        public string MemberName = string.Empty;
        public bool IsCommand;
        public int Start;
        public int End;
    }

    /// <summary>
    /// Conservative, dependency-free C# text helpers for incremental regeneration: locate a class
    /// body, collect already declared member names and parse generated binding field declarations.
    /// <para>
    /// The merge strategy is fail-safe: whenever the existing source cannot be understood, the
    /// generators report an error instead of editing it. New declarations are only ever inserted
    /// before the class closing brace; user-authored lines are never rewritten.
    /// </para>
    /// </summary>
    internal static class SourceTextMerger
    {
        private const string TypePattern =
            @"(?:global::)?[A-Za-z_][A-Za-z0-9_]*(?:(?:\.|::)[A-Za-z_][A-Za-z0-9_]*)*(?:\s*<[^;{}()]*?>)?(?:\s*\[\s*\])?\??";

        private const string ModifiersPattern =
            @"(?:(?:static|readonly|volatile|new|const|async|override|virtual|sealed|partial|extern|unsafe)\s+)*";

        private static readonly Regex FieldRegex = new Regex(
            @"\b(?:private|protected|public|internal)\s+" + ModifiersPattern + TypePattern +
            @"\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:=[^;{}]*)?;",
            RegexOptions.CultureInvariant);

        private static readonly Regex MemberRegex = new Regex(
            @"\b(?:private|protected|public|internal)\s+" + ModifiersPattern + TypePattern +
            @"\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\(|=>|\{)",
            RegexOptions.CultureInvariant);

        private static readonly Regex BindingAttributeRegex = new Regex(
            @"\[\s*(?:global::)?(?:[A-Za-z_][A-Za-z0-9_]*\.)*(?<kind>UIBindData|UIBindCommand)(?:Attribute)?\s*\(\s*nameof\s*\(\s*(?<target>[^()]+?)\s*\)",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Finds the body range of <c>partial class &lt;className&gt;</c>. Returns false with a
        /// human readable reason when the type is missing, not partial, or has unbalanced braces.
        /// </summary>
        public static bool TryFindClassBody(
            string source,
            string className,
            out int bodyStart,
            out int bodyEnd,
            out string? error)
        {
            bodyStart = -1;
            bodyEnd = -1;
            error = null;

            if (string.IsNullOrWhiteSpace(source))
            {
                error = "The existing source is empty.";
                return false;
            }

            var escaped = Regex.Escape(className);
            var partial = Regex.Match(source, @"\bpartial\s+class\s+" + escaped + @"\b", RegexOptions.CultureInvariant);
            if (!partial.Success)
            {
                error = Regex.IsMatch(source, @"\bclass\s+" + escaped + @"\b", RegexOptions.CultureInvariant)
                    ? "Type '" + className + "' is declared but not as a partial class. Add the 'partial' modifier so generated declarations can be appended safely."
                    : "Type '" + className + "' was not found in the existing source. Refusing to modify the file.";
                return false;
            }

            var open = source.IndexOf('{', partial.Index + partial.Length);
            if (open < 0)
            {
                error = "Could not find the body of class '" + className + "'.";
                return false;
            }

            if (!TryFindMatchingBrace(source, open, out var close))
            {
                error = "Could not match the braces of class '" + className + "'. Fix the existing source and retry.";
                return false;
            }

            bodyStart = open + 1;
            bodyEnd = close;
            return true;
        }

        /// <summary>Collects field/property/method names declared anywhere in the source (heuristic, conservative).</summary>
        public static HashSet<string> CollectMemberNames(string source)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(source))
                return names;

            foreach (Match match in FieldRegex.Matches(source))
                names.Add(match.Groups[1].Value);
            foreach (Match match in MemberRegex.Matches(source))
                names.Add(match.Groups[1].Value);
            return names;
        }

        /// <summary>
        /// Parses generated <c>[UIBindData]</c>/<c>[UIBindCommand]</c> field declarations whose
        /// <c>nameof</c> target belongs to <paramref name="viewTypeName"/>.
        /// </summary>
        public static List<BindingDeclaration> CollectBindingDeclarations(string source, string viewTypeName)
        {
            var declarations = new List<BindingDeclaration>();
            if (string.IsNullOrEmpty(source))
                return declarations;

            foreach (Match match in BindingAttributeRegex.Matches(source))
            {
                var target = match.Groups["target"].Value.Trim();
                var split = target.LastIndexOf('.');
                if (split <= 0 || split == target.Length - 1)
                    continue;

                var viewText = target.Substring(0, split).Trim();
                var memberName = target.Substring(split + 1).Trim();
                if (!string.Equals(LastTypeSegment(viewText), viewTypeName, StringComparison.Ordinal))
                    continue;

                if (!TrySkipAttributeTail(source, match.Index + match.Length, out var afterAttribute))
                    continue;
                if (!TryReadFollowingField(source, afterAttribute, out var fieldName, out var declarationEnd))
                    continue;

                declarations.Add(new BindingDeclaration
                {
                    FieldName = fieldName,
                    ViewTypeText = viewText,
                    MemberName = memberName,
                    IsCommand = string.Equals(match.Groups["kind"].Value, "UIBindCommand", StringComparison.Ordinal),
                    Start = ComputeAttributeBlockStart(source, match.Index),
                    End = declarationEnd,
                });
            }

            return declarations;
        }

        /// <summary>Inserts a block of new members just before the class closing brace.</summary>
        public static string InsertMembersBeforeClassEnd(string source, int bodyEnd, string memberBlock)
        {
            if (memberBlock.Length == 0)
                return source;

            var indentStart = bodyEnd;
            while (indentStart > 0 && (source[indentStart - 1] == ' ' || source[indentStart - 1] == '\t'))
                indentStart--;

            var prefix = indentStart > 0 && (source[indentStart - 1] == '\n' || source[indentStart - 1] == '\r')
                ? string.Empty
                : "\n";
            return source.Substring(0, indentStart) + prefix + memberBlock + "\n" + source.Substring(indentStart);
        }

        /// <summary>Removes the supplied [start,end) ranges (already sorted or not) from the source.</summary>
        public static string RemoveRanges(string source, IReadOnlyList<KeyValuePair<int, int>> ranges)
        {
            if (ranges.Count == 0)
                return source;

            var ordered = new List<KeyValuePair<int, int>>(ranges);
            ordered.Sort((left, right) => right.Key.CompareTo(left.Key));
            var builder = new StringBuilder(source);
            foreach (var range in ordered)
            {
                if (range.Key < 0 || range.Value > builder.Length || range.Key >= range.Value)
                    continue;
                builder.Remove(range.Key, range.Value - range.Key);
            }

            return builder.ToString();
        }

        /// <summary>Detects the indentation used by existing class members, for consistent inserted declarations.</summary>
        public static string DetectMemberIndent(string source, int bodyStart, int bodyEnd)
        {
            var body = source.Substring(bodyStart, bodyEnd - bodyStart);
            var match = Regex.Match(body, @"(?m)^([ \t]+)\S", RegexOptions.CultureInvariant);
            if (match.Success)
                return match.Groups[1].Value;

            var classIndentMatch = Regex.Match(source.Substring(0, Math.Min(bodyStart, source.Length)), @"(?m)^([ \t]*)public\s+partial\s+class\b", RegexOptions.CultureInvariant);
            var classIndent = classIndentMatch.Success ? classIndentMatch.Groups[1].Value : string.Empty;
            return classIndent + "    ";
        }

        private static string LastTypeSegment(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.StartsWith("global::", StringComparison.Ordinal))
                trimmed = trimmed.Substring("global::".Length);
            var split = trimmed.LastIndexOf('.');
            return split < 0 ? trimmed : trimmed.Substring(split + 1);
        }

        private static bool TrySkipAttributeTail(string source, int position, out int afterAttribute)
        {
            afterAttribute = -1;
            while (position < source.Length)
            {
                var character = source[position];
                if (character == '"')
                {
                    if (!TrySkipString(source, ref position))
                        return false;
                    continue;
                }

                if (character == ']')
                {
                    afterAttribute = position + 1;
                    return true;
                }

                position++;
            }

            return false;
        }

        private static bool TryReadFollowingField(string source, int start, out string fieldName, out int declarationEnd)
        {
            fieldName = string.Empty;
            declarationEnd = -1;
            var position = start;
            string? lastIdentifier = null;

            while (position < source.Length)
            {
                var character = source[position];
                if (char.IsWhiteSpace(character))
                {
                    position++;
                    continue;
                }

                if (character == '/' && position + 1 < source.Length)
                {
                    if (source[position + 1] == '/')
                    {
                        var newline = source.IndexOf('\n', position);
                        position = newline < 0 ? source.Length : newline + 1;
                        continue;
                    }

                    if (source[position + 1] == '*')
                    {
                        var end = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
                        if (end < 0)
                            return false;
                        position = end + 2;
                        continue;
                    }
                }

                if (character == '[')
                {
                    if (!TrySkipBalanced(source, position, '[', ']', out position))
                        return false;
                    continue;
                }

                if (character == ';')
                {
                    if (string.IsNullOrEmpty(lastIdentifier))
                        return false;
                    fieldName = lastIdentifier!;
                    declarationEnd = position + 1;
                    if (declarationEnd < source.Length && source[declarationEnd] == '\r')
                        declarationEnd++;
                    if (declarationEnd < source.Length && source[declarationEnd] == '\n')
                        declarationEnd++;
                    return true;
                }

                if (character == '(' || character == '{' || character == '=' || character == '}' || character == ')')
                    return false;

                if (character == ':' && position + 1 < source.Length && source[position + 1] == ':')
                {
                    position += 2;
                    continue;
                }

                if (character == '.' || character == '?' || character == ',' || character == '<' || character == '>')
                {
                    position++;
                    continue;
                }

                if (char.IsLetter(character) || character == '_' || character == '@')
                {
                    var tokenStart = position;
                    if (character == '@')
                        position++;
                    var identifierStart = position;
                    while (position < source.Length &&
                           (char.IsLetterOrDigit(source[position]) || source[position] == '_'))
                        position++;
                    if (position == identifierStart)
                        return false;
                    lastIdentifier = source.Substring(identifierStart, position - identifierStart);
                    continue;
                }

                return false;
            }

            return false;
        }

        private static int ComputeAttributeBlockStart(string source, int attributeIndex)
        {
            var lineStart = source.LastIndexOf('\n', Math.Max(0, attributeIndex - 1)) + 1;
            var beforeAttributeOnLine = source.Substring(lineStart, attributeIndex - lineStart);
            var start = beforeAttributeOnLine.Trim().Length == 0 ? lineStart : attributeIndex;

            while (start > 0)
            {
                var previousLineEnd = start - 1;
                while (previousLineEnd >= 0 && (source[previousLineEnd] == '\r' || source[previousLineEnd] == '\n'))
                    previousLineEnd--;
                if (previousLineEnd < 0)
                    break;

                var previousLineStart = source.LastIndexOf('\n', Math.Max(0, previousLineEnd - 1)) + 1;
                var previousLine = source.Substring(previousLineStart, previousLineEnd - previousLineStart + 1).Trim();
                if (previousLine.StartsWith("[", StringComparison.Ordinal) &&
                    previousLine.EndsWith("]", StringComparison.Ordinal))
                {
                    start = previousLineStart;
                    continue;
                }

                break;
            }

            return start;
        }

        private static bool TryFindMatchingBrace(string source, int openIndex, out int closeIndex)
        {
            closeIndex = -1;
            var depth = 0;
            var position = openIndex;
            while (position < source.Length)
            {
                var character = source[position];
                if (character == '"')
                {
                    if (!TrySkipString(source, ref position, IsVerbatimStringStart(source, position)))
                        return false;
                    continue;
                }

                if (character == '\'')
                {
                    if (!TrySkipCharLiteral(source, ref position))
                        return false;
                    continue;
                }

                if (character == '/' && position + 1 < source.Length)
                {
                    if (source[position + 1] == '/')
                    {
                        var newline = source.IndexOf('\n', position);
                        position = newline < 0 ? source.Length : newline + 1;
                        continue;
                    }

                    if (source[position + 1] == '*')
                    {
                        var end = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
                        if (end < 0)
                            return false;
                        position = end + 2;
                        continue;
                    }
                }

                if (character == '{')
                {
                    depth++;
                }
                else if (character == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        closeIndex = position;
                        return true;
                    }

                    if (depth < 0)
                        return false;
                }

                position++;
            }

            return false;
        }

        private static bool IsVerbatimStringStart(string source, int quoteIndex)
        {
            var index = quoteIndex - 1;
            if (index >= 0 && source[index] == '@')
                return true;
            if (index >= 0 && source[index] == '$')
                return index - 1 >= 0 && source[index - 1] == '@';
            return false;
        }

        private static bool TrySkipString(string source, ref int position, bool verbatim = false)
        {
            // source[position] == '"'
            position++;
            while (position < source.Length)
            {
                var character = source[position];
                if (character == '"')
                {
                    if (verbatim && position + 1 < source.Length && source[position + 1] == '"')
                    {
                        position += 2;
                        continue;
                    }

                    position++;
                    return true;
                }

                if (!verbatim && character == '\\')
                {
                    position += 2;
                    continue;
                }

                position++;
            }

            return false;
        }

        private static bool TrySkipCharLiteral(string source, ref int position)
        {
            position++;
            while (position < source.Length)
            {
                var character = source[position];
                if (character == '\\')
                {
                    position += 2;
                    continue;
                }

                if (character == '\'')
                {
                    position++;
                    return true;
                }

                position++;
            }

            return false;
        }

        private static bool TrySkipBalanced(string source, int openIndex, char open, char close, out int afterClose)
        {
            afterClose = -1;
            var depth = 0;
            var position = openIndex;
            while (position < source.Length)
            {
                var character = source[position];
                if (character == '"')
                {
                    if (!TrySkipString(source, ref position))
                        return false;
                    continue;
                }

                if (character == open)
                {
                    depth++;
                }
                else if (character == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        afterClose = position + 1;
                        return true;
                    }
                }

                position++;
            }

            return false;
        }
    }
}
