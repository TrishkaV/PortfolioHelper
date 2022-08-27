using NLog;

namespace PortfolioHelper
{
    internal static class SysUtils
    {
        internal static readonly string appName = "PortfolioHelper";
        internal static readonly string logName = $"{appName}.txt";
        internal static readonly string logPath = @$"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/Log";
        internal static readonly Logger log = InitializeEnv.InstantiateLogger(logName, logPath);
        internal static readonly bool isPaperAccount = InitializeEnv.isPaperAccount();
        internal static readonly string workingDirectory = Environment.CurrentDirectory;
        internal static readonly string[] necessaryFiles = new string[] { "tickers_watched_alarm.csv", "available_indicators.csv", ".env", "blocking_errors.csv" };
        internal static readonly string alarmsCSVPath = Path.Combine(workingDirectory, "tickers_watched_alarm.csv");
        internal static readonly string indicatorsCSVPath = Path.Combine(workingDirectory, "available_indicators.csv");
        internal static readonly string portfolioPath = Path.Combine(workingDirectory, "portfolio.csv");
        internal static readonly string openOrdersPath = Path.Combine(workingDirectory, "open_orders.csv");
        internal static readonly string buyingPowerPath = Path.Combine(workingDirectory, "buying_power.csv");
        internal static readonly string blockingErrorsPath = Path.Combine(Environment.CurrentDirectory, "blocking_errors.csv");
        internal static readonly string dbPath = Path.Combine(workingDirectory, "aurora.db");
        internal static readonly string ibApiModuleName = "ib_api";
        internal static readonly Dictionary<string, string> availableIndicators;

        static SysUtils()
        {
            var isNecessaryCSVPresent = FileUtils.CreateNecessaryFiles(SysUtils.workingDirectory, SysUtils.necessaryFiles);
            if (!isNecessaryCSVPresent)
            {
                SysUtils.log.Error($"It was not possible to create the necessary csv at startup. Program will abend.");
                Environment.Exit(1);
            }
            availableIndicators = FileUtils.ParseCSVAsDic(indicatorsCSVPath);
        }

        internal static bool LogErrAndNotify(string err)
        {
            log.Error($"ERROR from \"" + (new System.Diagnostics.StackTrace())?.GetFrame(1)?.GetMethod()?.Name + "()\" --> " +
                      $"{err}");
            TelegramBotAPI.Notify(err);

            return true;
        }

        internal static bool LogPythonErrAndNotify(string moduleName, string args, string err, bool notify = true)
        {
            var errMessage = $"ERROR from \"" + (new System.Diagnostics.StackTrace())?.GetFrame(2)?.GetMethod()?.Name + "()\" --> " +
                             $"It was not possible to execute the Python module \"{moduleName}\" with arguments \"{args}\".\n";
            log.Error($"{errMessage}\nThe following error has occurred:\n{err}");
            if (notify)
                TelegramBotAPI.Notify(errMessage);

            return true;
        }

        internal static bool LogWarnAndNotify(string err)
        {
            log.Warn($"WARN from \"" + (new System.Diagnostics.StackTrace())?.GetFrame(1)?.GetMethod()?.Name + "()\" --> " +
                     $"{err}");
            TelegramBotAPI.Notify(err);

            return true;
        }

        internal static bool LogInfoAndNotify(string err)
        {
            log.Info($"INFO from \"" + (new System.Diagnostics.StackTrace())?.GetFrame(1)?.GetMethod()?.Name + "()\" --> " +
                     $"{err}");
            TelegramBotAPI.Notify(err);

            return true;
        }
    }
}