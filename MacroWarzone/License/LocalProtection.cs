using Microsoft.Win32;
using System;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;


namespace MacroWarzone.License
{
    public static class LocalProtection
    {
        public static string HardwareFingerprint()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var guid = key?.GetValue("MachineGuid")?.ToString();

            if (string.IsNullOrWhiteSpace(guid))
                guid = "UNKNOWN";

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(guid));

            return Convert.ToHexString(hash);
        }
        public static void AntiClockCheck()
        {
            var lastRunFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MyApp",
                "last_run.txt");
            DateTime now = DateTime.UtcNow;
            if (File.Exists(lastRunFile))
            {
                var lastRunStr = File.ReadAllText(lastRunFile);
                if (DateTime.TryParse(lastRunStr, out DateTime lastRun))
                {
                    if (now < lastRun)
                    {
                        throw new Exception("Rilevata manomissione dell'orologio di sistema.");
                    }
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(lastRunFile)!);
            File.WriteAllText(lastRunFile, now.ToString("o"));
        }
    }
}
