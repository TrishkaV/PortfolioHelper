using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace PortfolioHelper
{
    static internal class TelegramBotAPI
    {
        private static CancellationTokenSource cts = new CancellationTokenSource();
        private static CancellationToken ctLocal = cts.Token;
        private static CancellationToken ctExternal;
        internal static bool isRestartMode = false;
        private static bool isRestartCausedByErr = false;
        private static (DateTime lastOccurrence, byte nErr) errLoopStatus = (DateTime.Now, 0);

        private static readonly string botToken = DotEnvUtils.GetEnvVar("TelegramBotToken");
        private static readonly long botChatId = long.Parse(DotEnvUtils.GetEnvVar("TelegramBotAcceptedChatID"));
        private static TelegramBotClient botClient = new TelegramBotClient(botToken);
        private static readonly ReceiverOptions receiverOptionsAll = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };

        private static readonly string[] acceptedTopics = new string[] { "info", "check", "describe", "example", "alarm", "manage", "order-canc", "request", "restart", "log" };
        private static readonly string[] acceptedManageArgs = new string[] { "get_portfolio", "stop_all", "get_alarms", "get_orders" };


        internal static CancellationToken BotLaunch(CancellationToken ct)
        {
            isRestartMode = false;
            ctExternal = ct;

            botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptionsAll,
                cancellationToken: ctExternal
            );

            if (Environment.GetCommandLineArgs()[1].ToUpper() == "LIVE")
                Notify("Using LIVE account.");
            if (isRestartCausedByErr)
            {
                Notify("One or more critical errors happened and all services were restarted, if these were caused from the telegram bot can be most likely ignored.\n\n");
                var statusErr = FileUtils.ParseCSVAsList(SysUtils.blockingErrorsPath);
                var errToNotify = statusErr.Where(x => !x.StartsWith("no")).ToList().ConvertAll(x => $"error --> {x}.");
                NotifyMultiple(errToNotify);
                isRestartCausedByErr = false;
            }

            return ctLocal;
        }


        internal static bool SendNotifPFChange(Dictionary<string, string> line, CancellationToken ct)
        {
            var investedCapital = Math.Round(double.Parse(line["current_quantity"]) * double.Parse(line["average_cost"]), 2);
            var averageCost = Math.Round(double.Parse(line["average_cost"]), 2);
            var messageBuy = $"A buy operation has been registered for ticker \"{line["ticker"]}\" -->\n" +
                             $"- quantity: {line["current_quantity"]}\n" +
                             $"- average cost: {averageCost}\n" +
                             $"- invested capital: {investedCapital}";

            var messageSell = $"A sell operation has been registered for ticker \"{line["ticker"]}\" -->\n" +
                              $"- current quantity: {line["current_quantity"]}\n" +
                              $"- previous quantity: {line["previous_quantity"]}\n" +
                              $"- average cost: {averageCost}\n" +
                              $"- estimated sell price: {line["market_price"]}\n" +
                              $"- estimated return: {line["return_percent"]}";

            var messageToSend = line["side"] == "buy" ? messageBuy : messageSell;

            var isNotifyOK = Notify(messageToSend);

            return isNotifyOK;
        }

#pragma warning disable CS1998
        // This method does not await anything since is not currently needed but it must be async to
        // match the library interface
        internal static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
        {
            if (update.Message is not { } message)
            {
                Notify("Message cannot be empty.");
                return;
            }
            if (message.Chat.Id != botChatId) /* only allow the intended user to interact with the bot */
            {
                //TODO for future release, log and warn via email
                return;
            }
            if (message.Text is not { } messageText)
            {
                Notify("Message must only contain text.");
                return;
            }

            messageText = messageText.ToLower().Replace(" ", string.Empty);

            var messageParsed = ParseMessage(messageText, ct);
            if (!messageParsed.isValid)
                return;
            else if (messageParsed.command == null || messageParsed.command.Any(x => string.IsNullOrWhiteSpace(x)))
            {
                SysUtils.LogErrAndNotify($"This error should not happen.");
                return;
            }

            bool? isHandleManageOK = messageParsed.command[0] switch
            {
                "alarm" => HandleAlarm(messageParsed.command[1]),
                "manage" => HandleManage(messageParsed.command[1], SysUtils.isPaperAccount),
                "order-canc" => HandleOrderCanc(messageParsed.command[1]),
                "request" => HandleRequest(messageParsed.command[1]),
                "log" => HandleLog(messageParsed.command[1]),
                _ => null
            };
            if (isHandleManageOK == null)
            {
                SysUtils.LogErrAndNotify($"Topic passed: \"{messageParsed.command[0]}\" is not valid, did you mean one of these?\n\n" +
                                         $"- {string.Join(",\n- ", acceptedTopics)}");
                return;
            }
        }
