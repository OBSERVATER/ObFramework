using MessagePack;
using System.Collections.Generic;

namespace ObFramework.Integrated.Save
{
    [MessagePackObject(AllowPrivate = true)]
    internal class SaveSlot
    {
        [Key(0)]
        public int Version { get; set; } = 1;
        [Key(1)]
        public Dictionary<string, byte[]> Data { get; set; } = new();
    }
}
