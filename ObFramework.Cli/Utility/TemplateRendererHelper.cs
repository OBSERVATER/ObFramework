using System.Text.RegularExpressions;

namespace ObFramework.Cli.Utility
{
    internal static partial class TemplateRendererHelper
    {
        [GeneratedRegex(@"#([A-Z][A-Z0-9_]*)#")]
        internal static partial Regex TagRegex();
        internal static string Render(string template,IReadOnlyDictionary<string, string> values)
        {
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(values);

            return TagRegex().Replace(template, match =>
            {
                var key = match.Groups[0].Value;
                if (!values.TryGetValue(key, out var value))
                {
                    throw new InvalidOperationException($"Unknown template tag: {key}");
                }
                return value;
            });
        }
    }
}