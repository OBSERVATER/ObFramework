#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ObFramework.Editor.Core
{
    /// <summary>Kind of one emitted editable/generated source file.</summary>
    public enum GeneratedFileKind
    {
        /// <summary>Editable business View (<c>partial class XView : AUIView</c>).</summary>
        View = 0,

        /// <summary>Editable Unity window controller (<c>partial class XWindow : AUIWindow</c>).</summary>
        Window = 1,

        /// <summary>Editable View contract interface implemented by the window.</summary>
        Contract = 2,

        /// <summary>Replaceable static UI registration source generated from setup.json.</summary>
        Registry = 3,
    }

    /// <summary>One source file produced by editor generation.</summary>
    public sealed class GeneratedFile
    {
        public GeneratedFile(string path, GeneratedFileKind kind, string content, bool isModified)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A generated file path is required.", nameof(path));
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            Path = path.Replace('\\', '/');
            Kind = kind;
            Content = content;
            IsModified = isModified;
        }

        /// <summary>Project-relative forward-slash path, for example <c>Assets/Generated/UI/ShopView.cs</c>.</summary>
        public string Path { get; }

        /// <summary>File role.</summary>
        public GeneratedFileKind Kind { get; }

        /// <summary>Complete file content; callers write it through Unity's AssetDatabase.</summary>
        public string Content { get; }

        /// <summary>
        /// False when the file content is byte-identical to the requested existing source (the
        /// Editor can skip writing/importing it).
        /// </summary>
        public bool IsModified { get; }
    }

    /// <summary>Kind of a destructive binding change detected while regenerating an editable window.</summary>
    public enum DestructiveChangeKind
    {
        /// <summary>The same serialized field now binds a different View member.</summary>
        Rename = 0,

        /// <summary>A generated serialized field is no longer part of the binding table.</summary>
        Delete = 1,
    }

    /// <summary>
    /// One destructive change to an existing editable window file. The generator never applies it
    /// silently: unconfirmed changes are only described here (and reported as a warning diagnostic)
    /// so the Editor can show a preview dialog. Confirmed ids are applied and flagged
    /// <see cref="Applied"/>.
    /// </summary>
    public sealed class DestructiveChange
    {
        public DestructiveChange(
            string id,
            DestructiveChangeKind kind,
            string description,
            string target,
            string? before = null,
            string? after = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A destructive change id is required.", nameof(id));

            Id = id;
            Kind = kind;
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Before = before;
            After = after;
        }

        /// <summary>Stable id passed back through <see cref="EditorGenerationRequest.ConfirmedDestructiveChangeIds"/>.</summary>
        public string Id { get; }

        /// <summary>Rename or delete.</summary>
        public DestructiveChangeKind Kind { get; }

        /// <summary>Human readable preview text for the confirmation dialog.</summary>
        public string Description { get; }

        /// <summary>File/member the change applies to (for example <c>ShopWindow.submitButton</c>).</summary>
        public string Target { get; }

        /// <summary>Previous View member/target, when known.</summary>
        public string? Before { get; }

        /// <summary>New View member/target, when known.</summary>
        public string? After { get; }

        /// <summary>True when the user confirmed this change and the generator applied it.</summary>
        public bool Applied { get; internal set; }

        public override string ToString()
        {
            var state = Applied ? "applied" : "pending";
            return Kind + " (" + state + "): " + Description;
        }
    }

    /// <summary>
    /// Result of <see cref="EditorTemplateGenerator.Generate"/>: generated files, structured
    /// diagnostics and the destructive change preview. <see cref="Success"/> is false when any
    /// <see cref="EditorDiagnosticSeverity.Error"/> was reported; callers must then not write files.
    /// </summary>
    public sealed class EditorGenerationResult
    {
        private const string WindowFieldChangePrefix = "window-field:";

        public EditorGenerationResult(
            IReadOnlyList<GeneratedFile> files,
            IReadOnlyList<EditorDiagnostic> diagnostics,
            IReadOnlyList<DestructiveChange> destructiveChanges)
        {
            if (files == null)
                throw new ArgumentNullException(nameof(files));
            if (diagnostics == null)
                throw new ArgumentNullException(nameof(diagnostics));
            if (destructiveChanges == null)
                throw new ArgumentNullException(nameof(destructiveChanges));

            Files = files;
            Diagnostics = diagnostics;
            DestructiveChanges = destructiveChanges;
        }

        /// <summary>Files to write when <see cref="Success"/> is true (existing unchanged files included with <see cref="GeneratedFile.IsModified"/> false).</summary>
        public IReadOnlyList<GeneratedFile> Files { get; }

        /// <summary>Structured diagnostics, ordered as produced.</summary>
        public IReadOnlyList<EditorDiagnostic> Diagnostics { get; }

        /// <summary>Destructive renames/deletes detected against the existing window source.</summary>
        public IReadOnlyList<DestructiveChange> DestructiveChanges { get; }

        /// <summary>True when no error diagnostic was reported.</summary>
        public bool Success => Diagnostics.All(diagnostic => !diagnostic.IsError);

        /// <summary>True when a destructive change was detected but not confirmed/applied.</summary>
        public bool HasPendingDestructiveChanges => DestructiveChanges.Any(change => !change.Applied);

        /// <summary>Only the error diagnostics (convenience for Editor dialogs).</summary>
        public IReadOnlyList<EditorDiagnostic> Errors
            => Diagnostics.Where(diagnostic => diagnostic.IsError).ToArray();

        /// <summary>Builds the stable change id for one generated window serialized field.</summary>
        public static string WindowFieldChangeId(string fieldName) => WindowFieldChangePrefix + fieldName;
    }
}
