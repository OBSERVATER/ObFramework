using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ObFramework.Cli.Config
{
    /// <summary>
    /// Typed model of one <c>setup.json</c> document.
    /// <para>
    /// Document shape:
    /// <code>
    /// {
    ///   "root": "D:/GameProject",          // absolute development root
    ///   "modules": {
    ///     "unity": { "scriptOutputRoot": "Assets/Generated/UI" }
    ///   }
    /// }
    /// </code>
    /// The document is kept as a raw JSON tree so that unknown top-level properties and
    /// unregistered/disabled module sections are preserved verbatim. Writing a module section only
    /// replaces <c>modules.&lt;section&gt;</c>; nothing else in the file is touched.
    /// </para>
    /// The Config CLI is a pure .NET file-editing capability; it is intentionally independent of
    /// the runtime <c>ConfigPlugin</c>, diagnostics.json and DiagnosticsCommand.
    /// </summary>
    public sealed class SetupJsonDocument
    {
        /// <summary>Default discovery file name.</summary>
        public const string DefaultFileName = "setup.json";

        /// <summary>Absolute development root property.</summary>
        public const string RootPropertyName = "root";

        /// <summary>Container property for module sections.</summary>
        public const string ModulesPropertyName = "modules";

        private const string TempFileSuffix = ".tmp";

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static readonly JsonSerializerOptions ModuleJsonOptions = CreateModuleJsonOptions();

        private readonly JsonObject _document;
        private string? _filePath;
        private bool _exists;

        private SetupJsonDocument(JsonObject document, string? filePath, bool exists)
        {
            _document = document;
            _filePath = filePath;
            _exists = exists;
        }

        /// <summary>Absolute path of the backing file; set by <see cref="Create"/> or <see cref="Load"/>.</summary>
        public string? FilePath => _filePath;

        /// <summary>Whether the backing file exists on disk.</summary>
        public bool Exists => _exists;

        /// <summary>Top-level property names in document order (including unknown properties).</summary>
        public IReadOnlyList<string> PropertyNames
            => _document.Select(static property => property.Key).ToArray();

        /// <summary>All module section names present in the document (registered or not).</summary>
        public IReadOnlyList<string> ModuleSectionNames
        {
            get
            {
                var modules = GetModulesObject(createIfMissing: false);
                return modules == null
                    ? Array.Empty<string>()
                    : modules.Select(static property => property.Key).ToArray();
            }
        }

        /// <summary>Creates an empty in-memory document bound to <paramref name="filePath"/>; call <see cref="Save"/> to persist.</summary>
        public static SetupJsonDocument Create(string filePath)
        {
            var fullPath = ResolveNewFilePath(filePath);
            return new SetupJsonDocument(new JsonObject(), fullPath, exists: false);
        }

        /// <summary>Parses JSON text without touching the file system.</summary>
        public static SetupJsonDocument Parse(string json, string? filePath = null)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            JsonObject document;
            try
            {
                var node = JsonNode.Parse(
                    json,
                    nodeOptions: null,
                    documentOptions: new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip,
                    });

                if (node == null)
                    throw new SetupConfigException($"Configuration is empty{FormatPathSuffix(filePath)}.");

                document = node as JsonObject
                    ?? throw new SetupConfigException(
                        $"Configuration root must be a JSON object{FormatPathSuffix(filePath)}.");
            }
            catch (JsonException ex)
            {
                throw new SetupConfigException(
                    $"Invalid JSON{FormatPathSuffix(filePath)}: {ex.Message}", ex);
            }
            catch (ArgumentException ex)
            {
                throw new SetupConfigException(
                    $"Invalid JSON{FormatPathSuffix(filePath)}: {ex.Message}", ex);
            }

            return new SetupJsonDocument(document, filePath == null ? null : Path.GetFullPath(filePath), exists: false);
        }

        /// <summary>Loads and parses an existing setup.json file.</summary>
        public static SetupJsonDocument Load(string filePath)
        {
            var fullPath = ResolveNewFilePath(filePath);

            string text;
            try
            {
                text = File.ReadAllText(fullPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                throw new SetupConfigException($"Failed to read configuration file '{fullPath}': {ex.Message}", ex);
            }

            var document = Parse(text, fullPath);
            document._exists = true;
            return document;
        }

        /// <summary>Loads the file when it exists, otherwise creates a new empty document.</summary>
        public static SetupJsonDocument LoadOrCreate(string filePath)
        {
            var fullPath = ResolveNewFilePath(filePath);
            return File.Exists(fullPath) ? Load(fullPath) : Create(fullPath);
        }

        /// <summary>Loads the file when it exists, otherwise returns <see langword="null"/>.</summary>
        public static SetupJsonDocument? TryLoad(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            var fullPath = Path.GetFullPath(filePath);
            return File.Exists(fullPath) ? Load(fullPath) : null;
        }

        /// <summary>
        /// Raw <c>root</c> value as stored; <see langword="null"/> when absent.
        /// Throws when the property exists but is not a JSON string.
        /// </summary>
        public string? GetRootValue()
        {
            if (!_document.TryGetPropertyValue(RootPropertyName, out var node) || node == null)
                return null;

            if (node is JsonValue value && value.TryGetValue<string>(out var text))
                return text;

            throw new SetupConfigException("setup.json 'root' must be a string.");
        }

        /// <summary>Writes the validated absolute <c>root</c>.</summary>
        public void SetRoot(string absoluteRoot)
            => _document[RootPropertyName] = SetupConfigPaths.ValidateAbsoluteRoot(absoluteRoot);

        /// <summary>Returns the validated absolute root; throws when missing or relative.</summary>
        public string GetAbsoluteRoot() => SetupConfigPaths.ValidateAbsoluteRoot(GetRootValue());

        /// <summary>Non-throwing form of <see cref="GetAbsoluteRoot"/>.</summary>
        public bool TryGetAbsoluteRoot(out string root, out string? error)
        {
            try
            {
                root = GetAbsoluteRoot();
                error = null;
                return true;
            }
            catch (SetupConfigException ex)
            {
                root = string.Empty;
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Whether the <c>modules</c> object contains <paramref name="sectionName"/>.</summary>
        public bool HasModuleSection(string sectionName)
        {
            var modules = GetModulesObject(createIfMissing: false);
            return modules != null && modules.ContainsKey(sectionName);
        }

        /// <summary>Deserializes one module section into <typeparamref name="TConfig"/>; null when absent.</summary>
        public TConfig? GetModuleConfig<TConfig>(string sectionName)
            where TConfig : class
        {
            var section = GetModuleSectionNode(sectionName);
            if (section == null)
                return null;

            try
            {
                return section.Deserialize<TConfig>(ModuleJsonOptions);
            }
            catch (JsonException ex)
            {
                throw new SetupConfigException(
                    $"Module section 'modules.{sectionName}' does not match {typeof(TConfig).FullName}: {ex.Message}",
                    ex);
            }
        }

        /// <summary>Deserializes the registered module section; null when absent.</summary>
        public object? GetModuleConfig(SetupConfigRegistration registration)
        {
            if (registration == null)
                throw new ArgumentNullException(nameof(registration));

            var section = GetModuleSectionNode(registration.SectionName);
            if (section == null)
                return null;

            try
            {
                return section.Deserialize(registration.ConfigType, ModuleJsonOptions);
            }
            catch (JsonException ex)
            {
                throw new SetupConfigException(
                    $"Module section 'modules.{registration.SectionName}' does not match " +
                    $"{registration.ConfigType.FullName}: {ex.Message}",
                    ex);
            }
        }

        /// <summary>Returns the registered module section or a default instance when absent.</summary>
        public object GetOrCreateModuleConfig(SetupConfigRegistration registration)
        {
            if (registration == null)
                throw new ArgumentNullException(nameof(registration));

            return GetModuleConfig(registration) ?? registration.CreateDefault();
        }

        /// <summary>Replaces only the <c>modules.&lt;sectionName&gt;</c> node.</summary>
        public void SetModuleConfig<TConfig>(string sectionName, TConfig config)
            where TConfig : class
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var section = SerializeModule(ValidateSectionName(sectionName), config, typeof(TConfig));
            GetModulesObject(createIfMissing: true)![sectionName] = section;
        }

        /// <summary>Replaces only the registered module section node.</summary>
        public void SetModuleConfig(SetupConfigRegistration registration, object config)
        {
            if (registration == null)
                throw new ArgumentNullException(nameof(registration));
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (!registration.ConfigType.IsInstanceOfType(config))
                throw new ArgumentException(
                    $"Configuration instance must be of type '{registration.ConfigType.FullName}'.", nameof(config));

            var section = SerializeModule(registration.SectionName, config, registration.ConfigType);
            GetModulesObject(createIfMissing: true)![registration.SectionName] = section;
        }

        /// <summary>Removes one module section. Returns whether it existed.</summary>
        public bool RemoveModuleConfig(string sectionName)
        {
            var modules = GetModulesObject(createIfMissing: false);
            if (modules == null || !modules.Remove(ValidateSectionName(sectionName)))
                return false;

            if (modules.Count == 0)
                _document.Remove(ModulesPropertyName);
            return true;
        }

        /// <summary>Serializes the whole document (indented, unknown content preserved).</summary>
        public string ToJsonString() => _document.ToJsonString(ModuleJsonOptions);

        /// <summary>Atomically writes the document to its backing path.</summary>
        public void Save()
        {
            if (string.IsNullOrWhiteSpace(_filePath))
                throw new SetupConfigException("Configuration file path is not set.");

            var fullPath = _filePath!;
            var json = ToJsonString() + Environment.NewLine;

            try
            {
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + TempFileSuffix;
                try
                {
                    File.WriteAllText(tempPath, json, Utf8NoBom);
                    File.Move(tempPath, fullPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                throw new SetupConfigException($"Failed to write configuration file '{fullPath}': {ex.Message}", ex);
            }

            _exists = true;
        }

        /// <summary>Binds the document to <paramref name="filePath"/> and writes it.</summary>
        public void Save(string filePath)
        {
            _filePath = ResolveNewFilePath(filePath);
            Save();
        }

        private static JsonObject SerializeModule(string sectionName, object config, Type configType)
        {
            try
            {
                var node = JsonSerializer.SerializeToNode(config, configType, ModuleJsonOptions);
                return node as JsonObject
                    ?? throw new SetupConfigException(
                        $"Module section '{sectionName}' must serialize to a JSON object.");
            }
            catch (JsonException ex)
            {
                throw new SetupConfigException(
                    $"Failed to serialize module section '{sectionName}': {ex.Message}", ex);
            }
        }

        private JsonObject? GetModuleSectionNode(string sectionName)
        {
            var section = ValidateSectionName(sectionName);
            var modules = GetModulesObject(createIfMissing: false);
            if (modules == null || !modules.TryGetPropertyValue(section, out var node) || node == null)
                return null;

            return node as JsonObject
                ?? throw new SetupConfigException(
                    $"setup.json section 'modules.{section}' must be a JSON object.");
        }

        private JsonObject? GetModulesObject(bool createIfMissing)
        {
            if (!_document.TryGetPropertyValue(ModulesPropertyName, out var node) || node == null)
            {
                if (!createIfMissing)
                    return null;

                var created = new JsonObject();
                _document[ModulesPropertyName] = created;
                return created;
            }

            return node as JsonObject
                ?? throw new SetupConfigException($"setup.json '{ModulesPropertyName}' must be a JSON object.");
        }

        private static string ResolveNewFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new SetupConfigException("Configuration file path must not be empty.");

            try
            {
                return Path.GetFullPath(filePath);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                throw new SetupConfigException($"Configuration file path is invalid: '{filePath}'. {ex.Message}", ex);
            }
        }

        private static string ValidateSectionName(string sectionName)
        {
            if (string.IsNullOrWhiteSpace(sectionName))
                throw new SetupConfigException("Module section name must not be empty.");

            return sectionName.Trim();
        }

        private static string FormatPathSuffix(string? filePath)
            => string.IsNullOrWhiteSpace(filePath) ? string.Empty : $" in '{filePath}'";

        private static JsonSerializerOptions CreateModuleJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }
}
