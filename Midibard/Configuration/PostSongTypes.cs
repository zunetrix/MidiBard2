namespace MidiBard;

public enum PostSongMode
{
    /// <summary>
    /// Build the chat message from a template string using {Token} placeholders
    /// filled from the song's database fields (Name, Artist, Year, Duration, Comments, Tags).
    /// </summary>
    DatabaseTemplate = 0,

    /// <summary>
    /// Build the chat message by applying a capture regex to the file name,
    /// then formatting captured groups via an output format string.
    /// </summary>
    FilepathRegex = 1,
}

public class PostSongConfig
{
    public bool Enabled = false;
    public ChatType ChatTarget = ChatType.Current;
    public PostSongMode Mode = PostSongMode.DatabaseTemplate;

    // Mode 0: DatabaseTemplate
    /// <summary>
    /// Template string for Mode 0.  Supported tokens:
    ///   {SongName}  {Artist}  {Year}  {Duration}  {Comments}  {Tag[0]}  {Tag[1]} …
    /// </summary>
    public string Template = "{SongName}";

    // Mode 1: FilepathRegex
    /// <summary>Regex applied to the file name (without extension) to capture groups.</summary>
    public string CaptureRegex = "";
    /// <summary>Output format for Mode 1.  Use $1, $2, ... for captured groups.</summary>
    public string OutputFormat = "";

    // Post-processing (both modes)
    public string FindRegex = "";
    public string Replacement = "";
}
