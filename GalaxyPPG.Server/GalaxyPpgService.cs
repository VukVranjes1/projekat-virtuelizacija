using System;
using System.IO;
using System.ServiceModel;
using System.Threading;
using GalaxyPPG.Common;

namespace GalaxyPPG.Server
{
    /// <summary>
    /// Implementacija WCF servisa.
    ///
    /// InstanceContextMode.PerSession: svaka klijentska sesija dobija svoju
    /// instancu - per-session resursi (RejectsWriter, SessionFileWriter,
    /// session meta) drže se u poljima instance.
    ///
    /// KT2 zadatak 6: pri StartSession kreiramo strukturu
    ///   Data/&lt;ParticipantId&gt;/E4/&lt;YYYY-MM-DD&gt;/session.csv  (validni redovi)
    ///   Data/&lt;ParticipantId&gt;/E4/&lt;YYYY-MM-DD&gt;/rejects.csv  (nevalidni)
    ///
    /// KT2 zadatak 7: server prikazuje "prenos u toku" status periodično,
    /// i "prenos završen" na EndSession (ili dispose).
    /// </summary>
    [ServiceBehavior(
        InstanceContextMode = InstanceContextMode.PerSession,
        ConcurrencyMode = ConcurrencyMode.Single,
        IncludeExceptionDetailInFaults = false)]
    public class GalaxyPpgService : IGalaxyPpgService, IDisposable
    {
        private SessionMeta _meta;
        private SessionFileWriter _session;
        private RejectsWriter _rejects;
        private string _sessionId;
        private long _validCount;
        private long _rejectCount;
        private bool _disposed;
        private bool _transferStartedAnnounced;

        // Bazni direktorijum - dalje formira Data/<P>/E4/<YYYY-MM-DD>/.
        private readonly string _baseDataDir;

        // Koliko redova proći pre nego što server izbaci progress poruku.
        private const int ProgressEveryN = 500;

        public GalaxyPpgService()
            : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data"))
        {
        }

        public GalaxyPpgService(string baseDataDir)
        {
            _baseDataDir = baseDataDir;
            Directory.CreateDirectory(_baseDataDir);
        }

        public string StartSession(SessionMeta meta)
        {
            if (meta == null)
            {
                throw new FaultException<ValidationFault>(
                    new ValidationFault
                    {
                        Code = "INVALID_META",
                        Message = "SessionMeta is null.",
                        Field = nameof(SessionMeta)
                    },
                    new FaultReason("Invalid session meta."));
            }

            if (string.IsNullOrWhiteSpace(meta.ParticipantId))
            {
                throw new FaultException<ValidationFault>(
                    new ValidationFault
                    {
                        Code = "INVALID_PARTICIPANT",
                        Message = "ParticipantId is required.",
                        Field = nameof(SessionMeta.ParticipantId)
                    },
                    new FaultReason("ParticipantId is required."));
            }

            _meta = meta;
            _sessionId = Guid.NewGuid().ToString("N");

            // KT2 zadatak 6: Data/<P>/E4/<YYYY-MM-DD>/{session,rejects}.csv
            //
            // Datum biramo iz meta.StartTimestampUnix kada je validan, inače UTC danas.
            // Ovo omogućava da reprodukovani snimak (sa starim timestamp-ovima) završi
            // u "ispravnoj" YYYY-MM-DD direktorijumu, a ne u trenutnom.
            DateTime sessionDate = (meta.StartTimestampUnix > 0)
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)(meta.StartTimestampUnix * 1000)).UtcDateTime
                : DateTime.UtcNow;

            var sessionDir = Path.Combine(
                _baseDataDir,
                meta.ParticipantId,
                "E4",
                sessionDate.ToString("yyyy-MM-dd"));

            Directory.CreateDirectory(sessionDir);

            _session = new SessionFileWriter(Path.Combine(sessionDir, "session.csv"));
            _rejects = new RejectsWriter(Path.Combine(sessionDir, "rejects.csv"));

            Console.WriteLine($"[Service] StartSession id={_sessionId} participant={meta.ParticipantId} " +
                              $"device={meta.DeviceId} rateHz={meta.SampleRateHz}");
            Console.WriteLine($"[Service]   -> {sessionDir}");

