namespace ObFramework.Integrated.Resource.Handle
{
    public class AssetHandle : AHandle
    {
        private AssetManager _manager;
        private AssetEntry _entry;
        public AssetHandle(AssetManager manager, AssetEntry entry)
        {
            _manager = manager;
            _entry = entry;
            Location = entry.Key.Location;
        }
        public override T Get<T>() where T : class
        {
            ThrowIfDisposed();
            return _entry.Asset as T;
        }
        protected override void Release()
        {
            _manager.ReleaseEntry(_entry);
        }
    }
}
