using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ObFramework.Integrated.Resource;
using FileAccess = Godot.FileAccess;

namespace ObFramework.Adapter.Godot.Resource
{
    public class GodotAssetAdapter : IAssetAdapter<global::Godot.Resource>,IRawFileAdapter<global::Godot.Resource>
    {
        public AssetLoadResult<T, global::Godot.Resource> Read<T>(AssetKey request) where T : class
        {
            var raw = ResourceLoader.Load<T>(request.Location);
            if (raw == null)
            {
                throw new FileNotFoundException($"Resource not found: {request.Location}");
            }
            var result = new AssetLoadResult<T, global::Godot.Resource>(raw,null);
            return result;
        }
        public async Task<AssetLoadResult<T, global::Godot.Resource>> ReadAsync<T>(AssetKey request, CancellationToken ct = default) where T : class
        {
            var sceneTree = Engine.GetMainLoop() as SceneTree ?? throw new InvalidOperationException("SceneTree is not running.");
            var error = ResourceLoader.LoadThreadedRequest(request.Location);
            if (error != Error.Ok)
            {
                throw new IOException($"Failed to request resource: {request.Location}, {error}");
            }

            var status = ResourceLoader.LoadThreadedGetStatus(request.Location);
            while (status == ResourceLoader.ThreadLoadStatus.InProgress)
            {
                await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);
                status = ResourceLoader.LoadThreadedGetStatus(request.Location);
            }
            if (status != ResourceLoader.ThreadLoadStatus.Loaded)
            {
                throw new IOException("invalid resource");
            }
            
            var raw = ResourceLoader.LoadThreadedGet(request.Location);
            return raw is not T resource ? 
                throw new InvalidCastException($"Resource {request.Location} cannot be converted to {typeof(T).Name}") 
                : new AssetLoadResult<T, global::Godot.Resource>(resource, null);
        }
        public void Release(global::Godot.Resource handle)
        {
        }
        
        public Task<AssetLoadResult<byte[], global::Godot.Resource>> ReadRawFileAsync(string location)
        {
            return Task.Run(() =>
            {
                using var file = FileAccess.Open(location, FileAccess.ModeFlags.Read);

                if (file == null)
                {
                    throw new IOException($"Failed to open file: {location}, {FileAccess.GetOpenError()}");
                }

                var bytes = file.GetBuffer((long)file.GetLength());
                return new AssetLoadResult<byte[], global::Godot.Resource>(bytes, null);
            });
        }

    }
}