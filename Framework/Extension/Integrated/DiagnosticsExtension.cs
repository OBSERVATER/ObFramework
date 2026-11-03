using Serilog;
using Serilog.Core;

namespace ObFramework.Extension.Integrated
{
    public static class DiagnosticsExtension
    {
        public static void I(this ILogger self,string content) => self.Information(content);
        public static void W(this ILogger self, string content) => self.Warning(content);
        public static void E(this ILogger self, string content) => self.Error(content);
        public static void D(this ILogger self, string content) => self.Debug(content);
    }
}
