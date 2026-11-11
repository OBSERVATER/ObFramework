using ObFramework.Core;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace ObFramework.Integrated.UI
{
    /// <summary>
    /// Engine-side UI adapter. It owns native objects and resource handles and constructs the
    /// engine VC together with its paired business View. The adapter never runs business
    /// lifecycle methods; UIManager drives those and the binding layer owns the final View dispose.
    /// </summary>
    public interface IUIAdapter
    {
        /// <summary>
        /// Creates the VC/View pair in an inactive state. The returned instance is not shown and
        /// business lifecycle methods are not invoked.
        /// </summary>
        Task<UIViewInstance?> CreateAsync(Type viewType, CancellationToken ct = default);

        /// <summary>Shows/activates the existing native object. No business lifecycle call.</summary>
        void Open(UIViewInstance instance, IUIData? data = null);

        /// <summary>Hides/deactivates the existing native object. No business lifecycle call.</summary>
        void Close(UIViewInstance instance);

        /// <summary>
        /// Releases native objects and resource handles. The engine binding layer disposes the
        /// business View exactly once; UIManager must not dispose it independently.
        /// </summary>
        void Release(UIViewInstance instance);

        /// <summary>Resolves the business View paired with a native window, when already bound.</summary>
        bool TryGetView(IUIWindow window, out AUIView view);
    }
}
