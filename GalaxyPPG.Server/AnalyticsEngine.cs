using System;
using System.Configuration;
using System.Globalization;
using GalaxyPPG.Common;

namespace GalaxyPPG.Server
{
    /// <summary>
    /// Motor za analitiku i događaje (KT2 zadaci 8, 9, 10).
    ///
    /// Izlaže 4 događaja preko klasičnih C# event-ova:
    ///   - OnTransferStarted   (kad krene sesija)
    ///   - OnSampleReceived    (svaki primljen uzorak)
    ///   - OnTransferCompleted (kad se sesija završi)
    ///   - OnWarningRaised     (kad analitika detektuje uslov alarma)
    ///
    /// Pragovi se čitaju iz App.config (KT2 z.8), pa je klasa imutabilna
    /// nakon konstrukcije - "po uređaju/učesniku" varijanta bi prosledila
    /// drugačije pragove pri instanciranju.
    /// </summary>
    public sealed class AnalyticsEngine
    {
        // ---- Pragovi (KT2 z.8) ----
        public double BvpSpikeThreshold { get; }
        public double SkinTempMinC { get; }
        public double SkinTempMaxC { get; }
        public double AccMotionThreshold { get; }

        // ---- Stanje za running statistike ----
        private double? _previousBvp;
        private double _ibiRunningMean;
        private long _ibiCount;

        // ---- Događaji ----
        public event EventHandler<TransferEventArgs> OnTransferStarted;
        public event EventHandler<SampleReceivedEventArgs> OnSampleReceived;
        public event EventHandler<TransferEventArgs> OnTransferCompleted;
        public event EventHandler<WarningRaisedEventArgs> OnWarningRaised;

        public AnalyticsEngine(double bvpSpikeThreshold,
                               double skinTempMinC,
                               double skinTempMaxC,
                               double accMotionThreshold)
        {
            BvpSpikeThreshold = bvpSpikeThreshold;
            SkinTempMinC = skinTempMinC;
            SkinTempMaxC = skinTempMaxC;
            AccMotionThreshold = accMotionThreshold;
        }

        /// <summary>
        /// Pravi engine sa pragovima iz App.config-a. Ako neki ključ nedostaje
        /// koriste se razumne default vrednosti.
        /// </summary>
        public static AnalyticsEngine FromConfig()
        {
            return new AnalyticsEngine(
                ReadDouble("BvpSpikeThreshold", 500.0),
                ReadDouble("SkinTempMinC", 20.0),
                ReadDouble("SkinTempMaxC", 45.0),
                ReadDouble("AccMotionThreshold", 2.0));
        }

        private static double ReadDouble(string key, double fallback)
        {
            var raw = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            if (double.TryParse(raw, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out double v))
                return v;
            return fallback;
        }

        // ---- Eksterno raised-ovani event-i (poziva ih servis) ----

        public void RaiseTransferStarted(string sessionId, string participantId)
        {
            OnTransferStarted?.Invoke(this,
                new TransferEventArgs(sessionId, participantId, totalSamples: 0));
        }

        public void RaiseTransferCompleted(string sessionId, string participantId, long totalSamples)
        {
            OnTransferCompleted?.Invoke(this,
                new TransferEventArgs(sessionId, participantId, totalSamples));
        }

        // ---- Glavni obrada-uzorka ulaz ----

        /// <summary>
        /// Procesira jedan uzorak: emituje OnSampleReceived, pa proverava
        /// analitičke uslove i emituje OnWarningRaised po potrebi.
        ///
        /// Napomena: ne pravimo razliku između validnih i nevalidnih uzoraka -
        /// upozorenja se podižu nezavisno od validacije, što omogućava da
        /// analitika "vidi" i one redove koji bi inače otišli u rejects.
        /// </summary>
        public void ProcessSample(E4Sample s)
        {
            if (s == null) return;
            OnSampleReceived?.Invoke(this, new SampleReceivedEventArgs(s));

            CheckBvpSpike(s);
            CheckSkinTemp(s);
            CheckMotion(s);
            CheckIbiBand(s);
        }

