using System;
using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Integrated.UI
{
    public interface IUIAdapter
    {
        /// <summary>
        /// 只负责创建实例，不负责显示
        /// </summary>
        Task<T?> CreateAsync<T>(CancellationToken ct = default)where T : class, IUIWindow;
        Task<IUIWindow> CreateAsync(Type type, CancellationToken ct = default);
        /// <summary>
        /// 显示实例
        /// </summary>
        void Open(IUIWindow instance,IUIData? data = null);
        /// <summary>
        /// 关闭当前使用周期，但保留实例(进LRU)
        /// </summary>
        void Close(IUIWindow instance);
        /// <summary>
        /// 彻底释放实例
        /// </summary>
        void Release(IUIWindow instance);
    }
}
