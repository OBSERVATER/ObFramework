using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ObFramework.Generator.CDE
{
    /// <summary>One generated source file for one business View.</summary>
    internal sealed class CdeSource
    {
        public CdeSource(string hintName, string text)
        {
            HintName = hintName;
            Text = text;
        }

        public string HintName { get; }

        public string Text { get; }
    }

    /// <summary>Everything the generator needs to report and add for one generator run.</summary>
    internal sealed class CdePlan
    {
        public CdePlan(ImmutableArray<Diagnostic> diagnostics, ImmutableArray<CdeSource> sources)
        {
            Diagnostics = diagnostics;
            Sources = sources;
        }

        public ImmutableArray<Diagnostic> Diagnostics { get; }

        public ImmutableArray<CdeSource> Sources { get; }
    }

    /// <summary>Groups attributed fields and methods by owning View and emits one partial per View.</summary>
    internal static class CdePlanner
    {
        public static CdePlan Plan(
            ImmutableArray<IFieldSymbol> fields,
            ImmutableArray<IMethodSymbol> methods,
            Compilation compilation,
            CancellationToken cancellationToken)
        {
            var groups = new Dictionary<INamedTypeSymbol, ViewGroup>(SymbolEqualityComparer.Default);
            foreach (var field in OrderFields(fields))
                GetGroup(groups, field.ContainingType).Fields.Add(field);
            foreach (var method in OrderMethods(methods))
                GetGroup(groups, method.ContainingType).Methods.Add(method);

            var diagnostics = new List<Diagnostic>();
            var sources = new List<CdeSource>();
            foreach (var group in groups.Values.OrderBy(static item => item.View.ToDisplayString(), StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ValidateView(group.View, compilation, diagnostics))
                    continue;

                var emitter = new CdeViewEmitter(compilation, group.View);
                var text = emitter.Emit(group.Fields.ToImmutableArray(), group.Methods.ToImmutableArray());
                diagnostics.AddRange(emitter.Diagnostics);
                if (text == null)
                    continue;
                sources.Add(new CdeSource(GetHintName(group.View), text));
            }
            return new CdePlan(diagnostics.ToImmutableArray(), sources.ToImmutableArray());
        }

        private static ViewGroup GetGroup(Dictionary<INamedTypeSymbol, ViewGroup> groups, INamedTypeSymbol view)
        {
            if (groups.TryGetValue(view, out var group))
                return group;
            group = new ViewGroup(view);
            groups.Add(view, group);
            return group;
        }

        private static IEnumerable<IFieldSymbol> OrderFields(ImmutableArray<IFieldSymbol> fields)
        {
            return fields
                .OrderBy(static field => field.ContainingType.ToDisplayString(), StringComparer.Ordinal)
                .ThenBy(static field => GetFilePath(field), StringComparer.Ordinal)
                .ThenBy(static field => GetStart(field))
                .ThenBy(static field => field.Name, StringComparer.Ordinal);
        }

        private static IEnumerable<IMethodSymbol> OrderMethods(ImmutableArray<IMethodSymbol> methods)
        {
            return methods
                .OrderBy(static method => method.ContainingType.ToDisplayString(), StringComparer.Ordinal)
                .ThenBy(static method => GetFilePath(method), StringComparer.Ordinal)
                .ThenBy(static method => GetStart(method))
                .ThenBy(static method => method.Name, StringComparer.Ordinal);
        }

        private static string GetFilePath(ISymbol symbol)
        {
            var location = CdeSymbols.GetLocation(symbol);
            return location.SourceTree?.FilePath ?? string.Empty;
        }

        private static int GetStart(ISymbol symbol)
        {
            var location = CdeSymbols.GetLocation(symbol);
            return location.SourceSpan.Start;
        }

        private static string GetHintName(INamedTypeSymbol view)
        {
            var name = view.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            const string globalPrefix = "global::";
            if (name.StartsWith(globalPrefix, StringComparison.Ordinal))
                name = name.Substring(globalPrefix.Length);
            return name + ".CDE.g.cs";
        }

        private static bool ValidateView(INamedTypeSymbol view, Compilation compilation, List<Diagnostic> diagnostics)
        {
            var location = CdeSymbols.GetLocation(view);
            if (view.TypeKind != TypeKind.Class)
            {
                diagnostics.Add(Diagnostic.Create(CdeDiagnostics.ViewNotClass, location, view.ToDisplayString()));
                return false;
            }
            if (view.ContainingType != null)
            {
                diagnostics.Add(Diagnostic.Create(CdeDiagnostics.ViewNested, location, view.ToDisplayString()));
                return false;
            }
            if (view.TypeParameters.Length > 0)
            {
                diagnostics.Add(Diagnostic.Create(CdeDiagnostics.ViewGeneric, location, view.ToDisplayString()));
                return false;
            }
            if (!IsPartial(view))
            {
                diagnostics.Add(Diagnostic.Create(CdeDiagnostics.ViewNotPartial, location, view.ToDisplayString()));
                return false;
            }
            if (!DerivesFromAUIView(view, compilation))
            {
                diagnostics.Add(Diagnostic.Create(CdeDiagnostics.ViewNotAUIView, location, view.ToDisplayString()));
                return false;
            }
            return true;
        }

        private static bool IsPartial(INamedTypeSymbol view)
        {
            var declarations = view.DeclaringSyntaxReferences;
            if (declarations.Length == 0)
                return false;
            foreach (var declaration in declarations)
            {
                if (declaration.GetSyntax() is not ClassDeclarationSyntax syntax)
                    return false;
                if (!syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
                    return false;
            }
            return true;
        }

        private static bool DerivesFromAUIView(INamedTypeSymbol view, Compilation compilation)
        {
            var auiView = compilation.GetTypeByMetadataName(CdeSymbols.AUIViewMetadataName);
            if (auiView == null)
                return false;
            for (var current = view.BaseType; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, auiView))
                    return true;
            }
            return false;
        }

        private sealed class ViewGroup
        {
            public ViewGroup(INamedTypeSymbol view)
            {
                View = view;
            }

            public INamedTypeSymbol View { get; }

            public List<IFieldSymbol> Fields { get; } = new List<IFieldSymbol>();

            public List<IMethodSymbol> Methods { get; } = new List<IMethodSymbol>();
        }
    }
}
