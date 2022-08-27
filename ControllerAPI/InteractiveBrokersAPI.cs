using System.Diagnostics;
using NLog;

namespace PortfolioHelper
{
    static internal class InteractiveBrokersAPI
    {
        private static Dictionary<string, int> IBInfoCSVRC = new Dictionary<string, int>();
        private static string pythonModuleTicket = string.Empty;
        internal static int nBrokerClientIssues = 0;


        internal static bool CancelOpenOrder(string ticker, double price, bool direction, string moduleName, string openOrdersPath, bool isPaperAccount)
        {
            ticker = ETLUtils.SanifyTicker(ticker);

            var entriesOpenOrders = GetOpenOrders(moduleName, openOrdersPath, isPaperAccount);

            var entryOpenOrder = entriesOpenOrders.Where(x => x.Values.Contains(ticker)).ToList();
            var entryDirectional = entryOpenOrder.Where(x => x["direction"] == (direction ? "true" : "false")).ToList();
            var entryPriceMatch = entryDirectional.Count != 0 && price != 0 ?
                                  entryDirectional.Where(x => double.Parse(x["price_level"]) == price).ToList() :
                                  entryDirectional;
            /* you can include a quantity filter if you want */
            // var entryQuantityMatch = entryPriceMatch.Count != 0 ?
            //                       entryPriceMatch.Where(x => (int)double.Parse(x["open_quantity"]) == quantity).ToList() :
            //                       entryPriceMatch;

            switch (entryPriceMatch.Count)
            {
                case 0:
                    SysUtils.LogWarnAndNotify($"There is no open order for ticker \"{ticker}\", at price \"{price}\" as {(direction ? "buy" : "sell")} order, it will be considered closed.");
                    break;
                case 1:
                    SysUtils.LogWarnAndNotify($"There is 1 open orders for ticker \"{ticker}\", at price \"{price}\" as {(direction ? "buy" : "sell")} order, it will be cancelled.");
                    break;
                default:
                    SysUtils.LogWarnAndNotify($"There are {entryPriceMatch.Count} open orders for ticker \"{ticker}\", at {(price == 0 ? "MKT price" : $"price {price}")} as {(direction ? "buy" : "sell")} order, they will all be cancelled.");
                    break;
            }
            SysUtils.LogInfoAndNotify("Consider that a client can only modify orders that it has created itself, if you find your order still pending then you must use the brokers interface.\n\nplease run the \"manage:get_orders\" command to verify.");

            var entriesNotCanceled = new List<string>();
            for (var i = 0; i < entryPriceMatch.Count; i++)
            {
                var args = $"cancel_order {ticker}"
                        + $" {price} {(direction ? "True" : "False")} {(isPaperAccount ? "True" : "False")}";
                try
                {
                    lock (pythonModuleTicket)
                        PythonUtils.PythonCall(moduleName, args);
                    SysUtils.LogInfoAndNotify($"Order cancel command {i + 1}/{entryPriceMatch.Count} has been executed successfully.");
                }
                catch (Exception ex)
                {
                    SysUtils.LogPythonErrAndNotify(moduleName, args, ex.Message);
                    entriesNotCanceled.Add(args);
                }
            }
            if (entriesNotCanceled.Count > 0)
            {
                TelegramBotAPI.Notify("The following order cancel commands could not be processed, please check the broker client.");
                TelegramBotAPI.NotifyMultiple(entriesNotCanceled);
                return false;
            }

            return true;
        }

        //TODO for future release, keep a watcher service running. This function has not been tested
        internal static Task<bool> MonitorIBModule(string args, CancellationToken ct, string moduleName, string modulePath)
        {
            var ibModuleManager = Task.Factory.StartNew(() =>
            {
                Thread.CurrentThread.Name = "ibModuleManager";

                while (!ct.IsCancellationRequested)
                {
                    var isModuleRunning = Process.GetProcessesByName(moduleName).Length > 0;
                    if (!isModuleRunning)
                        try
                        {
                            lock (pythonModuleTicket)
                                PythonUtils.PythonCall(moduleName, args);
                        }
                        catch (Exception ex)
                        {
                            SysUtils.LogPythonErrAndNotify(moduleName, args, ex.Message);
                            return false;
                        }

                    Thread.Sleep(30_000);
                }

                return true;
            }, ct);

            return ibModuleManager;
        }

        internal static bool PlaceOrder(string moduleName, string orderType, string ticker,
                                        int quantity, double price, bool direction, bool isPaperAccount,
                                        string portfolioPath, string openOrdersPath, string buyingPowerPath,
                                        string? dbPath = null)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                SysUtils.LogErrAndNotify($"module name \"{moduleName}\" cannot be empty.");
                return false;
            }
            else if (!new string[] { "MARKET", "LIMIT" }.Contains(orderType))
            {
                SysUtils.LogErrAndNotify($"order type \"{orderType}\" is not valid.");
                return false;
            }
            else if (string.IsNullOrWhiteSpace(ticker))
            {
                SysUtils.LogErrAndNotify($"ticker \"{ticker}\" cannot be empty.");
                return false;
            }

