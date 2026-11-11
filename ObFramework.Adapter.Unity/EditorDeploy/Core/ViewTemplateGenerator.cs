#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace ObFramework.Editor.Core
{
    /// <summary>One generated <c>[ObservableProperty]</c> View state field derived from a binding row.</summary>
    internal sealed class ViewStateMember
    {
        public string PropertyName = string.Empty;
        public string FieldName = string.Empty;
        public string StateType = string.Empty;
        public string? Initializer;
        public UIComponentKind Kind;
    }

    /// <summary>One generated <c>[RelayCommand]</c> View method derived from a button binding row.</summary>
    internal sealed class ViewCommandMember
    {
        public string MethodName = string.Empty;
        public string CommandName = string.Empty;
        public string TargetText = string.Empty;
    }

    /// <summary>
    /// Emits and incrementally updates the editable business View template
    /// (<c>partial class XView : AUIView</c>). Existing user-authored members, attributes and
    /// formatting are preserved; only missing generated declarations are appended.
    /// </summary>
    internal static class ViewTemplateGenerator
    {
        internal static GeneratedFile? Generate(
            EditorGenerationRequest request,
            string viewName,
            string @namespace,
            string viewPath,
            IReadOnlyList<ViewStateMember> states,
            IReadOnlyList<ViewCommandMember> commands,
            ICollection<EditorDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(request.ExistingViewSource))
            {
                var content = BuildTemplate(viewName, @namespace, states, commands, request.EmitLifecycleHooks);
                return new GeneratedFile(viewPath, GeneratedFileKind.View, content, isModified: true);
            }

            var existing = request.ExistingViewSource!;
            if (!SourceTextMerger.TryFindClassBody(existing, viewName, out var bodyStart, out var bodyEnd, out var error))
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.ExistingSourceNotEditable,
                    "The existing View source could not be updated: " + error,
                    viewPath));
                return null;
            }

            var declared = SourceTextMerger.CollectMemberNames(existing);
            var missingStates = new List<ViewStateMember>();
            foreach (var state in states)
            {
                if (declared.Contains(state.FieldName) || declared.Contains(state.PropertyName))
                    continue;
                missingStates.Add(state);
            }

            var missingCommands = new List<ViewCommandMember>();
            foreach (var command in commands)
            {
                if (declared.Contains(command.MethodName) || declared.Contains(command.CommandName))
                    continue;
                missingCommands.Add(command);
            }

            var hasLifecycle = !request.EmitLifecycleHooks ||
                               (declared.Contains("OnInit") && declared.Contains("OnClose"));
            if (missingStates.Count == 0 && missingCommands.Count == 0 && hasLifecycle)
                return new GeneratedFile(viewPath, GeneratedFileKind.View, existing, isModified: false);

            var indent = SourceTextMerger.DetectMemberIndent(existing, bodyStart, bodyEnd);
            var block = new StringBuilder();
            if (!request.EmitLifecycleHooks)
            {
                // Nothing to add.
            }
            else
            {
                if (!declared.Contains("OnInit"))
                    AppendSeparated(block, LifecycleOnInit(indent));
                if (!declared.Contains("OnClose"))
                    AppendSeparated(block, LifecycleOnClose(indent));
            }

            foreach (var state in missingStates)
                AppendSeparated(block, StateDeclaration(indent, state));
            foreach (var command in missingCommands)
                AppendSeparated(block, CommandDeclaration(indent, command));

            var updated = SourceTextMerger.InsertMembersBeforeClassEnd(existing, bodyEnd, block.ToString());
            return new GeneratedFile(viewPath, GeneratedFileKind.View, updated, isModified: true);
        }

        internal static string BuildTemplate(
            string viewName,
            string @namespace,
            IReadOnlyList<ViewStateMember> states,
            IReadOnlyList<ViewCommandMember> commands,
            bool emitLifecycleHooks)
        {
            var hasNamespace = !string.IsNullOrEmpty(@namespace);
            var classIndent = hasNamespace ? "    " : string.Empty;
            var memberIndent = classIndent + "    ";

            var builder = new StringBuilder();
            builder.AppendLine("// ------------------------------------------------------------------------------");
            builder.AppendLine("// OBF UI 2.0 editable business View template. This file is yours to edit.");
            builder.AppendLine("// Regeneration (Tools/OBF/UI/Generate View+VC) only adds missing [ObservableProperty]");
            builder.AppendLine("// fields, [RelayCommand] methods and lifecycle hooks; it never overwrites or reformats");
            builder.AppendLine("// existing members or attributes.");
            builder.AppendLine("// ------------------------------------------------------------------------------");
            builder.AppendLine("#nullable enable");
            if (hasNamespace)
            {
                builder.Append("namespace ").Append(@namespace).AppendLine();
                builder.AppendLine("{");
            }

            builder.Append(classIndent).AppendLine("/// <summary>Engine-independent business View. Observable state and commands live here; Unity rendering stays in the paired window.</summary>");
            builder.Append(classIndent).Append("public partial class ").Append(viewName).Append(" : ")
                .Append(EditorFrameworkNames.Global(EditorFrameworkNames.AUIView)).AppendLine();
            builder.Append(classIndent).AppendLine("{");

            var members = new List<string>();
            if (emitLifecycleHooks)
            {
                members.Add(LifecycleOnInit(memberIndent));
                members.Add(LifecycleOnClose(memberIndent));
            }

            foreach (var state in states)
                members.Add(StateDeclaration(memberIndent, state));
            foreach (var command in commands)
                members.Add(CommandDeclaration(memberIndent, command));

            for (var index = 0; index < members.Count; index++)
            {
                if (index > 0)
                    builder.AppendLine();
                builder.AppendLine(members[index]);
            }

            builder.Append(classIndent).AppendLine("}");
            if (hasNamespace)
                builder.AppendLine("}");
            return builder.ToString();
        }

        private static string StateDeclaration(string indent, ViewStateMember state)
        {
            var builder = new StringBuilder();
            builder.Append(indent).Append('[').Append(EditorFrameworkNames.Global(EditorFrameworkNames.ObservablePropertyAttribute)).AppendLine("]");
            builder.Append(indent).Append("private ").Append(state.StateType).Append(' ').Append(state.FieldName);
            if (!string.IsNullOrEmpty(state.Initializer))
                builder.Append(" = ").Append(state.Initializer);
            builder.Append(';');
            return builder.ToString();
        }

        private static string CommandDeclaration(string indent, ViewCommandMember command)
        {
            var builder = new StringBuilder();
            builder.Append(indent).Append('[').Append(EditorFrameworkNames.Global(EditorFrameworkNames.RelayCommandAttribute)).AppendLine("]");
            builder.Append(indent).Append("private void ").Append(command.MethodName).AppendLine("()");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).Append('}');
            return builder.ToString();
        }

        private static string LifecycleOnInit(string indent)
        {
            var builder = new StringBuilder();
            builder.Append(indent).Append("protected override void OnInit(")
                .Append(EditorFrameworkNames.Global(EditorFrameworkNames.IUIData)).AppendLine("? data)");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).Append('}');
            return builder.ToString();
        }

        private static string LifecycleOnClose(string indent)
        {
            var builder = new StringBuilder();
            builder.Append(indent).AppendLine("protected override void OnClose()");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).Append('}');
            return builder.ToString();
        }

        private static void AppendSeparated(StringBuilder builder, string member)
        {
            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(member);
        }
    }
}
