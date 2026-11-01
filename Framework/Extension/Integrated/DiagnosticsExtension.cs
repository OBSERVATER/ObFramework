using Serilog.Core;

namespace ObFramework.Extension.Integrated
{
    public static class DiagnosticsExtension
    {
        public static void I(this Logger self,string content) => self.Information(content);
        public static void W(this Logger self, string content) => self.Warning(content);
        public static void E(this Logger self, string content) => self.Error(content);
        public static void D(this Logger self, string content) => self.Debug(content);
    }
}
