using System.Threading.Tasks;

namespace ObFramework.Integrated.Resource
{
    public class AssetEntry
    {
        public AssetKey Key;
        public object Asset;
        public int RefCount;
        internal TaskCompletionSource<bool> LoadingTask;
    }

    internal sealed class AssetEntry<TNativeHandle> : AssetEntry
    {
        public TNativeHandle NativeHandle;
        public bool HasNativeHandle;
    }

    internal interface IAssetEntryOwner
    {
        void ReleaseEntry(AssetEntry entry);
    }
}