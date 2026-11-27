using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ObFramework.Integrated.Config;
using ObFramework.Integrated.GAS.GameplayEffect;

namespace ObFramework.Integrated.GAS.Config
{
    public sealed class EffectConfig : IConfig
    {
        [JsonProperty(Required = Required.Always)]
        public int Id { get; set; }

        public string Name { get; set; } = "";

        [JsonProperty(Required = Required.Always)]
        public EEffectDurationType DurationType { get; set; }

        public int DurationTicks { get; set; }
        public int PeriodTicks { get; set; }
        public bool ExecuteOnApplication { get; set; }
        public int MaxStacks { get; set; }

        public string[] EffectTags { get; set; } = Array.Empty<string>();

        [JsonProperty(Required = Required.Always)]
        public JObject[] Components { get; set; } = Array.Empty<JObject>();
    }
}