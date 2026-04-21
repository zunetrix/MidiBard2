using System.Collections.Generic;

namespace BardMusicPlayer.XIVMIDI.IO
{
    public enum Requester
    {
        NONE = 0,
        JSON = 1,
        DOWNLOAD = 2
    }

    public static class Misc
    {
        /// <summary>
        /// Maps a UI combo-box index (0 = any) to the ensemble size string
        /// expected by the BardMusicPlayer search API.
        /// </summary>
        public static readonly Dictionary<int, string> EnsembleSize = new()
        {
            [0] = "",        // no filter
            [1] = "solo",
            [2] = "duo",
            [3] = "trio",
            [4] = "quartet",
            [5] = "quintet",
            [6] = "sextet",
            [7] = "septet",
            [8] = "octet"
        };

        /// <summary>
        /// Available source websites that can be used as a filter.
        /// </summary>
        public static readonly Dictionary<int, string> Sources = new()
        {
            [0] = "",                               // all sources
            [1] = "xivmidi.com",
            [2] = "songs.bardmusicplayer.com"
        };

        /// <summary>
        /// Sort options accepted by the API.
        /// </summary>
        public static readonly Dictionary<int, string> SortOptions = new()
        {
            [0] = "-createdAt",   // Newest First
            [1] = "createdAt",    // Oldest First
            [2] = "titleSort",    // A–Z
            [3] = "-titleSort",   // Z–A
            [4] = "-downloads"    // Most Downloaded
        };
    }
}
