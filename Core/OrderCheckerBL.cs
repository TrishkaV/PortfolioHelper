namespace PortfolioHelper
{
    internal static class OrderCheckerBL
    {
        private static List<Dictionary<string, string>>? portfolioCached;

        internal static Task<bool> NotifyOnClosedOrders(CancellationToken ct)
        {
            portfolioCached = GetPF();
            SysUtils.log.Info("Portfolio notifier is running.");

            var ordersNotifierManager = Task.Factory.StartNew(() =>
            {
                Thread.CurrentThread.Name = "ordersNotifierManager";

                while (!ct.IsCancellationRequested)
                {
                    var portfolioUpdated = UpdatePortfolio();

                    var tickersOpened = portfolioUpdated.Where(x => !portfolioCached.Any(y => y["ticker"] == x["ticker"])).ToList();
                    var tickersClosed = portfolioCached.Where(x => !portfolioUpdated.Any(y => y["ticker"] == x["ticker"])).ToList();
                    var tickersChanged = portfolioUpdated.Where(x => portfolioCached.Any(y => y["ticker"] == x["ticker"] &&
                                                                                              double.Parse(y["open_position"]) != double.Parse(x["open_position"]))).ToList();

                    var notifications = new List<string>() { };

                    foreach (var _ in tickersOpened)
                        notifications.Add($"buy,{_["ticker"]},{_["open_position"]},0,{_["average_cost"]},0,0");
                    foreach (var _ in tickersClosed)
                    {
                        var returnPercent = Math.Round(((double.Parse(_["market_price"]) - double.Parse(_["average_cost"])) / double.Parse(_["average_cost"])) * 100, 2);
                        notifications.Add($"sell,{_["ticker"]},0,{_["open_position"]},{_["average_cost"]},{_["market_price"]},{returnPercent}%");
                    }
                    foreach (var _ in tickersChanged)
                    {
                        var tickerCached = portfolioCached.Where(x => x["ticker"] == _["ticker"]).Single();
                        var side = double.Parse(_["open_position"]) > double.Parse(tickerCached["open_position"]) ? "buy" : "sell";
                        var returnPercent = side == "buy" ? 0d : Math.Round(((double.Parse(_["market_price"]) - double.Parse(_["average_cost"])) / double.Parse(_["average_cost"])) * 100, 2);
                        notifications.Add($"{side},{_["ticker"]},{_["open_position"]},{tickerCached["open_position"]},{_["average_cost"]},{_["market_price"]},{returnPercent}%");
                    }

                    portfolioCached = portfolioUpdated;

                    if (notifications.Count == 0)
                    {
#if !DEBUG
                        Thread.Sleep(300_000); /* there is no reason to constantly update this information */
#else
                        Thread.Sleep(10_000);
#endif
                        continue;
                    }

                    var keys = new List<string> { "side", "ticker", "current_quantity", "previous_quantity", "average_cost", "market_price", "return_percent" };
                    var notifAsListOfDic = ETLUtils.MergeKeysAndValuesLists(keys, notifications);

                    foreach (var _ in notifAsListOfDic)
                        TelegramBotAPI.SendNotifPFChange(_, ct);
                }

                return true;
            });

            return ordersNotifierManager;
        }

        private static List<Dictionary<string, string>> GetPF()
        {
            return SQLiteDL.SelectFromPortfolio(active: true, dbPath: SysUtils.dbPath);
        }

        private static List<Dictionary<string, string>> UpdatePortfolio()
        {
            return InteractiveBrokersAPI.GetPortfolioEntries(ticker: string.Empty, "ib_api", SysUtils.portfolioPath, SysUtils.isPaperAccount);
        }
    }
}
