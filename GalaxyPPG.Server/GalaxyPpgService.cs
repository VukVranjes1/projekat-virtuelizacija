using System;
using System.IO;
using System.ServiceModel;
using System.Threading;
using GalaxyPPG.Common;

namespace GalaxyPPG.Server
{
    [ServiceBehavior(
        InstanceContextMode = InstanceContextMode.PerSession,
        ConcurrencyMode = ConcurrencyMode.Single,
        IncludeExceptionDetailInFaults = false)]
    public class GalaxyPpgService : IGalaxyPpgService, IDisposable
    {
        private SessionMeta _meta;
        private SessionFileWriter _session;
        private RejectsWriter _rejects;
        private WarningLogWriter _warningLog;
        private AnalyticsEngine _analytics;
        private ConsoleEventListener _consoleListener;
        private string _sessionId;
        private long _validCount;
        private long _rejectCount;
        private bool _disposed;
        private bool _transferStartedAnnounced;

        private readonly string _baseDataDir;
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
                throw new FaultException<ValidationFault>(
                    new ValidationFault { Code = "INVALID_META", Message = "SessionMeta is null.", Field = nameof(SessionMeta) },
                    new FaultReason("Invalid session meta."));

            if (string.IsNullOrWhiteSpace(meta.ParticipantId))
                throw new FaultException<ValidationFault>(
                    new ValidationFault { Code = "INVALID_PARTICIPANT", Message = "ParticipantId is required.", Field = nameof(SessionMeta.ParticipantId) },
                    new FaultReason("ParticipantId is required."));

            _meta = meta;
            _sessionId = Guid.NewGuid().ToString("N");

            DateTime sessionDate = (meta.StartTimestampUnix > 0)
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)(meta.StartTimestampUnix * 1000)).UtcDateTime
                : DateTime.UtcNow;

            var sessionDir = Path.Combine(_baseDataDir, meta.ParticipantId, "E4", sessionDate.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(sessionDir);

            _session = new SessionFileWriter(Path.Combine(sessionDir, "session.csv"));
            _rejects = new RejectsWriter(Path.Combine(sessionDir, "rejects.csv"));
            _warningLog = new WarningLogWriter(Path.Combine(sessionDir, "warnings.csv"));

            _analytics = AnalyticsEngine.FromConfig();
            _consoleListener = new ConsoleEventListener();
            _consoleListener.Subscribe(_analytics);
            _warningLog.Subscribe(_analytics);

            Console.WriteLine($"[Service] StartSession id={_sessionId} participant={meta.ParticipantId} device={meta.DeviceId} rateHz={meta.SampleRateHz}");
            Console.WriteLine($"[Service]   -> {sessionDir}");
            Console.WriteLine($"[Service]   thresholds: BvpSpike={_analytics.BvpSpikeThreshold}, SkinTemp=[{_analytics.SkinTempMinC},{_analytics.SkinTempMaxC}], AccMotion={_analytics.AccMotionThreshold}");

            _analytics.RaiseTransferStarted(_sessionId, meta.ParticipantId);
            return _sessionId;
        }

        public AckResult PushSample(E4Sample sample)
        {
            if (_meta == null)
                throw new FaultException<ValidationFault>(
                    new ValidationFault { Code = "NO_ACTIVE_SESSION", Message = "Call StartSession before PushSample.", Field = null },
                    new FaultReason("No active session."));

            if (sample == null)
                throw new FaultException<ValidationFault>(
                    new ValidationFault { Code = "NULL_SAMPLE", Message = "Sample is null.", Field = null },
                    new FaultReason("Sample is null."));

            if (!_transferStartedAnnounced)
            {
                Console.WriteLine($"[Service] Prenos u toku (session {_sessionId})...");
                _transferStartedAnnounced = true;
            }

            _analytics?.ProcessSample(sample);

            if (!string.Equals(sample.ParticipantId, _meta.ParticipantId, StringComparison.Ordinal))
            {
                Reject(sample, "PARTICIPANT_MISMATCH", $"Expected {_meta.ParticipantId}, got {sample.ParticipantId}", nameof(E4Sample.ParticipantId));
                throw new FaultException<ValidationFault>(
                    new ValidationFault { Code = "PARTICIPANT_MISMATCH", Message = "Sample participant does not match session participant.", Field = nameof(E4Sample.ParticipantId), RowIndex = sample.RowIndex },
                    new FaultReason("Participant mismatch."));
            }

            var result = SampleValidator.Validate(sample);
            if (!result.IsValid)
            {
                Reject(sample, result.Code, result.Message, result.Field);
                throw new FaultException<ValidationFault>(
                    new ValidationFault { Code = result.Code, Message = result.Message, Field = result.Field, RowIndex = sample.RowIndex },
                    new FaultReason(result.Message));
            }

            _session?.Append(sample);

            var newCount = Interlocked.Increment(ref _validCount);
            if (newCount % ProgressEveryN == 0)
                Console.WriteLine($"[Service] Prenos u toku - primljeno {newCount} validnih ({Interlocked.Read(ref _rejectCount)} rejected)");

            return new AckResult { Accepted = true, Status = "OK", RowIndex = sample.RowIndex };
        }

        public void EndSession()
        {
            Console.WriteLine($"[Service] Prenos zavrsen. id={_sessionId} valid={_validCount} rejects={_rejectCount}");

            _analytics?.RaiseTransferCompleted(_sessionId, _meta?.ParticipantId, _validCount);

            var sessionPath = _session?.FilePath;
            var rejectsPath = _rejects?.FilePath;
            var warningsPath = _warningLog?.FilePath;

            Dispose();

            if (rejectsPath != null)
                Console.WriteLine($"[Service]   rejects.csv: {CountRows(rejectsPath)} row(s)");
            if (warningsPath != null && File.Exists(warningsPath))
                Console.WriteLine($"[Service]   warnings.csv: {CountRows(warningsPath)} row(s)");
            if (sessionPath != null && File.Exists(sessionPath))
                Console.WriteLine($"[Service]   session.csv: {new FileInfo(sessionPath).Length} bytes");
        }

        private static int CountRows(string path)
        {
            if (!File.Exists(path)) return 0;

            int totalLines = 0;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
            {
                while (sr.ReadLine() != null) totalLines++;
            }
            return totalLines > 0 ? totalLines - 1 : 0;
        }

        private void Reject(E4Sample sample, string code, string message, string field)
        {
            Interlocked.Increment(ref _rejectCount);
            _rejects?.WriteReject(sample.ParticipantId, sample.RowIndex, code, field, message, SerializeRaw(sample));
        }

        private static string SerializeRaw(E4Sample s)
        {
            return $"ts={s.TimestampUnix};bvp={s.BVP};acc=({s.AccX},{s.AccY},{s.AccZ});hr={s.HeartRate};ibi={s.IBI_ms};temp={s.SkinTemp}";
        }

        public void Dispose()
        {
            if (_disposed) return;

            if (_analytics != null)
            {
                try { _consoleListener?.Unsubscribe(_analytics); } catch { }
                try { _warningLog?.Unsubscribe(_analytics); } catch { }
            }

            try { _warningLog?.Dispose(); } catch { }
            try { _session?.Dispose(); } catch { }
            try { _rejects?.Dispose(); } catch { }

            _warningLog = null;
            _session = null;
            _rejects = null;
            _analytics = null;
            _consoleListener = null;
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }
}
