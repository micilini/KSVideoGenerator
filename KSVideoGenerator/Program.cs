using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using KSVideoGenerator.Models;
using KSVideoGenerator.Services;
using System.Runtime.InteropServices;

namespace KSVideoGenerator
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            // 0) Process Started
            Console.OutputEncoding = Encoding.UTF8;

            var welcome = new WelcomeMessageService();
            welcome.ShowWelcome();

            // 1) Argument parse
            var flagService = new FlagManageService(args);

            if (!flagService.TryToProcessFlags(out Flag[] flags))
            {
                Console.WriteLine(flagService.ErrorMessage);
                return 1;
            }

            // 2) Creates directories if they don't exist, and also deletes any type of file that exists inside them
            var fileService = new FileManagerService();
            fileService.PrepareDirectory("temp_images");
            fileService.EnsureDirectoryExists("videos");

            // 2.1) Check if User are using Windows or Linux to change Tools Paths
            var isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            var chromeRel = isWin
                ? "tools/windows-x64/chromium/chrome.exe"
                : "tools/linux-x64/chromium/chrome";

            var ffmpegRel = isWin
                ? "tools/windows-x64/ffmpeg/ffmpeg.exe"
                : "tools/linux-x64/ffmpeg/ffmpeg";

            if (!fileService.FileExists(chromeRel) || !fileService.FileExists(ffmpegRel))
            {
                Console.Error.WriteLine("[ERROR] chromium or ffmpeg not found in tools folder.");
                return 1;
            }

            // 3) Initialize and prepare Chromium services to take multiple screenshots
            var chromePath = Path.Combine(AppContext.BaseDirectory, chromeRel);
            var tempDir = Path.Combine(AppContext.BaseDirectory, "temp_images");

            var chromium = new ChromiumManagerService(chromePath, tempDir, flags[0].ChromiumDebugPort);

            AppDomain.CurrentDomain.ProcessExit += (_, __) => chromium.StopChromium();
            Console.CancelKeyPress += (_, e) =>
            {
                chromium.StopChromium();
                e.Cancel = false;
            };

            try
            {
                await chromium.CaptureAsync(
                    url: flags[0].Url,
                    duration: flags[0].Duration,
                    fps: flags[0].Fps,
                    width: flags[0].Width,
                    height: flags[0].Height,
                    chromiumArgs: flags[0].ChromiumArgs
                );
            }
            catch (TimeoutException tx)
            {
                Console.Error.WriteLine("[ERROR] Capture aborted: " + tx.Message);
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.Message}");
                Console.Error.WriteLine("[ERROR] Unable to generate video.");
                return 1;
            }

            // 4) Creates an .MP4 video by joining all the images in temp_images
            var ffmpegPath = Path.Combine(AppContext.BaseDirectory, ffmpegRel);
            var videoDir = Path.Combine(AppContext.BaseDirectory, "videos");

            var ffmpegService = new FFMPEGManagerService(ffmpegPath, tempDir, videoDir, flags[0].SoundTrack);
            var result = ffmpegService.BuildVideo(flags[0].Fps);

            if (!result.Success)
            {
                Console.Error.WriteLine(result.ErrorMessage);
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine($"✅ Video Generated At: {result.OutputFile}");

            // 5) Cleans the images in the temp_images folder
            fileService.PrepareDirectory("temp_images");

            return 0;
        }
    }
}