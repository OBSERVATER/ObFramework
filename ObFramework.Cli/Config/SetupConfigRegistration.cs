using System;
using System.Collections.Generic;
using System.Reflection;

namespace ObFramework.Cli.Config
{
    /// <summary>
    /// One explicit module configuration registration: a CLI module name, the setup.json section
    /// name and the POCO type that owns the section content.
    /// </summary>
    public sealed class SetupConfigRegistration
    {
        public SetupConfigRegistration(string moduleName, string sectionName, Type configType)
        {
            ModuleName = ValidateName(moduleName, nameof(moduleName));
            SectionName = ValidateName(sectionName, nameof(sectionName));
            ConfigType = configType ?? throw new ArgumentNullException(nameof(configType));

            if (configType.IsAbstract || configType.IsInterface)
                throw new ArgumentException(
                    $"Configuration type '{configType.FullName}' must be a concrete class.", nameof(configType));
            if (configType.GetConstructor(Type.EmptyTypes) == null)
                throw new ArgumentException(
                    $"Configuration type '{configType.FullName}' must have a public parameterless constructor.",
                    nameof(configType));

            PathProperties = DiscoverPathProperties(configType);
        }

        /// <summary>CLI facing name used by <c>ob config &lt;module&gt;</c>.</summary>
        public string ModuleName { get; }

        /// <summary>JSON property name below the <c>modules</c> object.</summary>
        public string SectionName { get; }

        /// <summary>POCO configuration type.</summary>
        public Type ConfigType { get; }

        /// <summary>
        /// Public properties marked with <see cref="SetupPathAttribute"/>, sorted by name so
        /// diagnostics and normalization order are deterministic.
        /// </summary>
        public IReadOnlyList<PropertyInfo> PathProperties { get; }

        /// <summary>Creates a default POCO instance (used when the section is absent from setup.json).</summary>
        public object CreateDefault() => Activator.CreateInstance(ConfigType)!;

        /// <summary>
        /// Validates and normalizes the module path members of <paramref name="config"/> against
        /// <paramref name="absoluteRoot"/>. Valid values are written back in normalized relative
        /// form; invalid values are reported and left untouched. The caller must not persist the
        /// configuration when <see cref="SetupPathNormalizationResult.IsValid"/> is false.
        /// </summary>
        public SetupPathNormalizationResult NormalizePaths(string absoluteRoot, object config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (!ConfigType.IsInstanceOfType(config))
                throw new ArgumentException(
                    $"Configuration instance must be of type '{ConfigType.FullName}'.", nameof(config));

            var errors = new List<string>();
            var paths = new List<SetupPathValue>();

            foreach (var property in PathProperties)
            {
                var attribute = property.GetCustomAttribute<SetupPathAttribute>()!;

                if (property.PropertyType == typeof(string))
                {
                    var value = (string?)property.GetValue(config);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        if (!attribute.AllowEmpty)
                            errors.Add($"{property.Name}: module path must not be empty.");
                        continue;
                    }

                    try
                    {
                        var normalized = SetupConfigPaths.NormalizeModulePath(absoluteRoot, value);
                        property.SetValue(config, normalized);
                        paths.Add(new SetupPathValue(property.Name, value, normalized));
                    }
                    catch (SetupConfigException ex)
                    {
                        errors.Add($"{property.Name}: {ex.Message}");
                    }

                    continue;
                }

                if (property.PropertyType == typeof(string[]))
                {
                    var values = (string[]?)property.GetValue(config) ?? Array.Empty<string>();
                    var normalizedValues = new string[values.Length];
                    var allValid = true;

                    for (var index = 0; index < values.Length; index++)
                    {
                        var value = values[index];
                        var label = $"{property.Name}[{index}]";

                        if (string.IsNullOrWhiteSpace(value))
                        {
                            if (!attribute.AllowEmpty)
                            {
                                errors.Add($"{label}: module path must not be empty.");
                                allValid = false;
                            }

                            normalizedValues[index] = value;
                            continue;
                        }

                        try
                        {
                            normalizedValues[index] = SetupConfigPaths.NormalizeModulePath(absoluteRoot, value);
                            paths.Add(new SetupPathValue(label, value, normalizedValues[index]));
                        }
                        catch (SetupConfigException ex)
                        {
                            errors.Add($"{label}: {ex.Message}");
                            allValid = false;
                        }
                    }

                    if (allValid)
                        property.SetValue(config, normalizedValues);
                }
            }

