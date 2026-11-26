using System;
using System.Collections.Generic;
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

            var handle = await StartLoadAsync<T>(request.Location, ct);
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

        private static async Task<AsyncOperationHandle<T>> StartLoadAsync<T>(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var lookup = Addressables.LoadResourceLocationsAsync(key, typeof(T));
            try
            {
                var locations = await lookup.Task;
                ct.ThrowIfCancellationRequested();
                if (lookup.Status != AsyncOperationStatus.Succeeded || locations == null)
                    throw new InvalidOperationException($"Could not resolve resource: {key}", lookup.OperationException);
                if (locations.Count == 0)
                    throw new KeyNotFoundException($"No {typeof(T).Name} at Addressables key '{key}'.");

                return Addressables.LoadAssetAsync<T>(locations[0]);
            }
            finally
            {
                Addressables.Release(lookup);
            }
        }

        public async Task<AssetLoadResult<byte[], AsyncOperationHandle>> ReadRawFileAsync(string location)
        {
            var handle = await StartLoadAsync<TextAsset>(location, CancellationToken.None);

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