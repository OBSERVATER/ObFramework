using System.Collections.Generic;

namespace ObFramework.Integrated.Save
{
    /// <summary>
    /// 存档迁移接口
    /// </summary>
    public interface ISaveMigration
    {
        int FromVersion { get; }
        void Migrate(Dictionary<string, byte[]> entries);
    }

}