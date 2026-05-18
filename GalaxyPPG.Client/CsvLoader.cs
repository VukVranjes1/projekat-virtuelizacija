using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace GalaxyPPG.Client
{
    /// <summary>
    /// Učitava E4 CSV fajlove (BVP, HR, IBI, ACC, TEMP) za jednog učesnika.
    ///
    /// Pravila (KT1 zadatak 5):
    ///   - InvariantCulture + decimalna tačka
    ///   - NaN -&gt; null (nedostajuća vrednost)
    ///   - Problematične redove zapisujemo u rejected_client.csv preko
    ///     <see cref="RejectedClientWriter"/>
    ///   - Timestamp na disku je u mikrosekundama od epohe (16 cifara);
    ///     ostavljamo ga kao long radi tačnog poređenja u merge fazi i
    ///     konvertujemo u float-sekunde tek pri pravljenju E4Sample-a.
    /// </summary>
    internal static class CsvLoader
    {
        // Mapiranje "NaN" tekstualne vrednosti u null. Tabelа dataset-a koristi
        // ćemo dosta tipičnih oznaka kao bezbednu rezervu.
        private static readonly HashSet<string> NullTokens =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "nan", "NaN", "null", "NULL", "", "na", "n/a", "-"
            };

        // ----------------------------------------------------------------
        // BVP.csv  ->  value,timestamp
        // ----------------------------------------------------------------
        public static List<BvpRow> LoadBvp(string path, RejectedClientWriter rejects, int maxRows = int.MaxValue)
        {
            var result = new List<BvpRow>(capacity: 256_000);
            int valueColIdx = 0;
            int timeColIdx = 1;

            // FileStream + StreamReader u using bloku - tačno onako kako KT1 zadatak 4
            // traži i kako Dispose pattern nalaže (deterministički zatvaramo resurse).
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sr = new StreamReader(fs))
            {
                ReadHeader(sr, path, expectedCols: 2);

                string line;
                long lineNumber = 1; // već smo pročitali zaglavlje
                while ((line = sr.ReadLine()) != null && result.Count < maxRows)
                {
                    lineNumber++;
                    var parts = line.Split(',');
                    if (parts.Length < 2)
                    {
                        rejects?.WriteReject(path, lineNumber, "Too few columns", line);
                        continue;
                    }

                    if (!TryParseDouble(parts[valueColIdx], out double? value))
                    {
                        rejects?.WriteReject(path, lineNumber, "BVP value not a number", line);
                        continue;
                    }

                    if (!TryParseLong(parts[timeColIdx], out long ts))
                    {
                        rejects?.WriteReject(path, lineNumber, "Timestamp not an integer", line);
                        continue;
                    }

                    result.Add(new BvpRow { TimestampMicro = ts, Value = value });
                }
            }

            return result;
        }

        // ----------------------------------------------------------------
        // HR.csv  ->  value,timestamp
        // ----------------------------------------------------------------
        public static List<HrRow> LoadHr(string path, RejectedClientWriter rejects)
        {
            var result = new List<HrRow>(capacity: 4096);

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sr = new StreamReader(fs))
            {
                ReadHeader(sr, path, expectedCols: 2);

                string line;
                long lineNumber = 1;
                while ((line = sr.ReadLine()) != null)
                {
                    lineNumber++;
                    var parts = line.Split(',');
                    if (parts.Length < 2)
                    {
                        rejects?.WriteReject(path, lineNumber, "Too few columns", line);
                        continue;
                    }
                    if (!TryParseDouble(parts[0], out double? value))
                    {
                        rejects?.WriteReject(path, lineNumber, "HR value not a number", line);
                        continue;
                    }
                    if (!TryParseLong(parts[1], out long ts))
                    {
                        rejects?.WriteReject(path, lineNumber, "Timestamp not an integer", line);
                        continue;
                    }
                    result.Add(new HrRow { TimestampMicro = ts, Value = value });
                }
            }
            return result;
        }

        // ----------------------------------------------------------------
        // IBI.csv  ->  timestamp,duration   (PAŽNJA: drugačiji redosled kolona)
        // Duration je u mikrosekundama na disku - konvertujemo u milisekunde
        // jer naš DataContract polje IBI_ms očekuje ms.
        // ----------------------------------------------------------------
        public static List<IbiRow> LoadIbi(string path, RejectedClientWriter rejects)
        {
            var result = new List<IbiRow>(capacity: 4096);

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sr = new StreamReader(fs))
            {
                ReadHeader(sr, path, expectedCols: 2);

                string line;
                long lineNumber = 1;
                while ((line = sr.ReadLine()) != null)
                {
                    lineNumber++;
                    var parts = line.Split(',');
                    if (parts.Length < 2)
                    {
                        rejects?.WriteReject(path, lineNumber, "Too few columns", line);
                        continue;
                    }
                    if (!TryParseLong(parts[0], out long ts))
                    {
                        rejects?.WriteReject(path, lineNumber, "Timestamp not an integer", line);
                        continue;
                    }
                    if (!TryParseDouble(parts[1], out double? durationMicro))
                    {
                        rejects?.WriteReject(path, lineNumber, "IBI duration not a number", line);
                        continue;
                    }

                    // µs -> ms: dataset duration je u mikrosekundama.
                    double? durationMs = durationMicro.HasValue
                        ? (double?)(durationMicro.Value / 1000.0)
                        : null;

                    result.Add(new IbiRow { TimestampMicro = ts, DurationMs = durationMs });
                }
            }
            return result;
        }

        // ----------------------------------------------------------------
        // ACC.csv  ->  x,y,z,timestamp
        // ----------------------------------------------------------------
        public static List<AccRow> LoadAcc(string path, RejectedClientWriter rejects)
        {
            var result = new List<AccRow>(capacity: 128_000);

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sr = new StreamReader(fs))
            {
                ReadHeader(sr, path, expectedCols: 4);

                string line;
                long lineNumber = 1;
                while ((line = sr.ReadLine()) != null)
                {
                    lineNumber++;
                    var parts = line.Split(',');
                    if (parts.Length < 4)
                    {
                        rejects?.WriteReject(path, lineNumber, "Too few columns", line);
                        continue;
                    }

                    if (!TryParseDouble(parts[0], out double? x) ||
                        !TryParseDouble(parts[1], out double? y) ||
                        !TryParseDouble(parts[2], out double? z))
                    {
                        rejects?.WriteReject(path, lineNumber, "ACC component not a number", line);
                        continue;
                    }
                    if (!TryParseLong(parts[3], out long ts))
                    {
                        rejects?.WriteReject(path, lineNumber, "Timestamp not an integer", line);
                        continue;
                    }

                    result.Add(new AccRow { TimestampMicro = ts, X = x, Y = y, Z = z });
                }
            }
            return result;
        }

        // ----------------------------------------------------------------
        // TEMP.csv  ->  value,timestamp
        // ----------------------------------------------------------------
        public static List<TempRow> LoadTemp(string path, RejectedClientWriter rejects)
        {
            var result = new List<TempRow>(capacity: 16_000);

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sr = new StreamReader(fs))
            {
                ReadHeader(sr, path, expectedCols: 2);

                string line;
                long lineNumber = 1;
                while ((line = sr.ReadLine()) != null)
                {
                    lineNumber++;
                    var parts = line.Split(',');
                    if (parts.Length < 2)
                    {
                        rejects?.WriteReject(path, lineNumber, "Too few columns", line);
                        continue;
                    }
                    if (!TryParseDouble(parts[0], out double? value))
                    {
                        rejects?.WriteReject(path, lineNumber, "TEMP value not a number", line);
                        continue;
                    }
                    if (!TryParseLong(parts[1], out long ts))
                    {
                        rejects?.WriteReject(path, lineNumber, "Timestamp not an integer", line);
                        continue;
                    }
                    result.Add(new TempRow { TimestampMicro = ts, Value = value });
                }
            }
            return result;
        }

        // ----------------------------------------------------------------
        // Helperi
        // ----------------------------------------------------------------

        // Pročita prvi red (zaglavlje) iz CSV-a i ignoriše ga. Nema validacije
        // konkretnih imena kolona - dataset je dovoljno stabilan, ali brojimo
        // kolone radi sanity check-a.
        private static void ReadHeader(StreamReader sr, string filePath, int expectedCols)
        {
            var header = sr.ReadLine();
            if (header == null)
                throw new InvalidDataException($"CSV is empty: {filePath}");

            var cols = header.Split(',');
            if (cols.Length < expectedCols)
                throw new InvalidDataException(
                    $"Header has {cols.Length} columns, expected at least {expectedCols}: {filePath}");
        }

        // NaN/null tokeni -> null; sve ostalo parsiramo kao double sa
        // InvariantCulture (decimalna tačka).
        private static bool TryParseDouble(string raw, out double? value)
        {
            value = null;
            if (raw == null) return false;
            raw = raw.Trim();

            if (NullTokens.Contains(raw))
            {
                value = null;
                return true;
            }

            if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands,
                                CultureInfo.InvariantCulture, out double parsed))
            {
                value = double.IsNaN(parsed) ? (double?)null : parsed;
                return true;
            }
            return false;
        }

        private static bool TryParseLong(string raw, out long value)
        {
            value = 0;
            if (raw == null) return false;
            return long.TryParse(raw.Trim(), NumberStyles.Integer,
                                 CultureInfo.InvariantCulture, out value);
        }
    }
}
