#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ObFramework.Editor.Core
{
    /// <summary>
    /// Pure entry point for Unity Editor CDE generation: validates a
    /// <see cref="EditorGenerationRequest"/>, builds the editable View/window/contract templates
    /// (with incremental, non-destructive regeneration) and returns structured diagnostics.
    /// <para>
    /// This type has no Unity or file system dependency: the Editor glue supplies the current file
    /// contents and writes the returned <see cref="GeneratedFile"/> contents through the
    /// AssetDatabase. Tests exercise it from ordinary .NET.
    /// </para>
    /// </summary>
    public static class EditorTemplateGenerator
    {
        /// <summary>Generates/updates the editable View, window (VC) and contract sources.</summary>
        public static EditorGenerationResult Generate(EditorGenerationRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var diagnostics = new List<EditorDiagnostic>();
            var changes = new List<DestructiveChange>();
            var prepared = Prepare(request, diagnostics);

            if (HasErrors(diagnostics))
                return new EditorGenerationResult(Array.Empty<GeneratedFile>(), diagnostics, changes);

            var files = new List<GeneratedFile>();

            var viewFile = ViewTemplateGenerator.Generate(
                request,
                prepared.ViewName,
                prepared.Namespace,
                prepared.ViewPath!,
                prepared.States,
                prepared.Commands,
                diagnostics);
            if (viewFile != null)
                files.Add(viewFile);

            var windowFile = WindowTemplateGenerator.Generate(
                request,
                prepared.ViewName,
                prepared.WindowName,
                prepared.ContractName,
                prepared.Namespace,
                prepared.ArchitectureTypeFullName,
                prepared.Level,
                prepared.WindowPath!,
                prepared.Fields,
                changes,
                diagnostics);
            if (windowFile != null)
                files.Add(windowFile);

            if (request.GenerateContractInterface)
            {
                var contractFile = GenerateContract(request, prepared.ContractName, prepared.Namespace, prepared.ContractPath, diagnostics);                if (contractFile != null)
                    files.Add(contractFile);
            }

            if (HasErrors(diagnostics))
                return new EditorGenerationResult(Array.Empty<GeneratedFile>(), diagnostics, changes);

            return new EditorGenerationResult(files, diagnostics, changes);
        }

        private static PreparedGeneration Prepare(EditorGenerationRequest request, ICollection<EditorDiagnostic> diagnostics)
        {
            var prepared = new PreparedGeneration();

            var viewName = (request.ViewName ?? string.Empty).Trim();
            if (viewName.Length == 0)
            {
                diagnostics.Add(Error(EditorDiagnosticCodes.InvalidIdentifier, "A business View name is required (for example 'ShopView').", "ViewName"));
                viewName = "GeneratedView";
            }
            else if (!EditorNaming.IsValidIdentifier(viewName))
            {
                diagnostics.Add(Error(EditorDiagnosticCodes.InvalidIdentifier, "'" + viewName + "' is not a valid C# View class name.", "ViewName"));
            }
            prepared.ViewName = viewName;

            var windowName = (request.WindowName ?? string.Empty).Trim();
            if (windowName.Length == 0)
                windowName = EditorNaming.DefaultWindowName(viewName);
            if (!EditorNaming.IsValidIdentifier(windowName))
            {
                diagnostics.Add(Error(EditorDiagnosticCodes.InvalidIdentifier, "'" + windowName + "' is not a valid C# window class name.", "WindowName"));
            }
            prepared.WindowName = windowName;

            var @namespace = (request.Namespace ?? string.Empty).Trim();
            if (!EditorNaming.IsValidNamespace(@namespace))
            {
                diagnostics.Add(Error(EditorDiagnosticCodes.InvalidIdentifier, "'" + @namespace + "' is not a valid C# namespace.", "Namespace"));
            }
            prepared.Namespace = @namespace;

            var contractName = (request.ContractName ?? string.Empty).Trim();
            if (contractName.Length == 0)
                contractName = EditorNaming.DefaultContractName(viewName);
            if (!EditorNaming.IsValidIdentifier(contractName))
            {
                diagnostics.Add(Error(EditorDiagnosticCodes.InvalidIdentifier, "'" + contractName + "' is not a valid C# contract interface name.", "ContractName"));
            }
            prepared.ContractName = contractName;

            var level = (request.Level ?? string.Empty).Trim();
            if (level.Length == 0)
                level = "Common";
            if (!EditorNaming.TryNormalizeLevel(level, out var normalizedLevel))
            {
                diagnostics.Add(Error(EditorDiagnosticCodes.InvalidLevel, "'" + level + "' is not a Unity UI level. Use Scene, Common or Overlay.", "Level"));
                normalizedLevel = "Common";
            }
            prepared.Level = normalizedLevel;

            var architecture = (request.ArchitectureTypeFullName ?? string.Empty).Trim();
            if (architecture.Length == 0)
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Warning,
                    EditorDiagnosticCodes.ArchitectureNotSelected,
                    "No architecture was selected; the generated window will throw NotImplementedException in GetArchitecture() until it is selected.",
                    "Architecture"));
            }
            else if (!EditorNaming.IsValidQualifiedName(architecture))
            {
                diagnostics.Add(Error(EditorDiagnosticCodes.InvalidIdentifier,
                    "'" + architecture + "' is not a valid fully qualified architecture type name (expected e.g. 'Game.GameArchitecture').",
                    "Architecture"));
            }
            prepared.ArchitectureTypeFullName = architecture;

            if (!string.IsNullOrWhiteSpace(request.PrefabPath) &&
                !UISetupPaths.TryNormalizeModuleRelative(request.PrefabPath, "Prefab", out _, out var prefabError))
            {
                diagnostics.Add(Error(EditorDiagnosticCodes.InvalidPrefabPath, prefabError!, "Prefab"));
            }

            prepared.ViewPath = ResolveOutputPath(request.ViewOutputPath, request.OutputDirectory, viewName + ".cs", "View", diagnostics);
            prepared.WindowPath = ResolveOutputPath(request.WindowOutputPath, request.OutputDirectory, windowName + ".cs", "window", diagnostics);
            if (request.GenerateContractInterface)
                prepared.ContractPath = ResolveOutputPath(request.ContractOutputPath, request.OutputDirectory, contractName + ".cs", "contract", diagnostics);

            PrepareRows(request, prepared, diagnostics);

            if (prepared.Fields.Count == 0)
            {
                diagnostics.Add(new EditorDiagnostic(
                    EditorDiagnosticSeverity.Info,
                    EditorDiagnosticCodes.EmptyBindingTable,
                    "The binding table has no usable rows; empty editable templates will be generated.",
                    viewName));
            }

            return prepared;
        }

        private static void PrepareRows(
            EditorGenerationRequest request,
            PreparedGeneration prepared,
            ICollection<EditorDiagnostic> diagnostics)
        {
            var rows = request.Rows ?? Array.Empty<BindingTableRow>();
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            var stateByProperty = new Dictionary<string, ViewStateMember>(StringComparer.Ordinal);
            var commandByMethod = new Dictionary<string, ViewCommandMember>(StringComparer.Ordinal);

            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                var rowLabel = "row " + (index + 1).ToString();
                if (row == null)
                {
                    diagnostics.Add(Error(EditorDiagnosticCodes.InvalidBindingRow, "Binding table " + rowLabel + " is null.", rowLabel));
                    continue;
                }

                var componentName = (row.ComponentName ?? string.Empty).Trim();
                if (componentName.Length == 0)
                {
                    diagnostics.Add(Error(
                        EditorDiagnosticCodes.InvalidBindingRow,
                        "Binding table " + rowLabel + " has no component reference. Select a Prefab control and add it to the table again.",
                        rowLabel));
                    continue;
                }

                if (!UIComponentKindInfo.IsSupported(row.Kind))
                {
                    diagnostics.Add(Error(
                        EditorDiagnosticCodes.UnsupportedControlKind,
                        "Binding table " + rowLabel + " ('" + componentName + "') uses an unsupported control kind '" + row.Kind +
                        "'. Supported kinds: Button, Text, TMP_Text, InputField, TMP_InputField, Toggle, Slider.",
                        componentName));
                    continue;
                }

                var fieldName = string.IsNullOrWhiteSpace(row.FieldName)
                    ? EditorNaming.DefaultFieldName(componentName)
                    : row.FieldName!.Trim();
                if (!EditorNaming.IsValidIdentifier(fieldName))
                {
                    diagnostics.Add(Error(
                        EditorDiagnosticCodes.InvalidIdentifier,
                        "Binding table " + rowLabel + " ('" + componentName + "') field name '" + fieldName + "' is not a valid C# field name.",
                        componentName));
                    continue;
                }

                if (!fieldNames.Add(fieldName))
                {
                    diagnostics.Add(Error(
                        EditorDiagnosticCodes.DuplicateBinding,
                        "Binding table " + rowLabel + " ('" + componentName + "') reuses serialized field name '" + fieldName + "'.",
                        componentName));
                    continue;
                }

                var prefabPath = !string.IsNullOrWhiteSpace(row.PrefabPath) ? row.PrefabPath : request.PrefabPath;
                if (string.IsNullOrWhiteSpace(prefabPath))
                {
                    diagnostics.Add(new EditorDiagnostic(
                        EditorDiagnosticSeverity.Warning,
                        EditorDiagnosticCodes.MissingPrefabReference,
                        "Binding table " + rowLabel + " ('" + componentName + "') has no Prefab reference; the Editor cannot assign the serialized field automatically.",
                        componentName));
                }
                else if (!UISetupPaths.TryNormalizeModuleRelative(prefabPath, "binding row Prefab", out var normalizedPrefab, out var prefabError))
                {
                    diagnostics.Add(Error(EditorDiagnosticCodes.InvalidPrefabPath, prefabError!, componentName));
                }
                else if (!normalizedPrefab.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(Error(
                        EditorDiagnosticCodes.InvalidPrefabPath,
                        "Binding table " + rowLabel + " ('" + componentName + "') Prefab path '" + normalizedPrefab + "' must point to a .prefab asset.",
                        componentName));
                }

                var controlTypeName = ResolveControlTypeName(request, row.Kind);
                if (controlTypeName == null)
                {
                    diagnostics.Add(Error(
                        EditorDiagnosticCodes.UnsupportedControlKind,
                        "No Unity control type is configured for control kind '" + row.Kind + "'.",
                        componentName));
                    continue;
                }

                if (UIComponentKindInfo.IsCommand(row.Kind))
                {
                    var commandTarget = !string.IsNullOrWhiteSpace(row.ViewMemberName)
                        ? row.ViewMemberName!.Trim()
                        : EditorNaming.DefaultCommandName(componentName);
                    if (!EditorNaming.TryResolveCommandNames(commandTarget, out var methodName, out var commandName))
                    {
                        diagnostics.Add(Error(
                            EditorDiagnosticCodes.InvalidIdentifier,
                            "Binding table " + rowLabel + " ('" + componentName + "') has an invalid command target '" + commandTarget +
                            "'. Use a View method name such as 'Submit', 'SubmitAsync' or the generated command name 'SubmitCommand'.",
                            componentName));
                        continue;
                    }

                    if (!commandByMethod.ContainsKey(methodName))
                    {
                        var command = new ViewCommandMember
                        {
                            MethodName = methodName,
                            CommandName = commandName,
                            TargetText = commandTarget,
                        };
                        commandByMethod[methodName] = command;
                        prepared.Commands.Add(command);
                    }

                    prepared.Fields.Add(new WindowFieldMember
                    {
                        FieldName = fieldName,
                        Row = row,
                        ControlTypeName = controlTypeName,
                        IsCommand = true,
                        CommandName = commandByMethod[methodName].CommandName,
                        ViewTarget = commandTarget,
                    });
                    continue;
                }

                var propertyName = !string.IsNullOrWhiteSpace(row.ViewMemberName)
                    ? row.ViewMemberName!.Trim()
                    : EditorNaming.DefaultViewMemberName(componentName);
                if (!EditorNaming.IsValidIdentifier(propertyName))
                {
                    diagnostics.Add(Error(
                        EditorDiagnosticCodes.InvalidIdentifier,
                        "Binding table " + rowLabel + " ('" + componentName + "') has an invalid View property name '" + propertyName + "'.",
                        componentName));
                    continue;
                }

                var stateType = UIComponentKindInfo.ViewStateTypeName(row.Kind);
                if (stateType == null)
                {
                    diagnostics.Add(Error(
                        EditorDiagnosticCodes.UnsupportedControlKind,
                        "Control kind '" + row.Kind + "' has no View observable state type.",
                        componentName));
                    continue;
                }

                if (!stateByProperty.TryGetValue(propertyName, out var state))
                {
                    state = new ViewStateMember
                    {
                        PropertyName = propertyName,
                        FieldName = "_" + EditorNaming.ToCamelCase(propertyName),
                        StateType = stateType,
                        Initializer = stateType == "string" ? "string.Empty" : null,
                        Kind = row.Kind,
                    };
                    stateByProperty[propertyName] = state;
                    prepared.States.Add(state);
                }
                else if (!string.Equals(state.StateType, stateType, StringComparison.Ordinal))
                {
                    diagnostics.Add(Error(
                        EditorDiagnosticCodes.DuplicateBinding,
                        "View property '" + propertyName + "' is bound to incompatible control kinds ('" + state.Kind + "' and '" + row.Kind + "').",
                        propertyName));
                    continue;
                }

                prepared.Fields.Add(new WindowFieldMember
                {
                    FieldName = fieldName,
                    Row = row,
                    ControlTypeName = controlTypeName,
                    IsCommand = false,
                    ViewTarget = propertyName,
                });
            }
        }

        private static string? ResolveControlTypeName(EditorGenerationRequest request, UIComponentKind kind)
        {
            if (request.ControlTypeNameOverrides != null &&
                request.ControlTypeNameOverrides.TryGetValue(kind, out var overridden) &&
                !string.IsNullOrWhiteSpace(overridden))
            {
                var value = overridden!.Trim();
                return value.StartsWith("global::", StringComparison.Ordinal) ? value : "global::" + value;
            }

            return UIComponentKindInfo.UnityControlTypeName(kind);
        }

        private static string? ResolveOutputPath(
            string? explicitPath,
            string? outputDirectory,
            string fileName,
            string description,
            ICollection<EditorDiagnostic> diagnostics)
        {
            string candidate;
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                candidate = explicitPath!.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                if (!UISetupPaths.TryNormalizeModuleRelative(outputDirectory, description + " output directory", out var directory, out var directoryError))
                {
                    diagnostics.Add(Error(EditorDiagnosticCodes.InvalidOutputPath, directoryError!, description));
                    return null;
                }

                candidate = directory + "/" + fileName;
            }
            else
            {
                diagnostics.Add(Error(
                    EditorDiagnosticCodes.InvalidOutputPath,
                    "No " + description + " output path was supplied and the output directory is empty. " +
                    "Configure the setup.json unity scriptOutputRoot or pick an output folder in the Editor.",
                    description));
                return null;
            }

            if (!UISetupPaths.TryNormalizeProjectFile(candidate, description + " output path", out var normalized, out var error))
            {
                diagnostics.Add(Error(EditorDiagnosticCodes.InvalidOutputPath, error!, description));
                return null;
            }

            return normalized;
        }

        private static GeneratedFile? GenerateContract(
            EditorGenerationRequest request,
            string contractName,
            string @namespace,
            string? contractPath,
            ICollection<EditorDiagnostic> diagnostics)
        {
            if (contractPath == null)
                return null;

            if (!string.IsNullOrWhiteSpace(request.ExistingContractSource))
            {
                var existing = request.ExistingContractSource!;
                if (!Regex.IsMatch(existing, @"\bpartial\s+interface\s+" + Regex.Escape(contractName) + @"\b", RegexOptions.CultureInvariant))
                {
                    diagnostics.Add(Error(
                        EditorDiagnosticCodes.ExistingSourceNotEditable,
                        "The existing contract source does not declare 'partial interface " + contractName +
                        "'. Add the partial modifier or disable contract interface generation and author the contract elsewhere.",
                        contractPath));
                    return null;
                }

                return new GeneratedFile(contractPath, GeneratedFileKind.Contract, existing, isModified: false);
            }

            var builder = new StringBuilder();
            builder.AppendLine("// ------------------------------------------------------------------------------");
            builder.AppendLine("// OBF UI 2.0 editable View contract implemented by the generated Unity window.");
            builder.AppendLine("// Extend it with the UI operations the business View needs from its window (optional).");
            builder.AppendLine("// ------------------------------------------------------------------------------");
            var hasNamespace = !string.IsNullOrEmpty(@namespace);
            var classIndent = hasNamespace ? "    " : string.Empty;
            if (hasNamespace)
            {
                builder.Append("namespace ").Append(@namespace).AppendLine();
                builder.AppendLine("{");
            }

            builder.Append(classIndent).AppendLine("/// <summary>UI contract implemented by the generated window; extend with UI-facing members when the View needs them.</summary>");
            builder.Append(classIndent).Append("public partial interface ").Append(contractName).AppendLine();
            builder.Append(classIndent).AppendLine("{");
            builder.Append(classIndent).AppendLine("}");
            if (hasNamespace)
                builder.AppendLine("}");
            return new GeneratedFile(contractPath, GeneratedFileKind.Contract, builder.ToString(), isModified: true);
        }

        private static EditorDiagnostic Error(string code, string message, string target)
            => new EditorDiagnostic(EditorDiagnosticSeverity.Error, code, message, target);

        private static bool HasErrors(IReadOnlyCollection<EditorDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.IsError)
                    return true;
            }

            return false;
        }

        private sealed class PreparedGeneration
        {
            public string ViewName = string.Empty;
            public string WindowName = string.Empty;
            public string Namespace = string.Empty;
            public string ContractName = string.Empty;
            public string ArchitectureTypeFullName = string.Empty;
            public string Level = "Common";
            public string? ViewPath;
            public string? WindowPath;
            public string? ContractPath;
            public readonly List<WindowFieldMember> Fields = new List<WindowFieldMember>();
            public readonly List<ViewStateMember> States = new List<ViewStateMember>();
            public readonly List<ViewCommandMember> Commands = new List<ViewCommandMember>();
        }
    }
}
