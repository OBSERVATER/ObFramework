#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ObFramework.Editor.Core
{
    /// <summary>One generated <c>[SerializeField]</c> window field derived from a binding row.</summary>
    internal sealed class WindowFieldMember
    {
        public string FieldName = string.Empty;
        public BindingTableRow Row = new BindingTableRow();
        public string ControlTypeName = string.Empty;
        public string ViewTarget = string.Empty;
        public bool IsCommand;
        public string CommandName = string.Empty;
    }

    /// <summary>
    /// Emits and incrementally updates the editable Unity window controller template
    /// (<c>partial class XWindow : AUIWindow</c>): <c>[BindView]</c> attribute, contract interface,
    /// selected Architecture/Level and serialized control fields carrying <c>[UIBindData]</c>/
    /// <c>[UIBindCommand]</c> attributes consumed by the Roslyn view-binding generator.
    /// <para>
    /// Destructive renames/deletes of generated serialized fields are never applied silently: they
    /// are reported as <see cref="DestructiveChange"/> entries (and warning diagnostics) until the
    /// caller confirms their ids.
    /// </para>
    /// </summary>
    internal static class WindowTemplateGenerator
    {
        internal static GeneratedFile? Generate(
            EditorGenerationRequest request,
            string viewName,
            string windowName,
            string contractName,
            string @namespace,
            string architectureTypeFullName,
            string level,
            string windowPath,
            IReadOnlyList<WindowFieldMember> fields,
            IList<DestructiveChange> changes,
            ICollection<EditorDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(request.ExistingWindowSource))
            {
                var content = BuildTemplate(
                    viewName, windowName, contractName, @namespace, architectureTypeFullName, level, fields);
                return new GeneratedFile(windowPath, GeneratedFileKind.Window, content, isModified: true);
            }

            var existing = request.ExistingWindowSource!;
            if (!SourceTextMerger.TryFindClassBody(existing, windowName, out var bodyStart, out var bodyEnd, out var error))
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.ExistingSourceNotEditable,
                    "The existing window source could not be updated: " + error,
                    windowPath));
                return null;
            }

            var confirmed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in request.ConfirmedDestructiveChangeIds ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(id))
                    confirmed.Add(id.Trim());
            }

            var rowByField = new Dictionary<string, WindowFieldMember>(StringComparer.Ordinal);
            foreach (var field in fields)
                rowByField[field.FieldName] = field;

            var changeByField = new Dictionary<string, DestructiveChange>(StringComparer.Ordinal);
            var removalRanges = new List<KeyValuePair<int, int>>();
            var updates = new List<SourceReplacement>();
            var declarations = SourceTextMerger.CollectBindingDeclarations(existing, viewName);
            foreach (var declaration in declarations)
            {
                var id = EditorGenerationResult.WindowFieldChangeId(declaration.FieldName);
                if (rowByField.TryGetValue(declaration.FieldName, out var field) && TargetsMatch(declaration, field))
                {
                    // The binding table can change Mode/Format for an existing control without
                    // renaming it. Refresh only the generator-owned attribute/field declaration and
                    // keep hand-written attributes/comments (see BuildUpdatedFieldDeclaration).
                    if (FieldDeclarationNeedsUpdate(declaration, existing, field))
                    {
                        updates.Add(new SourceReplacement(
                            declaration.Start,
                            declaration.End,
                            BuildUpdatedFieldDeclaration(existing, declaration, field, viewName)));
                    }

                    continue;
                }

                var change = rowByField.TryGetValue(declaration.FieldName, out field)
                    ? new DestructiveChange(
                        id,
                        DestructiveChangeKind.Rename,
                        "Serialized field '" + declaration.FieldName + "' on " + windowName +
                        " now binds a different View member (was '" + declaration.MemberName + "').",
                        windowName + "." + declaration.FieldName,
                        before: declaration.MemberName,
                        after: field.IsCommand ? field.CommandName : field.ViewTarget)
                    : new DestructiveChange(
                        id,
                        DestructiveChangeKind.Delete,
                        "Serialized field '" + declaration.FieldName + "' on " + windowName +
                        " (bound to View member '" + declaration.MemberName + "') is no longer in the binding table.",
                        windowName + "." + declaration.FieldName,
                        before: declaration.MemberName);

                ApplyConfirmation(change, confirmed, removalRanges, declaration, diagnostics);
                changes.Add(change);
                changeByField[declaration.FieldName] = change;
            }

            // Rows explicitly flagged as destructive in the binding table require the same confirmation
            // even when the existing source does not reveal a rename (for example a lost component path).
            foreach (var field in fields)
            {
                if (!field.Row.IsDestructive || changeByField.ContainsKey(field.FieldName))
                    continue;

                var id = EditorGenerationResult.WindowFieldChangeId(field.FieldName);
                var change = new DestructiveChange(
                    id,
                    DestructiveChangeKind.Rename,
                    "Binding table row '" + field.Row.ComponentName + "' is flagged as a destructive change; " +
                    "the serialized field '" + field.FieldName + "' is only added after confirmation.",
                    windowName + "." + field.FieldName,
                    after: field.IsCommand ? field.CommandName : field.ViewTarget);
                if (confirmed.Contains(id))
                {
                    change.Applied = true;
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Info,
                        EditorDiagnosticCodes.DestructiveChangeApplied,
                        "Confirmed destructive change applied: " + change.Description,
                        change.Target));
                }
                else
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Warning,
                        EditorDiagnosticCodes.DestructiveChangePending,
                        change.Description + " Confirm the change in the preview to apply it.",
                        change.Target));
                }

                changes.Add(change);
                changeByField[field.FieldName] = change;
            }

            var updated = removalRanges.Count == 0
                ? existing
                : SourceTextMerger.RemoveRanges(existing, removalRanges);
            if (updates.Count > 0)
                updated = SourceTextMerger.ReplaceRanges(updated, updates);

            if (!SourceTextMerger.TryFindClassBody(updated, windowName, out bodyStart, out bodyEnd, out error))
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Error,
                    EditorDiagnosticCodes.ExistingSourceNotEditable,
                    "The existing window source could not be re-read after applying confirmed changes: " + error,
                    windowPath));
                return null;
            }

            var declared = SourceTextMerger.CollectMemberNames(updated);
            var missing = new List<WindowFieldMember>();
            foreach (var field in fields)
            {
                if (declared.Contains(field.FieldName))
                    continue;
                if (changeByField.TryGetValue(field.FieldName, out var change) && !change.Applied)
                    continue;
                missing.Add(field);
            }

            if (missing.Count > 0)
            {
                var indent = SourceTextMerger.DetectMemberIndent(updated, bodyStart, bodyEnd);
                var block = new StringBuilder();
                foreach (var field in missing)
                {
                    if (block.Length > 0)
                        block.AppendLine();
                    block.Append(BuildFieldDeclaration(indent, viewName, field));
                }

                updated = SourceTextMerger.InsertMembersBeforeClassEnd(updated, bodyEnd, block.ToString());
            }

            updated = UpdateGeneratedWindowMembers(updated, windowName, architectureTypeFullName, level, diagnostics);

            return new GeneratedFile(windowPath, GeneratedFileKind.Window, updated, isModified: !string.Equals(updated, existing, StringComparison.Ordinal));
        }

        internal static string BuildTemplate(
            string viewName,
            string windowName,
            string contractName,
            string @namespace,
            string architectureTypeFullName,
            string level,
            IReadOnlyList<WindowFieldMember> fields)
        {
            var hasNamespace = !string.IsNullOrEmpty(@namespace);
            var classIndent = hasNamespace ? "    " : string.Empty;
            var memberIndent = classIndent + "    ";

            var builder = new StringBuilder();
            builder.AppendLine("// ------------------------------------------------------------------------------");
            builder.AppendLine("// OBF UI 2.0 editable Unity window controller (VC) generated from the CDE binding table.");
            builder.AppendLine("// This file is yours to edit: engine-only code belongs here, business logic belongs in the View.");
            builder.AppendLine("// Regeneration only adds missing serialized control fields; renames/deletes are previewed.");
            builder.AppendLine("// ------------------------------------------------------------------------------");
            if (hasNamespace)
            {
                builder.Append("namespace ").Append(@namespace).AppendLine();
                builder.AppendLine("{");
            }

            builder.Append(classIndent).Append('[').Append(EditorFrameworkNames.Global(EditorFrameworkNames.BindViewAttribute))
                .Append("(typeof(").Append(viewName).Append("), typeof(").Append(contractName).AppendLine("))]");
            builder.Append(classIndent).Append("public partial class ").Append(windowName).Append(" : ")
                .Append(EditorFrameworkNames.Global(EditorFrameworkNames.AUIWindow)).Append(", ").Append(contractName).AppendLine();
            builder.Append(classIndent).AppendLine("{");

            var members = new List<string>
            {
                BuildArchitectureMember(memberIndent, architectureTypeFullName),
                BuildLevelMember(memberIndent, level),
            };
            foreach (var field in fields)
                members.Add(BuildFieldDeclaration(memberIndent, viewName, field));

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

        private static void ApplyConfirmation(
            DestructiveChange change,
            HashSet<string> confirmed,
            ICollection<KeyValuePair<int, int>> removalRanges,
            BindingDeclaration declaration,
            ICollection<EditorDiagnostic> diagnostics)
        {
            if (!confirmed.Contains(change.Id))
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Warning,
                    EditorDiagnosticCodes.DestructiveChangePending,
                    change.Description + " Confirm the change in the preview to apply it.",
                    change.Target));
                return;
            }

            change.Applied = true;
            removalRanges.Add(new KeyValuePair<int, int>(declaration.Start, declaration.End));
            diagnostics.Add(new EditorDiagnostic(
                EditorDiagnosticSeverity.Info,
                EditorDiagnosticCodes.DestructiveChangeApplied,
                "Confirmed destructive change applied: " + change.Description,
                change.Target));
        }

        private static bool TargetsMatch(BindingDeclaration declaration, WindowFieldMember field)
        {
            if (declaration.IsCommand != field.IsCommand)
                return false;

            if (!field.IsCommand)
                return string.Equals(declaration.MemberName, field.ViewTarget, StringComparison.Ordinal);

            return EditorNaming.TryResolveCommandNames(declaration.MemberName, out _, out var existingCommand) &&
                   string.Equals(existingCommand, field.CommandName, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the generated [UIBindData] options (Mode/Format) on an existing serialized field
        /// no longer match the binding table. Commands have no options, so they never update in place.
        /// </summary>
        private static bool FieldDeclarationNeedsUpdate(BindingDeclaration declaration, string existing, WindowFieldMember field)
        {
            if (field.IsCommand)
                return false;

            var block = existing.Substring(declaration.Start, declaration.End - declaration.Start);
            var expectsTwoWay = field.Row.EffectiveMode == UIBindMode.TwoWay;
            var hasTwoWay = block.IndexOf("TwoWay", StringComparison.Ordinal) >= 0;
            if (expectsTwoWay != hasTwoWay)
                return true;

            var format = field.Row.Format;
            if (string.IsNullOrEmpty(format))
                return block.IndexOf("Format", StringComparison.Ordinal) >= 0;

            return block.IndexOf("Format", StringComparison.Ordinal) < 0 ||
                   block.IndexOf(EditorNaming.ToCSharpStringLiteral(format), StringComparison.Ordinal) < 0;
        }

        /// <summary>
        /// Rebuilds one generated field declaration with the current Mode/Format while preserving
        /// hand-written attributes ([Obsolete], [Header], ...), comments and the original indentation.
        /// The field declaration itself and the generator-owned attributes are rewritten.
        /// </summary>
        private static string BuildUpdatedFieldDeclaration(
            string existing,
            BindingDeclaration declaration,
            WindowFieldMember field,
            string viewName)
        {
            var block = existing.Substring(declaration.Start, declaration.End - declaration.Start);
            var indentMatch = Regex.Match(block, @"(?m)^([ \t]+)\S", RegexOptions.CultureInvariant);
            var indent = indentMatch.Success ? indentMatch.Groups[1].Value : "    ";

            var preserved = new StringBuilder();
            var normalized = block.Replace("\r\n", "\n");
            foreach (var rawLine in normalized.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;

                if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith("/*", StringComparison.Ordinal) ||
                    trimmed.StartsWith("*", StringComparison.Ordinal))
                {
                    preserved.AppendLine(indent + trimmed);
                    continue;
                }

                if (!trimmed.StartsWith("[", StringComparison.Ordinal))
                    continue; // the serialized field declaration is regenerated below

                if (trimmed.IndexOf("UIBindData", StringComparison.Ordinal) >= 0 ||
                    trimmed.IndexOf("UIBindCommand", StringComparison.Ordinal) >= 0 ||
                    trimmed.IndexOf("SerializeField", StringComparison.Ordinal) >= 0)
                {
                    continue; // generator-owned attributes are rebuilt from the binding table
                }

                preserved.AppendLine(indent + trimmed);
            }

            preserved.Append(BuildFieldDeclaration(indent, viewName, field));
            return preserved.ToString();
        }

        /// <summary>
        /// Refreshes the generator-owned Architecture and Level members when the binding table's
        /// explicit selection changed. User-authored members are untouched; a missing template member
        /// produces a warning instead of a silent rewrite.
        /// </summary>
        private static string UpdateGeneratedWindowMembers(
            string source,
            string windowName,
            string architectureTypeFullName,
            string level,
            ICollection<EditorDiagnostic> diagnostics)
        {
            var updated = UpdateGeneratedMember(
                source,
                @"(?m)^(?<indent>[ \t]*)public\s+override\s+(?:global::)?[A-Za-z_][A-Za-z0-9_.]*\s+GetArchitecture\s*\(\s*\)",
                indent => BuildArchitectureMember(indent, architectureTypeFullName),
                "GetArchitecture()",
                diagnostics);
            updated = UpdateGeneratedMember(
                updated,
                @"(?m)^(?<indent>[ \t]*)public\s+override\s+(?:global::)?[A-Za-z_][A-Za-z0-9_.]*\s+Level\s*(?:=>|\{)",
                indent => BuildLevelMember(indent, level),
                "Level",
                diagnostics);
            return updated;
        }

        private static string UpdateGeneratedMember(
            string source,
            string pattern,
            Func<string, string> build,
            string memberLabel,
            ICollection<EditorDiagnostic> diagnostics)
        {
            var match = Regex.Match(source, pattern, RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Warning,
                    EditorDiagnosticCodes.InvalidBindingRow,
                    "The generated window template has no '" + memberLabel + "' member to update; " +
                    "update the existing source manually or regenerate the file.",
                    memberLabel));
                return source;
            }

            if (!SourceTextMerger.TryFindMemberEnd(source, match.Index + match.Length, out var memberEnd))
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Warning,
                    EditorDiagnosticCodes.ExistingSourceNotEditable,
                    "Could not find the end of the generated '" + memberLabel + "' member; the existing source was left untouched.",
                    memberLabel));
                return source;
            }

            var indent = match.Groups["indent"].Value;
            var newLine = source.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var replacement = build(indent);
            if (newLine == "\n")
                replacement = replacement.Replace("\r\n", "\n");

            var start = SourceTextMerger.GetMemberStartWithDocComment(source, match.Index);
            var existingMember = source.Substring(start, memberEnd - start);
            if (NormalizeMemberText(existingMember) == NormalizeMemberText(replacement))
                return source;

            return source.Substring(0, start) + replacement + source.Substring(memberEnd);
        }

        private static string NormalizeMemberText(string text)
        {
            var normalized = text.Replace("\r\n", "\n").Trim();
            var lines = normalized.Split('\n');
            var builder = new StringBuilder(normalized.Length);
            foreach (var line in lines)
                builder.Append(line.Trim()).Append('\n');
            return builder.ToString();
        }

        private static string BuildArchitectureMember(string indent, string architectureTypeFullName)
        {
            var builder = new StringBuilder();
            if (string.IsNullOrEmpty(architectureTypeFullName))
            {
                builder.Append(indent).AppendLine("/// <summary>No architecture was selected during generation; return the owning Architecture here.</summary>");
                builder.Append(indent).Append("public override ").Append(EditorFrameworkNames.Global(EditorFrameworkNames.IArchitecture)).AppendLine(" GetArchitecture()");
                builder.Append(indent).AppendLine("{");
                builder.Append(indent).Append("    throw new ").Append("global::System.NotImplementedException")
                    .AppendLine("(\"No architecture was selected for this generated window.\");");
                builder.Append(indent).Append('}');
                return builder.ToString();
            }

            builder.Append(indent).Append("/// <summary>Architecture selected when this window was generated: ").Append(architectureTypeFullName).AppendLine(".</summary>");
            builder.Append(indent).Append("public override ").Append(EditorFrameworkNames.Global(EditorFrameworkNames.IArchitecture)).AppendLine(" GetArchitecture()");
            builder.Append(indent).Append("    => ").Append(EditorFrameworkNames.Global(architectureTypeFullName)).Append(".Interface;");
            return builder.ToString();
        }

        private static string BuildLevelMember(string indent, string level)
        {
            var builder = new StringBuilder();
            builder.Append(indent).AppendLine("/// <summary>UI level selected when this window was generated.</summary>");
            builder.Append(indent).Append("public override ").Append(EditorFrameworkNames.Global(EditorFrameworkNames.EUILevel)).AppendLine(" Level");
            builder.Append(indent).Append("    => ").Append(EditorFrameworkNames.Global(EditorFrameworkNames.EUILevel)).Append('.').Append(level).AppendLine(";");
            return builder.ToString();
        }

        private static string BuildFieldDeclaration(string indent, string viewName, WindowFieldMember field)
        {
            var builder = new StringBuilder();
            builder.Append(indent).Append('[').Append(EditorFrameworkNames.Global(EditorFrameworkNames.UnitySerializeField)).AppendLine("]");
            if (field.IsCommand)
            {
                builder.Append(indent).Append('[').Append(EditorFrameworkNames.Global(EditorFrameworkNames.UIBindCommandAttribute))
                    .Append("(nameof(").Append(viewName).Append('.').Append(field.CommandName).AppendLine("))]");
            }
            else
            {
                var attribute = new StringBuilder();
                attribute.Append(EditorFrameworkNames.Global(EditorFrameworkNames.UIBindDataAttribute))
                    .Append("(nameof(").Append(viewName).Append('.').Append(field.ViewTarget).Append(')');
                if (field.Row.EffectiveMode == UIBindMode.TwoWay)
                    attribute.Append(", Mode = ").Append(EditorFrameworkNames.Global(EditorFrameworkNames.UIBindMode)).Append(".TwoWay");
                if (!string.IsNullOrEmpty(field.Row.Format))
                    attribute.Append(", Format = ").Append(EditorNaming.ToCSharpStringLiteral(field.Row.Format));
                attribute.Append(')');
                builder.Append(indent).Append('[').Append(attribute).AppendLine("]");
            }

            builder.Append(indent).Append("private ").Append(field.ControlTypeName).Append(' ').Append(field.FieldName).Append(';');
            return builder.ToString();
        }
    }
}
