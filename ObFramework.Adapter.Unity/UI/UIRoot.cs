using UnityEngine;

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

        private void Awake()
        {
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