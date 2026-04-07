using Trading.Oms.Infrastructure.Services.Mock;

namespace Trading.Oms.Infrastructure.Tests.Services;

public class MockOrderSequenceAllocatorTests
{
    [Test]
    public async Task AllocateNextSequenceForAccount_StartsAtOneAndIncrementsPerAccount()
    {
        var allocator = new MockOrderSequenceAllocator();

        var first = await allocator.AllocateNextSequenceForAccount(123);
        var second = await allocator.AllocateNextSequenceForAccount(123);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AllocateNextSequenceForAccount_TracksEachAccountIndependently()
    {
        var allocator = new MockOrderSequenceAllocator();

        var account123First = await allocator.AllocateNextSequenceForAccount(123);
        var account456First = await allocator.AllocateNextSequenceForAccount(456);
        var account123Second = await allocator.AllocateNextSequenceForAccount(123);

        Assert.Multiple(() =>
        {
            Assert.That(account123First, Is.EqualTo(1));
            Assert.That(account456First, Is.EqualTo(1));
            Assert.That(account123Second, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AllocateNextSequenceForAccount_IsSafeForConcurrentAllocation()
    {
        var allocator = new MockOrderSequenceAllocator();

        var sequences = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(_ => allocator.AllocateNextSequenceForAccount(123)));

        Assert.That(sequences, Is.EquivalentTo(Enumerable.Range(1, 50).Select(value => (uint)value)));
    }
}
