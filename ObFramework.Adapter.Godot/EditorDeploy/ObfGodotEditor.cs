#if TOOLS
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;
using ObFramework.Editor.Core;

[Tool]
public partial class ObfGodotEditor : EditorPlugin
{
    private const string Menu = "OBF: Generate UI";
    private const string TablePath = "res://obf-ui.json";
    private static readonly Dictionary<string, string> Controls = new(StringComparer.Ordinal)
    {
        ["Label"] = "Label", ["Button"] = "Button", ["LineEdit"] = "LineEdit",
        ["CheckBox"] = "CheckBox", ["CheckButton"] = "CheckButton",
        ["HSlider"] = "HSlider", ["VSlider"] = "VSlider"
    };

    public override void _EnterTree() => AddToolMenuItem(Menu, Callable.From(Generate));
    public override void _ExitTree() => RemoveToolMenuItem(Menu);

    private void Generate()
    {
        try
        {
            var root = ProjectSettings.GlobalizePath("res://");
            var setup = ReadSetup(root);
            var table = JsonSerializer.Deserialize<BindingTable>(File.ReadAllText(ProjectSettings.GlobalizePath(TablePath)),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Empty UI table.");
            if (!EditorNaming.IsValidNamespace(table.RegistryNamespace)) throw new InvalidDataException("Invalid registry namespace.");
            var output = ResolvePath(root, setup.ScriptOutputRoot);
            var registrations = new List<UIRegistryResolvedEntry>();
            var pending = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var seenViews = new HashSet<string>(StringComparer.Ordinal);
            var seenScenes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var window in table.Windows)
            {
                Validate(window);
                var view = Qualify(window.Namespace, window.View);
                var vc = Qualify(window.Namespace, window.Window);
                if (!seenViews.Add(view) || !seenScenes.Add(window.Scene))
                    throw new InvalidDataException($"Duplicate View or scene: {view}, {window.Scene}");
                if (!window.Scene.StartsWith("res://", StringComparison.Ordinal) || !window.Scene.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Invalid scene: {window.Scene}");
                var sceneFile = ResolvePath(root, window.Scene.Substring(6));
                if (!File.Exists(sceneFile)) throw new FileNotFoundException("UI scene does not exist.", sceneFile);
                var directory = Path.Combine(output, window.Namespace.Replace('.', Path.DirectorySeparatorChar));
                var path = Path.Combine(directory, window.Window + ".cs");
                if (window.GenerateWindow && !pending.TryAdd(path, BuildWindow(window)))
                    throw new InvalidDataException($"Duplicate window output: {path}");
                registrations.Add(new UIRegistryResolvedEntry(view, vc, window.View, window.Scene));
            }
            var registry = Path.Combine(output, "GeneratedUIRegistry.g.cs");
            if (pending.ContainsKey(registry)) throw new InvalidDataException("Registry path collides with window output.");
            foreach (var pair in pending)
            {
                if (File.Exists(pair.Key)) { GD.Print($"OBF: kept existing {pair.Key}"); continue; }
                Write(pair.Key, pair.Value);
            }
            var source = UIRegistrySourceGenerator.BuildSource(table.RegistryNamespace, "GeneratedUIRegistry", "RegisterAll",
                "ObFramework.Adapter.Godot.UI.GodotUIAdapter", registrations)
                .Replace("Unity Editor tooling", "Godot editor tooling").Replace("Explicit Unity", "Explicit Godot")
                .Replace("Tools/OBF/UI/Generate UI Registry", "Project/Tools/OBF: Generate UI");
            if (!File.Exists(registry) || File.ReadAllText(registry) != source) Write(registry, source);
            EditorInterface.Singleton.GetResourceFilesystem().Scan();
            GD.Print($"OBF: {registrations.Count} UI registrations generated. Build, attach the window script, and assign exports in Inspector.");
        }
        catch (Exception error) { GD.PushError(error.ToString()); }
    }

    private static string BuildWindow(WindowSpec w)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Godot;").AppendLine("using ObFramework.Core;")
            .AppendLine("using ObFramework.Integrated.UI;").AppendLine("using ObFramework.Adapter.Godot.UI;")
            .AppendLine("using ObFramework.Adapter.Godot.ViewBinding;");
        if (w.Namespace.Length > 0) sb.AppendLine($"namespace {w.Namespace}").AppendLine("{");
        sb.AppendLine($"[BindView(typeof({w.View}), typeof(global::{w.Contract}))]")
            .AppendLine($"public partial class {w.Window} : AUIWindow, global::{w.Contract}").AppendLine("{")
            .AppendLine($"    public override IArchitecture GetArchitecture() => global::{w.Architecture}.Interface;")
            .AppendLine($"    public override EUILevel Level => EUILevel.{w.Level};");
        foreach (var b in w.Bindings)
        {
            sb.AppendLine("    [Export]");
            if (b.Command)
                sb.AppendLine($"    [UIBindCommand(nameof({w.View}.{b.Member}))]");
            else
            {
                sb.Append($"    [UIBindData(nameof({w.View}.{b.Member})");
                if (b.TwoWay) sb.Append(", Mode = UIBindMode.TwoWay");
                if (b.Format != null) sb.Append(", Format = ").Append(EditorNaming.ToCSharpStringLiteral(b.Format));
                sb.AppendLine(")]");
            }
            sb.AppendLine($"    private {Controls[b.Control]} {b.Field} = null!;");
        }
        sb.AppendLine("}");
        if (w.Namespace.Length > 0) sb.AppendLine("}");
        return sb.ToString();
    }

