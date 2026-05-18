using System.Runtime.Serialization;

namespace GalaxyPPG.Common
{
    /// <summary>
    /// Potvrda koju server vraća za svaki primljeni uzorak.
    /// </summary>
    [DataContract]
    public class AckResult
    {
        [DataMember]
        public bool Accepted { get; set; }

        // Status "OK", "WRITTEN", "WARNING", itd. (proširuje se u KT2).
        [DataMember]
        public string Status { get; set; }

        [DataMember]
        public long RowIndex { get; set; }
    }
}
