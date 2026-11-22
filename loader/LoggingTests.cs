using System;
using System.IO;
using System.Text;

namespace RyGT
{
    static class LoggingTests
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }

        static int Main()
        {
            Check(LogFiles.Levels.Length == 5 && LogFiles.LevelIndex("Critical") == 4, "Critical merged into Error");
            Check(LogFiles.LevelIndex(null) == 3 && LogFiles.LevelIndex("bad") == 3, "Warning default");
            string root = Path.Combine(Path.GetTempPath(), "RyGT-logging-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string launchRoot = Path.Combine(root, "launch");
                LogFiles.PrepareRuntimeLogs(launchRoot, "CUSA03220");
                Check(Directory.GetFiles(Path.Combine(launchRoot, "logs", "CUSA03220")).Length == 2, "first launch creates exactly two files");
                File.WriteAllText(Path.Combine(launchRoot, "logs", "CUSA03220", "stdout.log"), "keep");
                LogFiles.PrepareRuntimeLogs(launchRoot, "CUSA03220");
                Check(Directory.GetFiles(Path.Combine(launchRoot, "logs", "CUSA03220")).Length == 4, "second launch archives each file once");
                Check(Array.Exists(Directory.GetFiles(Path.Combine(launchRoot, "logs", "CUSA03220")), path => File.ReadAllText(path) == "keep"), "previous output retained");
                foreach (string name in new[] { "stdout.log", "stderr.log", "setup.log" })
                {
                    File.WriteAllText(Path.Combine(root, name), "legacy");
                    Directory.CreateDirectory(Path.Combine(root, "logs", "CUSA03220"));
                    File.WriteAllText(Path.Combine(root, "logs", "CUSA03220", name), "old");
                    string active = LogFiles.Prepare(root, name, "CUSA03220");
                    Check(!File.Exists(active) && !File.Exists(Path.Combine(root, name)), "rotate and migrate before writing");
                    File.WriteAllText(active, "new");
                    for (int repeat = 0; repeat < 10; ++repeat)
                    {
                        active = LogFiles.Prepare(root, name, "CUSA03220");
                        File.WriteAllText(active, "new-" + repeat);
                    }
                    var archives = Array.FindAll(Directory.GetFiles(Path.Combine(root, "logs", "CUSA03220")),
                        path => Path.GetFileName(path).StartsWith(Path.GetFileNameWithoutExtension(name) + "-", StringComparison.Ordinal) &&
                                Path.GetExtension(path) == Path.GetExtension(name)); // FIX-RT-026
                    Check(archives.Length == 12, "every prior log retained");
                    Check(Array.Exists(archives, path => File.ReadAllText(path) == "legacy"), "legacy contents retained");
                    Check(Array.Exists(archives, path => File.ReadAllText(path) == "old"), "prior contents retained");
                }
                var assembly = System.Reflection.Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RyGTLauncher.exe"));
                var settingsType = assembly.GetType("RyGT.Settings", true);
                string ini = Path.Combine(root, "CUSA03220.RyGT.ini"); // FEAT-GTS-291: GT Sport's settings file
                var settings = Activator.CreateInstance(settingsType, new object[] { ini });
                var levelField = settingsType.GetField("StdoutLevel");
                Check((string)levelField.GetValue(settings) == "Warning", "launcher default is Warning");
                levelField.SetValue(settings, "Debug");
                settingsType.GetMethod("Save").Invoke(settings, null);
                var restored = Activator.CreateInstance(settingsType, new object[] { ini });
                Check((string)levelField.GetValue(restored) == "Debug", "launcher level persists");
                File.WriteAllText(ini, "stdout_level=invalid");
                restored = Activator.CreateInstance(settingsType, new object[] { ini });
                Check((string)levelField.GetValue(restored) == "Warning", "invalid launcher level falls back to Warning");
                string directory = Path.Combine(root, "logs", "CUSA03220");
                File.WriteAllText(Path.Combine(directory, "stdout-same.log"), "keep");
                Check(Path.GetFileName(LogFiles.ArchiveName(directory, "stdout.log", "same")) == "stdout-same-1.log", "timestamp collision suffix");
                Check(Path.GetFileName(LogFiles.ArchiveName(directory, "stdout.log", "other")) == "stdout-other.log", "archive keeps the .log extension");
                Check(File.ReadAllText(Path.Combine(directory, "stdout-same.log")) == "keep", "archive never overwritten");
            }
            finally { Directory.Delete(root, true); }
            Console.WriteLine("PASS: Warning default, settings persistence, migration and lossless rotation");
            return 0;
        }
    }
}
