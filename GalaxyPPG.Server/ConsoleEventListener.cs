using System;
using System.Threading;

namespace GalaxyPPG.Server
{
    /// <summary>
    /// Konzolni "slušalac" događaja - jedan od više mogućih (logovanje,
    /// vizuelni prikaz, notifikacije) koje KT2 z.8 pominje.
    ///
    /// OnSampleReceived se NE pretplaćuje na konzolu - na 64 Hz bi to bilo
    /// 64 print-a u sekundi i konzola bi postala neupotrebljiva. Umesto toga
    /// pratimo brojač u GalaxyPpgService-u (progress poruka na 500 redova).
    ///
    /// OnWarningRaised se prikazuje samo za prvih N upozorenja, pa onda
    /// jedna sumarna poruka - inače bi 5000 motion warning-a zatrpalo log.
    /// </summary>
    public sealed class ConsoleEventListener
    {
        private const int MaxWarningsOnConsole = 10;
        private long _warningCount;
        private bool _suppressionAnnounced;

        public void Subscribe(AnalyticsEngine engine)
        {
            engine.OnTransferStarted += HandleTransferStarted;
            engine.OnTransferCompleted += HandleTransferCompleted;
            engine.OnWarningRaised += HandleWarning;
        }

        public void Unsubscribe(AnalyticsEngine engine)
        {
            engine.OnTransferStarted -= HandleTransferStarted;
            engine.OnTransferCompleted -= HandleTransferCompleted;
            engine.OnWarningRaised -= HandleWarning;
        }

        private void HandleTransferStarted(object sender, TransferEventArgs e)
        {
            Console.WriteLine($"[Event] OnTransferStarted: session={e.SessionId} participant={e.ParticipantId}");
        }

        private void HandleTransferCompleted(object sender, TransferEventArgs e)
        {
            Console.WriteLine($"[Event] OnTransferCompleted: session={e.SessionId} " +
                              $"participant={e.ParticipantId} totalSamples={e.TotalSamples} " +
                              $"warnings={Interlocked.Read(ref _warningCount)}");
        }

        private void HandleWarning(object sender, WarningRaisedEventArgs e)
        {
            var count = Interlocked.Increment(ref _warningCount);

            if (count <= MaxWarningsOnConsole)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[WARN {e.Type}] row={e.RowIndex} {e.Message}");
                Console.ResetColor();
            }
            else if (!_suppressionAnnounced)
            {
                _suppressionAnnounced = true;
                Console.WriteLine($"[Event] (further warnings suppressed on console; " +
                                  $"see warnings.csv for full log)");
            }
        }
    }
}
