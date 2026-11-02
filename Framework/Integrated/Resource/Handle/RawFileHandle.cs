using System;
using System.Text;

namespace ObFramework.Integrated.Resource.Handle
{
    public class RawFileHandle : AHandle
    {
        private readonly IAssetEntryOwner _manager;
        private readonly AssetEntry _entry;

        internal RawFileHandle(IAssetEntryOwner manager, AssetEntry entry)
        {
            _manager = manager;
            _entry = entry;
            Location = entry.Key.Location;
        }

        public override T? Get<T>() where T : class
        {
            ThrowIfDisposed();
            byte[] bytes = (byte[])_entry.Asset;

            if (typeof(T) == typeof(string))
                return Encoding.UTF8.GetString(bytes) as T;

            if (typeof(T) == typeof(byte[]))
                return bytes as T;

            throw new NotSupportedException($"Unsupported raw file type: {typeof(T)}");
        }

        protected override void Release()
        {
            _manager.ReleaseEntry(_entry);
        }
    }
}