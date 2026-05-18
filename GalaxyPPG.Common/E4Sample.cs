using System.Runtime.Serialization;

namespace GalaxyPPG.Common
{
    /// <summary>
    /// Složeni tip koji nosi jedan uzorak sa Empatica E4 uređaja.
    /// Sva merenja su nullable: NaN vrednosti se na klijentu mapiraju u null
    /// pre slanja, jer je tako lakše razlikovati "validna nula" od "nedostaje".
    /// </summary>
    [DataContract]
    public class E4Sample
    {
        // Vremenska oznaka u Unix sekundama (float epoch).
        // Klijent konvertuje izvorni timestamp (ms/µs/ns) u sekunde sa decimalama.
        [DataMember]
        public double TimestampUnix { get; set; }

        // Sirovi PPG signal sa BVP kanala E4 uređaja.
        [DataMember]
        public double? BVP { get; set; }

        [DataMember]
        public double? AccX { get; set; }

        [DataMember]
        public double? AccY { get; set; }

        [DataMember]
        public double? AccZ { get; set; }

        // Otkucaji srca u BPM, izvedeni iz HR.csv.
        [DataMember]
        public double? HeartRate { get; set; }

        // Inter-Beat-Interval u milisekundama (iz IBI.csv).
        [DataMember]
        public double? IBI_ms { get; set; }

        // Temperatura kože u stepenima Celzijusa.
        [DataMember]
        public double? SkinTemp { get; set; }

        // ID učesnika (P01..P24).
        [DataMember]
        public string ParticipantId { get; set; }

        // Redni broj reda u izvornom CSV-u — koristi se za rejects log.
        [DataMember]
        public long RowIndex { get; set; }
    }
}
