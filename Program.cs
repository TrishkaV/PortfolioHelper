using PortfolioHelper;
do
{
    //TODO for future release, have the program sleep (or terminate non necessary processes) 
    //outside of market hours

    var isInitializationOK = InitializeEnv.Initialize();
    if (!isInitializationOK)
    {
        SysUtils.log.Error("Could not initialize environment, services will not be started, application will abend.");
        Environment.Exit(1);
    }
    SysUtils.log.Info($"App {SysUtils.appName} started.");

    var tsMain = new CancellationTokenSource();
    var ctMain = tsMain.Token;
    var tsAlarms = new CancellationTokenSource();
    var ctAlarms = tsAlarms.Token;
    var tsTelegramBot = new CancellationTokenSource();
    var ctTelegramBot = tsTelegramBot.Token;
    var tsOrdersNotifier = new CancellationTokenSource();
    var ctOrdersNotifier = tsOrdersNotifier.Token;

    SysUtils.log.Info("Environment initialized.");

    // sample commands to place or cancel orders
    // InteractiveBrokersAPI.PlaceOrder(ibApiModule, "LIMIT", "META", 50, 130, true,
    //                                 isPaperAccount, SysUtils.portfolioPath, openOrdersPath, buyingPowerPath, dbPath);
    // InteractiveBrokersAPI.CancelOpenOrder("META", 0, false, ibApiModule, openOrdersPath, isPaperAccount);

    var telegramBotToken = TelegramBotAPI.BotLaunch(ctTelegramBot);
    var isAlarmManagerOK = AlarmsBL.RunAlarmsCore(ctAlarms);
    var isOrderCheckerOK = OrderCheckerBL.NotifyOnClosedOrders(ctOrdersNotifier);
    SysUtils.LogInfoAndNotify("All services launched.\nIf you're in doubt, try asking \"info\" to get started.");

    while (!ctMain.IsCancellationRequested)
    {
#if !DEBUG
        Thread.Sleep(300_000); /* this service is designed to keep running, there is no reason to check constantly */
#else
        Thread.Sleep(10_000);
#endif
        if (telegramBotToken.IsCancellationRequested)
        {
            SysUtils.log.Warn($"Processing cancellation request from telegram bot, terminating running services...");

            tsAlarms.Cancel();
            tsOrdersNotifier.Cancel();
            if (isAlarmManagerOK.Result && isOrderCheckerOK.Result)
            {
                SysUtils.LogWarnAndNotify($"All services stopped, {(TelegramBotAPI.isRestartMode ? "restarting" : "terminating")} now.");
                tsTelegramBot.Cancel();
                tsMain.Cancel();
            }
        }
    }
}
while (TelegramBotAPI.isRestartMode);

Environment.Exit(0);
