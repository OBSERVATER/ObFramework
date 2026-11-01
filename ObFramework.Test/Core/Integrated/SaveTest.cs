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
public class SaveTest
{
    [SetUp]
    public void Setup()
    {
    }
    
    // [Test]
    // public async Task SaveLoadAndGeneratorTest()
    // {
    //     var model = new PlayerModel();
    //     var dict = new Dictionary<string, ICanSave>()
    //     {
    //         ["player"] = model
    //     };
    //     SaveModule.Init(new FileSaveStore(".."),dict);
    //     await SaveModule.SaveAsync("slot_01");
    //     model._level = 799;
    //     model._playerId = "zc";
    //     await SaveModule.LoadAsync("slot_01");
    //     Assert.That(model._playerId == "325799Test" && model._level == 325, Is.True);
    // }
    // [Test]
    // public async Task MoreModel()
    // {
    //     var model = new PlayerModel();
    //     var inventory = new InventoryModel();
    //
    //     SaveModule.Init(new FileSaveStore(".."), );
    //     await SaveModule.SaveAsync("slot_01");
    //     model._level = 799;
    //     model._playerId = "zc";
    //     inventory.Gold = 0;
    //     inventory.Items.Clear();
    //     await SaveModule.LoadAsync("slot_01");
    //     Assert.That(model._playerId == "325799Test" && model._level == 325, Is.True);
    //     Assert.That(inventory.Gold == 3250 && inventory.Items["aaa"] == 1 && inventory.Items["bbb"] == 114514, Is.True);
    // }
}
