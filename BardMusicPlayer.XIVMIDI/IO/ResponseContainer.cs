using System.Collections.Generic;

namespace BardMusicPlayer.XIVMIDI.IO
{
    /// <summary>
    /// Containers for all API responses.
    /// </summary>
    public static class ResponseContainer
    {
        // MIDI file download result (unchanged – still returned after a download)

        /// <summary>
        /// Holds a downloaded MIDI file's name and raw bytes.
        /// </summary>
        public record MidiFile
        {
            /// <summary>Filename of the downloaded MIDI.</summary>
            public string Filename { get; set; } = "";

            /// <summary>Raw bytes – ready to use with <c>File.WriteAllBytes</c>.</summary>
            public byte[] data { get; set; } = null;
        }

        // Search API response  (https://bardmusicplayer.com/api/midi-search)

        /// <summary>
        /// Top-level response returned by the MIDI search endpoint.
        /// </summary>
        public record ApiResponse
        {
            /// <summary>List of matched MIDI entries.</summary>
            public List<MidiEntry> docs { get; set; } = new();
        }

        /// <summary>
        /// A single MIDI entry as returned by the search API.
        /// </summary>
        public record MidiEntry
        {
            public int id { get; set; }
            public string title { get; set; } = "";
            public string titleSort { get; set; } = "";
            public string artist { get; set; } = "";

            /// <summary>Album / game title or origin of the song.</summary>
            public string source { get; set; } = "";

            /// <summary>Display name of the arranger / editor.</summary>
            public string arranger { get; set; } = "";

            /// <summary>Ensemble size string, e.g. "solo", "octet".</summary>
            public string ensembleSize { get; set; } = "";


            /// <summary>Human-readable duration, e.g. "6:30".</summary>
            public string duration { get; set; } = "";

            public string notes { get; set; } = "";
            public int downloads { get; set; }
            public string originalSourceUrl { get; set; } = "";

            /// <summary>Duration in milliseconds.</summary>
            public long songDurationMs { get; set; }

            /// <summary>Direct URL to download the MIDI file.</summary>
            public string url { get; set; } = "";

            /// <summary>Suggested filename for the download.</summary>
            public string filename { get; set; } = "";


            /// <summary>Uploader / submitter details.</summary>
            public UploadedBy uploadedBy { get; set; }
        }

        /// <summary>
        /// Information about the user who uploaded the entry.
        /// </summary>
        public record UploadedBy
        {
            public int id { get; set; }
            public string displayName { get; set; } = "";
        }
    }
}
