using System.Data.SQLite;
using System.Globalization;

namespace PortfolioHelper
{
    internal static class SQLiteDL
    {
        private static readonly string csInMemory = "Data Source=:memory:";
        private static readonly string DBLOCK = "DBLOCK";

        internal static bool RefreshPortfolio(List<Dictionary<string, string>> entriesPortfolio, string? dbPath = null)
        {
            var (con, cmd) = OpenConnectionToDB(dbPath);

            var queryInsert = $"REPLACE INTO portfolio(ticker, open_position, average_cost, market_price, unrealized_pnl, active) VALUES ";

            var i = 0;
            foreach (var _ in entriesPortfolio)
            {
                queryInsert += $"(@ticker{i}, @open_position{i}, @average_cost{i}, @market_price{i}, @unrealized_pnl{i}, @active{i}), ";

                cmd.Parameters.AddWithValue($"@ticker{i}", _["ticker"]);
                cmd.Parameters.AddWithValue($"@open_position{i}", _["open_position"]);
                cmd.Parameters.AddWithValue($"@average_cost{i}", _["average_cost"]);
                cmd.Parameters.AddWithValue($"@market_price{i}", _["market_price"]);
                cmd.Parameters.AddWithValue($"@unrealized_pnl{i}", _["unrealized_pnl"]);
                cmd.Parameters.AddWithValue($"@active{i}", true);
                i++;
            }
            queryInsert = queryInsert.Remove(queryInsert.Length - 2);
            queryInsert += ";";
            cmd.CommandText = queryInsert;

            var isCMDOK = RunCMD(cmd);

            var tickersInPortfolio = SelectFromPortfolio(select: new string[] { "ticker" }, dbPath: dbPath).Select(x => x["ticker"]).ToList();
            var tickersClosed = tickersInPortfolio.Where(x => entriesPortfolio.All(y => y["ticker"] != x)).ToList();

            foreach (var _ in tickersClosed)
                UpdatePortfolio(_, "active", "false", dbPath);

            return true;
        }

        internal static bool UpdatePortfolio(string ticker, string fieldName, string fieldValue, string? dbPath = null)
        {
            var (con, cmd) = OpenConnectionToDB(dbPath);

            var queryUpdate = $"UPDATE portfolio SET {fieldName} = @field_value WHERE ticker == @ticker;";
            cmd.CommandText = queryUpdate;
            cmd.Parameters.AddWithValue("@ticker", ticker);
            cmd.Parameters.AddWithValue("@field_value", bool.TryParse(fieldValue, out _) ?
                                                            bool.Parse(fieldValue) : fieldValue);


            var isCMDOK = RunCMD(cmd);
            return isCMDOK;
        }

        internal static List<Dictionary<string, string>> SelectFromPortfolio(string? ticker = null, string[]? select = null, bool? active = null, string? ORDERBY = null, string? TOP = null, string? dbPath = null)
        {
            var (con, cmd) = OpenConnectionToDB(dbPath);
            select ??= new string[] { "*" };

            var querySelect = $"SELECT {string.Join(",", select)} FROM portfolio WHERE 1 == 1;";
            if (ticker != null)
            {
                querySelect = querySelect.Insert(querySelect.Length - 1, $" AND ticker == @ticker");
                cmd.Parameters.AddWithValue("@ticker", ticker);
            }
            if (active != null)
            {
                querySelect = querySelect.Insert(querySelect.Length - 1, $" AND active == @active");
                cmd.Parameters.AddWithValue("@active", active);
            }
            if (ORDERBY != null)
            {
                querySelect = querySelect.Insert(querySelect.Length - 1, $" ORDER BY @ORDERBY DESC");
                cmd.Parameters.AddWithValue("@ORDERBY", ORDERBY);
            }
            if (TOP != null)
            {
                querySelect = querySelect.Insert(querySelect.Length - 1, $" LIMIT @TOP");
                cmd.Parameters.AddWithValue("@TOP", TOP);
            }
            cmd.CommandText = querySelect;

            try
            {
                var rowsFound = ReaderQueryResult(querySelect, cmd);
                return rowsFound;
            }
            catch (Exception ex)
            {
                SysUtils.LogErrAndNotify($"{ex.Message}\n" +
                                         $"ticker: {ticker}\n" +
                                         $"query: {querySelect}\n" +
                                         $"Inner Exception (se presente): {ex.InnerException?.Message}\n" +
                                         $"Errore completo: {ex}");
                return new List<Dictionary<string, string>>();
            }
            finally
            {
                cmd.Dispose();
            }
        }

