namespace PortfolioHelper
{
    static internal class AlphaVantageAPI
    {
        private static readonly List<string> APIKeys = GetAVAPIKeys();
        private static int APIKeyIndex = 0;
        internal static bool APIAVAILABLE = true;
        private static readonly int nAPICallsPerMinute = int.Parse(DotEnvUtils.GetEnvVar("NAPICallsPerMinute")) * APIKeys.Count;
        private static readonly TimeSpan APITimeSpan = new TimeSpan(0, 0, 1, 0);
        private static Queue<DateTime> APICallsTime = new Queue<DateTime>();

        /* this method should be called before every API call */
        private static void APIAccessRC()
        {
            TimeSpan timeRemaining;

            lock (APICallsTime)
            {
                if (APICallsTime.Count < nAPICallsPerMinute)
                {
                    APICallsTime.Enqueue(DateTime.Now);
                    return;
                }

                var timePassed = DateTime.Now - APICallsTime.First();
                if (timePassed > APITimeSpan)
                {
                    APICallsTime.Dequeue();
                    APICallsTime.Enqueue(DateTime.Now);
                    return;
                }

                timeRemaining = APITimeSpan - timePassed;
                APICallsTime.Dequeue();
                APICallsTime.Enqueue(DateTime.Now + timeRemaining);
            }

            Thread.Sleep(timeRemaining);
        }


        internal static void APIAccessRCAlternative()
        {
            var timePassed = DateTime.Now - APICallsTime.First();
            if (timePassed > APITimeSpan)
                lock (APICallsTime)
                {
                    APICallsTime.Dequeue();
                    APICallsTime.Enqueue(DateTime.Now);
                }
            else if (APICallsTime.Count >= nAPICallsPerMinute)
            {
                var timeRemaining = APITimeSpan - timePassed;
                lock (APICallsTime)
                {
                    APICallsTime.Dequeue();
                    APICallsTime.Enqueue(DateTime.Now + timeRemaining);
                }
                Thread.Sleep(timeRemaining);
            }
            else
                lock (APICallsTime)
                    APICallsTime.Enqueue(DateTime.Now);
        }


        private static int GetAPIKeyIndex()
        {
            var currentIndex = APIKeyIndex++;
            if (APIKeyIndex == APIKeys.Count)
                APIKeyIndex = 0;
            return currentIndex;
        }

        private static string GetAPIKey()
        {
            lock (APIKeys)
            {
                var index = GetAPIKeyIndex();
                var APIKey = APIKeys[index];
                return APIKey;
            }
        }


        private static List<string> GetAVAPIKeys()
        {
            var AVKeys = new List<string>();
            for (var i = 0; i < byte.MaxValue; i++)
            {
                var key = DotEnvUtils.GetEnvVar($"AlphaVantageAPIKey{i}", isNullOK: true);
                if (key == "NA")
                    break;
                AVKeys.Add(ETLUtils.SanifyAPIKey(key));
            }

            return AVKeys;
        }

        private static List<string> calls = new List<string>();
        internal static string[] AVCall(string function, string interval, string ticker)
        {
            var APIKey = GetAPIKey();
            APIAccessRC();
            calls.Add($"{APIKey} - {APICallsTime.Count}");
            var queryUrl = $"https://www.alphavantage.co/query?function={function}&symbol={ticker}&interval={interval}&apikey={APIKey}&datatype=csv";

            using var client = new HttpClient();
            var result = client.GetStringAsync(queryUrl).Result;

            /* Possible implementation, return a parsed value */
            // if (!function.Contains("EXTENDED"))
            // {
            //     dynamic json_data = JsonSerializer.Deserialize<Dictionary<string, dynamic>>(result);
            //     return json_data;
            // }

            var parsedDF = ParseDataPointsForDB(result, separator: "\r\n");
            return parsedDF;
        }

        static internal Dictionary<string, List<string>> ParseDataPointsForInMemory(string file, string? separator = null)
        {
            separator = separator ?? ",";

            var result = new Dictionary<string, List<string>>();

            var lines = file.Split("\r\n");
            if (lines.Length == 0)
                return result;

            var nElementsInRow = lines[0].Split(new[] { separator }, StringSplitOptions.None).Length;
            for (var m = 0; m < nElementsInRow; m++)
            {
                var rowDictionary = new List<string>();

                /* line 0 is the index */
                for (var i = 1; i < lines.Length; i++)
                {
                    var currentRow = lines[i].Split(new[] { separator }, StringSplitOptions.None);
                    if (currentRow.Length < 2)
                        continue;

                    rowDictionary.Add(currentRow[m]);
                }

                result.Add(lines[0].Split(new[] { separator }, StringSplitOptions.None)[m], rowDictionary);
            }

            return result;
        }

        static internal string[] ParseDataPointsForDB(string file, string? separator = null)
        {
            separator = separator ?? ",";
            var lines = file.Split(separator);
            return lines;
        }
    }
}
