using System.Collections.Concurrent;

namespace PortfolioHelper
{
    internal static class TimeSeriesBL
    {
        internal static double GetLast(string ticker, string interval, string timeseriesDBPath)
        {
            var rows = SQLiteDL.SelectFromTimeseries(ticker, interval, ORDERBY: "time", TOP: "1", timeseriesDBPath);
            return rows.Count > 0 && rows.First().Count > 0 ? double.Parse(rows.Single()["close"]) : 0d;
        }

        internal static Task<List<string>> UpdateTickerIntraday(List<Alarm> tickers, Dictionary<string, string> availableIndicators, string timeseriesDBPath, string alarmsCSVPath)
        {
            var tickersUpdatedAll = Task.Factory.StartNew(() =>
            {
                Thread.CurrentThread.Name = "tickersUpdatedAll";

                var tickersStandardIndicator = tickers.Where(x => x.targetPrice != null).Select(x => x.ticker).Distinct().ToList();
                var tickersStandardUpdated = Task.Factory.StartNew(() =>
                {
                    Thread.CurrentThread.Name = "tickersStandardUpdated";
                    var interval = availableIndicators["default"];
                    var tickersStandardOK = UpdateTickerIntradayProcess(tickersStandardIndicator, interval, timeseriesDBPath, alarmsCSVPath);
                    tickersStandardOK = tickersStandardOK.ConvertAll(x => x + interval);
                    return tickersStandardOK;
                });

                var altUpdatePool = new List<Task<List<string>>>();
                var tickersAltIndicator = tickers.Where(x => x.targetIndicator != null).DistinctBy(x => x.ticker + x.targetIndicator).ToList();
                foreach (var _ in tickersAltIndicator)
                {
                    if (_.targetIndicator == null)
                    {
                        SysUtils.LogErrAndNotify($"ticker \"{_.ticker}\" has a null target indicator, this error should not be possible.");
                        continue;
                    }

                    var nRunning = altUpdatePool.Count(x => !x.IsCompleted);
                    if (nRunning > Environment.ProcessorCount / 2)
                        Task.WaitAny(altUpdatePool.ToArray());

                    var tickerUpdated = Task.Factory.StartNew(() =>
                    {
                        Thread.CurrentThread.Name = $"tickerUpdated -- {_.ticker}";
                        var interval = availableIndicators[_.targetIndicator];
                        var tickersAltOK = UpdateTickerIntradayProcess(new List<string>() { _.ticker }, interval, timeseriesDBPath, alarmsCSVPath);
                        tickersAltOK = tickersAltOK.ConvertAll(x => x + interval);
                        return tickersAltOK;
                    });

                    altUpdatePool.Add(tickerUpdated);
                }

                var tickersUpdated = new List<string>();
                tickersUpdated.AddRange(tickersStandardUpdated.Result);
                altUpdatePool.ForEach(x => tickersUpdated.AddRange(x.Result));

                return tickersUpdated;
            });

            return tickersUpdatedAll;
        }


        private static List<string> UpdateTickerIntradayProcess(List<string> tickers, string interval, string timeseriesDBPath, string alarmsCSVPath, int tries = 0)
        {
            var function = "TIME_SERIES_INTRADAY_EXTENDED";

            var exceptions = new ConcurrentDictionary<string, Exception>();
            var tickersNotUpdatedOnAPILimit = new List<string>();

#if !DEBUG
            var parallelUpdateIntraday = Parallel.ForEach(tickers, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, _ =>
#else
            var parallelUpdateIntraday = Parallel.ForEach(tickers, new ParallelOptions { MaxDegreeOfParallelism = 1 }, _ =>
#endif
            {
                Thread.CurrentThread.Name = $"parallelUpdateIntraday -- {_}";

                try
                {
                    var timeSeries = AlphaVantageAPI.AVCall(function, interval, _);

                    if (timeSeries.Length == 1 && timeSeries.Single().Contains("Thank you for using"))
                    {
#if DEBUG
                        SysUtils.LogErrAndNotify($"DEBUG: API calls rate limit has been hit, suspending API access to cooldown.\nNo notification will occur on access restore.");
#endif
                        if (AlphaVantageAPI.APIAVAILABLE)
                        {
                            AlphaVantageAPI.APIAVAILABLE = false;
                            Task.Factory.StartNew(() =>
                            {
                                Thread.CurrentThread.Name = "MakeAPIAVAILABLE";
                                Thread.Sleep(60_000);
                                AlphaVantageAPI.APIAVAILABLE = true;
                            });
                        }
                        tickersNotUpdatedOnAPILimit.Add(_);
                        return;
                    }
                    else if (timeSeries.Length == 2) /* non-valid tickers have only 2 rows --> "time,open..." -- "" */
                    {
                        FileUtils.AddLinesToCSV(new List<string> { $"-{_}," }, alarmsCSVPath);
                        SysUtils.LogErrAndNotify($"ticker \"{_}\" has no timesSeries data, will be removed from alarms during next cycle.");
                        return;
                    }

                    var isInsertOK = SQLiteDL.InsertMultipleTimeseries(_, interval, timeSeries, dbPath: timeseriesDBPath);
                }
                catch (Exception ex)
                {
                    exceptions.TryAdd(_, ex);
                }
            });

            var tickersOK = tickers.Where(x => !exceptions.Keys.Contains(x) &&
                                               !tickersNotUpdatedOnAPILimit.Contains(x)).ToList();

            return tickersOK;
        }
    }
}
