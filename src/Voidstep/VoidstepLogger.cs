using System;
using System.IO;
using TaleWorlds.Library;

namespace Voidstep
{
    internal sealed class VoidstepLogger
    {
        private readonly string _path;
        private readonly object _gate = new object();

        public VoidstepLogger()
        {
            try
            {
                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var directory = Path.Combine(documents, "Mount and Blade II Bannerlord", "Configs", "ModLogs");
                Directory.CreateDirectory(directory);
                _path = Path.Combine(directory, "Voidstep.log");
            }
            catch
            {
                _path = null;
            }
        }

        public void Debug(string message)
        {
            if (VoidstepSettings.Current.DebugLogging)
                Write("DEBUG", message, null);
        }

        public void Info(string message) => Write("INFO", message, null);

        public void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
            InformationManager.DisplayMessage(new InformationMessage($"Voidstep: {message}", Colors.Red));
        }

        private void Write(string level, string message, Exception exception)
        {
            try
            {
                var line = $"[{DateTime.UtcNow:O}] [{level}] {message}";
                if (exception != null)
                    line += Environment.NewLine + exception;
                if (string.IsNullOrEmpty(_path)) return;
                lock (_gate)
                    File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never affect combat state.
            }
        }
    }
}
