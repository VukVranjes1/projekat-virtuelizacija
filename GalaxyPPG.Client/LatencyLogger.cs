using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace GalaxyPPG.Client
{
    /// <summary>
    /// Hvata "vreme slanja" svakog reda i RTT (round-trip-time) servisa,
    /// upisujući to u latency_client.csv (KT2 zadatak 7).
    ///
    /// Korišćenje:
    ///   using (var log = new LatencyLogger(path)) {
    ///       var sendTime = DateTime.UtcNow;
    ///       proxy.PushSample(s);
    ///       log.LogSend(rowIndex, sendTime, (DateTime.UtcNow - sendTime).TotalMilliseconds);
    ///   }
    /// </summary>
    internal sealed class LatencyLogger : IDisposable
    {
        private FileStream _fileStream;
        private StreamWriter _writer;
        private readonly object _writeLock = new object();
        private bool _disposed;

        public string FilePath { get; }

        public LatencyLogger(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            FilePath = filePath;

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Nova sesija = nov fajl. RTT log nema smisla čuvati između pokretanja.
            _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            try
            {
                _writer = new StreamWriter(_fileStream, new UTF8Encoding(false))
                {
                    AutoFlush = false // bafer za performanse - flush radi Dispose
                };
                _writer.WriteLine("RowIndex,SendTimeUtc,RttMs");
            }
            catch
            {
                _fileStream.Dispose();
                _fileStream = null;
                throw;
            }
        }

        public void LogSend(long rowIndex, DateTime sendTimeUtc, double rttMs)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LatencyLogger));

            lock (_writeLock)
            {
                _writer.WriteLine(
                    $"{rowIndex}," +
                    $"{sendTimeUtc.ToString("O", CultureInfo.InvariantCulture)}," +
                    $"{rttMs.ToString("F3", CultureInfo.InvariantCulture)}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try { _writer?.Flush(); }
            catch { /* tiho */ }

            _writer?.Dispose();
            _fileStream?.Dispose();
            _writer = null;
            _fileStream = null;
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }
}