            ticker = ETLUtils.SanifyTicker(ticker);

            if (quantity < 1)
            {
                SysUtils.LogErrAndNotify($"\"{quantity}\" is an invalid quantity, please use a positive number also for \"sell\" orders.");
                return false;
            }
            else if (price < 1 && orderType == "LIMIT")
            {
                SysUtils.LogErrAndNotify($"A LIMIT order price \"{price}\" cannot be lower than \"1\".");
                return false;
            }

            /* buy 0.5% below target and sell 0.5% above target */
            price *= direction ? 0.995 : 1.005;

            /* Allow sells of owned positions only, excluding already open trades
               for the same ticker in the same direction
             */
            if (!direction)
            {
                var entriesPortfolio = GetPortfolioEntries(ticker, moduleName, portfolioPath, isPaperAccount);
                var entryPF = entriesPortfolio.Where(x => x.Values.Contains(ticker)).ToList();
                if (entryPF.Count == 0)
                {
                    Console.WriteLine($"ticker \"{ticker}\" is not held in portfolio and therefore cannot be sold, it is assumed it has already been sold.");
                    return true;
                }
                var openPosition = entryPF.Single().First(x => x.Key == "open_position").Value;
                var openPositionInt = (int)double.Parse(openPosition);

                var entriesOpenOrders = GetOpenOrders(moduleName, openOrdersPath, isPaperAccount);
                var entryOpenOrder = entriesOpenOrders.Where(x => x.Values.Contains(ticker)).ToList();
                var entryDirectional = entryOpenOrder.Where(x => x["direction"] == "false").ToList();

                var pendingQuantity = 0;
                entryDirectional.ForEach(x => pendingQuantity += (int)double.Parse(x["open_quantity"]));

                var quantityAvailable = openPositionInt - pendingQuantity;
                if (quantityAvailable < 0)
                {
                    SysUtils.LogErrAndNotify($"\"pendingQuantity\" ({pendingQuantity}) > \"openPosition\" ({openPosition}), please manually check the brokerage page, the order will not be sent.");
                    return false;
                }
                else if (quantityAvailable == 0)
                {
                    SysUtils.LogWarnAndNotify($"ticker \"{ticker}\" has an open position of \"{(int)double.Parse(openPosition)}\" and a pending sell order of \"{pendingQuantity}\", if you want to process a different sell order please cancel the pending one(s) first, order will not be added.");
                    return true;
                }
                else if (quantity > quantityAvailable)
                {
                    SysUtils.LogWarnAndNotify($"ticker \"{ticker}\" has an open position of \"{openPosition}\" and a pending sell order of \"{pendingQuantity}\", but the quantity selected to sell is \"{quantity}\", quantity will be adjusted to match the open position - the already pending order (thus selling \"{quantityAvailable}\".");
                    quantity = quantityAvailable;
                }
            }
            else /* buy orders are limited by buying power to not go into margin */
            {
                var infoRequested = "buying_power";
                var isGetBuyingPowerOK = GetIBInfo(moduleName, infoRequested, buyingPowerPath, isPaperAccount);
                var buyingPower = double.Parse(FileUtils.ParseCSVAsListOfDic(buyingPowerPath).Single()["buying_power"]);
                lock (IBInfoCSVRC)
                    IBInfoCSVRC[infoRequested]--;

                var totalPrice = quantity * price;
                if (totalPrice > buyingPower)
                {
                    var fixedQuantity = buyingPower / price;
                    SysUtils.LogWarnAndNotify($"order for ticker \"{ticker}\" is a buy of {quantity} shares at price {price} for a total price of {totalPrice} but the account's buying power is of {buyingPower}, an order for {fixedQuantity} shares will be placed instead");
                    quantity = (int)fixedQuantity;
                }
            }

            var args = $"place_order {orderType} {ticker} {quantity} {price} {(direction ? "True" : "False")} {(isPaperAccount ? "True" : "False")}";
            var isAPICallOK = CallPythonAPI(moduleName, args);

