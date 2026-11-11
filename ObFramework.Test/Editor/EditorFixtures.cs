using ObFramework.Editor.Core;

namespace ObFramework.Test.Editor;

/// <summary>Shared pure fixtures for the UI 2.0 Editor.Core tests (no Unity dependency).</summary>
internal static class EditorFixtures
{
    public const string PrefabPath = "Assets/ObFramework/UI/Shop.prefab";

    public static EditorGenerationRequest Request(params BindingTableRow[] rows)
    {
        var request = new EditorGenerationRequest
        {
            ViewName = "ShopView",
            Namespace = "Game.UI",
            ArchitectureTypeFullName = "Game.GameArchitecture",
            Level = "Common",
            OutputDirectory = "Assets/Generated/UI",
            PrefabPath = PrefabPath,
        };
        foreach (var row in rows)
            request.Rows.Add(row);
        return request;
    }

    public static BindingTableRow Row(
        UIComponentKind kind,
        string componentName,
        string fieldName,
        string viewMember,
        string? format = null,
        UIBindMode? mode = null)
    {
        return new BindingTableRow
        {
            ComponentName = componentName,
            Kind = kind,
            FieldName = fieldName,
            ViewMemberName = viewMember,
            Format = format,
            Mode = mode,
            PrefabPath = PrefabPath,
        };
    }

    public static UISetupSection Section(
        string? root = "D:/GameProject",
        string? scriptOutputRoot = "Assets/ObFramework/Generated/UI",
        string? uiAssetRoot = "Assets/ObFramework/UI",
        string? resourceKeyFormat = "UI/{view}",
        params (string View, string Key)[] overrides)
    {
        var section = new UISetupSection
        {
            Root = root,
            ScriptOutputRoot = scriptOutputRoot,
            UIAssetRoot = uiAssetRoot,
            ResourceKeyFormat = resourceKeyFormat,
        };
        if (overrides.Length > 0)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in overrides)
                map[pair.View] = pair.Key;
            section.ResourceKeyOverrides = map;
        }

        return section;
    }

    public static UISetupSnapshot Snapshot(params (string View, string Key)[] overrides)
    {
        var result = UISetupSnapshotFactory.Create(Section(overrides: overrides));
        Assert.That(result.Success, Is.True, FormatDiagnostics(result.Diagnostics));
        return result.Snapshot!;
    }

    public static string FormatDiagnostics(IEnumerable<EditorDiagnostic> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));

    public static EditorDiagnostic? SingleCode(IReadOnlyList<EditorDiagnostic> diagnostics, string code)
        => diagnostics.FirstOrDefault(diagnostic => string.Equals(diagnostic.Code, code, StringComparison.Ordinal));

    public static GeneratedFile File(EditorGenerationResult result, GeneratedFileKind kind)
        => result.Files.Single(file => file.Kind == kind);
}
