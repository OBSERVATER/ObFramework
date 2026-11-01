using ObFramework.Core;
using System.Threading.Tasks;

namespace ObFramework.Integrated.Resource
{
    public interface IAssetAdapter : IReadAdapter<AssetLoadResult, AssetKey>, IAsyncReadAdapter<AssetLoadResult, AssetKey>
    {
    }
    public interface IRawFileAdapter
    {
        Task<AssetLoadResult> ReadRawFileAsync(string location);
    }
}
