namespace Media.VideoProcessing
{
    public class FFmpegOptions
    {
        public string Path { get; set; }
        public int MaxConcurrentFFmpegProcesses { get; set; }
    }
}