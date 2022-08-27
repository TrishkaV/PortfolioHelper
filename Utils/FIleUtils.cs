using System.Text;

namespace PortfolioHelper
{
    static internal class FileUtils
    {
        private static readonly string Separator = ",";
        /* a simple string for file lock is sufficient, a more sophisticated approach could 
           use a "fileRequested" dictionary used for Reference Counting, similar to what happens 
           with "IBInfoCSVRC" */
        private static readonly string FILELOCK = "FILELOCK";

        internal static bool CreateNecessaryFiles(string workingDirectory, string[] necessaryFiles)
        {
            lock (FILELOCK)
            {
                var dotEnvPath = Path.Combine(workingDirectory, necessaryFiles.Single(x => x.Contains(".env")));
                if (!File.Exists(dotEnvPath))
                {
                    SysUtils.log.Warn("It is possible to get telegram chat id by asking IDBot (@myidbot) the \"/getid\" command.\n" +
                                      "This will be used to only allow your account to interact with the bot.\nPut this information in the \".env\" file.\n");
                    SysUtils.log.Warn("If you don't have a bot account you can create a new one (to associate with this program) by asking BotFather the \"/newbot\" command.\nPut this information in the \".env\" file.\n");
                    var dotEnvLines = new List<string> { "#EG=1", "NAPICallsPerMinute=1", "#EG=A0AA0AA0000AA0AA", "AlphaVantageAPIKey1=YOUR_KEY_HERE", "#OPTIONAL", "#EG=AaaAaaa", "TelegramBotName=BOT_NAME_HERE", "#OPTIONAL", "#EG=aaa_aaa_0_a", "TelegramBotUsername=BOT_USERNAME_HERE", "#EG=000000000:AAaaAAA0A00aa0aAaa00aA00AaaaA0aA0A00", "TelegramBotToken=BOT_TOKEN_HERE", "EG=000000000", "TelegramBotAcceptedChatID=BOT_CHAT_ID_HERE" };
                    File.Create(dotEnvPath).Dispose();
                    AddLinesToCSV(dotEnvLines, dotEnvPath);
                    SysUtils.log.Warn("\".env\" file didn't exist and was created with a template, please fill it in with your information. Program will now exit.");
                    Environment.Exit(0);
                }
                var dotEnvFile = FileUtils.ParseCSVAsList(dotEnvPath);
                if (dotEnvFile.Any(x => x.Contains("_HERE")))
                {
                    SysUtils.log.Error("\".env\" file is not correctly populated, if those are only the optional parameters please remove them entirely. Program will now abend.");
                    Environment.Exit(1);
                }
                SysUtils.log.Warn("If you get an error related to value formats not being correct at program lauch, it is likely a \".env\" parameter not in the correct format, to restore the default template with examples, delete your existing \".env\" file.");


                foreach (var _ in necessaryFiles)
                    if (!File.Exists(Path.Combine(workingDirectory, _)))
                        File.Create(Path.Combine(workingDirectory, _)).Dispose();

                var baseIndicators = new List<string> { "default,15min" };
                var baseIndicatorsPath = Path.Combine(workingDirectory, necessaryFiles.Single(x => x.Contains("available_indicators")));
                var isBaseIndicatorsPopulated =
                    new StreamReader(baseIndicatorsPath).ReadLine() != null ? true :
                    AddLinesToCSV(baseIndicators, baseIndicatorsPath);

                var blockingErrorsPath = Path.Combine(workingDirectory, necessaryFiles.Single(x => x.Contains("blocking_errors")));
                var isBlockingErrorsPopulated =
                    new StreamReader(blockingErrorsPath).ReadLine() != null ? true :
                    AddLinesToCSV(new List<string>() { "no" }, blockingErrorsPath);

                return isBaseIndicatorsPopulated;
            }
        }

