using System;
using System.Diagnostics;
using System.IO;

namespace KSVideoGenerator.Services
{
    /// <summary>
    /// Responsible for assembling a video from a sequence of PNG frames using FFmpeg.
    /// </summary>
    internal class FFMPEGManagerService
    {
        private readonly string _ffmpegPath;
        private readonly string _tempDir;
        private readonly string _videoDir;
        private readonly string _workingDir;

        /// <summary>
        /// </summary>
        /// <param name="ffmpegPath">Full path to the ffmpeg executable.</param>
        /// <param name="tempDir">Directory where frame*.png files live.</param>
        /// <param name="videoDir">Directory to write the final .mp4 into.</param>
        public FFMPEGManagerService(string ffmpegPath, string tempDir, string videoDir)
        {
            _ffmpegPath = ffmpegPath ?? throw new ArgumentNullException(nameof(ffmpegPath));
            _tempDir = tempDir ?? throw new ArgumentNullException(nameof(tempDir));
            _videoDir = videoDir ?? throw new ArgumentNullException(nameof(videoDir));
            _workingDir = AppContext.BaseDirectory;
        }

        /// <summary>
        /// Builds the final video at the given FPS.
        /// </summary>
        /// <param name="fps">Frames per second for the output video.</param>
        /// <returns>
        /// Tuple where:
        ///  Success = true if ffmpeg exited 0, along with OutputFile filled;
        ///  Success = false if something went wrong, ErrorMessage filled.
        /// </returns>
        public (bool Success, string OutputFile, string ErrorMessage) BuildVideo(int fps)
        {
            // 1) Gather all frames
            var pngFiles = Directory.GetFiles(_tempDir, "frame*.png");
            if (pngFiles.Length == 0)
            {
                return (false, null, "[ERROR] No frames found in temp directory, aborting.");
            }

            // 2) Prepare output filename
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var outputFile = Path.Combine(_videoDir, $"output_{timestamp}.mp4");

            // 3) Build FFmpeg arguments
            //    Using absolute path pattern for safety:
            var inputPattern = Path.Combine(_tempDir, "frame%04d.png");
            var ffArgs = $"-y " +                     // overwrite output
                         $"-framerate {fps} " +      // input FPS
                         $"-start_number 1 " +       // first frame index
                         $"-loglevel error " +       // only show errors
                         $"-i \"{inputPattern}\" " + // input pattern
                         $"-vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\" " + // ensure even dimensions
                         $"-c:v libx264 -pix_fmt yuv420p " +        // codec + pixel format
                         $"\"{outputFile}\"";        // output file

            Console.WriteLine("[INFO] FFMPEG Initialized");

            // 4) Configure and launch FFmpeg
            var psi = new ProcessStartInfo(_ffmpegPath, ffArgs)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = _workingDir
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (false, null, "[ERROR] Failed to start FFmpeg process.");

            Console.WriteLine("[INFO] Building Video...");

            // 5) Read both streams (this will block until FFmpeg exits)
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Console.WriteLine("[INFO] FFMPEG Stopped");

            // 6) Check exit code
            if (process.ExitCode != 0)
            {
                // include FFmpeg’s error output for debugging
                return (false, null,
                    $"[ERROR] FFmpeg exited with code {process.ExitCode}:\n{stderr}");
            }

            // 7) Success
            return (true, outputFile, stdout);
        }
    }
}