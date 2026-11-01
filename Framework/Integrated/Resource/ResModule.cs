using ObFramework.Integrated.Resource.Handle;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace ObFramework.Integrated.Resource
{
    public class ResModule
    {
        public static IAssetManager Mgr { get; private set; }

        public static void Init([NotNull] IAssetAdapter adapter)
        {
            if (Mgr != null)
            {
                return;
            }
            Mgr = new AssetManager(adapter);
        }
        public static Task<IHandle> LoadAssetAsync<T>(string location) => Mgr.LoadAssetAsync<T>(location);
        public static Task<RawFileHandle> LoadRawFileAsync(string location) => Mgr.LoadRawFileAsync(location);
        public static void Deinit()
        {
            Mgr = null;
        }
    }
}
