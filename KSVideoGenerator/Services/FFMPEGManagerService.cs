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
        private readonly string _soundTrack;

        /// <summary>
        /// </summary>
        /// <param name="ffmpegPath">Full path to the ffmpeg executable.</param>
        /// <param name="tempDir">Directory where frame*.png files live.</param>
        /// <param name="videoDir">Directory to write the final .mp4 into.</param>
        public FFMPEGManagerService(string ffmpegPath, string tempDir, string videoDir, string soundTrack)
        {
            _ffmpegPath = ffmpegPath ?? throw new ArgumentNullException(nameof(ffmpegPath));
            _tempDir = tempDir ?? throw new ArgumentNullException(nameof(tempDir));
            _videoDir = videoDir ?? throw new ArgumentNullException(nameof(videoDir));
            _soundTrack = soundTrack; // can be null or empty
            _workingDir = AppContext.BaseDirectory;
        }

        /// <summary>
        /// Validates and returns a usable .mp3 soundtrack path, or null if none.
        /// </summary>
        private string ResolveSoundTrack()
        {
            if (string.IsNullOrWhiteSpace(_soundTrack))
            {
                Console.WriteLine("[WARN] No soundTrack provided, proceeding without audio.");
                return null;
            }

            string path = _soundTrack;
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(_workingDir, path);
            }

            if (!File.Exists(path))
            {
                Console.WriteLine($"[WARN] soundTrack not found at '{path}', proceeding without audio.");
                return null;
            }

            if (!string.Equals(Path.GetExtension(path), ".mp3", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[WARN] soundTrack is not an MP3 file ('{path}'), proceeding without audio.");
                return null;
            }

            // Probe with FFmpeg to verify validity
            var probeArgs = $"-v error -i \"{path}\" -f null -";
            var probePsi = new ProcessStartInfo(_ffmpegPath, probeArgs)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };

            using var probeProc = Process.Start(probePsi);
            if (probeProc == null)
            {
                Console.WriteLine("[WARN] Could not start FFmpeg to probe soundTrack, proceeding without audio.");
                return null;
            }

            var probeError = probeProc.StandardError.ReadToEnd();
            probeProc.WaitForExit();

            if (probeProc.ExitCode != 0)
            {
                Console.WriteLine($"[WARN] soundTrack '{path}' is not a valid MP3 ({probeError.Trim()}), proceeding without audio.");
                return null;
            }

            return path;
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

            // 3) Validate optional soundtrack
            var audioPath = ResolveSoundTrack();

            // 4) Build FFmpeg arguments
            var inputPattern = Path.Combine(_tempDir, "frame%04d.png");
            var ffArgs = $"-y -framerate {fps} -start_number 1 -loglevel error " +
                         $"-i \"{inputPattern}\" ";

            if (audioPath != null)
            {
                ffArgs += $"-i \"{audioPath}\" ";
            }

            ffArgs += $"-vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\" -c:v libx264 -pix_fmt yuv420p ";

            if (audioPath != null)
            {
                ffArgs += "-c:a aac -shortest ";
            }

            ffArgs += $"\"{outputFile}\"";

            Console.WriteLine("[INFO] FFMPEG Initialized");
            //Console.WriteLine("[INFO] Running FFmpeg with args: " + ffArgs);

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