using System.Runtime.Serialization;

namespace GalaxyPPG.Common
{
    /// <summary>
    /// FaultContract koji se vraća klijentu kada validacija padne.
    /// Server koristi <c>FaultException&lt;ValidationFault&gt;</c> umesto sirovog
    /// izuzetka kako bi WCF kanal ostao u stanju Opened (sirovi exception bi
    /// kanal stavio u Faulted i klijent bi morao da pravi nov proxy).
    /// </summary>
    [DataContract]
    public class ValidationFault
    {
        // Stabilan kod greške ("BVP_OUT_OF_RANGE", "NAN_VALUE", ...).
        [DataMember]
        public string Code { get; set; }

        [DataMember]
        public string Message { get; set; }

        // Naziv polja koje nije prošlo validaciju (npr. nameof(E4Sample.BVP)).
        [DataMember]
        public string Field { get; set; }

        // Redni broj reda u izvornom CSV-u, radi lakše dijagnostike.
        [DataMember]
        public long RowIndex { get; set; }
    }
}
