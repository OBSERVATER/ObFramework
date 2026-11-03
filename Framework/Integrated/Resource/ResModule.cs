using ObFramework.Integrated.Resource.Handle;
using System;
using System.Threading.Tasks;

namespace ObFramework.Integrated.Resource
{
    public static class ResModule
    {
        public static IAssetManager Mgr { get; private set; }

        public static void Init<TNativeHandle>(IAssetAdapter<TNativeHandle> adapter)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));

            if (Mgr != null)
                return;

            Mgr = new AssetManager<TNativeHandle>(adapter);
        }

        public static IHandle LoadAsset<T>(string location) where T : class => Mgr.LoadAsset<T>(location);
        public static Task<IHandle> LoadAssetAsync<T>(string location) where T : class => Mgr.LoadAssetAsync<T>(location);
        public static Task<RawFileHandle> LoadRawFileAsync(string location) => Mgr.LoadRawFileAsync(location);

        public static void Deinit()
        {
            Mgr = null;
        }
    }
}