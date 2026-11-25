using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Adapter.Unity.UI;
using ObFramework.Adapter.Unity.ViewBinding;
using ObFramework.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityApplication = UnityEngine.Application;

namespace ObFramework.Adapter.Unity.Core
{
    [DisallowMultipleComponent]
    public sealed class UnityRuntimeHost : MonoBehaviour
    {
        private Scene _bootstrap;
        private HashSet<GameObject>? _persistentRoots;
        private bool _fallback;
        private bool _usesPrefabUIRoot;
        private bool _quitRequested;

        public static UnityRuntimeHost? Current { get; private set; }
        public PluginApplication? Application { get; private set; }
        public UIRoot? RuntimeUIRoot { get; private set; }
        internal bool IsStopping { get; private set; }

        internal static UnityRuntimeHost Reserve(GameObject launcher, GameObject? manager)
        {
            if (Current is not null)
                throw new InvalidOperationException("An OBF Unity runtime is already active.");

            var fallback = !manager;
            if (fallback)
            {
                manager = new GameObject("OBF Runtime");
                if (manager.scene != launcher.scene)
                    SceneManager.MoveGameObjectToScene(manager, launcher.scene);
            }
            else
            {
                ValidateRoot(launcher.scene, manager!);
                if (!manager!.activeInHierarchy)
                    throw new InvalidOperationException("The OBF game manager must be active.");
            }

            var host = manager!.AddComponent<UnityRuntimeHost>();
            host._bootstrap = launcher.scene;
            host._fallback = fallback;
            Current = host;
            UnityApplication.wantsToQuit += host.WantsToQuit;
            return host;
        }

        internal void LoadUIRoot(string resourceKey)
        {
            _usesPrefabUIRoot = true;
            var prefab = Resources.Load<GameObject>(resourceKey);
            if (!prefab)
                throw new InvalidOperationException($"Missing UI root prefab at Resources key '{resourceKey}'.");

            if (_fallback)
                gameObject.name = "GameManager";
            _persistentRoots = new HashSet<GameObject> { gameObject };
            DontDestroyOnLoad(gameObject);

            // Screen Space - Overlay needs a top-level Canvas. The host owns both DDOL roots.
            var root = Instantiate(prefab);
            _persistentRoots.Add(root);
            root.name = prefab.name;
            DontDestroyOnLoad(root);
            var ui = root.GetComponent<UIRoot>() ?? root.AddComponent<UIRoot>();
            ui.Init();
            if (!ui.SceneRoot || !ui.CommonRoot || !ui.PopupRoot)
                throw new InvalidOperationException(
                    $"UI root prefab '{resourceKey}' must have direct children named Scene, Common, and Popup.");
            RuntimeUIRoot = ui;
        }

        internal async Task StartAsync(PluginApplication application, UnityFrameLoopBridge driver,
            Func<CancellationToken, Task>? startGame)
        {
            if (!ReferenceEquals(Current, this))
            {
                await application.StopAsync();
                throw new OperationCanceledException();
            }

            Application = application;
            await application.StartAsync(driver, startGame, RequestExit);
            if (!_usesPrefabUIRoot && ReferenceEquals(Current, this) && UnityViewBindings.Current is { IsReady: true } views)
            {
                foreach (var ui in BootstrapUIRoots())
                {
                    var root = ui.gameObject;
                    if (root.scene == _bootstrap || !_persistentRoots!.Add(root))
                        continue;
                    ui.Init();
                    views.ScanRoots(new[] { root });
                }
            }
        }

        internal void Prepare(IEnumerable<GameObject> selectedRoots, UnityViewBindings views)
        {
            var roots = _persistentRoots ?? new HashSet<GameObject> { gameObject };
            foreach (var root in selectedRoots)
            {
                ValidateRoot(_bootstrap, root);
                roots.Add(root);
            }

            foreach (var ui in BootstrapUIRoots())
            {
                ValidateRoot(_bootstrap, ui.gameObject);
                roots.Add(ui.gameObject);
            }

            var initialRoots = new HashSet<GameObject>(_bootstrap.GetRootGameObjects());
            initialRoots.UnionWith(roots);
            _persistentRoots = roots;
            foreach (var root in roots)
            {
                DontDestroyOnLoad(root);
                foreach (var ui in root.GetComponentsInChildren<UIRoot>(true))
                    if (ui != RuntimeUIRoot)
                        ui.Init();
            }

            views.ScanRoots(initialRoots);
        }

        private IEnumerable<UIRoot> BootstrapUIRoots()
        {
            if (_usesPrefabUIRoot)
                yield break;
            // An active UIRoot can move itself to DDOL during Awake, before the launcher starts.
            foreach (var ui in FindObjectsOfType<UIRoot>(true))
                if (ui.transform.parent == null && (ui.gameObject.scene == _bootstrap || ui.OriginalScene == _bootstrap))
                    yield return ui;
        }

        private static void ValidateRoot(Scene bootstrap, GameObject root)
        {
            if (!root || root.transform.parent != null || root.GetComponentInChildren<AGameLauncher>(true) ||
                (root.scene != bootstrap &&
                 (!root.TryGetComponent<UIRoot>(out var ui) || ui.OriginalScene != bootstrap)))
                throw new InvalidOperationException("Persistent OBF objects must be Bootstrap roots outside the launcher hierarchy.");
        }

        public async Task ShutdownAsync()
        {
            if (!ReferenceEquals(Current, this))
                return;

            IsStopping = true;
            try
            {
                if (Application != null)
                    await Application.StopAsync();
            }
            finally
            {
                if (ReferenceEquals(Current, this))
                    Release();
            }
        }

        private void Release()
        {
            UnityApplication.wantsToQuit -= WantsToQuit;
            Current = null;
            var roots = _persistentRoots ?? new HashSet<GameObject>();
            foreach (var ui in BootstrapUIRoots())
                if (ui.gameObject.scene != _bootstrap)
                    roots.Add(ui.gameObject);
            if (this && !roots.Contains(gameObject))
                Destroy(_fallback ? (UnityEngine.Object)gameObject : this);
            foreach (var root in roots)
                if (root)
                    Destroy(root);
        }

        private bool WantsToQuit()
        {
            RequestExit();
            return false;
        }

        private void RequestExit()
        {
            if (_quitRequested || !ReferenceEquals(Current, this))
                return;
            _quitRequested = true;
            _ = QuitAsync();
        }

        private async Task QuitAsync()
        {
            await Task.Yield();
            await StopAndObserveAsync();
            await Task.Yield();
            UnityApplication.Quit();
        }

        private void OnDestroy() => _ = StopAndObserveAsync();

        internal async Task StopAndObserveAsync()
        {
            try { await ShutdownAsync(); }
            catch (Exception error) { Debug.LogError(error); }
        }
    }
}
