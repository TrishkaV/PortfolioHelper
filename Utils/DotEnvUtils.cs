namespace PortfolioHelper
{
    internal static class DotEnvUtils
    {
        internal static bool Load(string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            foreach (var line in File.ReadAllLines(filePath))
            {
                var parts = line.Split('=', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length != 2)
                    continue;

                Environment.SetEnvironmentVariable(parts[0], parts[1]);
            }

            return true;
        }

        internal static string GetEnvVar(string var, bool isNullOK = false)
        {
            var value = Environment.GetEnvironmentVariable(var);
            if (value == null && !isNullOK)
            {
                SysUtils.LogErrAndNotify($"Environment variable \"{var}\" has a null value, this is not optional and program will abend.");
                Environment.Exit(1);
            }
            return value ?? "NA";
        }
    }
}