        internal static bool InsertMultipleAlarms(List<Alarm> tickersAlarm, string? dbPath = null)
        {
            var (con, cmd) = OpenConnectionToDB(dbPath);

            var queryInsert = $"REPLACE INTO alarms(ticker, descriptor, time_updated, active, capital_to_invest) VALUES ";

            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var i = 0;
            foreach (var _ in tickersAlarm)
            {
                queryInsert += $"(@ticker{i}, @descriptor{i}, @time_updated{i}, @active{i}, @capital_to_invest{i}), ";

                var tickerSanified = _.ticker;
                var isDisableRequest = _.ticker.StartsWith("-");

                cmd.Parameters.AddWithValue($"@ticker{i}", tickerSanified);
                cmd.Parameters.AddWithValue($"@descriptor{i}", (_.targetIndicator != null ? _.targetIndicator : _.targetPrice) + ";" + _.direction);
                cmd.Parameters.AddWithValue($"@time_updated{i}", now);
                cmd.Parameters.AddWithValue($"@active{i}", !isDisableRequest ? true : false);
                cmd.Parameters.AddWithValue($"@capital_to_invest{i}", _.capitalToInvest);
                i++;
            }
            queryInsert = queryInsert.Remove(queryInsert.Length - 2);
            queryInsert += ";";
            cmd.CommandText = queryInsert;

            try
            {
                lock (DBLOCK)
                    cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                SysUtils.LogErrAndNotify($"Error in \"" + System.Reflection.MethodBase.GetCurrentMethod()?.Name + "()\" --> " +
                                    $"It was not possible to update \"alarms\" table, the current state of the table will be processed.\n\n" +
                                    $"{ex.Message}\n" +
                                    $"query: {queryInsert}\n" +
                                    $"Inner Exception (se presente): {ex.InnerException?.Message}\n" +
                                    $"Errore completo: {ex}");
                return false;
            }
        }

        internal static bool InsertMultipleTimeseries(string ticker, string interval, string[] rowsRawData, string? dbPath = null)
        {
            var (con, cmd) = OpenConnectionToDB(dbPath);

            var intervalParsed = ParseInterval(interval);
            var queryInsert = $"REPLACE INTO {intervalParsed}_series(ticker, time, open, high, low, close, volume) VALUES ";
            // row 0 is the header, last row is empty
            for (var i = 1; i < rowsRawData.Length - 1; i++)
            {
                queryInsert += $"('{ticker}', @time{i}, @open{i}, @high{i}, @low{i}, @close{i}, @volume{i}), ";

                var rowValues = rowsRawData[i].Split(",");

                cmd.Parameters.AddWithValue($"@time{i}", rowValues[0]);
                cmd.Parameters.AddWithValue($"@open{i}", rowValues[1]);
                cmd.Parameters.AddWithValue($"@high{i}", rowValues[2]);
                cmd.Parameters.AddWithValue($"@low{i}", rowValues[3]);
                cmd.Parameters.AddWithValue($"@close{i}", rowValues[4]);
                cmd.Parameters.AddWithValue($"@volume{i}", rowValues[5]);
            }
            queryInsert = queryInsert.Remove(queryInsert.Length - 2);
            queryInsert += ";";
            cmd.CommandText = queryInsert;

            try
            {
                lock (DBLOCK)
                    cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                SysUtils.LogErrAndNotify($"{ex.Message}\n" +
                                         $"ticker: {ticker}\n" +
                                         $"query: {queryInsert}\n" +
                                         $"Inner Exception (se presente): {ex.InnerException?.Message}\n" +
                                         $"Errore completo: {ex}");
                return false;
            }
        }

