#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ObFramework.Editor.Core
{
    /// <summary>
    /// C# naming helpers shared by the Editor glue (default field/member names derived from the
    /// Prefab component name) and by the generators (field/method/property declaration names).
    /// Pure string logic; testable from ordinary .NET tests.
    /// </summary>
    public static class EditorNaming
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
            "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while",
        };

        /// <summary>True when <paramref name="value"/> is a valid, non-keyword C# identifier.</summary>
        public static bool IsValidIdentifier(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            if (Keywords.Contains(value!))
                return false;

            var first = value![0];
            if (!char.IsLetter(first) && first != '_')
                return false;

            for (var index = 1; index < value.Length; index++)
            {
                var character = value[index];
                if (!char.IsLetterOrDigit(character) && character != '_')
                    return false;
            }

            return true;
        }

        /// <summary>True when <paramref name="value"/> is a dotted C# namespace; empty is accepted (global namespace).</summary>
        public static bool IsValidNamespace(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;
            return IsValidQualifiedName(value);
        }

        /// <summary>True when <paramref name="value"/> is a dotted, non-empty C# type name.</summary>
        public static bool IsValidQualifiedName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var segments = value!.Trim().Split('.');
            foreach (var segment in segments)
            {
                if (!IsValidIdentifier(segment))
                    return false;
            }

            return true;
        }

        /// <summary>True when <paramref name="value"/> is a valid C# UI level name (<c>Scene</c>, <c>Common</c>, <c>Overlay</c>).</summary>
        public static bool IsValidLevel(string? value)
        {
            return TryNormalizeLevel(value, out _);
        }

        /// <summary>Normalizes a level name to the canonical <c>EUILevel</c> member name.</summary>
        public static bool TryNormalizeLevel(string? value, out string level)
        {
            level = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value!.Trim();
            foreach (var candidate in new[] { "Scene", "Common", "Overlay" })
            {
                if (string.Equals(trimmed, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    level = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>"ShopView" -&gt; "ShopWindow"; names without the "View" suffix get "Window" appended.</summary>
        public static string DefaultWindowName(string? viewName)
        {
            var name = (viewName ?? string.Empty).Trim();
            if (name.Length == 0)
                return "GeneratedWindow";
            return name.EndsWith("View", StringComparison.Ordinal) && name.Length > "View".Length
                ? name.Substring(0, name.Length - "View".Length) + "Window"
                : name + "Window";
        }

        /// <summary>"ShopView" -&gt; "IShopContract"; names without the "View" suffix become "IShopContract" from the whole name.</summary>
        public static string DefaultContractName(string? viewName)
        {
            var name = (viewName ?? string.Empty).Trim();
            if (name.Length == 0)
                return "IGeneratedContract";

            var stem = name.EndsWith("View", StringComparison.Ordinal) && name.Length > "View".Length
                ? name.Substring(0, name.Length - "View".Length)
                : name;
            return "I" + stem + "Contract";
        }

        /// <summary>
        /// Default serialized window field name for a Prefab component: "BtnSubmit" -&gt;
        /// "btnSubmit", "Title Text" -&gt; "titleText". Falls back to "field" for unsanitizable names.
        /// </summary>
        public static string DefaultFieldName(string? componentName)
        {
            var pascal = ToPascalCase(componentName);
            if (pascal.Length == 0)
                return "field";
            return ToCamelCase(pascal);
        }

        /// <summary>Default observable View property name for a display/input component: "Title Text" -&gt; "TitleText".</summary>
        public static string DefaultViewMemberName(string? componentName)
        {
            var pascal = ToPascalCase(componentName);
            return pascal.Length == 0 ? "Value" : pascal;
        }

        /// <summary>
        /// Default command target for a button component. Common button name prefixes are removed:
        /// "BtnSubmit" -&gt; "SubmitCommand", "Submit" -&gt; "SubmitCommand".
        /// </summary>
        public static string DefaultCommandName(string? componentName)
        {
            var pascal = ToPascalCase(componentName);
            if (pascal.Length == 0)
                return "SubmitCommand";

            foreach (var prefix in new[] { "Button", "Btn" })
            {
                if (pascal.Length > prefix.Length && pascal.StartsWith(prefix, StringComparison.Ordinal))
                {
                    pascal = pascal.Substring(prefix.Length);
                    break;
                }
            }

            return pascal + "Command";
        }

        /// <summary>Sanitizes arbitrary text into PascalCase (invalid characters act as word separators).</summary>
        public static string ToPascalCase(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new StringBuilder(value!.Length);
            var capitalizeNext = true;
            foreach (var character in value)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    capitalizeNext = true;
                    continue;
                }

                if (builder.Length == 0 && char.IsDigit(character))
                    builder.Append('_');

                builder.Append(capitalizeNext && char.IsLetter(character)
                    ? char.ToUpperInvariant(character)
                    : character);
                capitalizeNext = false;
            }

            return builder.ToString();
        }

        /// <summary>Sanitizes arbitrary text into camelCase; an all-caps acronym is lowercased ("HP" -&gt; "hp").</summary>
        public static string ToCamelCase(string? value)
        {
            var pascal = ToPascalCase(value);
            if (pascal.Length == 0)
                return string.Empty;

            if (pascal.Length > 1 && IsAllUpper(pascal))
                return pascal.ToLowerInvariant();

            var first = char.ToLowerInvariant(pascal[0]);
            return pascal.Length == 1 ? first.ToString() : first + pascal.Substring(1);
        }

        /// <summary>
        /// Splits a command target (method name, command name or nested command type name) into the
        /// View method name and the generated nested command type name.
        /// <para>
        /// "Submit" -&gt; ("Submit", "SubmitCommand"); "SubmitCommand" -&gt; ("Submit", "SubmitCommand");
        /// "SubmitAsync" -&gt; ("SubmitAsync", "SubmitCommand").
        /// </para>
        /// </summary>
        public static bool TryResolveCommandNames(string? commandTarget, out string methodName, out string commandName)
        {
            methodName = string.Empty;
            commandName = string.Empty;
            if (string.IsNullOrWhiteSpace(commandTarget))
                return false;

            var target = commandTarget!.Trim();
            if (target.EndsWith("Command", StringComparison.Ordinal) && target.Length > "Command".Length)
            {
                var stem = target.Substring(0, target.Length - "Command".Length);
                if (stem.EndsWith("Async", StringComparison.Ordinal) && stem.Length > "Async".Length)
                {
                    methodName = stem;
                    commandName = stem.Substring(0, stem.Length - "Async".Length) + "Command";
                }
                else
                {
                    methodName = stem;
                    commandName = target;
                }
            }
            else if (target.EndsWith("Async", StringComparison.Ordinal) && target.Length > "Async".Length)
            {
                methodName = target;
                commandName = target.Substring(0, target.Length - "Async".Length) + "Command";
            }
            else
            {
                methodName = target;
                commandName = target + "Command";
            }

            return IsValidIdentifier(methodName) && IsValidIdentifier(commandName);
        }

        /// <summary>Escapes a string for use as a C# string literal (including the surrounding quotes).</summary>
        public static string ToCSharpStringLiteral(string? value)
        {
            if (value == null)
                return "null";

            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (char.IsControl(character))
                            builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(character);
                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static bool IsAllUpper(string value)
        {
            foreach (var character in value)
            {
                if (char.IsLetter(character) && !char.IsUpper(character))
                    return false;
            }

            return true;
        }
    }
}
