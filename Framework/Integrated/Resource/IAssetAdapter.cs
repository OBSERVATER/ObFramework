using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Integrated.Resource
{
    public interface IAssetAdapter<TNativeHandle>
    {
        AssetLoadResult<T, TNativeHandle> Read<T>(AssetKey request) where T : class;
        Task<AssetLoadResult<T, TNativeHandle>> ReadAsync<T>(AssetKey request, CancellationToken ct = default) where T : class;
        void Release(TNativeHandle handle);
    }

    public interface IRawFileAdapter<TNativeHandle>
    {
        Task<AssetLoadResult<byte[], TNativeHandle>> ReadRawFileAsync(string location);
    }
}