        internal static List<Alarm> SelectFromAlarms(string? ticker = null, bool? active = null, string? dbPath = null)
        {
            var (con, cmd) = OpenConnectionToDB(dbPath);

            var querySelect = $"SELECT * FROM alarms WHERE 1==1 ";
            if (active != null)
                querySelect = querySelect.Insert(querySelect.Length - 1, $" AND active == {active}");
            if (ticker != null)
            {
                querySelect = querySelect.Insert(querySelect.Length - 1, $" AND ticker == @ticker");
                cmd.Parameters.AddWithValue("@ticker", ticker);
            }
            /* return random order to not have only the first alarms checked befor an API limit is met */
            querySelect = querySelect.Insert(querySelect.Length - 1, $" ORDER BY RANDOM()");
            querySelect = querySelect.Remove(querySelect.Length - 1);
            querySelect += ";";
            cmd.CommandText = querySelect;

            try
            {
                var rowsFound = ReaderQueryResult(querySelect, cmd);
                var alarmsRetrieved = new List<Alarm>();

                foreach (var _ in rowsFound)
                {
                    var descriptor = _["descriptor"].Split(';');
                    alarmsRetrieved.Add(new Alarm(
                        _["ticker"], descriptor[0], bool.Parse(descriptor[1]),
                        !string.IsNullOrWhiteSpace(_["capital_to_invest"]) ?
                        double.Parse(_["capital_to_invest"]) : null));
                }

                return alarmsRetrieved;
            }
            catch (Exception ex)
            {
                SysUtils.LogErrAndNotify($"Errore in \"" + System.Reflection.MethodBase.GetCurrentMethod()?.Name + "()\" --> " +
                                         $"{ex.Message}\n" +
                                         $"ticker: {ticker}\n" +
                                         $"Inner Exception (se presente): {ex.InnerException?.Message}\n" +
                                         $"Errore completo: {ex}");
                return new List<Alarm>();
            }
            finally
            {
                cmd.Dispose();
            }
        }
        internal static List<Dictionary<string, string>> SelectFromTimeseries(string ticker, string interval, string? ORDERBY = null, string? TOP = null, string? dbPath = null)
        {
            var (con, cmd) = OpenConnectionToDB(dbPath);

            var intervalParsed = ParseInterval(interval);
            var querySelect = $"SELECT * FROM {intervalParsed}_series WHERE ticker == @ticker;";
            if (ORDERBY != null)
                querySelect = querySelect.Insert(querySelect.Length - 1, $" ORDER BY {ORDERBY} DESC");
            if (TOP != null)
                querySelect = querySelect.Insert(querySelect.Length - 1, $" LIMIT {TOP}");
            cmd.CommandText = querySelect;
            cmd.Parameters.AddWithValue("@ticker", ticker);

            try
            {
                var rowsFound = ReaderQueryResult(querySelect, cmd);
                return rowsFound;
            }
            catch (Exception ex)
            {
                SysUtils.LogErrAndNotify($"Errore in \"" + System.Reflection.MethodBase.GetCurrentMethod()?.Name + "()\" --> " +
                                         $"{ex.Message}\n" +
                                         $"ticker: {ticker}\n" +
                                         $"query: {querySelect}\n" +
                                         $"Inner Exception (se presente): {ex.InnerException?.Message}\n" +
                                         $"Errore completo: {ex}");
                return new List<Dictionary<string, string>>();
            }
            finally
            {
                cmd.Dispose();
            }
        }

