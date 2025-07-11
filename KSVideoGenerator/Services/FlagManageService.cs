// File: Services/FlagManageService.cs
using System;
using System.IO;
using System.Text.Json;
using KSVideoGenerator.Models;

namespace KSVideoGenerator.Services
{
    /// <summary>
    /// Processes an array of args and either:
    ///  1) Loads all settings from a JSON config file (--configFile), ignoring other flags;
    ///  2) Or, if no configFile is provided, extracts individual flags (--url, --duration, etc.)
    /// </summary>
    public class FlagManageService
    {
        private readonly string[] _args;
        private int DefaultChromiumDebugPort = 9222;
        private string DefaultChromiumArgs =
            "--headless " +
            "--no-sandbox --force-device-scale-factor=1 " +
            "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows " +
            "--remote-debugging-address=127.0.0.1 ";

        /// <summary> Error message if extraction or validation fails. </summary>
        public string ErrorMessage { get; private set; }

        public FlagManageService(string[] args)
        {
            _args = args ?? throw new ArgumentNullException(nameof(args));
        }

        public bool TryToProcessFlags(out Flag[] flags)
        {
            // 1) Verifica se veio --configFile
            string configPath = null;
            for (int i = 0; i < _args.Length; i++)
            {
                if (_args[i] == "--configFile")
                {
                    if (i + 1 >= _args.Length)
                    {
                        ErrorMessage = "Flag --configFile requires a file path.";
                        flags = null;
                        return false;
                    }
                    configPath = _args[++i];
                    break;
                }
            }

            // 1.a) Se existir configFile, carrega e valida JSON
            if (configPath != null)
            {
                if (!File.Exists(configPath))
                {
                    ErrorMessage = $"Config file not found at '{configPath}'.";
                    flags = null;
                    return false;
                }

                Config cfg;
                try
                {
                    var json = File.ReadAllText(configPath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    cfg = JsonSerializer.Deserialize<Config>(json, options);
                }
                catch (JsonException ex)
                {
                    ErrorMessage = $"Invalid JSON format in config file: {ex.Message}";
                    flags = null;
                    return false;
                }

                // Valida estrutura obrigatória
                if (cfg == null
                    || string.IsNullOrWhiteSpace(cfg.Url)
                    || cfg.Duration <= 0
                    || cfg.Fps <= 0
                    || cfg.Width <= 0
                    || cfg.Height <= 0
                    || cfg.ChromiumDebugPort <= 0
                    || string.IsNullOrWhiteSpace(cfg.ChromiumArgs))
                {
                    ErrorMessage = "[ERROR] Config file is missing required fields or contains invalid values.";
                    flags = null;
                    return false;
                }

                // Cria Flag única a partir do JSON
                flags = new[]
                {
                    new Flag(
                        cfg.Url,
                        cfg.Duration,
                        cfg.Fps,
                        cfg.Width,
                        cfg.Height,
                        cfg.ChromiumDebugPort,
                        cfg.ChromiumArgs,
                        cfg.SoundTrack
                    )
                };

                Console.WriteLine($"[INFO] Loaded config from '{configPath}': {flags[0]}");
                return true;
            }

            // 2) Sem configFile: processa flags individuais
            string url = null;
            double duration = 0;
            int fps = 0, width = 0, height = 0;
            string soundTrack = null;

            for (int i = 0; i < _args.Length; i++)
            {
                switch (_args[i])
                {
                    case "--url":
                        if (i + 1 >= _args.Length)
                        {
                            ErrorMessage = "Flag --url requires a value.";
                            flags = null;
                            return false;
                        }
                        url = _args[++i];
                        break;

                    case "--duration":
                        if (i + 1 >= _args.Length || !double.TryParse(_args[++i], out duration))
                        {
                            ErrorMessage = "Flag --duration requires a numeric value.";
                            flags = null;
                            return false;
                        }
                        break;

                    case "--fps":
                        if (i + 1 >= _args.Length || !int.TryParse(_args[++i], out fps))
                        {
                            ErrorMessage = "Flag --fps requires an integer value.";
                            flags = null;
                            return false;
                        }
                        break;

                    case "--width":
                        if (i + 1 >= _args.Length || !int.TryParse(_args[++i], out width))
                        {
                            ErrorMessage = "Flag --width requires an integer value.";
                            flags = null;
                            return false;
                        }
                        break;

                    case "--height":
                        if (i + 1 >= _args.Length || !int.TryParse(_args[++i], out height))
                        {
                            ErrorMessage = "Flag --height requires an integer value.";
                            flags = null;
                            return false;
                        }
                        break;

                    case "--chromiumDebugPort":
                        if (i + 1 >= _args.Length || !int.TryParse(_args[++i], out DefaultChromiumDebugPort))
                        {
                            ErrorMessage = "Flag --chromiumDebugPort requires an integer value (optional).";
                            flags = null;
                            return false;
                        }
                        break;

                    case "--chromiumArgs":
                        if (i + 1 >= _args.Length)
                        {
                            ErrorMessage = "Flag --chromiumArgs requires a value (optional).";
                            flags = null;
                            return false;
                        }
                        DefaultChromiumArgs = _args[++i];
                        break;

                    case "--soundtrack":
                        if (i + 1 >= _args.Length)
                        {
                            ErrorMessage = "Flag --soundtrack requires a value (optional).";
                            flags = null;
                            return false;
                        }
                        soundTrack = _args[++i];
                        break;

                    default:
                        ErrorMessage = $"Unknown flag '{_args[i]}'.";
                        flags = null;
                        return false;
                }
            }

            // Validação final das flags obrigatórias
            if (string.IsNullOrWhiteSpace(url)
                || duration <= 0
                || fps <= 0
                || width <= 0
                || height <= 0)
            {
                ErrorMessage = "Usage: KSVideoGenerator --url <url> --duration <seg> --fps <fps> --width <px> --height <px>";
                flags = null;
                return false;
            }

            // Cria Flag única com valores padrão de Chromium
            flags = new[]
            {
                new Flag(
                    url,
                    duration,
                    fps,
                    width,
                    height,
                    DefaultChromiumDebugPort,
                    DefaultChromiumArgs,
                    soundTrack
                )
            };
            Console.WriteLine($"[INFO] Flags received: {flags[0]}");
            return true;
        }

        // Classe interna para desserializar o JSON de configuração
        private class Config
        {
            public string Url { get; set; }
            public double Duration { get; set; }
            public int Fps { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int ChromiumDebugPort { get; set; }
            public string ChromiumArgs { get; set; }
            public string SoundTrack { get; set; }
        }
    }
}