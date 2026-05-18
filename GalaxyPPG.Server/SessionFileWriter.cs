using System;
using System.Globalization;
using System.IO;
using System.Text;
using GalaxyPPG.Common;

namespace GalaxyPPG.Server
{
    /// <summary>
    /// Pisač u session.csv za jednu sesiju (KT2 zadatak 6).
    /// Drži FileStream + StreamWriter otvorenim tokom prijema i koristi
    /// isti Dispose obrazac kao <see cref="RejectsWriter"/>.
    /// Zaglavlje fajla je fiksiran skup 10 kanala koji odgovaraju
    /// <see cref="E4Sample"/> DataContract-u.
    /// </summary>
    public sealed class SessionFileWriter : IDisposable
    {
        private FileStream _fileStream;
        private StreamWriter _writer;
        private readonly object _writeLock = new object();
        private bool _disposed;

        public string FilePath { get; }

        public SessionFileWriter(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            FilePath = filePath;

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Append - sesija može da nastavi prethodni dan ako klijent
            // ponovo otvori sa istim ParticipantId-jem na isti datum.
            _fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            try
            {
                _writer = new StreamWriter(_fileStream, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };

                // Ako je fajl prazan upiši zaglavlje sa tačno 10 kolona = 10 kanala.
                if (_fileStream.Length == 0)
                {
                    _writer.WriteLine(
                        "RowIndex,TimestampUnix,ParticipantId," +
                        "BVP,AccX,AccY,AccZ,HeartRate,IBI_ms,SkinTemp");
                }
            }
            catch
            {
                _fileStream.Dispose();
                _fileStream = null;
                throw;
            }
        }

        public void Append(E4Sample s)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SessionFileWriter));

            // Lock radi sigurnosti ako bismo prešli na ConcurrencyMode.Multiple
            // u nekoj kasnijoj iteraciji.
            lock (_writeLock)
            {
                _writer.WriteLine(
                    $"{s.RowIndex}," +
                    $"{s.TimestampUnix.ToString("F6", CultureInfo.InvariantCulture)}," +
                    $"{Csv(s.ParticipantId)}," +
                    $"{Format(s.BVP)}," +
                    $"{Format(s.AccX)},{Format(s.AccY)},{Format(s.AccZ)}," +
                    $"{Format(s.HeartRate)}," +
                    $"{Format(s.IBI_ms)}," +
                    $"{Format(s.SkinTemp)}");
            }
        }

        // nullable double -> InvariantCulture string ili prazno polje.
        private static string Format(double? value)
        {
            if (!value.HasValue) return string.Empty;
            return value.Value.ToString("R", CultureInfo.InvariantCulture);
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