        private static List<Dictionary<string, string>> ReaderQueryResult(string? querySelect = null, SQLiteCommand? cmd = null, string? dbPath = null)
        {
            if (cmd == null && dbPath != null)
            {
                (_, cmd) = OpenConnectionToDB(dbPath);
                cmd.CommandText = querySelect;
            }
            else if (cmd == null && dbPath == null)
            {
                SysUtils.LogErrAndNotify($"If parameter \"cmd\" is null, then parameter \"dbPath\" cannot be null aswell.");
                return new List<Dictionary<string, string>>();
            }
            else if (querySelect == null && cmd != null && cmd.CommandText == null)
            {
                SysUtils.LogErrAndNotify($"Either parameter \"querySelect\" or \"cmd.CommandText\" must contain a value.");
                return new List<Dictionary<string, string>>();
            }
            else if (cmd == null)
            {
                SysUtils.LogErrAndNotify("This exception should not be possible.");
                return new List<Dictionary<string, string>>();
            }

            var rowsFound = new List<Dictionary<string, string>>();

            try
            {
                lock (DBLOCK)
                {
                    var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        var row = new Dictionary<string, string>();
                        for (var i = 0; i < rdr.FieldCount; i++)
                        {
                            var fieldName = rdr.GetName(i);
                            var fieldValue = (rdr.GetFieldType(i).Name) switch
                            {
                                _ when rdr[i] == DBNull.Value => string.Empty,
                                "String" => rdr.GetString(i),
                                "Int32" => rdr.GetInt32(i).ToString(),
                                "Double" => rdr.GetDouble(i).ToString(),
                                "Boolean" => rdr.GetBoolean(i).ToString().ToLower(),
                                _ => string.Empty
                            };

                            row.Add(fieldName, fieldValue);
                        }

                        rowsFound.Add(row);
                    }
                }
            }
            finally
            {
                cmd.Dispose();
            }

            return rowsFound;
        }

        internal static bool RemoveFromAlarms(string alarmToRemove, string? alarmsDBPath = null)
        {
            var (con, cmd) = OpenConnectionToDB(alarmsDBPath);
            var keysForRemoval = alarmToRemove.Split(',');

            var queryDelete = "DELETE FROM alarms WHERE ticker == @ticker AND descriptor LIKE %@descriptor%;";
            cmd.CommandText = queryDelete;

            cmd.Parameters.AddWithValue("@ticker", keysForRemoval[0]);
            if (string.IsNullOrWhiteSpace(keysForRemoval[1]))
                cmd.CommandText = cmd.CommandText.Replace("AND descriptor LIKE %@descriptor%", string.Empty);
            else
                cmd.Parameters.AddWithValue("@descriptor", $"%{keysForRemoval[1]}%");

            var isCMDOK = RunCMD(cmd);
            return isCMDOK;
        }

        internal static List<Dictionary<string, string>> GetAlarms(bool? active, string? alarmsDBPath, List<string>? fields = null, List<string>? tickers = null, List<string>? descriptors = null)
        {
            var (con, cmd) = OpenConnectionToDB(alarmsDBPath);

            fields = fields ?? new List<string> { "*" };
            var querySelect = $"SELECT {string.Join(",", fields)} FROM alarms WHERE active = {active}";
            if (tickers != null)
            {
                tickers = tickers.ConvertAll(x => ETLUtils.SanifyTicker(x));
                querySelect += " AND ticker IN (@tickers)";
                cmd.Parameters.AddWithValue("@tickers", string.Join(", ", tickers));
            }
            if (descriptors != null)
            {
                querySelect += " AND descriptor IN (@descriptors)";
                cmd.Parameters.AddWithValue("@descriptors", string.Join(", ", descriptors));
            }
            querySelect += ";";
            cmd.CommandText = querySelect;

            try
            {
                var rowsFound = ReaderQueryResult(querySelect, cmd);
                var alarmsActive = new List<Dictionary<string, string>>();
                rowsFound.ForEach(x => alarmsActive.Add(x));

                return alarmsActive;
            }
            catch (Exception ex)
            {
                SysUtils.LogErrAndNotify($"{ex.Message}\n" +
                                         $"Inner Exception (se presente): {ex.InnerException?.Message}\n" +
                                         $"Errore completo: {ex}");
                return new List<Dictionary<string, string>>();
            }
        }

        internal static bool UpdateAlarm(List<Alarm> alarmsToUpdate, string? alarmsDBPath, string fieldName, string fieldValue)
        {
            var (con, cmd) = OpenConnectionToDB(alarmsDBPath);

            var queryUpdate = $"UPDATE alarms SET {fieldName} = @field_value WHERE ticker == @ticker AND descriptor LIKE @descriptor;";
            cmd.CommandText = queryUpdate;
            foreach (var alarm in alarmsToUpdate)
            {
                cmd.Parameters.AddWithValue("@field_value", bool.TryParse(fieldValue, out _) ?
                                                            bool.Parse(fieldValue) : fieldValue);
                cmd.Parameters.AddWithValue("@ticker", alarm.ticker);
                if (alarm.targetPrice != null)
                    cmd.Parameters.AddWithValue("@descriptor", $"%{alarm.targetPrice}%");
                else if (alarm.targetIndicator != null)
                    cmd.Parameters.AddWithValue("@descriptor", $"%{alarm.targetIndicator}%");
                else
                    cmd.CommandText = cmd.CommandText.Replace("AND descriptor LIKE %@descriptor%", string.Empty);

                var isCMDOK = RunCMD(cmd, disposeAfterExec: false);
                cmd.Parameters.Clear();
            }

            return true;
        }

