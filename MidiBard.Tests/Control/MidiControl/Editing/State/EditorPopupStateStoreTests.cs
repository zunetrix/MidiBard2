using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.State;

public class EditorPopupStateStoreTests
{
    [Fact]
    public void GetOrCreate_ReturnsSameTypedStateForSameKey()
    {
        var store = new EditorPopupStateStore();

        var first = store.GetOrCreate<TestPopupState>("operation.test");
        first.Count = 3;

        var second = store.GetOrCreate<TestPopupState>("operation.test");

        second.ShouldBeSameAs(first);
        second.Count.ShouldBe(3);
        store.Count.ShouldBe(1);
    }

    [Fact]
    public void GetOrCreate_UsesFactoryForMissingState()
    {
        var store = new EditorPopupStateStore();

        var state = store.GetOrCreate(
            "operation.factory",
            () => new TestPopupState { Count = 7 });

        state.Count.ShouldBe(7);
    }

    [Fact]
    public void GetOrCreate_RejectsTypeMismatchForExistingKey()
    {
        var store = new EditorPopupStateStore();
        store.Set("operation.test", new TestPopupState());

        Should.Throw<InvalidOperationException>(() => store.GetOrCreate<OtherPopupState>("operation.test"))
            .Message.ShouldContain("not a");
    }

    [Fact]
    public void TryGet_ReturnsFalseForMissingState()
    {
        var store = new EditorPopupStateStore();

        store.TryGet<TestPopupState>("operation.missing", out var state).ShouldBeFalse();
        state.ShouldBeNull();
    }

    [Fact]
    public void SetRemoveAndClear_ManageStoredStates()
    {
        var store = new EditorPopupStateStore();

        store.Set("operation.a", new TestPopupState());
        store.Set("operation.b", new OtherPopupState());
        store.Count.ShouldBe(2);

        store.Remove("operation.a").ShouldBeTrue();
        store.Count.ShouldBe(1);

        store.Clear();
        store.Count.ShouldBe(0);
    }

    private sealed class TestPopupState
    {
        public int Count { get; set; }
    }

    private sealed class OtherPopupState
    {
    }
}
