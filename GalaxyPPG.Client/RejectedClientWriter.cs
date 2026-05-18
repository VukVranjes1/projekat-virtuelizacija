using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace GalaxyPPG.Client
{
    /// <summary>
    /// Klijent-side pisač za <c>rejected_client.csv</c> (KT1 zadatak 5).
    /// Hvata redove iz CSV-a koji nisu mogli da se parsiraju (loš broj kolona,
    /// neispravan double, neispravan timestamp, ...). Sve takve redove
    /// zapisuje u jedan fajl sa razlogom i sirovim tekstom reda.
    ///
    /// Implementira Dispose pattern radi determinističkog zatvaranja
    /// FileStream/StreamWriter resursa.
    /// </summary>
    internal sealed class RejectedClientWriter : IDisposable
    {
        private FileStream _fileStream;
        private StreamWriter _writer;
        private readonly object _writeLock = new object();
        private bool _disposed;

        public string FilePath { get; }

        public RejectedClientWriter(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            FilePath = filePath;

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Append: ne brišemo prethodne zapise između pokretanja klijenta.
            _fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            try
            {
                _writer = new StreamWriter(_fileStream, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };

                if (_fileStream.Length == 0)
                {
                    _writer.WriteLine("TimestampUtc,SourceFile,LineNumber,Reason,RawLine");
                }
            }
            catch
            {
                _fileStream.Dispose();
                _fileStream = null;
                throw;
            }
        }

        public void WriteReject(string sourceFile, long lineNumber, string reason, string rawLine)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RejectedClientWriter));

            lock (_writeLock)
            {
                _writer.WriteLine(
                    $"{DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}," +
                    $"{Csv(sourceFile)}," +
                    $"{lineNumber}," +
                    $"{Csv(reason)}," +
                    $"{Csv(rawLine)}");
            }
        }

        // Standardni CSV escape.
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

            try { _writer?.Flush(); }
            catch { /* tiho */ }

            _writer?.Dispose();
            _fileStream?.Dispose();
            _writer = null;
            _fileStream = null;
            _disposed = true;

            // Klasa je sealed i ne drži direktno unmanaged resurse - finalizer
            // nam ne treba, pa SuppressFinalize ne pravi razliku, ali ostavljam
            // ga radi konzistentnosti sa ostalim Dispose obrascima u projektu.
            GC.SuppressFinalize(this);
        }
    }
}
