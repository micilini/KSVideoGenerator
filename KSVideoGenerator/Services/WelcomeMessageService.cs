using System;

namespace KSVideoGenerator.Services
{
    /// <summary>
    /// Exibe no console um banner de boas-vindas com informações sobre o KSVideoGenerator.
    /// </summary>
    internal class WelcomeMessageService
    {
        /// <summary>
        /// Exibe o banner e as descrições estilizadas.
        /// </summary>
        public void ShowWelcome()
        {
            // Banner ASCII multi-linha em literal verbatim para evitar escapes
            var banner = @"
 _  ______   __     ___     _               ____                           _             
| |/ / ___|  \ \   / (_) __| | ___  ___    / ___| ___ _ __   ___ _ __ __ _| |_ ___  _ __ 
| ' /\___ \   \ \ / /| |/ _` |/ _ \/ _ \  | |  _ / _ \ '_ \ / _ \ '__/ _` | __/ _ \| '__|
| . \ ___) |   \ V / | | (_| |  __/ (_) | | |_| |  __/ | | |  __/ | | (_| | || (_) | |   
|_|\_\____/     \_/  |_|\__,_|\___|\___/   \____|\___|_| |_|\___|_|  \__,_|\__\___/|_|   ";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(banner);
            Console.ResetColor();
            Console.WriteLine();

            // Title and Copy
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("KSVideoGenerator (0.0.1)");
            Console.WriteLine("Developed by Micilini");
            Console.ResetColor();
            Console.WriteLine();

            // About CLI
            Console.WriteLine("A command-line tool to create MP4 videos from web animations.");
            Console.WriteLine();

            Console.WriteLine("🚀 Starting up...");
            Console.WriteLine();
        }
    }
}