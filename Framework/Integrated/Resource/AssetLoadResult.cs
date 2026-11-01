using ObFramework.Integrated.Resource.Handle;
namespace ObFramework.Integrated.Resource
{
    public readonly struct AssetLoadResult
    {
        public readonly object Asset;
        public readonly IHandle Handle;

        public AssetLoadResult(object asset, IHandle nativeHandle)
        {
            Asset = asset;
            Handle = nativeHandle;
        }
    }
}