            return new SetupPathNormalizationResult(errors, paths);
        }

        /// <summary>
        /// Reads the module path members without modifying the configuration (used for display and
        /// inspection). Values are reported as stored, not normalized.
        /// </summary>
        public IReadOnlyList<SetupPathValue> ReadPathValues(object config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (!ConfigType.IsInstanceOfType(config))
                throw new ArgumentException(
                    $"Configuration instance must be of type '{ConfigType.FullName}'.", nameof(config));

            var values = new List<SetupPathValue>();
            foreach (var property in PathProperties)
            {
                if (property.PropertyType == typeof(string))
                {
                    var value = (string?)property.GetValue(config);
                    if (!string.IsNullOrWhiteSpace(value))
                        values.Add(new SetupPathValue(property.Name, value, value));
                    continue;
                }

                var array = (string[]?)property.GetValue(config) ?? Array.Empty<string>();
                for (var index = 0; index < array.Length; index++)
                {
                    if (!string.IsNullOrWhiteSpace(array[index]))
                        values.Add(new SetupPathValue($"{property.Name}[{index}]", array[index], array[index]));
                }
            }

            return values;
        }

        private static IReadOnlyList<PropertyInfo> DiscoverPathProperties(Type configType)
        {
            var properties = new List<PropertyInfo>();
            foreach (var property in configType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<SetupPathAttribute>() == null)
                    continue;

                if (!property.CanRead || !property.CanWrite || property.SetMethod == null || !property.SetMethod.IsPublic)
                    throw new ArgumentException(
                        $"[SetupPath] property '{configType.FullName}.{property.Name}' must have a public getter and setter.",
                        nameof(configType));

                if (property.PropertyType != typeof(string) && property.PropertyType != typeof(string[]))
                    throw new ArgumentException(
                        $"[SetupPath] property '{configType.FullName}.{property.Name}' must be a string or string[]; " +
                        $"'{property.PropertyType.FullName}' is not supported.",
                        nameof(configType));

                properties.Add(property);
            }

            properties.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
            return properties;
        }

        private static string ValidateName(string name, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name must not be empty.", parameterName);

            var trimmed = name.Trim();
            foreach (var character in trimmed)
            {
                if (char.IsLetterOrDigit(character) || character == '-' || character == '_')
                    continue;

                throw new ArgumentException(
                    $"'{name}' is not a valid setup.json name. Use letters, digits, '-' or '_' (no dots or slashes).",
                    parameterName);
            }

            return trimmed;
        }
    }

    /// <summary>One configured module path value.</summary>
    public readonly struct SetupPathValue
    {
        public SetupPathValue(string memberName, string originalValue, string normalizedValue)
        {
            MemberName = memberName;
            OriginalValue = originalValue;
            NormalizedValue = normalizedValue;
        }

        /// <summary>Property name, with an index suffix for array entries (for example <c>Paths[1]</c>).</summary>
        public string MemberName { get; }

        /// <summary>Value as stored in the configuration before normalization.</summary>
        public string OriginalValue { get; }

        /// <summary>Normalized forward-slash module-relative value.</summary>
        public string NormalizedValue { get; }
    }

    /// <summary>Result of validating and normalizing the module paths of a configuration instance.</summary>
    public sealed class SetupPathNormalizationResult
    {
        public SetupPathNormalizationResult(IReadOnlyList<string> errors, IReadOnlyList<SetupPathValue> paths)
        {
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
            Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        /// <summary>Human readable validation errors; empty when the configuration is valid.</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>Normalized path values (only the valid ones).</summary>
        public IReadOnlyList<SetupPathValue> Paths { get; }

        public bool IsValid => Errors.Count == 0;
    }
}
