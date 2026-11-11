using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ObFramework.Cli.Config
{
    /// <summary>
    /// Reflection based editor for POCO module configuration values used by
    /// <c>ob config &lt;module&gt; --set key=value</c>.
    /// <para>
    /// Only registered module types are edited; there is no assembly scanning and no generic
    /// runtime configuration binder. Property lookup is case-insensitive so both
    /// <c>--set scriptOutputRoot=...</c> (JSON style) and <c>--set ScriptOutputRoot=...</c> work.
    /// </para>
    /// </summary>
    public static class SetupConfigValues
    {
        /// <summary>Sets one scalar property from a command line value.</summary>
        public static bool TrySet(object config, string key, string value, out string? error)
        {
            error = null;
            if (config == null)
            {
                error = "Configuration instance must not be null.";
                return false;
            }

            var property = FindProperty(config.GetType(), key, out error);
            if (property == null)
                return false;

            if (!property.CanWrite || property.SetMethod == null || !property.SetMethod.IsPublic)
            {
                error = $"Option '{property.Name}' is read-only.";
                return false;
            }

            if (!TryConvert(value, property.PropertyType, out var converted, out var conversionError))
            {
                error = $"{property.Name}: {conversionError}";
                return false;
            }

            property.SetValue(config, converted);
            return true;
        }

        /// <summary>Resets one property to the value of a fresh default configuration instance.</summary>
        public static bool TryUnset(object config, string key, out string? error)
        {
            error = null;
            if (config == null)
            {
                error = "Configuration instance must not be null.";
                return false;
            }

            var property = FindProperty(config.GetType(), key, out error);
            if (property == null)
                return false;

            if (!property.CanWrite || property.SetMethod == null || !property.SetMethod.IsPublic)
            {
                error = $"Option '{property.Name}' is read-only.";
                return false;
            }

            object defaultInstance;
            try
            {
                defaultInstance = Activator.CreateInstance(config.GetType())!;
            }
            catch (Exception ex) when (ex is MissingMethodException || ex is MemberAccessException)
            {
                error = $"Configuration type '{config.GetType().FullName}' has no public parameterless constructor.";
                return false;
            }

            property.SetValue(config, property.GetValue(defaultInstance));
            return true;
        }

        /// <summary>Formats a configuration value for display.</summary>
        public static string FormatValue(object? value) => value switch
        {
            null => string.Empty,
            string text => text,
            bool flag => flag ? "true" : "false",
            string[] array => string.Join(", ", array),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        /// <summary>Describes the writable/readable properties of a configuration instance for display.</summary>
        public static IReadOnlyList<SetupConfigPropertyValue> Describe(object config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var properties = new List<SetupConfigPropertyValue>();
            foreach (var property in config.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;

                var jsonName = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                properties.Add(new SetupConfigPropertyValue(
                    property.Name,
                    jsonName,
                    FormatValue(property.GetValue(config)),
                    property.GetCustomAttribute<SetupPathAttribute>() != null,
                    property.PropertyType));
            }

            properties.Sort(static (left, right) => string.CompareOrdinal(left.JsonName, right.JsonName));
            return properties;
        }

        private static PropertyInfo? FindProperty(Type configType, string? key, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(key))
            {
                error = "Option name must not be empty.";
                return null;
            }

            var name = key.Trim();
            var exact = configType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (exact != null)
                return exact;

            PropertyInfo? match = null;
            foreach (var property in configType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (match != null)
                {
                    error = $"Option '{name}' is ambiguous on '{configType.FullName}'.";
                    return null;
                }

                match = property;
            }

            if (match != null)
                return match;

            var available = configType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)
                .Select(static property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
                .OrderBy(static propertyName => propertyName, StringComparer.Ordinal)
                .ToArray();

            error = available.Length == 0
                ? $"Unknown option '{name}'."
                : $"Unknown option '{name}'. Available options: {string.Join(", ", available)}.";
            return null;
        }

        private static bool TryConvert(string? raw, Type propertyType, out object? converted, out string? error)
        {
            converted = null;
            error = null;
            var value = raw ?? string.Empty;
            var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            if (targetType == typeof(string))
            {
                converted = value;
                return true;
            }

            if (targetType == typeof(bool))
            {
                if (bool.TryParse(value.Trim(), out var flag))
                {
                    converted = flag;
                    return true;
                }

                error = "expected 'true' or 'false'.";
                return false;
            }

            if (targetType.IsEnum)
            {
                var enumValue = value.Trim();
                try
                {
                    var parsed = Enum.Parse(targetType, enumValue, ignoreCase: true);
                    if (!Enum.IsDefined(targetType, parsed))
                    {
                        error = $"'{enumValue}' is not a defined {targetType.Name} value.";
                        return false;
                    }

                    converted = parsed;
                    return true;
                }
                catch (ArgumentException)
                {
                    error = $"'{enumValue}' is not a valid {targetType.Name} value.";
                    return false;
                }
            }

            if (targetType == typeof(int) || targetType == typeof(long) || targetType == typeof(short) ||
                targetType == typeof(byte) || targetType == typeof(double) || targetType == typeof(float) ||
                targetType == typeof(decimal))
            {
                try
                {
                    converted = Convert.ChangeType(value.Trim(), targetType, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is InvalidCastException)
                {
                    error = $"'{value}' is not a valid {targetType.Name} value.";
                    return false;
                }
            }

            if (targetType == typeof(string[]))
            {
                converted = ParseList(value);
                return true;
            }

            if (targetType == typeof(List<string>) || targetType == typeof(IReadOnlyList<string>) ||
                targetType == typeof(IList<string>))
            {
                converted = ParseList(value).ToList();
                return true;
            }

            error = $"option type '{propertyType.Name}' cannot be set from the command line; " +
                    "edit setup.json directly.";
            return false;
        }

        private static string[] ParseList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Array.Empty<string>();

            return value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    /// <summary>One display row of a module configuration POCO.</summary>
    public sealed class SetupConfigPropertyValue
    {
        public SetupConfigPropertyValue(
            string propertyName,
            string jsonName,
            string value,
            bool isModulePath,
            Type propertyType)
        {
            PropertyName = propertyName;
            JsonName = jsonName;
            Value = value;
            IsModulePath = isModulePath;
            PropertyType = propertyType;
        }

        /// <summary>CLR property name.</summary>
        public string PropertyName { get; }

        /// <summary>JSON property name stored in setup.json.</summary>
        public string JsonName { get; }

        /// <summary>Formatted current value.</summary>
        public string Value { get; }

        /// <summary>Whether the property is a module-relative path (<see cref="SetupPathAttribute"/>).</summary>
        public bool IsModulePath { get; }

        /// <summary>CLR property type.</summary>
        public Type PropertyType { get; }
    }
}
