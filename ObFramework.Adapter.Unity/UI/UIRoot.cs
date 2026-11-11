using ObFramework.Adapter.Unity.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ObFramework.Adapter.Unity.UI
{
    public class UIRoot : MonoBehaviour
    {
        private Transform _sceneRoot;
        private Transform _commonRoot;
        private Transform _popupRoot;

        public Transform SceneRoot => _sceneRoot;
        public Transform CommonRoot => _commonRoot;
        public Transform PopupRoot => _popupRoot;

        internal Scene OriginalScene { get; private set; }

        private void Awake()
        {
            OriginalScene = gameObject.scene;
            if (transform.parent == null && !GetComponentInChildren<AGameLauncher>(true))
                DontDestroyOnLoad(gameObject);
        }
        public void Init()
        {
            _sceneRoot = transform.Find("Scene");
            _commonRoot = transform.Find("Common");
            _popupRoot = transform.Find("Popup");
        }
    }
}