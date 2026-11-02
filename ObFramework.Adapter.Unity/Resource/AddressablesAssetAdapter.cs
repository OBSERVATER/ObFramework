using System;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Integrated.Resource;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ObFramework.Adapter.Unity.Resource
{
    public sealed class AddressablesAssetAdapter : IAssetAdapter<AsyncOperationHandle>, IRawFileAdapter<AsyncOperationHandle>
    {
        public AssetLoadResult<T, AsyncOperationHandle> Read<T>(AssetKey request) where T : class
        {
            throw new NotSupportedException("Synchronous loading is unsupported by this adapter.");
        }

        public async Task<AssetLoadResult<T, AsyncOperationHandle>> ReadAsync<T>(AssetKey request, CancellationToken ct = default) where T : class
        {
            if (!typeof(UnityEngine.Object).IsAssignableFrom(typeof(T)))
                throw new NotSupportedException($"Unsupported Unity asset type: {typeof(T)}");

            ct.ThrowIfCancellationRequested();

            var handle = Addressables.LoadAssetAsync<T>(request.Location);
            bool transferred = false;

            try
            {
                await handle.Task;
                ct.ThrowIfCancellationRequested();

                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                    throw new InvalidOperationException($"Failed to load asset: {request.Location}", handle.OperationException);

                var result = new AssetLoadResult<T, AsyncOperationHandle>(handle.Result, handle);
                transferred = true;
                return result;
            }
            finally
            {
                if (!transferred && handle.IsValid())
                    Addressables.Release(handle);
            }
        }

        public async Task<AssetLoadResult<byte[], AsyncOperationHandle>> ReadRawFileAsync(string location)
        {
            var handle = Addressables.LoadAssetAsync<TextAsset>(location);

            try
            {
                await handle.Task;

                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                    throw new InvalidOperationException($"Failed to load raw file: {location}", handle.OperationException);

                return new AssetLoadResult<byte[], AsyncOperationHandle>(handle.Result.bytes);
            }
            finally
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
        }

        public void Release(AsyncOperationHandle handle)
        {
            if (!handle.IsValid())
                throw new InvalidOperationException("Addressables handle is invalid or already released.");

            Addressables.Release(handle);
        }
    }
}