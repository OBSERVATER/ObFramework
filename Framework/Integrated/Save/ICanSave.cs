using System;

namespace ObFramework.Integrated.Save
{
    public interface ICanSave
    {
        string Key { get; }
        Type SaveDataType { get; }
        ASaveData Capture();
        void Restore(ASaveData value);
    }
}
