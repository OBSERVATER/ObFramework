#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace ObFramework.Editor.Core
{
    /// <summary>
    /// Minimal reader for the <c>resourceKeyOverrides</c> property of the raw setup.json
    /// <c>modules.unity</c> section text.
    /// <para>
    /// Unity 2022.3 <c>JsonUtility</c> cannot deserialize dictionaries, so the Editor glue reads the
    /// fixed string fields through a small JsonUtility DTO and passes the raw section JSON here for
    /// the override map. This reader deliberately supports only the shape setup.json uses
    /// (an object with string keys and string values) and never throws: malformed input becomes a
    /// structured error.
    /// </para>
    /// </summary>
    public static class UISetupOverridesJson
    {
        /// <summary>JSON property name read by this helper.</summary>
        public const string PropertyName = "resourceKeyOverrides";

        /// <summary>
        /// Reads the override map from raw section JSON. Missing property yields an empty map and
        /// <see langword="true"/>; malformed JSON yields a human readable <paramref name="error"/>.
        /// </summary>
        public static bool TryRead(
            string? sectionJson,
            out IReadOnlyDictionary<string, string> overrides,
            out string? error)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            overrides = result;
            error = null;

            if (string.IsNullOrWhiteSpace(sectionJson))
                return true;

            var text = sectionJson!;
            var keyLiteral = "\"" + PropertyName + "\"";
            var keyIndex = text.IndexOf(keyLiteral, StringComparison.Ordinal);
            if (keyIndex < 0)
                return true;

            var position = keyIndex + keyLiteral.Length;
            SkipWhitespace(text, ref position);
            if (position >= text.Length || text[position] != ':')
            {
                error = "'" + PropertyName + "' is not followed by ':'.";
                return false;
            }

            position++;
            SkipWhitespace(text, ref position);
            if (position >= text.Length || text[position] != '{')
            {
                error = "'" + PropertyName + "' must be a JSON object of View name to resource key strings.";
                return false;
            }

            position++;
            while (true)
            {
                SkipWhitespace(text, ref position);
                if (position >= text.Length)
                {
                    error = "'" + PropertyName + "' object is not closed.";
                    return false;
                }

                if (text[position] == '}')
                    return true;

                if (!TryReadJsonString(text, ref position, out var viewName, out error))
                    return false;

                SkipWhitespace(text, ref position);
                if (position >= text.Length || text[position] != ':')
                {
                    error = "'" + PropertyName + "[" + viewName + "]' is not followed by ':'.";
                    return false;
                }

                position++;
                SkipWhitespace(text, ref position);
                if (position >= text.Length || text[position] != '"')
                {
                    error = "'" + PropertyName + "[" + viewName + "]' must be a JSON string.";
                    return false;
                }

                if (!TryReadJsonString(text, ref position, out var resourceKey, out error))
                    return false;

                result[viewName] = resourceKey;

                SkipWhitespace(text, ref position);
                if (position < text.Length && text[position] == ',')
                {
                    position++;
                    continue;
                }

                if (position < text.Length && text[position] == '}')
                    return true;

                error = "Malformed '" + PropertyName + "' JSON object.";
                return false;
            }
        }

        private static void SkipWhitespace(string text, ref int position)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
                position++;
        }

        private static bool TryReadJsonString(string text, ref int position, out string value, out string? error)
        {
            value = string.Empty;
            error = null;
            if (position >= text.Length || text[position] != '"')
            {
                error = "Expected a JSON string.";
                return false;
            }

            position++;
            var builder = new StringBuilder();
            while (position < text.Length)
            {
                var character = text[position++];
                if (character == '"')
                {
                    value = builder.ToString();
                    return true;
                }

                if (character != '\\')
                {
                    builder.Append(character);
                    continue;
                }

                if (position >= text.Length)
                {
                    error = "Unterminated escape sequence in a JSON string.";
                    return false;
                }

                var escape = text[position++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (position + 4 > text.Length ||
                            !TryParseHex(text, position, out var code))
                        {
                            error = "Invalid unicode escape in a JSON string.";
                            return false;
                        }
                        builder.Append((char)code);
                        position += 4;
                        break;
                    default:
                        error = "Unsupported JSON escape sequence '\\" + escape + "'.";
                        return false;
                }
            }

            error = "Unterminated JSON string.";
            return false;
        }

        private static bool TryParseHex(string text, int position, out int value)
        {
            value = 0;
            for (var index = 0; index < 4; index++)
            {
                var character = text[position + index];
                int digit;
                if (character >= '0' && character <= '9')
                    digit = character - '0';
                else if (character >= 'a' && character <= 'f')
                    digit = character - 'a' + 10;
                else if (character >= 'A' && character <= 'F')
                    digit = character - 'A' + 10;
                else
                    return false;

                value = (value << 4) + digit;
            }

            return true;
        }
    }
}
