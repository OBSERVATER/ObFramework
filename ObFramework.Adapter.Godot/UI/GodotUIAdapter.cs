using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ObFramework.Integrated.Resource;
using ObFramework.Integrated.UI;

namespace ObFramework.Adapter.Godot.UI
{
    public class GodotUIAdapter : IUIAdapter
    {
        public UIRoot Root;
        public async Task<T?> CreateAsync<T>(CancellationToken ct = default)
            where T : class, IUIWindow
        {
            return await CreateAsync(typeof(T), ct) as T;
        }

        public async Task<IUIWindow> CreateAsync(Type type, CancellationToken ct = default)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!typeof(IUIWindow).IsAssignableFrom(type))
                throw new ArgumentException($"{type.Name} must implement IUIWindow.");

            ct.ThrowIfCancellationRequested();

            var location = $"res://UI/{type.Name}.tscn";
            using var handle = await ResModule.LoadAssetAsync<PackedScene>(location);
            Node? node = null;

            try
            {
                ct.ThrowIfCancellationRequested();

                var scene = handle.Get<PackedScene>();
                node = scene.Instantiate();

                if (node is not Control control ||
                    node is not IUIWindow window ||
                    !type.IsInstanceOfType(window))
                {
                    throw new InvalidOperationException($"Scene {location} root must implement {type.Name} and inherit Control.");
                }

                control.Hide();

                var parent = GetParent(window.Level);
                parent.AddChild(control);
                window.Init();

                return window;
            }
            catch
            {
                if (node != null && GodotObject.IsInstanceValid(node))
                    node.QueueFree();
                throw;
            }
        }

        public void Open(IUIWindow instance, IUIData? data = null)
        {
            var control = (Control)instance;
            var parent = GetParent(instance.Level);

            if (control.GetParent() == null)
                parent.AddChild(control);
            else if (control.GetParent() != parent)
                control.Reparent(parent, false);

            control.MoveToFront();
            control.Show();
            instance.Open(data);
        }

        public void Close(IUIWindow instance)
        {
            instance.Close();
            ((Control)instance).Hide();
        }

        public void Release(IUIWindow instance)
        {
            var node = (Control)instance;

            if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion())
            {
                node.QueueFree();
            }
        }
        private Control GetParent(EUILevel lvl) => lvl switch
        {
            EUILevel.Scene => Root.SceneRoot,
            EUILevel.Common => Root.CommonRoot,
            EUILevel.Overlay => Root.PopupRoot,
            _ => Root.CommonRoot
        };
    }
}