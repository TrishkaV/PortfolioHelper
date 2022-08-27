using System.Text.RegularExpressions;

namespace PortfolioHelper
{
    internal static class ETLUtils
    {
        internal static string SanifyTicker(string ticker)
        {
            var tickerSanified = Regex.Replace(ticker.ToUpper(), @"[\d-]", string.Empty);
            return tickerSanified;
        }

        internal static string SanifyIndicator(string indicator)
        {
            /* keep this function in case it is needed to process indicators names
               differently */
            var indicatorSanified = indicator.ToUpper();
            return indicatorSanified;
        }

        internal static string SanifyAPIKey(string key)
        {
            var keySanified = Regex.Replace(key, @"[^0-9a-zA-Z]+", "");
            return keySanified;
        }

        internal static List<Dictionary<string, string>> MergeKeysAndValuesLists(List<string> keys, List<string> values)
        {
            var merged = values.ConvertAll(x => keys.Zip(x.Split(","), (k, v) => new { k, v })
                                                    .ToDictionary(x => x.k, x => x.v));

            return merged;
        }
    }
}