        internal static Dictionary<string, string> ParseCSVAsDic(string indicatorsCSVPath, char separator = ',')
        {
            var result = new Dictionary<string, string>();
            string? line;
            lock (FILELOCK)
                using (var sr = new StreamReader(indicatorsCSVPath))
                    while ((line = sr.ReadLine()) != null)
                    {
                        var lineElements = line.Split(separator);
                        result.Add(lineElements.First(), lineElements.Last());
                    }

            return result;
        }

        internal static bool AddLinesToCSV(List<string> linesToAdd, string filePath)
        {
            lock (FILELOCK)
            {
                var linesAll = File.ReadAllLines(filePath).ToList();
                linesToAdd.ForEach(x => linesAll.Add(x));
                File.WriteAllLines(filePath, linesAll);
            }

            return true;
        }

        internal static bool RemoveLineFromCSV(string lineToRemove, string filePath)
        {
            lock (FILELOCK)
            {
                var linesAll = File.ReadAllLines(filePath).ToList();
                var islineRemoved = linesAll.Remove(lineToRemove);
                if (islineRemoved)
                    File.WriteAllLines(filePath, linesAll);

                return islineRemoved;
            }
        }

        internal static bool SanifyAlarmsCsv(string filePath, Dictionary<string, string> availableIndicators, char separator = ',')
        {
            var createdCSV = new List<List<string>>();
            var malformedInput = new List<string>();

            string? line;
            lock (FILELOCK)
            {
                using (var sr = new StreamReader(filePath))
                    while ((line = sr.ReadLine()) != null)
                        createdCSV.Add(line.Split(separator).Where(x => !string.IsNullOrWhiteSpace(x)).ToList());

                var mergedRows = new Dictionary<string, List<string>>();
                foreach (var elem in createdCSV)
                {
                    if (elem.Count == 0)
                        continue;
                    else if (elem[0].Contains("-"))
                        for (var i = elem.Count; i < 3; i++)
                            elem.Add(string.Empty);
                    else if (elem.Count < 3 || elem.Any(x => string.IsNullOrWhiteSpace(x)))
                    {
                        malformedInput.Add(string.Join(",", elem));
                        continue;
                    }
                    else if (!double.TryParse(elem[1], out _)) /* custom indicator */
                        if (!availableIndicators.ContainsKey(elem[1]))
                        {
                            malformedInput.Add(string.Join(",", elem));
                            SysUtils.LogWarnAndNotify($"Custom indicator \"{elem[1]}\" is not supported. If you think this is an error please check the \"custom indicators\" csv.");
                            continue;
                        }

                    var PKey = elem[0] + elem[1] + elem[2];

                    var cleanRow = elem;
                    if (mergedRows.ContainsKey(PKey))
                    {
                        cleanRow = mergedRows[PKey];

                        for (int i = 0; i < cleanRow.Count; i++)
                            if (string.IsNullOrWhiteSpace(cleanRow[i]))
                                cleanRow[i] = elem[i];
                    }

                    mergedRows[PKey] = cleanRow.ToList();
                }

                var cleanedCSV = new List<string>();
                mergedRows.Values.ToList().ForEach(x => cleanedCSV.Add(string.Join(separator, x).ToLower()));

                File.WriteAllLines(filePath, cleanedCSV);
            }

            if (malformedInput.Count > 0)
                foreach (var _ in malformedInput)
                    SysUtils.LogWarnAndNotify($"Alarm input \"{_}\" is malformed and will not be considered, please check if it has been typed correctly.\n\nUse the \"example\" command if in doubt.");

            return true;
        }

        /* "direction" will be ignored in the calling methods, only the ticker
           and, at most, the target will be considered
         */
        internal static List<Alarm> ParseCSVAlarmsToDisable(string fileName, string? sep = null)
        {
            sep = sep ?? Separator;

            var result = new List<Alarm>();

            string[] lines;
            lock (FILELOCK)
                lines = File.ReadAllLines(fileName, Encoding.UTF8);
            if (lines.Length == 0) return result;

            foreach (var _ in lines)
            {
                var currentRow = _.Split(new[] { Separator }, StringSplitOptions.None);
                if (!currentRow[0].StartsWith("-"))
                    continue;

                var target = !string.IsNullOrWhiteSpace(currentRow[1]) ?
                             currentRow[1] : string.Empty;

                result.Add(new Alarm(ETLUtils.SanifyTicker(currentRow[0]), currentRow[1]));
            }

            return result;
        }