        internal static bool CreateTables(string? dbPath = null)
        {
            var (con, cmd) = OpenConnectionToDB(dbPath);

            lock (DBLOCK)
            {
                /* custom_indicators = "evwma=102.3;evwma_fstd_up=137.1;..." */
                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS minutes_one_series(ticker TEXT NOT NULL, time TEXT, open REAL, high REAL, low REAL, close REAL, volume INT, custom_indicators TEXT, PRIMARY KEY (ticker, time));";
                cmd.ExecuteNonQuery();
                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS minutes_five_series(ticker TEXT NOT NULL, time TEXT, open REAL, high REAL, low REAL, close REAL, volume INT, custom_indicators TEXT, PRIMARY KEY (ticker, time));";
                cmd.ExecuteNonQuery();
                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS minutes_fifteen_series(ticker TEXT NOT NULL, time TEXT, open REAL, high REAL, low REAL, close REAL, volume INT, custom_indicators TEXT, PRIMARY KEY (ticker, time));";
                cmd.ExecuteNonQuery();
                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS minutes_thirty_series(ticker TEXT NOT NULL, time TEXT, open REAL, high REAL, low REAL, close REAL, volume INT, custom_indicators TEXT, PRIMARY KEY (ticker, time));";
                cmd.ExecuteNonQuery();
                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS minutes_sixty_series(ticker TEXT NOT NULL, time TEXT, open REAL, high REAL, low REAL, close REAL, volume INT, custom_indicators TEXT, PRIMARY KEY (ticker, time));";
                cmd.ExecuteNonQuery();

                /* descriptor = "105,down" -- "evwma,down" */
                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS alarms(ticker TEXT NOT NULL, descriptor TEXT NOT NULL, time_updated TEXT NOT NULL, active BOOL NOT NULL, triggered_datetime TEXT, capital_to_invest REAL, PRIMARY KEY (ticker, descriptor));";
                cmd.ExecuteNonQuery();

                /* there can be one position for ticker */
                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS portfolio(ticker TEXT NOT NULL, open_position REAL NOT NULL, average_cost REAL NOT NULL, market_price REAL NOT NULL, unrealized_pnl REAL NOT NULL, active BOOL NOT NULL, PRIMARY KEY (ticker));";
                cmd.ExecuteNonQuery();
            }

            return true;
        }

        private static bool RunCMD(SQLiteCommand cmd, bool disposeAfterExec = true)
        {
            try
            {
                lock (DBLOCK)
                    cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                SysUtils.LogErrAndNotify($"{ex.Message}\n" +
                                         $"Inner Exception (se presente): {ex.InnerException?.Message}\n" +
                                         $"Errore completo: {ex}");
                return false;
            }
            finally
            {
                if (disposeAfterExec)
                    cmd.Dispose();
            }
        }

        private static (SQLiteConnection con, SQLiteCommand cmd) OpenConnectionToDB(string? dbPath = null)
        {
            var cs = dbPath != null ? @$"URI=file:{dbPath}" : csInMemory;
            var con = new SQLiteConnection(cs);
            var cmd = new SQLiteCommand(con);

            con.Open();

            return (con, cmd);
        }

        private static string ParseInterval(string interval)
        {
            var intervalParsed = interval == "1min" ? "minutes_one" :
                                 interval == "5min" ? "minutes_five" :
                                 interval == "15min" ? "minutes_fifteen" :
                                 interval == "30min" ? "minutes_thirty" :
                                 interval == "60min" ? "minutes_sixty" :
                                 string.Empty;

            return intervalParsed;
        }
    }
}
