using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Fclp;
using Microsoft.Win32;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace AmcacheParser
{
    internal class Program
    {
        private static Logger _logger;
        private static Stopwatch _sw;

        private static bool CheckForDotnet46()
        {
            using (
                var ndpKey =
                    RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                        .OpenSubKey("SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full\\"))
            {
                var releaseKey = Convert.ToInt32(ndpKey.GetValue("Release"));

                return (releaseKey >= 393295);
            }
        }

        private static void Main(string[] args)
        {
            SetupNLog();

            _logger = LogManager.GetCurrentClassLogger();

            if (!CheckForDotnet46())
            {
                _logger.Warn(".net 4.6 not detected. Please install .net 4.6 and try again.");
                return;
            }

            var p = new FluentCommandLineParser<ApplicationArguments>
            {
                IsCaseSensitive = false
            };

            p.Setup(arg => arg.File)
                .As('f')
                .WithDescription("Amcache.hve file to parse. This is required").Required();

//            p.Setup(arg => arg.Extension)
//                .As('e')
//                .WithDescription("File extension to include. Default is all extensions. dll would include only files ending in .dll, exe would include only .exe files");

            p.Setup(arg => arg.IncludeLinked)
                .As('i').SetDefault(true)
                .WithDescription("Include file entries for Programs entries");

            p.Setup(arg => arg.Whitelist)
                .As('w')
                .WithDescription("Path to file containing SHA-1 hashes to exclude from the results");

            var header =
                $"AmcacheParser version {Assembly.GetExecutingAssembly().GetName().Version}" +
                "\r\n\r\nAuthor: Eric Zimmerman (saericzimmerman@gmail.com)" +
                "\r\nhttps://github.com/EricZimmerman/AmcacheParser";

            var footer = @"Examples: AmcacheParser.exe -f ""C:\Temp\UsrClass 1.dat"" --ls URL" + "\r\n\t " +
                         @" AmcacheParser.exe -f ""C:\Temp\someFile.txt"" --lr guid" + "\r\n\t " +
                         @" AmcacheParser.exe -f ""C:\Temp\someOtherFile.txt"" --lr cc -sa" + "\r\n\t " +
                         @" AmcacheParser.exe -f ""C:\Temp\someOtherFile.txt"" --lr cc -sa -m 15 -x 22" + "\r\n\t " +
                         @" AmcacheParser.exe -f ""C:\Temp\UsrClass 1.dat"" --ls mui -sl" + "\r\n\t ";

            p.SetupHelp("?", "help").WithHeader(header).Callback(text => _logger.Info(text + "\r\n" + footer));

            var result = p.Parse(args);

            if (result.HelpCalled)
            {
                return;
            }

            if (result.HasErrors)
            {
                _logger.Error("");
                _logger.Error(result.ErrorText);

                p.HelpOption.ShowHelp(p.Options);

                return;
            }

            if (!File.Exists(p.Object.File))
            {
                _logger.Warn($"'{p.Object.File}' not found. Exiting");
                return;
            }

            _logger.Info(header);
            _logger.Info("");

            _sw = new Stopwatch();
            _sw.Start();

            try
            {
                _sw.Start();

                var am = new Amcache.Amcache(p.Object.File);

                _sw.Stop();

                var suffix = am.UnassociatedFileEntries.Count == 1 ? "y" : "ies";

                _logger.Info("");
                _logger.Info(
                    $"Found {am.UnassociatedFileEntries.Count:N0} unassociated file entr{suffix} and {am.ProgramsEntries.Count:N0} program entries in {_sw.Elapsed.TotalSeconds:N3} seconds.");
            }
            catch (Exception ex)
            {
                _logger.Error($"There was an error: {ex.Message}");
            }


#if DEBUG
            _logger.Warn("Press a key to exit");
            Console.ReadKey();

#endif
        }

        private static void SetupNLog()
        {
            var config = new LoggingConfiguration();
            var loglevel = LogLevel.Info;

            var layout = @"${message}";

            var consoleTarget = new ColoredConsoleTarget();

            config.AddTarget("console", consoleTarget);

            consoleTarget.Layout = layout;

            var rule1 = new LoggingRule("*", loglevel, consoleTarget);
            config.LoggingRules.Add(rule1);

            LogManager.Configuration = config;
        }
    }

    internal class ApplicationArguments
    {
        public string File { get; set; }
        //       public string Extension { get; set; } = string.Empty;
        public string Whitelist { get; set; } = string.Empty;
        public bool IncludeLinked { get; set; } = true;
    }
}