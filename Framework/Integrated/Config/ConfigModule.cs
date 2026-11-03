using Newtonsoft.Json;
using ObFramework.Integrated.Resource;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ObFramework.Integrated.Config
{
    public static class ConfigModule
    {
        private static Dictionary<Type, IReadOnlyDictionary<int,IConfig>> _configMap = new();

        /// <summary>
        /// 一次性加载全部配置
        /// </summary>
        public static async Task LoadAllAsync(IConfigCatalog catalog)
        {
            var tmp = new Dictionary<Type, IReadOnlyDictionary<int, IConfig>>();

                foreach (var item in catalog.Entries)
                {
                    var dict = new Dictionary<int, IConfig>();
                    tmp.Add(item.Type, dict);
                    using var handle = await ResModule.LoadRawFileAsync(item.Location);
                    var raw = handle.Get<string>();
                    var listType = typeof(List<>).MakeGenericType(item.Type);
                    var list = JsonConvert.DeserializeObject(raw, listType) as IEnumerable<IConfig>;
                    foreach (var config in list!)
                    {
                        dict.Add(config.Id, config);
                    }
                }

            _configMap = tmp;
        }

        /// <summary>
        /// 获取某种配置的全部记录
        /// </summary>
        public static IReadOnlyList<T> Get<T>() where T : class,IConfig => _configMap.GetValueOrDefault(typeof(T)).Values.Cast<T>().ToList();
        /// <summary>
        /// 根据 ID 获取记录
        /// </summary>
        public static T? Get<T>(int id) where T : class,IConfig
        {
            T? result = null;
            if (_configMap.TryGetValue(typeof(T),out var dict))
            {
                if (dict.TryGetValue(id,out var config))
                {
                    result = config as T;
                }
            }
            return result;
        }
    }
}
