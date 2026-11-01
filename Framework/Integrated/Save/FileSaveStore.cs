using System;
using System.IO;
using System.Threading.Tasks;

namespace ObFramework.Integrated.Save
{
    public interface ISaveStore
    {
        Task WriteAsync(byte[] data, string slotId);
        Task<byte[]> TryReadAsync(string slotId);
        bool Exists(string slotId);
        void Delete(string slotId);
    }
    public class FileSaveStore : ISaveStore
    {
        private readonly string _path;

        public FileSaveStore(string path)
        {
            _path = Path.Combine(path,"Saves");
        }

        public void Delete(string slotId)
        {
            var path = Path.Combine(_path, $"{slotId}.sav");
            File.Delete(path);
        }

        public bool Exists(string slotId)
        {
            var path = Path.Combine(_path, $"{slotId}.sav");
            return File.Exists(path);
        }

        public async Task<byte[]> TryReadAsync(string slotId)
        {
            var path = Path.Combine(_path, $"{slotId}.sav");
            if (!Exists(slotId))
            {
                return Array.Empty<byte>();
            }
            try
            {
                return await File.ReadAllBytesAsync(path);
            }
            catch (Exception)
            {
                throw;
            }
        }

        public async Task WriteAsync(byte[] data, string slotId)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            // slotId 应在这里或统一路径方法中验证
            var path = Path.Combine(_path, $"{slotId}.sav");
            var tmpPath = Path.Combine(_path, $"{slotId}.{Guid.NewGuid():N}.tmp");

            Directory.CreateDirectory(_path);

            try
            {
                await File.WriteAllBytesAsync(tmpPath, data).ConfigureAwait(false);

                if (File.Exists(path))
                {
                    File.Replace(tmpPath, path, null);
                }
                else
                {
                    File.Move(tmpPath, path);
                }
            }
            finally
            {
                if (File.Exists(tmpPath))
                {
                    File.Delete(tmpPath);
                }
            }
        }
    }
}
