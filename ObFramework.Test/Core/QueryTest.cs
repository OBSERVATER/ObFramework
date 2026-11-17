using ObFramework.Core;
using ObFramework.Extension;
using ObFramework.Utility.Collection;
namespace ObFramework.Test.Core;
[TestFixture]
public class QueryTest
{
    public interface IState : IModel { Blackboard Board { get; } }
    public sealed class State : AModel, IState
    {
        public Blackboard Board { get; } = new();
        protected override void OnInit() { Board.Set(Health, 100); Board.Set(Name, "player"); }
    }
    public sealed class Architecture : AArchitecture<Architecture>
    {
        protected override void Init() => RegisterModel<IState, State>();
    }
    private static readonly BlackboardKey<int> Health = new("health");
    private static readonly BlackboardKey<string> Name = new("name");
    private sealed class Reader(IArchitecture architecture) : ICanQuery
    {
        public IArchitecture GetArchitecture() => architecture;
    }
    private sealed class Read<T>(BlackboardKey<T> key) : AQuery<T>
    {
        public override T Q() => this.GetModel<IState>().Board.Get(key);
    }
    public sealed class ReadHealth : AQuery<int>
    {
        public override int Q() => this.GetModel<IState>().Board.Get(Health);
    }
    private sealed class Failure(Exception error) : AQuery<int>
    {
        public override int Q() => throw error;
    }
    [Test]
    public void ReadsCurrentValuesWithoutWritingOrRefreshing()
    {
        var architecture = Architecture.Interface;
        try
        {
            var board = architecture.GetModel<IState>().Board;
            var first = new Reader(architecture);
            var second = new Reader(architecture);
            var version = board.Version;
            Assert.That(first.Query(new Read<int>(Health)), Is.EqualTo(100));
            Assert.That(second.Query<ReadHealth, int>(), Is.EqualTo(100));
            Assert.That(board.Version, Is.EqualTo(version));
            board.Set(Health, 42);
            Assert.That(first.Query(new Read<int>(Health)), Is.EqualTo(42));
            Assert.That(architecture.SendQuery<ReadHealth, int>(), Is.EqualTo(42));
            Assert.That(second.Query(new Read<string>(Name)), Is.EqualTo("player"));
            Assert.That(board.Version, Is.EqualTo(version + 1));
        }
        finally { architecture.Deinit(); }
    }
    [Test]
    public void ErrorsPropagateAndLaterQueriesStillWork()
    {
        var architecture = Architecture.Interface;
        try
        {
            var error = new InvalidOperationException("query failed");
            Assert.That(Assert.Throws<InvalidOperationException>(() => architecture.SendQuery(new Failure(error))), Is.SameAs(error));
            Assert.Throws<ArgumentNullException>(() => architecture.SendQuery<int>(null!));
            Assert.That(architecture.SendQuery<ReadHealth, int>(), Is.EqualTo(100));
        }
        finally { architecture.Deinit(); }
    }
}
