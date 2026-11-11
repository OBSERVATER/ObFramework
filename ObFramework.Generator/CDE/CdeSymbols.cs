using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ObFramework.Generator.CDE
{
    /// <summary>
    /// Symbol, attribute and naming helpers shared by the CDE generator pipeline. The generator
    /// resolves the runtime attributes by metadata name from the consumer compilation, so it works
    /// with a referenced ObFramework assembly and never depends on the Framework source project.
    /// </summary>
    internal static class CdeSymbols
    {
        internal const string ObservablePropertyAttribute = "ObFramework.Integrated.UI.ObservablePropertyAttribute";
        internal const string RelayCommandAttribute = "ObFramework.Integrated.UI.RelayCommandAttribute";
        internal const string NotifyCanExecuteChangedForAttribute = "ObFramework.Integrated.UI.NotifyCanExecuteChangedForAttribute";
        internal const string NotifyPropertyChangedForAttribute = "ObFramework.Integrated.UI.NotifyPropertyChangedForAttribute";
        internal const string AUIViewMetadataName = "ObFramework.Integrated.UI.AUIView";
        internal const string CancellationTokenMetadataName = "System.Threading.CancellationToken";

        /// <summary>Fully qualified type names, including nullable reference annotations, for generated code.</summary>
        internal static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
            .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        internal static string DisplayType(ITypeSymbol type)
        {
            return type.ToDisplayString(TypeFormat);
        }

        internal static bool HasAttribute(ISymbol symbol, string metadataName)
        {
            return FindAttribute(symbol, metadataName) != null;
        }

        internal static AttributeData? FindAttribute(ISymbol symbol, string metadataName)
        {
            var attributes = symbol.GetAttributes();
            for (var index = 0; index < attributes.Length; index++)
            {
                var attributeClass = attributes[index].AttributeClass;
                if (attributeClass == null)
                    continue;
                if (string.Equals(attributeClass.ToDisplayString(), metadataName, StringComparison.Ordinal))
                    return attributes[index];
            }
            return null;
        }

        /// <summary>
        /// Reads a string constructor argument. When the argument is a nameof expression that the
        /// current semantic model cannot bind yet (for example nameof of a command type generated in
        /// the same compilation), the argument syntax text is used as a fallback.
        /// </summary>
        internal static string? GetStringArgument(AttributeData attribute, int index)
        {
            if (attribute.ConstructorArguments.Length > index)
            {
                var argument = attribute.ConstructorArguments[index];
                if (argument.Kind != TypedConstantKind.Error && argument.Value is string text && text.Length > 0)
                    return text;
            }
            return TryReadNameOfArgument(attribute, index);
        }

        internal static string? GetNamedStringArgument(AttributeData? attribute, string name)
        {
            if (attribute == null)
                return null;
            foreach (var pair in attribute.NamedArguments)
            {
                if (!string.Equals(pair.Key, name, StringComparison.Ordinal))
                    continue;
                if (pair.Value.Kind != TypedConstantKind.Error && pair.Value.Value is string text && text.Length > 0)
                    return text;
                return TryReadNamedNameOfArgument(attribute, name);
            }
            return null;
        }

        internal static bool HasNamedArgument(AttributeData? attribute, string name)
        {
            if (attribute == null)
                return false;
            foreach (var pair in attribute.NamedArguments)
            {
                if (string.Equals(pair.Key, name, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        internal static bool? GetNamedBoolArgument(AttributeData? attribute, string name)
        {
            if (attribute == null)
                return null;
            foreach (var pair in attribute.NamedArguments)
            {
                if (string.Equals(pair.Key, name, StringComparison.Ordinal) && pair.Value.Value is bool value)
                    return value;
            }
            return null;
        }

        private static string? TryReadNameOfArgument(AttributeData attribute, int index)
        {
            var attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
            var argumentList = attributeSyntax?.ArgumentList;
            if (argumentList == null || argumentList.Arguments.Count <= index)
                return null;
            return ReadNameOfExpression(argumentList.Arguments[index].Expression);
        }

        private static string? TryReadNamedNameOfArgument(AttributeData attribute, string name)
        {
            var attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
            var argumentList = attributeSyntax?.ArgumentList;
            if (argumentList == null)
                return null;
            foreach (var argument in argumentList.Arguments)
            {
                if (argument.NameEquals == null)
                    continue;
                if (!string.Equals(argument.NameEquals.Name.Identifier.ValueText, name, StringComparison.Ordinal))
                    continue;
                return ReadNameOfExpression(argument.Expression);
            }
            return null;
        }

        private static string? ReadNameOfExpression(ExpressionSyntax expression)
        {
            if (expression is not InvocationExpressionSyntax invocation)
                return null;
            if (invocation.Expression is not IdentifierNameSyntax nameOfIdentifier || nameOfIdentifier.Identifier.ValueText != "nameof")
                return null;
            if (invocation.ArgumentList.Arguments.Count != 1)
                return null;
            return GetLastIdentifier(invocation.ArgumentList.Arguments[0].Expression);
        }

        private static string? GetLastIdentifier(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case IdentifierNameSyntax identifier:
                    return identifier.Identifier.ValueText;
                case MemberAccessExpressionSyntax memberAccess:
                    return memberAccess.Name.Identifier.ValueText;
                case AliasQualifiedNameSyntax aliasQualified:
                    return aliasQualified.Name.Identifier.ValueText;
                case QualifiedNameSyntax qualifiedName:
                    return qualifiedName.Right.Identifier.ValueText;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Derives the generated PascalCase property name from a private field name. Leading
        /// underscores and a legacy 'm_' prefix are trimmed, then the first character is uppercased.
        /// </summary>
        internal static string? GetGeneratedPropertyName(IFieldSymbol field)
        {
            var name = field.Name;
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
            return SyntaxFacts.IsValidIdentifier(name) ? name : null;
        }

        /// <summary>
        /// Derives the nested command type name from the relay method name; a trailing 'Async'
        /// suffix is dropped before appending 'Command' (Submit/SubmitAsync -> SubmitCommand).
        /// </summary>
        internal static string GetGeneratedCommandName(IMethodSymbol method)
        {
            var name = method.Name;
            const string asyncSuffix = "Async";
            if (name.Length > asyncSuffix.Length && name.EndsWith(asyncSuffix, StringComparison.Ordinal))
                name = name.Substring(0, name.Length - asyncSuffix.Length);
            return name + "Command";
        }

        internal static string EscapeIdentifier(string identifier)
        {
            return SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ? "@" + identifier : identifier;
        }

        internal static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        internal static string GetNamespaceName(INamedTypeSymbol type)
        {
            var names = new List<string>();
            var current = type.ContainingNamespace;
            while (current != null && !current.IsGlobalNamespace)
            {
                names.Add(EscapeIdentifier(current.Name));
                current = current.ContainingNamespace;
            }
            names.Reverse();
            return string.Join(".", names);
        }

        internal static Location GetLocation(ISymbol symbol)
        {
            foreach (var location in symbol.Locations)
            {
                if (location.IsInSource)
                    return location;
            }
            return Location.None;
        }

        internal static Location GetAttributeLocation(AttributeData attribute, Location fallback)
        {
            var syntax = attribute.ApplicationSyntaxReference?.GetSyntax();
            return syntax?.GetLocation() ?? fallback;
        }
    }
}