        // ---- KT2 z.9: BVP nagli skok ----
        //
        // Pratimo prethodnu vrednost BVP-a i podižemo upozorenje ako razlika
        // pređe prag. NaN/null uzorci se ignorišu (ne resetuju "previous").
        private void CheckBvpSpike(E4Sample s)
        {
            if (!s.BVP.HasValue || double.IsNaN(s.BVP.Value)) return;

            if (_previousBvp.HasValue)
            {
                var delta = s.BVP.Value - _previousBvp.Value;
                if (Math.Abs(delta) > BvpSpikeThreshold)
                {
                    var direction = delta > 0 ? "rising" : "falling";
                    RaiseWarning(
                        WarningType.BvpSpikeWarning,
                        s,
                        $"BVP spike {direction}: |Δ|={Math.Abs(delta).ToString("F2", CultureInfo.InvariantCulture)} " +
                        $"(prev={_previousBvp.Value.ToString("F2", CultureInfo.InvariantCulture)}, " +
                        $"curr={s.BVP.Value.ToString("F2", CultureInfo.InvariantCulture)})");
                }
            }
            _previousBvp = s.BVP.Value;
        }

        // ---- KT2 z.9: SkinTemp van opsega ----
        private void CheckSkinTemp(E4Sample s)
        {
            if (!s.SkinTemp.HasValue || double.IsNaN(s.SkinTemp.Value)) return;

            var t = s.SkinTemp.Value;
            if (t < SkinTempMinC || t > SkinTempMaxC)
            {
                RaiseWarning(
                    WarningType.SkinTempOutOfRangeWarning,
                    s,
                    $"SkinTemp={t.ToString("F2", CultureInfo.InvariantCulture)} " +
                    $"outside [{SkinTempMinC}, {SkinTempMaxC}] C");
            }
        }

        // ---- KT2 z.10: Anorm motion ----
        private void CheckMotion(E4Sample s)
        {
            if (!s.AccX.HasValue || !s.AccY.HasValue || !s.AccZ.HasValue) return;
            if (double.IsNaN(s.AccX.Value) || double.IsNaN(s.AccY.Value) || double.IsNaN(s.AccZ.Value)) return;

            // Anorm = sqrt(X² + Y² + Z²)
            var x = s.AccX.Value;
            var y = s.AccY.Value;
            var z = s.AccZ.Value;
            var aNorm = Math.Sqrt(x * x + y * y + z * z);

            if (aNorm > AccMotionThreshold)
            {
                RaiseWarning(
                    WarningType.ExcessiveMotionWarning,
                    s,
                    $"|Acc|={aNorm.ToString("F2", CultureInfo.InvariantCulture)} > " +
                    $"threshold={AccMotionThreshold.ToString("F2", CultureInfo.InvariantCulture)} " +
                    $"(x={x},y={y},z={z})");
            }
        }

        // ---- KT2 z.10: IBI ±20% od running mean ----
        //
        // Koristimo online mean (Welford-style, samo prvi moment) - O(1) memorije,
        // O(1) po uzorku, dovoljno tačno za detekciju anomalija.
        private void CheckIbiBand(E4Sample s)
        {
            if (!s.IBI_ms.HasValue || double.IsNaN(s.IBI_ms.Value)) return;

            var ibi = s.IBI_ms.Value;

            // Proverimo opseg PRE nego što ažuriramo mean - inače bi novi outlier
            // pomerao srednju vrednost ka sebi i izbegao detekciju.
            if (_ibiCount > 0)
            {
                var lower = 0.80 * _ibiRunningMean;
                var upper = 1.20 * _ibiRunningMean;
                if (ibi < lower || ibi > upper)
                {
                    RaiseWarning(
                        WarningType.IbiOutOfBandWarning,
                        s,
                        $"IBI={ibi.ToString("F2", CultureInfo.InvariantCulture)} ms outside " +
                        $"±20% of running mean " +
                        $"{_ibiRunningMean.ToString("F2", CultureInfo.InvariantCulture)} ms " +
                        $"(band [{lower.ToString("F2", CultureInfo.InvariantCulture)}, " +
                        $"{upper.ToString("F2", CultureInfo.InvariantCulture)}])");
                }
            }

            // Onda ažuriraj mean inkrementalno.
            _ibiCount++;
            _ibiRunningMean += (ibi - _ibiRunningMean) / _ibiCount;
        }

        private void RaiseWarning(WarningType type, E4Sample s, string message)
        {
            OnWarningRaised?.Invoke(this,
                new WarningRaisedEventArgs(type, message, s.RowIndex, s.ParticipantId));
        }
    }
}