        internal static List<Alarm> ParseCSVAlarm(string fileName, string? sep = null)
        {
            sep = sep ?? Separator;

            var result = new List<Alarm>();

            string[] lines;
            lock (FILELOCK)
                lines = File.ReadAllLines(fileName, Encoding.UTF8);
            if (lines.Length == 0) return result;

            foreach (var _ in lines)
            {
                var currentRow = _.Split(new[] { Separator }, StringSplitOptions.None);

                bool? direction = currentRow[2] switch
                {
                    /* price action crosses indicator down, triggering a buy order */
                    "crossdown" => true,
                    "crossup" => false,
                    _ => null
                };
                if (direction == null)
                {
                    SysUtils.LogErrAndNotify($"\"direction\" parameter has not been provided for alarm CSV line \"{_}\", this is NOT optional and alarm will not be added.\n\nUse the \"example\" command if in doubt.");
                    FileUtils.RemoveLineFromCSV(_, SysUtils.alarmsCSVPath);
                    continue;
                }

                result.Add(new Alarm(ETLUtils.SanifyTicker(currentRow[0]),
                                     currentRow[1], direction.Value,
                                     /* optional, capital to invest when alarm is triggered if buy order, n. shares to sell if sell order*/
                                     currentRow.Length > 3 ? currentRow[3] : null)
                );
                FileUtils.RemoveLineFromCSV(_, SysUtils.alarmsCSVPath);
            }

            return result;
        }

        internal static List<Dictionary<string, string>> ParseCSVAsListOfDic(string fileName, string? sep = null)
        {
            sep = sep ?? Separator;
            var result = new List<Dictionary<string, string>>();

            string[] lines;
            lock (FILELOCK)
                lines = File.ReadAllLines(fileName, Encoding.UTF8);
            if (lines.Length == 0) return result;

            /* Prima riga: intestazione di colonna */
            var header = lines[0].Split(new[] { sep }, StringSplitOptions.None);

            for (var i = 1; i < lines.Length; i++)
            {
                var currentRow = lines[i].Split(new[] { sep }, StringSplitOptions.None);
                if (header.Length != currentRow.Length)
                {
                    SysUtils.LogErrAndNotify($"Row \"{lines[i]}\" has lenght \"{currentRow.Length}\" while header has length \"{header.Length}\", these must be equal so this row will be discarded.");
                    continue;
                }

                var rowDictionary = new Dictionary<string, string>();
                for (var k = 0; k < currentRow.Length; k++)
                    rowDictionary.Add(header[k].ToLowerInvariant(), currentRow[k]);

                result.Add(rowDictionary);
            }

            return result;
        }

        internal static List<string> ParseCSVAsList(string fileName, string? sep = null)
        {
            sep = sep ?? Separator;

            var result = new List<string>();

            var lines = File.ReadAllLines(fileName, Encoding.UTF8);
            if (lines.Length == 0) return result;

            for (var i = 0; i < lines.Length; i++)
                result.Add(lines[i]);

            return result;
        }

        internal static Dictionary<string, List<string>> ParseCSV(string fileName, string? sep = null)
        {
            sep = sep ?? Separator;

            var result = new Dictionary<string, List<string>>();

            var lines = File.ReadAllLines(fileName, Encoding.UTF8);
            if (lines.Length == 0) return result;

            //-- Row 0 == datapoint, Row 1 == date
            for (var i = 2; i < lines.Length; i++)
            {
                var currentRow = lines[i].Split(new[] { Separator }, StringSplitOptions.None);

                var rowDictionary = new List<string>();
                for (var k = 0; k < currentRow.Length; k++)
                    rowDictionary.Add(currentRow[k]);

                result.Add(lines[0].Split(',')[i - 2], rowDictionary);
            }

            return result;
        }
    }
}
