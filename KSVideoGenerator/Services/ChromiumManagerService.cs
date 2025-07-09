using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KSVideoGenerator.Services
{
    internal class ChromiumManagerService: IDisposable
    {
        private readonly string _chromePath;
        private readonly string _tempDir;
        private readonly int _debugPort;
        private Process _chromeProcess;
        private ClientWebSocket _ws;
        private HttpClient _http;

        public ChromiumManagerService(string chromePath, string tempDir, int debugPort = 9222)
        {
            _chromePath = chromePath;
            _tempDir = tempDir;
            _debugPort = debugPort;
            var handler = new HttpClientHandler
            {
                UseProxy = false
            };
            _http = new HttpClient(handler);
        }

        /// <summary>
        /// Checks if the URL is accessible and returns code 2xx.
        /// Throws exceptions or returns false in any other situation.
        /// </summary>
        private async Task ValidateUrlAsync(string url)
        {
            HttpResponseMessage resp;
            try
            {
                resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"[ERROR] HTTP request failed: {ex.Message}");
                throw;
            }
            catch (TaskCanceledException ex)
            {
                // timeout
                Console.Error.WriteLine($"[ERROR] Timeout when accessing {url}: {ex.Message}");
                throw;
            }

            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine(
                    $"[ERROR] HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} when accessing {url}"
                );
                throw new InvalidOperationException("URL did not return 2xx status.");
            }
        }

        /// <summary>
        /// Captures the following frames <paramref name="url"/> during <paramref name="duration"/> in <paramref name="fps"/> fps.
        /// </summary>
        public async Task CaptureAsync(string url, double duration, int fps, int width, int height, string chromiumArgs)
        {
            // 0) Validate URL
            await ValidateUrlAsync(url);

            await LaunchHeadlessBrowserAsync(width, height, chromiumArgs);
            await ConnectToDevToolsAsync();

            int id = 1;
            await PauseAnimationsAsync(id++);
            await EnablePageAndNavigateAsync(id++, url);
            await ResumeAnimationsAsync(id++);

            await InjectCssRepaintAsync(id++, fps);
            await Task.Delay((int)Math.Round(500.0 / fps));

            await StartScreencastAsync(id++);
            await CaptureFramesAsync(id, duration, fps);
            await StopScreencastAndTeardownAsync(id);
        }

        private async Task LaunchHeadlessBrowserAsync(int width, int height, string chromiumArgs)
        {
            var args = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(chromiumArgs))
                args.Append(chromiumArgs.Trim());

            args.Append(" --mute-audio");

            args.Append($" --remote-debugging-port={_debugPort}");

            args.Append(" --remote-debugging-address=127.0.0.1");

            args.Append($" --window-size={width},{height}");

            var psi = new ProcessStartInfo(_chromePath, args.ToString())
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(_chromePath)
            };

            _chromeProcess = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to initialize Chromium.");

            _chromeProcess.BeginOutputReadLine();
            _chromeProcess.BeginErrorReadLine();

            Console.WriteLine("[INFO] Chromium Initialized");

            // give DevTools some time to load
            await WaitForDevToolsAsync();
        }

        private async Task WaitForDevToolsAsync(int timeoutMs = 20000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    var resp = await _http.GetAsync($"http://localhost:{_debugPort}/json");
                    if (resp.IsSuccessStatusCode)
                    {
                        var txt = await resp.Content.ReadAsStringAsync();
                        if (txt.TrimStart().StartsWith("[")) return;
                    }
                }
                catch { /* silence, try again... */ }
                await Task.Delay(100);
            }
            throw new TimeoutException("DevTools did not respond within the expected time.");
        }

        private async Task ConnectToDevToolsAsync()
        {
            try
            {
                var listJson = await _http.GetStringAsync($"http://localhost:{_debugPort}/json");
                using var listDoc = JsonDocument.Parse(listJson);
                var wsUrl = listDoc.RootElement[0]
                                     .GetProperty("webSocketDebuggerUrl")
                                     .GetString()
                             ?? throw new InvalidOperationException("webSocketDebuggerUrl não encontrado.");

                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
            }
            catch (HttpRequestException hre)
            {
                throw new InvalidOperationException(
                    $"Could not reach DevTools (http://localhost:{_debugPort}/json): {hre.Message}"
                );
            }
        }

        private Task PauseAnimationsAsync(int id)
            => SendCommand(_ws, id, "Page.addScriptToEvaluateOnNewDocument", new
            {
                source = @"
                    (function(){
                        const s = document.createElement('style');
                        s.id = '__paused';
                        s.textContent = '*{animation-play-state:paused!important;transition:none!important;}';
                        document.head.appendChild(s);
                    })();"
            });

        private async Task EnablePageAndNavigateAsync(int id, string url)
        {
            await SendCommand(_ws, id, "Page.enable", new { });

            await SendCommand(_ws, id + 1, "Page.navigate", new { url });

            await WaitForPageLoad(_ws);
        }

        private async Task ResumeAnimationsAsync(int id)
        {
            await SendCommand(_ws, id, "Runtime.evaluate", new
            {
                expression = "document.getElementById('__paused')?.remove();"
            });

            // Give the style some time to be removed.
            await Task.Delay(50);
        }

        private Task StartScreencastAsync(int id)
            => SendCommand(_ws, id, "Page.startScreencast", new
            {
                format = "png",
                everyNthFrame = 1,
                maxWidth = 1920,
                maxHeight = 1080
            });

        private async Task CaptureFramesAsync(int startId, double duration, int fps)
        {
            int total = (int)Math.Ceiling(duration * fps);
            Console.WriteLine($"[INFO] Capturing {total} frames…");
            int id = startId;

            for (int frame = 1; frame <= total; frame++)
            {
                // timeout 10 seconds per frame
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                string b64 = null;
                long sessionId = 0;

                try
                {
                    while (true)
                    {
                        var msg = (await ReceiveFullMessageAsync(_ws, cts.Token)).Trim();

                        if (!msg.StartsWith("{")) continue;

                        using var doc = JsonDocument.Parse(msg);
                        if (doc.RootElement.TryGetProperty("method", out var m)
                            && m.GetString() == "Page.screencastFrame")
                        {
                            var p = doc.RootElement.GetProperty("params");
                            b64 = p.GetProperty("data").GetString();
                            sessionId = p.GetProperty("sessionId").GetInt64();
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException($"[ERROR] No frame received after 10s (frame {frame}/{total}).");
                }

                // ACK + recording…
                await SendCommand(_ws, id++, "Page.screencastFrameAck", new { sessionId });
                var path = Path.Combine(_tempDir, $"frame{frame:0000}.png");
                await File.WriteAllBytesAsync(path, Convert.FromBase64String(b64));

                // Update Draw Bar...
                DrawProgressBar(frame, total);
            }

            Console.WriteLine();
        }

        private async Task StopScreencastAndTeardownAsync(int id)
        {
            await SendCommand(_ws, id, "Page.stopScreencast", new { });
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            if (!_chromeProcess.HasExited)
            {
                _chromeProcess.Kill();
                Console.WriteLine("[INFO] Chromium Stopped");
            }
                
        }

        private Task InjectCssRepaintAsync(int id, int fps)
        {
            var period = Math.Round(1000.0 / fps);
            var script = $@"
                            (function(){{
                                // Define keyframes que alternam opacity do html
                                const style = document.createElement('style');
                                style.textContent = `
                                    @keyframes __repaintAnim {{
                                        0%   {{ opacity: 1; }}
                                        50%  {{ opacity: 0.99; }}
                                        100% {{ opacity: 1; }}
                                    }}
                                    html {{
                                        animation: __repaintAnim {period}ms linear infinite;
                                    }}
                                `;
                                document.head.appendChild(style);
                            }})();";
            return SendCommand(_ws, id, "Runtime.evaluate", new { expression = script });
        }

        // ——— Static Helpers ———

        private static void DrawProgressBar(int progress, int total, int barWidth = 40)
        {
            double ratio = (double)progress / total;
            int filled = (int)(ratio * barWidth);
            string bar = new string('#', filled) + new string('-', barWidth - filled);
            int percent = (int)(ratio * 100);
            Console.Write($"\r[{bar}] {percent,3}% ({progress}/{total})");
        }

        private static async Task SendCommand(ClientWebSocket ws, int id, string method, object @params)
        {
            var cmd = new { id, method, @params };
            var json = JsonSerializer.Serialize(cmd);
            await ws.SendAsync(Encoding.UTF8.GetBytes(json),
                              WebSocketMessageType.Text, true, CancellationToken.None);

            // Consumes confirmation response, but without extra logic
            var buf = new ArraySegment<byte>(new byte[8192]);
            await ws.ReceiveAsync(buf, CancellationToken.None);
        }

        private static async Task WaitForPageLoad(ClientWebSocket ws)
        {
            var buf = new ArraySegment<byte>(new byte[4096]);
            while (true)
            {
                var res = await ws.ReceiveAsync(buf, CancellationToken.None);
                var msg = Encoding.UTF8.GetString(buf.Array, 0, res.Count);
                if (!msg.StartsWith("{")) continue;
                try
                {
                    using var doc = JsonDocument.Parse(msg);
                    if (doc.RootElement.TryGetProperty("method", out var m) &&
                        m.GetString() == "Page.loadEventFired")
                    {
                        break;
                    }
                }
                catch { }
            }
        }

        private async Task<string> ReceiveFullMessageAsync(ClientWebSocket ws, CancellationToken token)
        {
            using var ms = new MemoryStream();
            var chunk = new byte[64 * 1024]; // 64 KiB por iteração
            WebSocketReceiveResult res;
            do
            {
                res = await ws.ReceiveAsync(chunk, token);
                if (res.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("WebSocket closed unexpectedly");
                ms.Write(chunk, 0, res.Count);
            } while (!res.EndOfMessage);

            return Encoding.UTF8.GetString(ms.ToArray());
        }


        // --- IDisposable ---

        public void StopChromium()
        {
            if (_ws?.State == WebSocketState.Open)
                _ws.Abort();

            if (_chromeProcess != null && !_chromeProcess.HasExited)
            {
                _chromeProcess.Kill();
                Console.WriteLine("[INFO] Chromium Stopped");
            }
        }

        public void Dispose()
        {
            StopChromium();
        }
    }
}