    private static void Validate(WindowSpec w)
    {
        if (!EditorNaming.IsValidNamespace(w.Namespace) || !EditorNaming.IsValidIdentifier(w.View) ||
            !EditorNaming.IsValidIdentifier(w.Window) || !EditorNaming.IsValidQualifiedName(w.Contract) ||
            !EditorNaming.IsValidQualifiedName(w.Architecture) || w.View == w.Window ||
            !EditorNaming.TryNormalizeLevel(w.Level, out var level))
            throw new InvalidDataException($"Invalid window definition: {w.Window}");
        w.Level = level;
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in w.Bindings)
        {
            if (!Controls.ContainsKey(b.Control) || !EditorNaming.IsValidIdentifier(b.Field) ||
                !fields.Add(b.Field) || !EditorNaming.IsValidIdentifier(b.Member) ||
                (b.Command && (b.Control == "Label" || b.TwoWay || b.Format != null || !b.Member.EndsWith("Command", StringComparison.Ordinal))) ||
                (!b.Command && (b.Control == "Button" || (b.Control == "Label" && b.TwoWay))))
                throw new InvalidDataException($"Invalid binding: {w.Window}.{b.Field}");
        }
    }

    private static GodotSetup ReadSetup(string root)
    {
        var path = Path.Combine(root, "setup.json");
        if (!File.Exists(path)) return new GodotSetup();
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        if (json.RootElement.TryGetProperty("modules", out var modules) && modules.TryGetProperty("godot", out var godot))
            return JsonSerializer.Deserialize<GodotSetup>(godot.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new GodotSetup();
        return new GodotSetup();
    }

    private static string ResolvePath(string root, string relative)
    {
        if (!UISetupPaths.TryNormalizeModuleRelative(relative, "Godot path", out var normalized, out var error))
            throw new InvalidDataException(error);
        return Path.Combine(root, normalized);
    }
    private static string Qualify(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
    private static void Write(string path, string source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source, new UTF8Encoding(false));
    }

    public sealed class GodotSetup { public string ScriptOutputRoot { get; set; } = "Generated/UI"; }
    public sealed class BindingTable
    {
        public string RegistryNamespace { get; set; } = "Game.Generated.UI";
        public List<WindowSpec> Windows { get; set; } = new();
    }
    public sealed class WindowSpec
    {
        public string Namespace { get; set; } = "";
        public string View { get; set; } = "";
        public string Window { get; set; } = "";
        public string Contract { get; set; } = "";
        public string Architecture { get; set; } = "";
        public string Level { get; set; } = "Common";
        public string Scene { get; set; } = "";
        public bool GenerateWindow { get; set; } = true;
        public List<BindingSpec> Bindings { get; set; } = new();
    }
    public sealed class BindingSpec
    {
        public string Control { get; set; } = "";
        public string Field { get; set; } = "";
        public string Member { get; set; } = "";
        public bool Command { get; set; }
        public bool TwoWay { get; set; }
        public string? Format { get; set; }
    }
}
#endif
