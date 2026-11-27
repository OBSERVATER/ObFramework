using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ObFramework.Integrated.Config;

namespace ObFramework.Integrated.GAS.Config
{
    public sealed class AbilityConfig : IConfig
    {
        [JsonProperty(Required = Required.Always)]
        public int Id { get; set; }
        public string Name { get; set; } = "";
        
        [JsonProperty(Required = Required.Always)]
        public string Behavior { get; set; } = "";
        public JObject Parameters { get; set; } = new();
        public int? CommitEffectId { get; set; }
        
        public string[] AbilityTags { get; set; } = Array.Empty<string>();
        public string[] RequiredTags { get; set; } = Array.Empty<string>();
        public string[] BlockedTags { get; set; } = Array.Empty<string>();
        public string[] BlockAbilitiesWithTags { get; set; } = Array.Empty<string>();
    }
}