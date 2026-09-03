using MidiBard.RemoteControl;

namespace MidiBard.Tests.RemoteControl;

public class RemoteEnsembleActivityTimelineTests
{
    [Fact]
    public void BucketsNoteActivityAtOneHundredMillisecondResolution()
    {
        var buckets = RemoteEnsembleActivityTimeline.Bucket(
            new long[] { -1, 0, 20, 99, 100, 149, 275 });

        buckets.ShouldBe(
            new[]
            {
                new EnsembleActivityBucketResponse(0, 3),
                new EnsembleActivityBucketResponse(100, 2),
                new EnsembleActivityBucketResponse(200, 1),
            });
    }

    [Fact]
    public void EmptyActivityProducesNoBuckets()
    {
        RemoteEnsembleActivityTimeline.Bucket(Array.Empty<long>())
            .ShouldBeEmpty();
    }
}
