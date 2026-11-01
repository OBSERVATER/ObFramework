using System.Globalization;

namespace ObFramework.Cli.Utility
{
    internal static class TemplateGenerateHelper
    {
        public static string LoadTemplate(string temName)
        {
            var assembly = typeof(TemplateGenerateHelper).Assembly;
            var resName = $"{temName.ToLower()}.tpl";
            using var stream = assembly.GetManifestResourceStream(resName)?? throw new FileNotFoundException($"Template not found: {resName}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        public static string GenerateSourceCode(string template,string name,string ns)
        {
            return TemplateRendererHelper.Render(template,new Dictionary<string,string>()
            {
                ["#NAMESPACE#"] = ns,
                ["#DATETIME#"] = DateTime.Now.ToString("yyyy-M-dd HH:mm:ss",CultureInfo.InvariantCulture),
                ["#NAME#"] = name
            });
        }
        public static string Save(string source,string name,string path,string tem)
        {
            tem = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tem);
            name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
            var realPath = Path.Combine(path,$"{name}{tem}.cs");
            if (File.Exists(realPath))
            {
                File.Delete(realPath);
            }
            File.WriteAllText(realPath,source);
            return realPath;
        }
    }
}
