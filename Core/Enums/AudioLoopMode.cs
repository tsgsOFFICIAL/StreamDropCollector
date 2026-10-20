namespace Core.Enums
{
    /// <summary>
    /// Stream loop mode used instead of playing the video.
    /// </summary>
    public enum AudioLoopMode
    {
        /// <summary>No loop: the stream is played normally (160p video).</summary>
        Off,

        /// <summary>The stream playlist is polled and each new segment is only requested with HEAD (no body).</summary>
        HeadOnly
    }
}
