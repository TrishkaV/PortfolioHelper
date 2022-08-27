using System.Text;
using NLog;
using NLog.Targets;

namespace PortfolioHelper
{
    internal static class InitializeEnv
    {
        internal static bool isPaperAccount()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length == 1)
            {
                SysUtils.log.Error($"No argument was passed, the first argument should be either \"LIVE\" or \"PAPER\". Program will abend.");
                Environment.Exit(1);
            }
            else if (!new string[] { "LIVE", "PAPER" }.Contains(args[1].ToUpper()))
            {
                SysUtils.log.Error($"Argument \"{args[1]}\" is not valid, the first argument should be either \"LIVE\" or \"PAPER\". Program will abend.");
                Environment.Exit(1);
            }

            var isPaperAccount = args[1].ToUpper() == "PAPER" ? true : false;
            if (isPaperAccount)
                SysUtils.log.Info("Using PAPER account.");
            else
                SysUtils.log.Warn("Using LIVE account.");

            return isPaperAccount;
        }

        internal static bool Initialize()
        {
            var isDotEnvLoaded = DotEnvUtils.Load(Path.Combine(SysUtils.workingDirectory, ".env"));
            if (!isDotEnvLoaded)
                SysUtils.log.Error($"It was not possible to load the \".env\" file.");

            var isDBTablesCreated = SQLiteDL.CreateTables(SysUtils.dbPath);
            if (!isDotEnvLoaded)
                SysUtils.log.Error($"It was not possible to create the required DB tables.");

            /* first start clears runtime folder in case of previous abend */
            var isStartupCleanOK = InteractiveBrokersAPI.ClearIBInfo(new string[] { SysUtils.portfolioPath, SysUtils.openOrdersPath, SysUtils.buyingPowerPath });
            if (!isStartupCleanOK)
                SysUtils.log.Error($"It was not possible to clear the runtime folder at startup and a clean performance cannot be guaranteed.");


            return isDotEnvLoaded && isDBTablesCreated && isStartupCleanOK;
        }

        internal static Logger InstantiateLogger(string name, string logPath)
        {
            var basepath = $@"{logPath}";
            var fileName = $@"{basepath}/{name}";
            var config = new NLog.Config.LoggingConfiguration();

            var logfile = new FileTarget("logfile")
            {
                FileName = fileName,
                //Layout = "${longdate}|${event-properties:item=EventId_Id}|${uppercase:${level}}|${logger}|${message} ${exception:format=tostring}",
                Layout = "${longdate} || ${uppercase:${level}} || ${message}      ${exception:format=tostring}",
                ArchiveFileName = basepath + $@"/archives/{name}" + "-log.{#}.txt",
                ArchiveEvery = FileArchivePeriod.Day,
                ArchiveNumbering = ArchiveNumberingMode.Rolling,
                MaxArchiveFiles = 10,
                // ArchiveOldFileOnStartup = true,
                CleanupFileName = true,
                ConcurrentWriteAttemptDelay = 50,
                CreateDirs = true,
                ConcurrentWrites = true,
                Encoding = Encoding.Default,
                LineEnding = LineEndingMode.LF,
                // DeleteOldFileOnStartup = true,
            };
            var logconsole = new ColoredConsoleTarget("logconsole")
            {
                Layout = "${longdate} || ${uppercase:${level}} || ${message}      ${exception:format=tostring}",
                Encoding = Encoding.UTF8,
                AutoFlush = true,
                DetectConsoleAvailable = true,
            };

            config.AddRule(LogLevel.Info, LogLevel.Fatal, logconsole);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, logfile);

            LogManager.Configuration = config;

            var logger = LogManager.GetLogger(name);

            return logger;
        }
    }
}
