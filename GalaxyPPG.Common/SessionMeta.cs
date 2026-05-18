using System.Runtime.Serialization;

namespace GalaxyPPG.Common
{
    /// <summary>
    /// Meta informacije koje klijent šalje na početku sesije
    /// (StartSession). Polja odgovaraju tački 1 specifikacije:
    /// {ParticipantId, DeviceId, SampleRateHz, StartTimestampUnix}.
    /// </summary>
    [DataContract]
    public class SessionMeta
    {
        [DataMember]
        public string ParticipantId { get; set; }

        // Trenutno uvek "E4" (jedini uređaj koji obrađujemo u KT1).
        [DataMember]
        public string DeviceId { get; set; }

        // Frekvencija odabiranja kanala (npr. 64 Hz za BVP, 32 Hz za ACC).
        [DataMember]
        public double SampleRateHz { get; set; }

        // Vreme početka sesije u Unix sekundama.
        [DataMember]
        public double StartTimestampUnix { get; set; }
    }
}
