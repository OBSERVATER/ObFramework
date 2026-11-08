using MessagePack;
using ObFramework.Core;
using ObFramework.Integrated.Save;

namespace ObFramework.Test;
[CanSave("player")]
public partial class PlayerModel : AModel
{
    [SaveField]
    public int _level = 325;
    [SaveField]
    public string _playerId = "325799Test";
    private int Hp = 799;
    protected override void OnInit()
    {
    }
}
[CanSave("inventory")]
public partial class InventoryModel : AModel
{
    [SaveField]
    public int Gold = 3250;
    [SaveField]
    public Dictionary<string, int> Items = new()
    {
        ["aaa"] = 1,
        ["bbb"] = 114514
    };
    protected override void OnInit()
    {
    }
}
