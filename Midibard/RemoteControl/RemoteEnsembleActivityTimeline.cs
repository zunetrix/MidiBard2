using System.Collections.Generic;
using System.Linq;

namespace MidiBard.RemoteControl;

internal static class RemoteEnsembleActivityTimeline
{
    internal const long BucketMs = 100;

    internal static IReadOnlyList<EnsembleActivityBucketResponse> Bucket(
        IEnumerable<long> noteTimesMs)
    {
        return noteTimesMs
            .Where(timeMs => timeMs >= 0)
            .GroupBy(timeMs => timeMs / BucketMs * BucketMs)
            .OrderBy(group => group.Key)
            .Select(group => new EnsembleActivityBucketResponse(
                group.Key,
                group.Count()))
            .ToArray();
    }
}
