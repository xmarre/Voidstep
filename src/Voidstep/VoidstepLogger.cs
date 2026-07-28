using System;
using System.Collections.Generic;
using System.IO;
using TaleWorlds.Library;

namespace Voidstep
{
    internal sealed class VoidstepLogger
    {
        private static readonly object GlobalGate = new object();
        private readonly List<string> _paths = new List<string>(2);

        public VoidstepLogger()
        {
            AddDocumentsPath();
            AddModulePath();
        }

        public string PrimaryPath => _paths.Count > 0 ? _paths[0] : null;

        public void Debug(string message)
        {
            if (VoidstepSettings.Current.DebugLogging)
                Write("DEBUG", message, null);
        }

        public void Info(string message) => Write("INFO", message, null);

        public void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
            try { InformationManager.DisplayMessage(new InformationMessage($"Voidstep: {message}", Colors.Red)); }
            catch { }
        }

        private void AddDocumentsPath()
        {
            try
            {
                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrWhiteSpace(documents)) return;
                var directory = Path.Combine(documents, "Mount and Blade II Bannerlord", "Configs", "ModLogs");
                Directory.CreateDirectory(directory);
                _paths.Add(Path.Combine(directory, "Voidstep.log"));
            }
            catch { }
        }

        private void AddModulePath()
        {
            try
            {
                var root = BasePath.Name;
                if (string.IsNullOrWhiteSpace(root)) return;
                var directory = Path.Combine(root, "Modules", "Voidstep");
                if (!Directory.Exists(directory)) return;
                var path = Path.Combine(directory, "Voidstep.log");
                if (!_paths.Contains(path)) _paths.Add(path);
            }
            catch { }
        }

        private void Write(string level, string message, Exception exception)
        {
            var line = $"[{DateTime.UtcNow:O}] [{level}] {message}";
            if (exception != null)
                line += Environment.NewLine + exception;

            try { TaleWorlds.Library.Debug.Print("[Voidstep] " + line); }
            catch { }

            lock (GlobalGate)
            {
                for (var i = 0; i < _paths.Count; i++)
                {
                    try { File.AppendAllText(_paths[i], line + Environment.NewLine); }
                    catch { }
                }
            }
        }
    }
}
