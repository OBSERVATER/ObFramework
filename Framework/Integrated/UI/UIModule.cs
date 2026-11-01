using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Integrated.UI
{
    public class UIModule
    {
        private static IUIManager UIMgr { get; set; }
        public static void Init(IUIAdapter adapter, int cachedCapacity = 10)
        {
            if (UIMgr != null)
            {
                return;
            }

            UIMgr = new UIManager(adapter, cachedCapacity);
        }
        public static async Task<T?> OpenAsync<T>(EUIOpenMode mode = EUIOpenMode.Single,
            CancellationToken cancellationToken = default) where T : class, IUIWindow
            => await UIMgr.OpenAsync<T>(mode, cancellationToken);
        public static bool TryGet<T>(out T instance) where T : class, IUIWindow => UIMgr.TryGet<T>(out instance);
        public static bool Close(IUIWindow instance) => UIMgr.Close(instance);
        public static bool Close<T>() where T : class, IUIWindow => UIMgr.Close<T>();
        public static int CloseAll<T>() where T : class, IUIWindow => UIMgr.CloseAll<T>();
        public static void Push<T>(T ui) where T : IUIPanel => UIMgr.Push(ui);
        public async Task<T?> Pop<T>() where T : class, IUIPanel => await UIMgr.Pop<T>();
    }
}