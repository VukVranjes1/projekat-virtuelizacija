using System.Collections.Generic;
using GalaxyPPG.Common;

namespace GalaxyPPG.Client
{
    /// <summary>
    /// Merge-er više E4 kanala u jednu vremensku osu.
    ///
    /// Strategija (master-timeline po BVP-u):
    ///   1. BVP je kanal sa najvišom frekvencijom (~64 Hz) - njegovi timestamp-ovi
    ///      definišu zajedničku vremensku osu.
    ///   2. Za svaki BVP red u trenutku T, pratimo "cursor" po svakom drugom
    ///      kanalu (HR, IBI, ACC, TEMP) i uzimamo poslednju vrednost kojoj je
    ///      timestamp &lt;= T.
    ///   3. Ako neki sporiji kanal još nije imao nijedan uzorak pre T,
    ///      odgovarajuće polje ostaje null - validator to tretira kao missing.
    ///
    /// Pošto su svi kanali već učitani u sortiranim listama (po timestamp-u
    /// rastuće) i mi BVP idemo monotono napred, dovoljno je advanced-ovati
    /// kursore linearno (O(N) ukupno, ne O(N log N) sa binary search-em).
    /// </summary>
    internal static class SampleMerger
    {
        public static IEnumerable<E4Sample> Merge(
            List<BvpRow> bvp,
            List<HrRow> hr,
            List<IbiRow> ibi,
            List<AccRow> acc,
            List<TempRow> temp,
            string participantId,
            long maxSamples = long.MaxValue)
        {
            int hrIdx = 0;
            int ibiIdx = 0;
            int accIdx = 0;
            int tempIdx = 0;

            // Poslednja poznata vrednost po kanalu - počinju kao null jer
            // pre prvog uzorka kanala nemamo informaciju.
            double? lastHr = null;
            double? lastIbi = null;
            double? lastAccX = null, lastAccY = null, lastAccZ = null;
            double? lastTemp = null;

            long emitted = 0;
            for (int b = 0; b < bvp.Count && emitted < maxSamples; b++, emitted++)
            {
                long t = bvp[b].TimestampMicro;

                // Advance kursore: pomeri svaki kursor dokle god je sledeći
                // element-ov timestamp &lt;= t. Kada izađemo iz petlje, kursor
                // pokazuje na poslednji element &lt;= t.
                while (hrIdx < hr.Count && hr[hrIdx].TimestampMicro <= t)
                {
                    lastHr = hr[hrIdx].Value;
                    hrIdx++;
                }
                while (ibiIdx < ibi.Count && ibi[ibiIdx].TimestampMicro <= t)
                {
                    lastIbi = ibi[ibiIdx].DurationMs;
                    ibiIdx++;
                }
                while (accIdx < acc.Count && acc[accIdx].TimestampMicro <= t)
                {
                    lastAccX = acc[accIdx].X;
                    lastAccY = acc[accIdx].Y;
                    lastAccZ = acc[accIdx].Z;
                    accIdx++;
                }
                while (tempIdx < temp.Count && temp[tempIdx].TimestampMicro <= t)
                {
                    lastTemp = temp[tempIdx].Value;
                    tempIdx++;
                }

                yield return new E4Sample
                {
                    // µs -> s (float epoch). Dataset nosi mikrosekunde u 16-cifrenom long-u.
                    TimestampUnix = t / 1_000_000.0,
                    BVP = bvp[b].Value,
                    AccX = lastAccX,
                    AccY = lastAccY,
                    AccZ = lastAccZ,
                    HeartRate = lastHr,
                    IBI_ms = lastIbi,
                    SkinTemp = lastTemp,
                    ParticipantId = participantId,
                    RowIndex = emitted
                };
            }
        }
    }
}
