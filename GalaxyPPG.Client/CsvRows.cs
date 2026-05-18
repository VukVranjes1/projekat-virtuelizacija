namespace GalaxyPPG.Client
{
    // Mali POCO tipovi po jedan za svaki E4 kanal. Drže već parsirani red u
    // memoriji - timestamp je sirov (mikrosekunde od epohe) jer ćemo posle
    // raditi merge po timestamp-u, pa nema svrhe konvertovati za HR/IBI/ACC/TEMP.
    // Vrednosti su nullable da bi NaN sa diska mogao da se mapira na null.

    internal sealed class BvpRow
    {
        public long TimestampMicro;
        public double? Value;
    }

    internal sealed class HrRow
    {
        public long TimestampMicro;
        public double? Value;
    }

    internal sealed class IbiRow
    {
        public long TimestampMicro;
        public double? DurationMs;
    }

    internal sealed class AccRow
    {
        public long TimestampMicro;
        public double? X;
        public double? Y;
        public double? Z;
    }

    internal sealed class TempRow
    {
        public long TimestampMicro;
        public double? Value;
    }
}
