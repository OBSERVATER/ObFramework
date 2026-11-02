namespace ObFramework.Integrated.Resource
{
    public readonly struct AssetLoadResult<TAsset, TNativeHandle> where TAsset : class
    {
        public readonly TAsset Asset;
        public readonly TNativeHandle? NativeHandle;
        public readonly bool HasNativeHandle;

        public AssetLoadResult(TAsset asset, TNativeHandle nativeHandle)
        {
            Asset = asset;
            NativeHandle = nativeHandle;
            HasNativeHandle = true;
        }

        public AssetLoadResult(TAsset asset)
        {
            Asset = asset;
            NativeHandle = default;
            HasNativeHandle = false;
        }
    }
}