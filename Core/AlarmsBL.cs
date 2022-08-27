using System.Collections.Concurrent;
using System.Globalization;
using NLog;

namespace PortfolioHelper
{
    internal static class AlarmsBL
    {
        private static List<string> alarmsTried = new List<string>();

        internal static Task<bool> RunAlarmsCore(CancellationToken ct)
        {
            SysUtils.log.Info("Alarms service is running.");

            var alarmsManager = Task.Factory.StartNew(() =>
            {
                Thread.CurrentThread.Name = "alarmsManager";

                while (!ct.IsCancellationRequested)
                {
                    var tickersAlarm = UpdateAlarms();
                    if (tickersAlarm.Count == 0)
                    {
                        Thread.Sleep(60_000);
                        continue;
                    }

                    /* if the API is not available there is no point running the update procedure */
                    while (!AlphaVantageAPI.APIAVAILABLE)
                        Thread.Sleep(5_000);

                    var tickersUpdated = TimeSeriesBL.UpdateTickerIntraday(tickersAlarm, SysUtils.availableIndicators, SysUtils.dbPath, SysUtils.alarmsCSVPath);
                    var alarmsToManage = CheckAlarms(tickersUpdated.Result, tickersAlarm);

                    var ordersStatus = new Dictionary<string, bool>();
#if !DEBUG
                    var parallelOrderManagement = Parallel.ForEach(alarmsToManage, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, _ =>
#else
                    var parallelOrderManagement = Parallel.ForEach(alarmsToManage, new ParallelOptions { MaxDegreeOfParallelism = 1 }, _ =>
#endif
                    {
                        Thread.CurrentThread.Name = $"parallelOrderManagement -- {_.ticker}";

                        var interval = SysUtils.availableIndicators[_.targetIndicator ?? "default"];
                        var latestDatapoint = SQLiteDL.SelectFromTimeseries(_.ticker, interval, ORDERBY: "time", TOP: "1", SysUtils.dbPath);
                        var last = double.Parse(latestDatapoint.Single()["close"]);
                        var entryPrice = _.targetPrice ?? last;

                        var quantity = _.direction switch
                        {
                            true => /* buy signal */
                                _.capitalToInvest != null ?
                                       Math.Max((int)(_.capitalToInvest / last), 1) : 1,
                            false => /* sell signal */
                                _.capitalToInvest != null ?
                                       /* in sell signals (false) indicates the N of 
                                         shares to sell, as opposed to the capital to allocate */
                                       (int)_.capitalToInvest : 1_000_000
                        };

                        var orderType = _.targetPrice > last * 1.03 || _.targetPrice < last * 0.97 ?
                                        "MARKET" : "LIMIT";

                        var isOrderOK = InteractiveBrokersAPI.PlaceOrder(SysUtils.ibApiModuleName, orderType, _.ticker, quantity, entryPrice, _.direction, SysUtils.isPaperAccount, SysUtils.portfolioPath, SysUtils.openOrdersPath, SysUtils.buyingPowerPath, SysUtils.dbPath);

                        var targetUsed = _.targetPrice?.ToString() ?? _.targetIndicator;
                        var tickerCSVFormat = $"{_.ticker},{targetUsed}";
                        if (!isOrderOK)
                            if (!alarmsTried.Contains(tickerCSVFormat))
                            {
                                SysUtils.LogErrAndNotify($"The following triggered order could NOT be placed and it will be tried to process it at following service executions -->\n\n" +
                                                         $"- ticker: {_.ticker}\n- direction: {(_.direction ? "Buy" : "Sell")}\n- order type: {orderType}\n- entry price: {(orderType == "LIMIT" ? entryPrice : "-")}\n- quantity: {quantity}");
                                alarmsTried.Add(tickerCSVFormat);
                            }
                            else
                            {
                                SysUtils.LogErrAndNotify($"The following triggered order failed two times and will be removed from execution, if you think there is no mistake with the order please run a manual check -->\n\n" +
                                                         $"- ticker: {_.ticker}\n- direction: {(_.direction ? "Buy" : "Sell")}\n- order type: {orderType}\n- entry price: {(orderType == "LIMIT" ? entryPrice : "-")}\n- quantity: {quantity}");
                                isOrderOK = true; /* if an order failed two times it is deactivated anyway. */
                                alarmsTried.Remove(tickerCSVFormat);
                            }

                        ordersStatus.Add(tickerCSVFormat, isOrderOK);
                    });

                    var alarmsToRemove = ordersStatus.Where(x => x.Value).Select(x => x.Key).ToList();
                    if (alarmsToRemove.Count > 0)
                    {
                        var isTriggeredAlarmsRemoved = FileUtils.AddLinesToCSV(new List<string> { $"-{string.Join(",\n-", alarmsToRemove)}" }, SysUtils.alarmsCSVPath);
                        if (!isTriggeredAlarmsRemoved)
                        {
                            SysUtils.LogErrAndNotify($"The following triggered alarms could not be disabled -->\n\n{string.Join(", ", alarmsToRemove)}\n\n" +
                                                     $"The service will abend to avoid triggering duplicates. " +
                                                     $"Please disable the alarms manually and restart the service.");
                            Environment.Exit(1);
                        }
                    }
                }

                return true;
            });

            return alarmsManager;
        }

        internal static List<Alarm> UpdateAlarms()
        {
            var isAlarmsCSVSanified = FileUtils.SanifyAlarmsCsv(SysUtils.alarmsCSVPath, SysUtils.availableIndicators);
            var isAlarmsDisableOK = DisableAlarms();
            var tickersAlarmCSV = FileUtils.ParseCSVAlarm(SysUtils.alarmsCSVPath);

            if (tickersAlarmCSV.Count != 0)
                SQLiteDL.InsertMultipleAlarms(tickersAlarmCSV, SysUtils.dbPath);
            var alarmsEnabled = SQLiteDL.SelectFromAlarms(active: true, dbPath: SysUtils.dbPath);

            return alarmsEnabled;
        }

        private static bool DisableAlarms()
        {
            var alarmsToDisable = FileUtils.ParseCSVAlarmsToDisable(SysUtils.alarmsCSVPath);

            var fieldToUpdate = "active";
            var fieldValue = "false";
            var isAlarmDisabledInDB = SQLiteDL.UpdateAlarm(alarmsToDisable, SysUtils.dbPath, fieldToUpdate, fieldValue);

            var alarmsDisabled = 0;
            foreach (var _ in alarmsToDisable)
            {
                var alarmCSVFormat = $"{_.ticker.ToLower()}," +
                                     $"{(_.targetPrice != null ? _.targetPrice : _.targetIndicator?.ToLower())}";

                var alarmsInvalid = File.ReadLines(SysUtils.alarmsCSVPath).Where(x => x.Contains(alarmCSVFormat)).ToList();
                var alarmClearedFromCSV = new List<bool>();
                alarmsInvalid.ForEach(x =>
                {
                    var isAlarmRemovedFromCSV = FileUtils.RemoveLineFromCSV(x, SysUtils.alarmsCSVPath);
                    alarmClearedFromCSV.Add(isAlarmRemovedFromCSV);
                });
                var isAlarmRemovedFromCSV = alarmClearedFromCSV.All(x => x == true);

                if (isAlarmRemovedFromCSV && isAlarmDisabledInDB)
                    alarmsDisabled++;
            }

            return alarmsToDisable.Count == alarmsDisabled;
        }

        internal static List<Alarm> CheckAlarms(List<string> tickersUpdated, List<Alarm> tickersAlarm)
        {
            var alarms = new List<Alarm>();

            var exceptions = new ConcurrentDictionary<Alarm, Exception>();
#if !DEBUG
            var parallelCheckAlarms = Parallel.ForEach(tickersAlarm, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, _ =>
#else
            var parallelCheckAlarms = Parallel.ForEach(tickersAlarm, new ParallelOptions { MaxDegreeOfParallelism = 1 }, _ =>
#endif
            {
                Thread.CurrentThread.Name = $"parallelCheckAlarms -- {_.ticker}";

                var interval = SysUtils.availableIndicators[_.targetIndicator ?? "default"];

                if (!tickersUpdated.Any(x => x.Contains(_.ticker + interval)))
                {
#if DEBUG
                    if (!AlphaVantageAPI.APIAVAILABLE)
                        SysUtils.LogWarnAndNotify($"DEBUG: Ticker {_.ticker} on interval {interval} should be checked as alarm but is not included amongst the updated tickers due to API limit. " +
                                                   "It will be processed in upcoming cycles. If you keep seeing this error on the same ticker please check the API calls logic.");
                    else
                        SysUtils.LogErrAndNotify($"DEBUG: This is a rare exception when the thread that makes APIAVAILABLE true runs just after the ticker was skipped for its non availability.\n\nTicker: {_.ticker}\nInterval: {interval}.");
#endif
                    return;
                }

                var latestDatapoint = SQLiteDL.SelectFromTimeseries(_.ticker, interval, ORDERBY: "time", TOP: "1", SysUtils.dbPath);
                if (latestDatapoint.Count == 0 || latestDatapoint.Single().Count == 0)
                {
                    exceptions.TryAdd(_, new Exception($"There are no datapoints for ticker \"{_.ticker}\" on the {interval} interval or they are corrupt.\nIf the ticker is correct please check the table, otherwise you can ignore this message."));
                    return;
                }

                if (_.targetPrice != null)
                {
                    if ((!_.direction && double.Parse(latestDatapoint.Single()["high"]) > _.targetPrice) ||
                        (_.direction && double.Parse(latestDatapoint.Single()["low"]) < _.targetPrice))
                    {
                        lock (alarms)
                            alarms.Add(_);
                        return;
                    }

                    return;
                }

                if (_.targetIndicator == null)
                {
                    SysUtils.LogErrAndNotify($"Error in \"" + System.Reflection.MethodBase.GetCurrentMethod()?.Name + "()\" --> " +
                                             $"ticker \"{_.ticker}\" has a null target indicator, this error should not be possible.");
                    return;
                }

                var customIndicators = latestDatapoint.Single()["custom_indicators"].Split(";");
                var trackedIndicator = customIndicators.Where(x => x.Contains(_.targetIndicator)).SingleOrDefault();
                if (string.IsNullOrWhiteSpace(trackedIndicator))
                {
                    exceptions.TryAdd(_, new Exception($"Errore in \"" + System.Reflection.MethodBase.GetCurrentMethod()?.Name + "()\" --> " +
                                                       $"Indicator specified (\"{_.targetIndicator}\") is not tracked for ticker \"{_.ticker}\"."));
                    return;
                }

                var indicatorValue = double.Parse(trackedIndicator.Split("=").Last());
                if ((!_.direction && double.Parse(latestDatapoint.Single()["high"]) > indicatorValue) ||
                    (_.direction && double.Parse(latestDatapoint.Single()["low"]) < indicatorValue))
                {
                    lock (alarms)
                        alarms.Add(_);
                    return;
                }

                return;
            });

            //TODO for future release --> if an indicator custom is not tracked for a ticker
            // add here the logic to track it (add indicator to custom_indicators?)
            if (exceptions.Count > 0)
                foreach (var _ in exceptions)
                    if (_.Value is ArgumentException &&
                        RemoveAlarmLine(_.Key.ticker))
                        exceptions.TryRemove(_);
                    else if (_.Value.Message.Contains("is not tracked for ticker"))
                    {
                        SysUtils.LogWarnAndNotify($"Ticker {_.Key.ticker} was set to track the custom indicator... will be enabled.");
                        //logic here
                        exceptions.TryRemove(_);
                    }
            foreach (var _ in exceptions)
                SysUtils.LogErrAndNotify(_.Value.Message);

            var isUpdateTriggeredDatetimeOK = UpdateTriggeredDatetime(alarms);
            if (!isUpdateTriggeredDatetimeOK)
                SysUtils.LogErrAndNotify($"Some of these tickers with alarms could not have the \"triggered_datetime\" attribute updated in the \"alarms\" table -->\n\n{string.Join(", ", alarms.Select(x => x.ticker))} " +
                                          "Please run a manual update using this notification timestamp.");

            return alarms;
        }

        private static bool UpdateTriggeredDatetime(List<Alarm> alarmsToUpdate)
        {
            var fieldToUpdate = "triggered_datetime";
            var fieldValue = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var isUpdateOK = SQLiteDL.UpdateAlarm(alarmsToUpdate, SysUtils.dbPath, fieldToUpdate, fieldValue);

            return true;
        }

        internal static bool RemoveAlarmLine(string alarmToDelete)
        {
            var alarmInvalid = File.ReadLines(SysUtils.alarmsCSVPath).FirstOrDefault(x => x.Contains(alarmToDelete));
            var isAlarmRemovedFromCSV = !string.IsNullOrWhiteSpace(alarmInvalid) ?
                                        FileUtils.RemoveLineFromCSV(alarmInvalid, SysUtils.alarmsCSVPath) :
                                        true;
            var isAlarmRemovedFromDB = SQLiteDL.RemoveFromAlarms(alarmToDelete, SysUtils.dbPath);

            return isAlarmRemovedFromDB && isAlarmRemovedFromCSV;
        }
    }
}