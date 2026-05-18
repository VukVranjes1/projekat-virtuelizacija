using System;
using System.IO;
using System.Text;

namespace GalaxyPPG.Server
{
    /// <summary>
    /// Pisač u rejects.csv - drži <see cref="FileStream"/> i <see cref="StreamWriter"/>
    /// otvorenim tokom celog života sesije. Implementira pun Dispose pattern
    /// (KT1, zadatak 4):
    ///
    ///   public Dispose()  -> deterministički poziva korisnik / using-blok
    ///   protected Dispose(bool disposing) -> stvarni rad oslobađanja
    ///   ~RejectsWriter()  -> finalizer kao SAFETY NET ako neko zaboravi Dispose
    ///   GC.SuppressFinalize(this) -> sprečava nepotreban prolaz GC-a kroz finalization queue
    ///
    /// Napomena (SR): FileStream i StreamWriter su managed klase, ali interno
    /// drže nativni Win32 fajl-handle. Ako ih ne zatvorimo deterministički,
    /// fajl ostaje "lock-ovan" dok GC ne pokupi objekat - to je tačno ono
    /// što Dispose pattern rešava.
    /// </summary>
    public sealed class RejectsWriter : IDisposable
    {
        private FileStream _fileStream;
        private StreamWriter _writer;
        private readonly object _writeLock = new object();
        private bool _disposed;

        public string FilePath { get; }

        public RejectsWriter(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            FilePath = filePath;

            // Pravimo direktorijum ako ne postoji (u KT2 zadatku 6 ovo
            // postaje deo Data/<ParticipantId>/E4/<YYYY-MM-DD>/ strukture).
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Append: ne brišemo prethodne reject zapise - svaka sesija
            // dopisuje svoje redove. FileShare.Read da bi tail/Notepad
            // mogao da čita fajl dok sesija traje.
            _fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            try
            {
                // Ako StreamWriter konstruktor baci, moramo ručno zatvoriti FileStream
                // jer bi inače handle ostao otvoren do GC-a (bez Dispose-a).
                _writer = new StreamWriter(_fileStream, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };

                // Ako je fajl bio prazan (nova sesija), upiši zaglavlje.
                if (_fileStream.Length == 0)
                {
                    _writer.WriteLine("TimestampUtc,ParticipantId,RowIndex,Code,Field,Message,RawValues");
                }
            }
            catch
            {
                _fileStream.Dispose();
                _fileStream = null;
                throw;
            }
        }

        public void WriteReject(
            string participantId,
            long rowIndex,
            string code,
            string field,
            string message,
            string rawValues)
        {
            ThrowIfDisposed();

            // Lock zato što PerSession + ConcurrencyMode.Single garantuje
            // serijalizovan pristup u okviru jedne sesije, ali pisač je
            // po dizajnu spreman i za eventualno deljenje (npr. ako bismo
            // u KT2 prešli na ConcurrencyMode.Multiple).
            lock (_writeLock)
            {
                _writer.WriteLine(
                    $"{DateTime.UtcNow:O}," +
                    $"{Csv(participantId)}," +
                    $"{rowIndex}," +
                    $"{Csv(code)}," +
                    $"{Csv(field)}," +
                    $"{Csv(message)}," +
                    $"{Csv(rawValues)}");
            }
        }

        // Standardni CSV escape za polja koja sadrže zarez/navodnik/novi red.
        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RejectsWriter));
        }

        // ---- Dispose pattern ----

        public void Dispose()
        {
            Dispose(true);
            // Ne želimo da finalizer ponovo radi posao - skidamo objekat
            // iz finalization queue-a; ovo je takođe vid "ručnog upravljanja
            // GC-om" koji je deo zahteva (Dispose pattern + GC).
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // Managed resursi - oslobađamo redom: prvo writer (flush + close),
                // pa stream (zatvara nativni handle).
                try { _writer?.Flush(); }
                catch { /* tihi pad - finalizer ne sme da baca */ }

                _writer?.Dispose();
                _fileStream?.Dispose();
            }
            // Ovde bismo, da imamo, oslobađali nativne resurse (npr. IntPtr handles).

            _writer = null;
            _fileStream = null;
            _disposed = true;
        }

        // Finalizer - poziva ga GC ako klijent klase zaboravi Dispose.
        // Bezbednosna mreža: bolje da se resurs oslobodi sa zakašnjenjem
        // (kad GC stigne) nego da nikad ne bude oslobođen.
        ~RejectsWriter()
        {
            Dispose(false);
        }
    }
}
