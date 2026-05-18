using System;
using GalaxyPPG.Common;

namespace GalaxyPPG.Server
{
    /// <summary>
    /// Argumenti za OnTransferStarted i OnTransferCompleted - identičan
    /// payload, jedna klasa za oba događaja radi jednostavnosti (KT2 z.8).
    /// </summary>
    public sealed class TransferEventArgs : EventArgs
    {
        public string SessionId { get; }
        public string ParticipantId { get; }
        public DateTime TimestampUtc { get; }
        public long TotalSamples { get; } // 0 na start, prava vrednost na complete

        public TransferEventArgs(string sessionId, string participantId, long totalSamples)
        {
            SessionId = sessionId;
            ParticipantId = participantId;
            TotalSamples = totalSamples;
            TimestampUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Argumenti za OnSampleReceived - server je primio jedan uzorak
    /// (svejedno da li je validan ili će biti odbačen).
    /// </summary>
    public sealed class SampleReceivedEventArgs : EventArgs
    {
        public E4Sample Sample { get; }
        public DateTime TimestampUtc { get; }

        public SampleReceivedEventArgs(E4Sample sample)
        {
            Sample = sample;
            TimestampUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Argumenti za OnWarningRaised - nosi tip upozorenja i kratak opis,
    /// kao i referencu na uzorak koji ga je izazvao.
    /// </summary>
    public sealed class WarningRaisedEventArgs : EventArgs
    {
        public WarningType Type { get; }
        public string Message { get; }
        public long RowIndex { get; }
        public string ParticipantId { get; }
        public DateTime TimestampUtc { get; }

        public WarningRaisedEventArgs(WarningType type, string message,
                                      long rowIndex, string participantId)
        {
            Type = type;
            Message = message;
            RowIndex = rowIndex;
            ParticipantId = participantId;
            TimestampUtc = DateTime.UtcNow;
        }
    }
}
