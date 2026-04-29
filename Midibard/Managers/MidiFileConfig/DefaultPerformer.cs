using System.Collections.Generic;

namespace MidiBard.Managers;

internal class DefaultPerformer
{
    public Dictionary<ulong, List<int>> TrackMappingDict = new Dictionary<ulong, List<int>>();
}
