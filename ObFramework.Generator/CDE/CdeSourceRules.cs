using System;
using System.Globalization;

namespace ObFramework.Generator.SourceRules
{
    /// <summary>
    /// The single shared source-declaration naming rule used by BOTH generators: the engine-neutral
    /// CDE generator (CdeSymbols) and the Unity engine binding generator (ViewSourceModel), which
    /// re-derives the same property and command names from the business View source declarations.
    ///
    /// This file is compiled into each generator assembly independently (the CDE project includes it
    /// directly; ObFramework.Generator.Unity links it with a Compile Include item and there is NO
    /// project reference between the two generator assemblies), so one declaration can never be
    /// interpreted differently by the two pipelines. The file is self-contained: BCL only, no Roslyn
    /// and no Framework dependency, and generated code has no runtime dependency on it.
    /// </summary>
    internal static class CdeSourceRules
    {
        private const string AsyncMethodSuffix = "Async";
        private const string CommandTypeSuffix = "Command";

        /// <summary>
        /// Derives the generated PascalCase property name from an <c>[ObservableProperty]</c> field
        /// name: leading underscores and a legacy <c>m_</c> prefix are trimmed (also when they nest,
        /// so <c>m__state</c> becomes <c>State</c>), then the first character is uppercased.
        /// Returns <c>null</c> when no valid identifier can be derived.
        /// </summary>
        internal static string? GetPropertyName(string? fieldName)
        {
            var name = fieldName ?? string.Empty;
            var index = 0;
            while (index < name.Length && name[index] == '_')
                index++;
            name = name.Substring(index);
            if (name.Length > 2 && name[0] == 'm' && name[1] == '_')
                name = name.Substring(2);
            index = 0;
            while (index < name.Length && name[index] == '_')
                index++;
            name = name.Substring(index);
            if (name.Length == 0)
                return null;
            name = char.ToUpperInvariant(name[0]) + name.Substring(1);
            return IsValidIdentifier(name) ? name : null;
        }

        /// <summary>
        /// Derives the nested command type name from a <c>[RelayCommand]</c> method name; a trailing
        /// <c>Async</c> suffix is dropped before appending <c>Command</c>
        /// (<c>Save</c> and <c>SaveAsync</c> both generate <c>SaveCommand</c>).
        /// </summary>
        internal static string GetCommandTypeName(string? methodName)
        {
            var name = methodName ?? string.Empty;
            if (name.Length > AsyncMethodSuffix.Length && name.EndsWith(AsyncMethodSuffix, StringComparison.Ordinal))
                name = name.Substring(0, name.Length - AsyncMethodSuffix.Length);
            return name + CommandTypeSuffix;
        }

        /// <summary>
        /// BCL-only equivalent of the C# identifier check used for derived property names. Keywords are
        /// never produced because a generated name always starts with an uppercase ASCII letter.
        /// </summary>
        internal static bool IsValidIdentifier(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (!IsIdentifierStartCharacter(name![0]))
                return false;
            for (var index = 1; index < name.Length; index++)
            {
                if (!IsIdentifierPartCharacter(name[index]))
                    return false;
            }
            return true;
        }

        private static bool IsIdentifierStartCharacter(char character)
        {
            switch (char.GetUnicodeCategory(character))
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.LetterNumber:
                    return true;
                default:
                    return character == '_';
            }
        }

        private static bool IsIdentifierPartCharacter(char character)
        {
            if (IsIdentifierStartCharacter(character))
                return true;
            switch (char.GetUnicodeCategory(character))
            {
                case UnicodeCategory.DecimalDigitNumber:
                case UnicodeCategory.ConnectorPunctuation:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.Format:
                    return true;
                default:
                    return false;
            }
        }
    }
}
