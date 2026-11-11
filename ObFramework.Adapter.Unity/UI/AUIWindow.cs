using ObFramework.Integrated.UI;

namespace ObFramework.Adapter.Unity.UI
{
    /// <summary>
    /// Unity window controller (VC) identity only: a <see cref="UnityEngine.MonoBehaviour"/> that
    /// belongs to the generated view binding and exposes its UI level. Business state and the
    /// business lifecycle (<c>OnInit</c>/<c>OnOpen</c>/<c>OnClose</c>/<c>OnDestroy</c>) live on the
    /// paired engine-independent <see cref="AUIView"/>; the UIManager drives them and the generated
    /// <c>[BindView]</c> partial owns the View instance. This type must not forward business
    /// lifecycle calls again.
    /// </summary>
    public abstract class AUIWindow : AViewController, IUIWindow
    {
        /// <summary>UI hierarchy level used by <see cref="UnityUIAdapter"/> to pick the parent root.</summary>
        public abstract EUILevel Level { get; }
    }
}
