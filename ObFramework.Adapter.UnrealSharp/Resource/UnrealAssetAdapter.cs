using ObFramework.Integrated.Resource;
using UnrealSharp;
using UnrealSharp.CoreUObject;

namespace ObFramework.Adapter.UnrealSharp.Resource
{
    public sealed class UnrealAssetAdapter : IAssetAdapter<TStrongObjectPtr<UObject>>,IRawFileAdapter<TStrongObjectPtr<UObject>>
    {
        public AssetLoadResult<T, TStrongObjectPtr<UObject>> Read<T>(AssetKey request) where T : class
        {
            ValidateType<T>();
            var loaded = UObject.StaticLoadObject<UObject>(request.Location);
            return CreateResult<T>(request, loaded);
        }

        public async Task<AssetLoadResult<T, TStrongObjectPtr<UObject>>> ReadAsync<T>(AssetKey request, CancellationToken ct = default) where T : class
        {
            ValidateType<T>();
            ct.ThrowIfCancellationRequested();

            var loaded = await UObject.AsyncStaticLoadObject<UObject>(request.Location).ConfigureWithUnrealContext();
            ct.ThrowIfCancellationRequested();
            return CreateResult<T>(request, loaded);
        }

        public void Release(TStrongObjectPtr<UObject> handle)
        {
            handle.Dispose();
        }

        private static AssetLoadResult<T, TStrongObjectPtr<UObject>> CreateResult<T>(AssetKey request, UObject loaded) where T : class
        {
            if (loaded == null || loaded.IsDestroyed)
                throw new InvalidOperationException($"Failed to load asset: {request.Location}");

            if (loaded is not T asset)
                throw new InvalidCastException($"Asset {request.Location} cannot be converted to {typeof(T).Name}");

            var handle = new TStrongObjectPtr<UObject>(loaded);

            return new AssetLoadResult<T, TStrongObjectPtr<UObject>>(asset, handle);
        }

        private static void ValidateType<T>() where T : class
        {
            if (!typeof(UObject).IsAssignableFrom(typeof(T)))
                throw new NotSupportedException($"Unsupported Unreal asset type: {typeof(T)}");
        }
        public async Task<AssetLoadResult<byte[], TStrongObjectPtr<UObject>>> ReadRawFileAsync(string location)
        {
            var raw = await File.ReadAllBytesAsync(location);
            return new AssetLoadResult<byte[], TStrongObjectPtr<UObject>>(raw);
        }
    }
}
