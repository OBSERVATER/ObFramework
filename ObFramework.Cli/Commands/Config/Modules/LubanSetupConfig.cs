namespace ObFramework.Cli.Commands.Config.Modules
{
    public sealed class LubanSetupConfig
    {
        [SetupPath]
        public string LubanDll { get; set; } = "Tools/Luban/Luban.dll";

        [SetupPath(AllowEmpty = true)]
        public string AgentDll { get; set; } = "Tools/Luban.Agent/Luban.Agent.dll";

        [SetupPath]
        public string ConfPath { get; set; } = "Configs/luban.conf";

        [SetupPath]
        public string CodeOutputRoot { get; set; } = "Generated/Config/Code";

        [SetupPath]
        public string DataOutputRoot { get; set; } = "Generated/Config/Data";

        public string Target { get; set; } = "client";
    }
}
