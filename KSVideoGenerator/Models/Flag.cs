// File: Models/Flag.cs
namespace KSVideoGenerator.Models
{
    /// <summary>
    /// Represents the set of valid flags for the video generator,
    /// incluindo opções de Chromium.
    /// </summary>
    public class Flag
    {
        public string Url { get; }
        public double Duration { get; }
        public int Fps { get; }
        public int Width { get; }
        public int Height { get; }
        public int ChromiumDebugPort { get; }
        public string ChromiumArgs { get; }

        public Flag(
            string url,
            double duration,
            int fps,
            int width,
            int height,
            int chromiumDebugPort,
            string chromiumArgs)
        {
            Url = url;
            Duration = duration;
            Fps = fps;
            Width = width;
            Height = height;
            ChromiumDebugPort = chromiumDebugPort;
            ChromiumArgs = chromiumArgs;
        }

        public override string ToString() =>
            $"Url=\"{Url}\", Duration={Duration}s, Fps={Fps}, " +
            $"Width={Width}px, Height={Height}px, " +
            $"ChromiumDebugPort={ChromiumDebugPort}, " +
            $"ChromiumArgs=\"{ChromiumArgs}\"";
    }
}