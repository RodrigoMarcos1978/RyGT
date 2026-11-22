using System;
using System.IO;

namespace RyGT
{
    static class LogFiles
    {
        internal static readonly string[] Levels = { "Trace", "Debug", "Info", "Warning", "Error" };
        internal static int LevelIndex(string value)
        {
            for (int i = 0; i < Levels.Length; ++i)
                if (String.Equals(value, Levels[i], StringComparison.OrdinalIgnoreCase)) return i;
            if (String.Equals(value, "Critical", StringComparison.OrdinalIgnoreCase)) return 4;
            return 3; // Warning is the default, including invalid settings.
        }

        /// FIX-RT-026: an archived log keeps its extension: stdout-20261122T172045123Z.log, not
        /// stdout.log.20261122T172045123Z. GitHub refused to attach a file whose extension is a
        /// timestamp, so players could not upload the log of the session that failed. "-1", "-2", ...
        /// resolve a same-millisecond collision (as the runtime's LogFiles.hpp names them).
        internal static string ArchiveName(string directory, string name, string stamp)
        {
            string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
            string path = Path.Combine(directory, stem + "-" + stamp + extension);
            for (int i = 1; File.Exists(path); ++i)
                path = Path.Combine(directory, stem + "-" + stamp + "-" + i + extension);
            return path;
        }

        /// A game's log folder: logs\<serial> (CUSA03220 GT Sport, CUSA24769 GT7), so two games' logs
        /// never mix in one folder.
        internal static string Folder(string root, string serial)
        {
            return Path.Combine(root, "logs", serial);
        }

        internal static string PrepareRuntimeLogs(string root, string serial)
        {
            foreach (string name in new[] { "stdout.log", "stderr.log" })
                using (var file = new FileStream(Prepare(root, name, serial), FileMode.Create,
                                                FileAccess.Write, FileShare.ReadWrite)) { }
            return Folder(root, serial);
        }

        internal static string Prepare(string root, string name, string serial)
        {
            string directory = Folder(root, serial);
            Directory.CreateDirectory(directory);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            string legacy = Path.Combine(root, name);
            if (File.Exists(legacy)) File.Move(legacy, ArchiveName(directory, name, stamp + "-legacy"));
            string current = Path.Combine(directory, name);
            if (File.Exists(current)) File.Move(current, ArchiveName(directory, name, stamp));
            return current;
        }

    }
}
