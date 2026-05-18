using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace GalaxyPPG.Server
{
    /// <summary>
    /// Slušalac koji upisuje sva upozorenja u warnings.csv pored
    /// session.csv-a i rejects.csv-a u istom direktorijumu sesije.
    /// Isti Dispose obrazac kao ostali pisači (KT1 z.4).
    /// </summary>
    public sealed class WarningLogWriter : IDisposable
    {
        private FileStream _fileStream;
        private StreamWriter _writer;
        private readonly object _writeLock = new object();
        private bool _disposed;

        public string FilePath { get; }

        public WarningLogWriter(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            FilePath = filePath;
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            try
            {
                _writer = new StreamWriter(_fileStream, new UTF8Encoding(false)) { AutoFlush = true };
                if (_fileStream.Length == 0)
                {
                    _writer.WriteLine("TimestampUtc,ParticipantId,RowIndex,WarningType,Message");
                }
            }
            catch
            {
                _fileStream.Dispose();
                _fileStream = null;
                throw;
            }
        }

        public void Subscribe(AnalyticsEngine engine)
        {
            engine.OnWarningRaised += HandleWarning;
        }

        public void Unsubscribe(AnalyticsEngine engine)
        {
            engine.OnWarningRaised -= HandleWarning;
        }

        private void HandleWarning(object sender, WarningRaisedEventArgs e)
        {
            if (_disposed) return; // pisač možda već zatvoren u EndSession

            lock (_writeLock)
            {
                _writer.WriteLine(
                    $"{e.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)}," +
                    $"{Csv(e.ParticipantId)}," +
                    $"{e.RowIndex}," +
                    $"{e.Type}," +
                    $"{Csv(e.Message)}");
            }
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        public void Dispose()
        {
            if (_disposed) return;

            try { _writer?.Flush(); } catch { /* tiho */ }
            _writer?.Dispose();
            _fileStream?.Dispose();
            _writer = null;
            _fileStream = null;
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }
}
