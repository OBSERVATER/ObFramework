
using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Core
{
    /// <summary>
    /// 无参读取
    /// </summary>
    public interface IReadAdapter<out TResult>
    {
        TResult Read();
    }
    /// <summary>
    /// 带参数读取
    /// </summary>
    public interface IReadAdapter<out TResult, in TRequest>
    {
        TResult Read(TRequest request);
    }
    public interface IAsyncReadAdapter<TResult, in TRequest>
    {
        Task<TResult> ReadAsync(TRequest request, CancellationToken ct = default);
    }
    /// <summary>
    /// 写入能力
    /// </summary>
    public interface IWriteAdapter<in TValue>
    {
        void Write(TValue value);
    }

    /// <summary>
    /// 无参读取 + 写入
    /// </summary>
    public interface IAdapter<out TResult, in TValue> : IReadAdapter<TResult>, IWriteAdapter<TValue>
    {
    }

    /// <summary>
    /// 带参数读取 + 写入
    /// </summary>
    public interface IAdapter<out TResult, in TRequest, in TValue> : IReadAdapter<TResult, TRequest>, IWriteAdapter<TValue>
    {
    }
}
