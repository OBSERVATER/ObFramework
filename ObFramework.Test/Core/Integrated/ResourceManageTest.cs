using ObFramework.Core;
using ObFramework.Extension;

namespace ObFramework.Test;
[TestFixture]
public class ResourceManageTest
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


}
