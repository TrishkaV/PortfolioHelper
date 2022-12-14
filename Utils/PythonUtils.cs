using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PortfolioHelper
{
    internal static class PythonUtils
    {
        internal static string PythonCall(string module, string? args = null)
        {
            var workingDirectory = Environment.CurrentDirectory;
            var start = new ProcessStartInfo();
            start.FileName = @"python3";
            //argument with file name and input parameters
            start.Arguments = $"{Path.Combine(workingDirectory + "/resources", $"{module}.py")} {args ?? string.Empty}";
            start.UseShellExecute = false;// Do not use OS shell
            start.CreateNoWindow = true; // We don't need new window
            start.RedirectStandardOutput = true;// Any output, generated by application will be redirected back
            start.RedirectStandardError = true; // Any error in standard output will be redirected back (for example exceptions)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                start.LoadUserProfile = true; // only used on Windows

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            using (Process process = Process.Start(start))
            using (StreamReader reader = process.StandardOutput)
            {
                string stderr = process.StandardError.ReadToEnd(); // Here are the exceptions from our Python script
                string result = reader.ReadToEnd(); // Here is the result of StdOut(for example: print "test")

                if (!string.IsNullOrWhiteSpace(stderr))
                    /* this exception returns the value of stderr, which must be handled in calling methods */
                    throw new Exception(//$"Error during execution of python script \"{module}\", w arguments \"{start.Arguments}\" -->" +
                                        $"\n\t{stderr}");

                return result;
            }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

        }
    }
}
