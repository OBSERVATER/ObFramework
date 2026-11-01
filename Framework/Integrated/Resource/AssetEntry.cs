using ObFramework.Integrated.Resource.Handle;
using System.Threading.Tasks;

namespace ObFramework.Integrated.Resource
{
    public class AssetEntry
    {
        public AssetKey Key;
        public IHandle Handle;
        public object Asset;
        public int RefCount;

        internal TaskCompletionSource<AssetLoadResult> LoadingTask;
    }
}
