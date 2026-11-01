using Microsoft.CodeAnalysis;
using System.Text;

namespace ObFramework.Generator.Integrated.Save
{
    /// <summary>
    /// 各种字符串替换
    /// </summary>
    internal static class SaveEmitter
    {
        private const string Template =
@"// auto generated
using MessagePack;
using ObFramework.Integrated.Save;
using System;

{{NAMESPACE_BEGIN}}
    public partial class {{CLASS_NAME}} : ICanSave
    {
        [MessagePackObject]
        public sealed partial class {{CLASS_NAME}}SaveData : ASaveData
        {
{{SAVE_FIELDS}}
        }

        public string Key => ""{{SAVE_KEY}}"";

        public Type SaveDataType => typeof({{CLASS_NAME}}SaveData);

        public ASaveData Capture()
        {
            return new {{CLASS_NAME}}SaveData
            {
{{CAPTURE_FIELDS}}
            };
        }

        public void Restore(ASaveData value)
        {
            var data = ({{CLASS_NAME}}SaveData)value;
{{RESTORE_FIELDS}}
        }
    }
{{NAMESPACE_END}}
";
        public static string Generate(string name,string ns, IFieldSymbol[] attrs, string key)
        {
            var sb = new StringBuilder();
            sb.Append(Template);
            sb.Replace("{{CLASS_NAME}}",name)
              .Replace("{{SAVE_KEY}}",key);
            if (!string.IsNullOrEmpty(ns))
            {
                sb.Replace("{{NAMESPACE_BEGIN}}",
@$"namespace {ns}
{{")
                  .Replace("{{NAMESPACE_END}}","}");
            }
            else
            {
                sb.Replace("{{NAMESPACE_BEGIN}}",string.Empty)
                  .Replace("{{NAMESPACE_END}}", string.Empty);
            }

            string saveFields = GenerateSaveFields(attrs);
            sb.Replace("{{SAVE_FIELDS}}",saveFields);
            string captureFields = GenerateCaptureFields(attrs);
            sb.Replace("{{CAPTURE_FIELDS}}", captureFields);
            string restoreFields = GenerateRestoreFields(attrs);
            sb.Replace("{{RESTORE_FIELDS}}", restoreFields);
            return sb.ToString();
        }

        private static string GenerateRestoreFields(IFieldSymbol[] attrs)
        {
            var sb = new StringBuilder();
            foreach (var attr in attrs)
            {
                sb.AppendLine($"this.{attr.Name} = data.{attr.Name};");
            }
            return sb.ToString();
        }

        private static string GenerateCaptureFields(IFieldSymbol[] attrs)
        {
            var sb = new StringBuilder();
            foreach (var attr in attrs)
            {
                sb.AppendLine($"{attr.Name} = this.{attr.Name},");
            }
            return sb.ToString();
        }

        private static string GenerateSaveFields(IFieldSymbol[] attrs)
        {
            var sb = new StringBuilder();
            foreach (var attr in attrs)
            {
                var typeName = attr.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                sb.AppendLine($"[Key(\"{attr.Name}\")]");
                sb.AppendLine($"public {typeName} {attr.Name};");
            }
            return sb.ToString();
        }
    }
}
