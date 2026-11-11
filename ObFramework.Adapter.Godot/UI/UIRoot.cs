using Godot;
using ObFramework.Adapter.Godot.Core;

namespace ObFramework.Adapter.Godot.UI
{
    public sealed class UIRoot
    {
        public Node Node { get; }
        public Control SceneRoot { get; }
        public Control CommonRoot { get; }
        public Control PopupRoot { get; }

        public UIRoot(Node root)
        {
            Node = root;
            SceneRoot = root.GetNode<Control>("SceneLayer/Root");
            CommonRoot = root.GetNode<Control>("CommonLayer/Root");
            PopupRoot = root.GetNode<Control>("PopupLayer/Root");
        }

        public static UIRoot Ensure(SceneTree tree)
        {
            const string name = "UIRoot";
            var root = tree.Root.GetNodeOrNull<Node>(name);
            if (root == null)
            {
                root = new Node { Name = name };
                CreateLayer(root, "SceneLayer", 1);
                CreateLayer(root, "CommonLayer", 2);
                CreateLayer(root, "PopupLayer", 3);
                tree.Root.AddChild(root);
                GodotRuntimeHost.Find(tree)?.OwnUIRoot(root);
            }
            return new UIRoot(root);
        }

        private static void CreateLayer(Node root, string name, int order)
        {
            var layer = new CanvasLayer
            {
                Name = name,
                Layer = order
            };

            root.AddChild(layer);

            var control = new Control
            {
                Name = "Root",
                MouseFilter = Control.MouseFilterEnum.Ignore
            };

            layer.AddChild(control);
            control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }
    }
}