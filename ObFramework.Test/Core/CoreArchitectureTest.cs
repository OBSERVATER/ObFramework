using ObFramework.Core;
using ObFramework.Extension;

namespace ObFramework.Test.Core;

[TestFixture]
internal class CoreArchitectureTest
{
    internal class TestArchitecture : AArchitecture<TestArchitecture>
    {
        protected override void Init()
        {
            RegisterModel<ITestModel, TestModel>();
            RegisterSystem<ITestSystem, TestSystem>();
        }
    }
    internal struct AddEvent
    {
        public int Num;
    }
    internal interface ITestSystem : ISystem
    {
        void Add(int num);
    }
    internal interface ITestModel : IModel
    {
        int Get();
        void Set(int num);
    }
    internal class TestModel : AModel, ITestModel
    {
        private int _num;
        public int Get() => _num;
        public void Set(int num) => _num = num;

        protected override void OnInit()
        {
            _num = 0;
        }
    }
    internal class TestSystem : ASystem, ITestSystem
    {
        public void Add(int num)
        {
            var n = this.GetModel<ITestModel>().Get();
            n += num;
            this.GetModel<ITestModel>().Set(n);
        }

        protected override void OnInit()
        {
            this.RegisterEvent<AddEvent>(x => Add(x.Num));
        }
    }
    internal class TestCommand1_AddValue : ACommand<int>
    {
        private int _val;

        public TestCommand1_AddValue(int val)
        {
            _val = val;
        }

        public override int Execute()
        {
            this.GetSystem<ITestSystem>().Add(_val);
            return this.GetModel<ITestModel>().Get();
        }
    }
    internal class TestCommand2_Async : AAsyncCommand<int>
    {
        private int _val;

        public TestCommand2_Async(int val)
        {
            _val = val;
        }

        public override async ValueTask<int> Execute(CancellationToken ct)
        {
            await Task.Delay(100, ct);
            this.GetSystem<ITestSystem>().Add(_val);
            return this.GetModel<ITestModel>().Get();
        }
    }
    internal class TestRuntimeData : IRuntimeData
    {
        public int Value { get; set; }

        public string Id => "1";
    }
    internal class TestQuery : AQuery<TestRuntimeData>
    {
        protected override void OnRefresh()
        {
            _data.GetOrAdd("1", new()
            {
                Value = this.GetModel<ITestModel>().Get(),
            });
        }
    }
    [Test]
    public async Task CoreTest_BasicCoreEvent()
    {
        var self = TestArchitecture.Arch;
        try
        {
            Assert.That(self.GetModel<ITestModel>().Get(), Is.EqualTo(0));
            self.GetSystem<ITestSystem>().Add(1);
            Assert.That(self.GetModel<ITestModel>().Get(), Is.EqualTo(1));
            self.SendEvent(new AddEvent { Num = 10 });
            Assert.That(self.GetModel<ITestModel>().Get(), Is.EqualTo(11));
            int result1 = self.SendCommand(new TestCommand1_AddValue(3));
            Assert.That(result1, Is.EqualTo(14));
            int result2 = await self.SendCommandAsync(new TestCommand2_Async(6));
            Assert.Multiple(() =>
            {
                Assert.That(result2, Is.EqualTo(20));
                Assert.That(self.GetModel<ITestModel>().Get(), Is.EqualTo(20));
                Assert.That(self.SendQuery<TestQuery, TestRuntimeData>().TryGet("1", out var val), Is.EqualTo(true));
                Assert.That(val.Value, Is.EqualTo(20));
            });
        }
        finally
        {
            self.Deinit();
        }
    }
    [Test]
    public void Bind_WithTrigger_OnlyNotifiesNewSubscriber()
    {
        var property = new BindableProperty<int>(100);

        int a = 0;
        int b = 0;

        property.Bind(_ => a++);
        property.Bind(_ => b++, trigger: true);

        Assert.That(a, Is.EqualTo(0));
        Assert.That(b, Is.EqualTo(1));
    }
}