            return isAPICallOK;
        }

        private static bool CallPythonAPI(string moduleName, string args, bool notifyErr = true)
        {
            try
            {
                lock (pythonModuleTicket)
                    PythonUtils.PythonCall(moduleName, args);
                return true;
            }
            catch (Exception ex)
            {
                SysUtils.LogPythonErrAndNotify(moduleName, args, ex.Message, notifyErr);
                return false;
            }
        }

        internal static List<Dictionary<string, string>> GetOpenOrders(string moduleName, string openOrdersPath, bool isPaperAccount)
        {
            var infoRequested = "open_orders";
            var isOpenOrdersOK = GetIBInfo(moduleName, infoRequested, openOrdersPath, isPaperAccount);
            if (!isOpenOrdersOK)
            {
                SysUtils.LogErrAndNotify($"Open orders retrieval failed, please check the broker client.");
                return new List<Dictionary<string, string>>();
            }
            var entriesOpenOrders = FileUtils.ParseCSVAsListOfDic(openOrdersPath);
            lock (IBInfoCSVRC)
                IBInfoCSVRC[infoRequested]--;

            return entriesOpenOrders;
        }

        internal static List<Dictionary<string, string>> GetPortfolioEntries(string ticker, string moduleName, string portfolioPath, bool isPaperAccount)
        {
            var entriesPortfolio = new List<Dictionary<string, string>>();

            var infoRequested = "portfolio";
            var isPortfolioOK = GetIBInfo(moduleName, infoRequested, portfolioPath, isPaperAccount);
            if (!isPortfolioOK)
            {
                /* this method is called by the OrderChecker logic which runs perpetually, 
                   it is sufficient to have an increment here for issue counting */
                if (++nBrokerClientIssues == 10)
                    SysUtils.LogErrAndNotify($"IB client might not be running, no more warnings will be issued but service will keep running. Try {nBrokerClientIssues}/10.");
                else if (nBrokerClientIssues < 10)
                    SysUtils.LogErrAndNotify($"IB client might not be running, please run a manual check. Try {nBrokerClientIssues}/10.");
                return entriesPortfolio;
            }

            try
            {
                entriesPortfolio = FileUtils.ParseCSVAsListOfDic(SysUtils.portfolioPath);
            }
            /* portfolio read is likely to run into FileNotFound Ex if broker client is not running */
            catch (Exception ex)
            {
                SysUtils.log.Error($"N broker client try: {nBrokerClientIssues} -- {ex.Message}");
                return entriesPortfolio;
            }
            finally
            {
                lock (IBInfoCSVRC)
                    IBInfoCSVRC[infoRequested]--;
            }

            var isPFRefreshOK = SQLiteDL.RefreshPortfolio(entriesPortfolio, SysUtils.dbPath);

            return entriesPortfolio;
        }

        internal static bool ClearIBInfo(string[] paths)
        {
            foreach (var _ in paths)
                if (File.Exists(_))
                    File.Delete(_);
            return true;
        }

        /* Every calling method must decrement "IBInfoCSVRC[infoRequested]" when 
           done with the csv, it is advised to do this in an appositely done method
         */
        internal static bool GetIBInfo(string moduleName, string infoRequested, string CSVPath, bool isPaperAccount, bool writeToCSV = true)
        {
            lock (IBInfoCSVRC)
                if (IBInfoCSVRC.ContainsKey(infoRequested))
                    IBInfoCSVRC[infoRequested]++;
                else
                    IBInfoCSVRC.Add(infoRequested, 1);

            if (File.Exists(CSVPath))
                return true;

            var infoCall = infoRequested switch
            {
                "portfolio" => "get_portfolio",
                "open_orders" => "get_open_orders",
                "buying_power" => "buying_power",
                _ => null
            };
            if (infoCall == null)
            {
                SysUtils.LogWarnAndNotify($"infoRequested \"{infoRequested}\" is not valid.");
                return false;
            }

            var args = $"{infoCall} {(isPaperAccount ? "True" : "False")} " +
                       (writeToCSV ? "True" : "False");

            /* "portfolio" is routinely checked by OrderChecker service and errors are 
               separately notified */
            var isAPICallOK = CallPythonAPI(moduleName, args, notifyErr: infoCall != "get_portfolio");

            var isIBInfoCleared = Task.Factory.StartNew(() =>
            {
                Thread.CurrentThread.Name = "isIBInfoCleared";

                var timePassed = 0;
                while (IBInfoCSVRC[infoRequested] > 0)
                {
                    Thread.Sleep(5_000);
                    timePassed += 5;

                    if (timePassed > 60)
                    {
                        if (nBrokerClientIssues < 10)
                            SysUtils.LogErrAndNotify($"IB data cleaning should not take this long, blocking resource is \"{infoRequested}\".\nForcing resource release.\n\nIf a connection or client issue happened, you can ignore this message.");
                        IBInfoCSVRC[infoRequested] = 0;
                        ClearIBInfo(new string[] { CSVPath });

                        return;
                    }
                }

                ClearIBInfo(new string[] { CSVPath });
            });

            return isAPICallOK;
        }
    }
}