            return _sessionId;
        }

        public AckResult PushSample(E4Sample sample)
        {
            if (_meta == null)
            {
                throw new FaultException<ValidationFault>(
                    new ValidationFault
                    {
                        Code = "NO_ACTIVE_SESSION",
                        Message = "Call StartSession before PushSample.",
                        Field = null
                    },
                    new FaultReason("No active session."));
            }

            if (sample == null)
            {
                throw new FaultException<ValidationFault>(
                    new ValidationFault
                    {
                        Code = "NULL_SAMPLE",
                        Message = "Sample is null.",
                        Field = null
                    },
                    new FaultReason("Sample is null."));
            }

            // KT2 zadatak 7: kad stigne prvi uzorak prijavimo "prenos u toku"
            // (samo jednom po sesiji), pa onda dalje izveštavamo na svakih N redova.
            if (!_transferStartedAnnounced)
            {
                Console.WriteLine($"[Service] Prenos u toku (session {_sessionId})...");
                _transferStartedAnnounced = true;
            }

            // Provera učesnika - mora da se poklapa sa sesijom.
            if (!string.Equals(sample.ParticipantId, _meta.ParticipantId, StringComparison.Ordinal))
            {
                Reject(sample, "PARTICIPANT_MISMATCH",
                    $"Expected {_meta.ParticipantId}, got {sample.ParticipantId}",
                    nameof(E4Sample.ParticipantId));

                throw new FaultException<ValidationFault>(
                    new ValidationFault
                    {
                        Code = "PARTICIPANT_MISMATCH",
                        Message = "Sample participant does not match session participant.",
                        Field = nameof(E4Sample.ParticipantId),
                        RowIndex = sample.RowIndex
                    },
                    new FaultReason("Participant mismatch."));
            }

            var result = SampleValidator.Validate(sample);
            if (!result.IsValid)
            {
                Reject(sample, result.Code, result.Message, result.Field);

                throw new FaultException<ValidationFault>(
                    new ValidationFault
                    {
                        Code = result.Code,
                        Message = result.Message,
                        Field = result.Field,
                        RowIndex = sample.RowIndex
                    },
                    new FaultReason(result.Message));
            }

            // KT2 zadatak 6: validan red ide u session.csv (append).
            _session?.Append(sample);

            var newCount = Interlocked.Increment(ref _validCount);
            if (newCount % ProgressEveryN == 0)
            {
                Console.WriteLine($"[Service] Prenos u toku - primljeno {newCount} validnih " +
                                  $"({Interlocked.Read(ref _rejectCount)} rejected)");
            }

            return new AckResult
            {
                Accepted = true,
                Status = "OK",
                RowIndex = sample.RowIndex
            };
        }

        public void EndSession()
        {
            Console.WriteLine($"[Service] Prenos završen. id={_sessionId} " +
                              $"valid={_validCount} rejects={_rejectCount}");

            // Putanje pre Dispose-a (kad se _session i _rejects null-iraju).
            var sessionPath = _session?.FilePath;
            var rejectsPath = _rejects?.FilePath;

            Dispose();

            if (rejectsPath != null)
            {
                var rejectRows = CountRejectRows(rejectsPath);
                Console.WriteLine($"[Service]   rejects.csv: {rejectRows} row(s)");
            }
            if (sessionPath != null && File.Exists(sessionPath))
            {
                Console.WriteLine($"[Service]   session.csv: {new FileInfo(sessionPath).Length} bytes");
            }
        }

        /// <summary>
        /// StreamReader + FileStream u using blokovima - eksplicitan primer
        /// pravilnog Dispose-a (KT1 zadatak 4).
        /// </summary>
        private static int CountRejectRows(string path)
        {
            if (!File.Exists(path)) return 0;

            int totalLines = 0;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
            {
                while (sr.ReadLine() != null) totalLines++;
            }
            return totalLines > 0 ? totalLines - 1 : 0; // -1 za zaglavlje
        }

        // ---- Pomoćne metode ----

        private void Reject(E4Sample sample, string code, string message, string field)
        {
            Interlocked.Increment(ref _rejectCount);
            _rejects?.WriteReject(
                sample.ParticipantId,
                sample.RowIndex,
                code,
                field,
                message,
                SerializeRaw(sample));
        }

        private static string SerializeRaw(E4Sample s)
        {
            return $"ts={s.TimestampUnix};bvp={s.BVP};acc=({s.AccX},{s.AccY},{s.AccZ});" +
                   $"hr={s.HeartRate};ibi={s.IBI_ms};temp={s.SkinTemp}";
        }

        // ---- Dispose ----

        public void Dispose()
        {
            if (_disposed) return;

            try { _session?.Dispose(); } catch { /* tiho */ }
            try { _rejects?.Dispose(); } catch { /* tiho */ }

            _session = null;
            _rejects = null;
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }
}