#pragma warning restore CS1998

        private static bool HandleRequest(string command)
        {
            Notify("TODO for future release.");
            return true;
        }

        private static (bool isValid, List<string>? command) ParseMessage(string input, CancellationToken ct)
        {
            if (!acceptedTopics.Any(x => input.StartsWith(x)))
            {
                Notify($"Input \"{input}\" is not valid, accepted topics are:\n- " +
                       $"{string.Join(",\n- ", acceptedTopics)}");
                return (false, null);
            }

            if (!input.Contains(":"))
            {
                switch (input)
                {
                    case "check":
                        var statusErr = FileUtils.ParseCSVAsList(SysUtils.blockingErrorsPath);
                        switch (statusErr.Count)
                        {
                            case 0:
                                SysUtils.LogErrAndNotify("\"blocking errors\" log is damaged, all services will be safely restarted to resolve the issue.");
                                RestartAllServices();
                                break;
                            case 1 when statusErr[0] == "no":
                                Notify($"Status --> {(isRestartMode ? "\"Restarting\", might stop responding at any moment..." : "\"Listening\", no errors has occurred.")}");
                                break;
                            default:
                                SysUtils.LogErrAndNotify($"The following errors have occurred:");
                                var errToNotify = statusErr.Where(x => !x.StartsWith("no")).ToList().ConvertAll(x => $"error --> {x}.");
                                NotifyMultiple(errToNotify, log: true);
                                Notify("Please consider resarting the application.\n\nListening...");
                                break;
                        }
                        break;
                    case "info":
                        Notify("Info -->\n\n" +
                        "- \"check\": check bot status.\n\n" +
                        "- \"describe\": get the description of a particular command.\n\n" +
                        "- \"example\": show examples of input commands.\n\n" +
                        "- \"alarm\": insert alarm, it will be executed.\n\n" +
                        "- \"order-canc\": remove specific open order.\n\n" +
                        "- \"manage\": misc management options.\n\n" +
                        "- \"request\": request details about a financial instrument.\n\n" +
                        "- \"log\": show last X messages of current log.\n\n" +
                        "- \"restart\": restart all services.");
                        break;
                    case "example":
                        Notify("Example commands -->\n\n" +
                        "- \"example\": \"example\" (you are here).\n\n" +
                        "- \"info\": \"info\".\n\n" +
                        "- \"check\": \"check\".\n\n" +
                        "- \"describe\": \"describe:alarm\".\n\n" +
                        "- \"alarm\":\n\"sell 500 MSFT at price 100\"---\"alarm:msft,100,crossup,500\".\n\"remove all MSFT alarms\"---\"-msft\".\n\"remove MSFT alarm at price 50\"---\"-msft,50\"\n\n" +
                        "- \"order-canc\": \"order-canc:msft,100,sell\" (use \"0\" as price parameter to cancel a MKT order).\n\n" +
                        "- \"manage\": \"manage:get_portfolio\",\n\"manage:get_orders\",\n\"manage:stop_all\",\n\"manage:get_alarms\".\n\n" +
                        "- \"request\": \"TODO for future release\"\n\n" +
                        "- \"log\": \"log:10\".\n\n" +
                        "- \"restart\": \"restart\".");
                        break;
                    case "restart":
                        Notify("\"restart\" command received successfully, all services will restart in upcoming minutes.\nTelegram bot will be shut down last and is usable until then.");
                        RestartAllServices();
                        break;
                    default:
                        Notify("You probably passed a topic that requires an argument, please use the format \"topic:argument\".\n" +
                               "If you're in doubt use the \"info\" or \"example\" commands.");
                        break;
                }
                return (false, null);
            }

            if (input.Split(":").Where(x => !string.IsNullOrWhiteSpace(x)).ToList() is not { Count: 2 } inputCommand)
            {
                Notify($"Input \"{input}\" is not valid, please use the format \"topic:argument\".\n" +
                        "If you're in doubt use the \"info\" or \"example\" commands.");
                return (false, null);
            }

            if (inputCommand[0] == "describe")
            {
                switch (inputCommand[1])
                {
                    case "info":
                        Notify("Info -->\n\n" +
                               "No parameters, get a list of available commands.");
                        break;
                    case "example":
                        Notify("Example -->\n\n" +
                               "No parameters, get an example of the correct syntax for every command.");
                        break;
                    case "manage":
                        Notify("Manage -->\n\n" +
                               "---------------\n" +
                               "manage:command\n" +
                               "manage:get_portfolio\n" +
                               "---------------\n\n\n" +
                               "\"command\":\nissue services management command.\n");
                        break;
                    case "request":
                        Notify("Request -->\n\n" +
                               "TODO for future release");
                        break;
                    case "describe":
                        Notify("Describe -->\n\n" +
                               "---------------\n" +
                               "describe:item\n" +
                               "describe:order\n" +
                               "---------------\n\n\n" +
                               "\"item\":\nitem to describe.\n");
                        break;
                    case "alarm":
                        Notify("Alarm -->\n\n" +
                               "---------------\n" +
                               "alarm:ticker,target,direction,[capital]\n" +
                               "alarm:msft,100,crossup,500\n" +
                               "---------------\n\n\n" +
                               "\"ticker\",\n\n" +
                               "\"target\" :\n\"target price OR indicator\",\n\n" +
                               "\"direction\" :\n\"crossup\" (sell when price is above the target)\nOR\n\"crossdown\" (buy when price is below the target),\n\n" +
                               "\"capital\" [optional] :\n\"capital to invest\" (capital to invest in buy operation, if not specified will buy 1 share)\nOR\n\"n. shares to sell\" (n. of shares to sell, if not specified will sell all).");
                        break;
                    case "order-canc":
                        Notify("Order cancel -->\n\n" +
                               "---------------\n" +
                               "order-canc:ticker,target,direction\n" +
                               "order-canc:msft,100,sell\n" +
                               "---------------\n\n\n" +
                               "\"ticker\",\n\n" +
                               "\"target\" :\n\"target price OR indicator\",\n\n" +
                               "\"direction\" :\n\"crossup\" (sell when price is above the target)\nOR\n\"crossdown\" (buy when price is below the target).");
                        break;
                    case "log":
                        Notify("Log -->\n\n" +
                               "---------------\n" +
                               "log:10\n" +
                               "---------------\n" +
                               "n or rows of the most recent log to get, from the most recent one.\n");
                        break;
                    case "check":
                        Notify("Check -->\n\n" +
                               "check status of all services.\n");
                        break;
                    case "restart":
                        Notify("Restart -->\n\n" +
                               "restart all services.\n");
                        break;
                    default:
                        Notify($"Requested argument \"{inputCommand[1]}\" is not descriptable, accepted arguments are:\n- " +
                               $"{string.Join(",\n- ", acceptedTopics)}");
                        break;
                }

                return (false, null);
            }

            return (true, inputCommand);
        }

        private static bool RestartAllServices()
        {
            isRestartMode = true;
            cts.Cancel();
            return true;
        }

        private static bool HandleLog(string message)
        {
            var nLogMessages = int.Parse(message);
            var log = FileUtils.ParseCSVAsList(Path.Combine(SysUtils.logPath, SysUtils.logName), "\n");
            if (log.Count > nLogMessages)
            {
                var nToSkip = log.Count - nLogMessages;
                log = log.Skip(nToSkip).Take(nLogMessages).ToList();
            }
            NotifyMultiple(log);
            return true;
        }

        private static bool HandleManage(string command, bool isPaperAccount)
        {
            switch (command)
            {
                case "get_portfolio":
                    var entriesPortfolio = InteractiveBrokersAPI.GetPortfolioEntries(ticker: string.Empty, SysUtils.ibApiModuleName, SysUtils.portfolioPath, SysUtils.isPaperAccount);
                    if (entriesPortfolio.Count == 0)
                    {
                        Notify("Portfolio has no entries, if this is incorrect please check the broker application.");
                        break;
                    }

                    Notify("Portfolio entries -->\n\n");
                    entriesPortfolio.ForEach(x =>
                    {
                        Notify($"ticker: {x["ticker"]}\n" +
                               $"open position: {Math.Round(double.Parse(x["open_position"]), 2)}\n" +
                               $"average cost: {Math.Round(double.Parse(x["average_cost"]), 2)}\n" +
                               $"market price: {Math.Round(double.Parse(x["market_price"]), 2)}\n" +
                               $"unrealized pnl: {Math.Round(double.Parse(x["unrealized_pnl"]), 2)}");
                    });
                    break;

                case "stop_all": /* "stop_all" is intentionally behind "manage", unlike "restart", since it requires a manual boot after it is issued.*/
                    Notify("Termination command sent successfully and all services will be terminated, " +
                           "telegram bot will be shut down last and is usable until then.");
                    SysUtils.log.Warn("Telegram bot has received a termination command.");
                    cts.Cancel();
                    break;

                case "get_alarms":
                    var alarmsActive = SQLiteDL.GetAlarms(active: true, SysUtils.dbPath, new List<string> { "ticker", "descriptor", "capital_to_invest" });
                    Notify("Alarms active -->\n\n");
                    var alarmsToNotify = new List<string>();
                    alarmsActive.ForEach(x =>
                    {
                        var descriptor = x["descriptor"].Split(";");
                        var direction = bool.Parse(descriptor[1]);
                        var quantity = !string.IsNullOrWhiteSpace(x["capital_to_invest"]) ? x["capital_to_invest"] : direction ? "Not defined, 1 share will be bought if possible" : "ALL";
                        alarmsToNotify.Add($"ticker: {x["ticker"]}\n" +
                                           $"target: {descriptor[0]}\n" +
                                           $"direction: {(direction ? "crossdown" : "crossup")}\n" +
                                           $"{(direction ? "capital to invest" : "quantity to sell")}: {quantity}");
                    });
                    NotifyMultiple(alarmsToNotify);
                    break;

                case "get_orders":
                    var orders = InteractiveBrokersAPI.GetOpenOrders(SysUtils.ibApiModuleName, SysUtils.openOrdersPath, SysUtils.isPaperAccount);
                    Notify("Orders active -->\n\n");
                    var ordersToNotify = new List<string>();
                    orders.ForEach(x =>
                    {
                        var priceLevel = double.Parse(x["price_level"]);
                        ordersToNotify.Add($"ticker: {x["ticker"]}\n" +
                                           $"open quantity: {(int)double.Parse(x["open_quantity"])}\n" +
                                           $"price level: {(priceLevel != 0d ? priceLevel : "MKT")}\n" +
                                           $"direction: {(bool.Parse(x["direction"]) ? "buy at price level" : "sell at price level")}\n");
                    });
                    NotifyMultiple(ordersToNotify);
                    break;

                default:
                    Notify($"Argument \"{command}\" is not valid, accepted arguments for topic \"manage\" are:\n- " +
                       $"{string.Join(",\n- ", acceptedManageArgs)}");
                    return false;
            }

            return true;
        }

        private static bool HandleAlarm(string alarm)
        {
            var isAlarmWriteOK = FileUtils.AddLinesToCSV(new List<string>() { alarm }, SysUtils.alarmsCSVPath);
            if (!isAlarmWriteOK)
                SysUtils.LogErrAndNotify($"Alarm write was NOT successful, please run a manual check.\n\nAlarm -->\n{alarm}");
            else
                Notify("Alarm will be added at the nearest alarms iteration, use the \"manage:get_alarms\" command to check running alarms.");
            return isAlarmWriteOK;
        }

        private static bool HandleOrderCanc(string order)
        {
            var input = order.Split(",");
            if (input.Length != 3)
            {
                SysUtils.LogErrAndNotify($"\"order-canc\" argument {order} is not valid, please use the \"example\" command for the accepted syntax.");
                return false;
            }
            var ticker = input[0].ToUpper();
            var price = double.TryParse(input[1], out _) ? double.Parse(input[1]) : -1;
            if (price == -1)
            {
                SysUtils.LogErrAndNotify($"\"order-canc\" argument {order} contains an invalid element as price, please use the \"example\" command for the accepted syntax.");
                return false;
            }
            bool? direction = input[2] == "sell" ? false : input[2] == "buy" ? true : null;
            if (direction == null)
            {
                SysUtils.LogErrAndNotify($"\"order-canc\" argument {order} contains an invalid element as direction, please use the \"example\" command for the accepted syntax.");
                return false;
            }
            var isOrderCancelOK = InteractiveBrokersAPI.CancelOpenOrder(ticker, price, direction.Value, SysUtils.ibApiModuleName, SysUtils.openOrdersPath, SysUtils.isPaperAccount);
            return true;
        }

        internal static bool Notify(string message)
        {
            var notification = botClient.SendTextMessageAsync(
                chatId: botChatId,
                text: message,
                cancellationToken: ctExternal);

            var result = !notification.IsFaulted;
            if (!result)
            {
                SysUtils.log.Error($"MISSING notification -->\n\n{message}.\n\nWill be added to the blocking errors csv in \"{SysUtils.blockingErrorsPath}\".");
                FileUtils.AddLinesToCSV(new List<string>() { $"no,missed notification --> {message}" }, SysUtils.blockingErrorsPath);
                Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(10_000); /* allow some time to collect all errors to the csv */
                    RestartAllServices();
                });
            }

            return result;
        }

        internal static bool NotifyMultiple(List<string> messages, bool log = false, string logLevel = "Error")
        {
            messages = messages.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

            byte sleeper = 0;
            foreach (var x in messages)
            {
                if (sleeper++ >= 50) /* telegram has burst limits for messages sent */
                {
                    Thread.Sleep(3_500);
                    sleeper = 0;
                }
                switch (log)
                {
                    case false:
                        Notify($"{x}");
                        break;
                    case true when logLevel == "Error":
                        SysUtils.LogErrAndNotify($"{x}");
                        break;
                    case true when logLevel == "Warn":
                        SysUtils.LogWarnAndNotify($"{x}");
                        break;
                    case true when logLevel == "Info":
                        SysUtils.LogInfoAndNotify($"{x}");
                        break;
                    default:
                        SysUtils.LogErrAndNotify($"This exception should never happen.");
                        return false;
                }
            };

            return true;
        }

        internal static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.InnerException?.Message ?? exception.ToString()
            };

            var errTimeThreshold = DateTime.Now - new TimeSpan(0, 1, 0); /* 1 minute tolerance */
            if (errLoopStatus.lastOccurrence < errTimeThreshold)
            {
                errLoopStatus.nErr = 0;
                errLoopStatus.lastOccurrence = DateTime.Now;
            }
            else if (errLoopStatus.nErr++ > 3)
            {
                var message = $"{errLoopStatus.nErr} telegram bot errors has occurred within {(DateTime.Now - errLoopStatus.lastOccurrence).Seconds} seconds, to avoid an error loop all services will be terminated, please run a manual check.";
                SysUtils.log.Error(message);
                cts.Cancel();
                Notify(message);
            }

            SysUtils.LogErrAndNotify("An error related to the telegram bot has occurred.\n\nError -->\n\n" + ErrorMessage);

            return Task.CompletedTask;
        }
    }
}
