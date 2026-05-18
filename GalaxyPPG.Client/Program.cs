using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.ServiceModel;
using GalaxyPPG.Common;

namespace GalaxyPPG.Client
{
    /// <summary>
    /// Konzolni klijent. Tri scenarija:
    ///   1) Real streaming - učita E4 CSV-ove izabranog učesnika, merge-uje
    ///      ih po master timeline-u (BVP) i šalje servisu jedan po jedan red
    ///      (KT1 z.5 + KT2 z.7).
    ///   2) Demo session  - mali test scenario sa 4 ručno napravljena uzorka
    ///      (KT1 z.4 - validacija + FaultException).
    ///   3) Broken transfer - exception pre EndSession, dokaz da finally
    ///      uredno oslobađa proxy i factory (KT1 z.4).
    /// </summary>
    internal static class Program
    {
        private static void Main()
        {
            Console.Title = "GalaxyPPG Client";

            Console.WriteLine("GalaxyPPG client.");
            Console.WriteLine("  1) Stream real participant data (KT1 z.5 + KT2 z.7)");
            Console.WriteLine("  2) Run demo session (3 valid + 1 invalid sample)");
            Console.WriteLine("  3) Simulate broken transfer (Dispose pattern test)");
            Console.Write("Choose [1/2/3]: ");
            var choice = Console.ReadLine()?.Trim();

            switch (choice)
            {
                case "1": RunRealStreaming(); break;
                case "3": RunBrokenTransfer(); break;
                default:  RunDemoSession(); break;
            }

            Console.WriteLine();
            Console.WriteLine("Done. Press <Enter> to exit.");
            Console.ReadLine();
        }

        // -----------------------------------------------------------------
        // Scenario 1: realan streaming iz dataset-a
        // -----------------------------------------------------------------
        private static void RunRealStreaming()
        {
            var datasetRoot = ConfigurationManager.AppSettings["DatasetRoot"] ?? @"..\..\..\Dataset";
            var maxRowsStr = ConfigurationManager.AppSettings["MaxRowsPerSession"] ?? "5000";
            var rejectedPath = ConfigurationManager.AppSettings["RejectedClientPath"] ?? "rejected_client.csv";
            var latencyPath = ConfigurationManager.AppSettings["LatencyLogPath"] ?? "latency_client.csv";

            if (!int.TryParse(maxRowsStr, out int maxRows) || maxRows <= 0) maxRows = 5000;

            // Konvertuj relativnu putanju u apsolutnu radi razumljivijih poruka.
            datasetRoot = Path.GetFullPath(datasetRoot);

            if (!Directory.Exists(datasetRoot))
            {
                Console.WriteLine($"[Client] Dataset folder not found: {datasetRoot}");
                Console.WriteLine("         Update DatasetRoot in App.config.");
                return;
            }

            Console.Write("Enter participant id [P01]: ");
            var participantId = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(participantId)) participantId = "P01";

            var e4Dir = Path.Combine(datasetRoot, participantId, "E4");
            if (!Directory.Exists(e4Dir))
            {
                Console.WriteLine($"[Client] E4 folder not found: {e4Dir}");
                return;
            }

            // 1) Učitaj sve CSV fajlove. RejectedClientWriter sakuplja loše redove.
            Console.WriteLine($"[Client] Loading CSVs from {e4Dir} ...");
            RejectedClientWriter rejects = null;
            try
            {
                rejects = new RejectedClientWriter(rejectedPath);

                var bvp = CsvLoader.LoadBvp(Path.Combine(e4Dir, "BVP.csv"), rejects, maxRows: maxRows);
                var hr = CsvLoader.LoadHr(Path.Combine(e4Dir, "HR.csv"), rejects);
                var ibi = CsvLoader.LoadIbi(Path.Combine(e4Dir, "IBI.csv"), rejects);
                var acc = CsvLoader.LoadAcc(Path.Combine(e4Dir, "ACC.csv"), rejects);
                var temp = CsvLoader.LoadTemp(Path.Combine(e4Dir, "TEMP.csv"), rejects);

                Console.WriteLine($"[Client] Loaded BVP={bvp.Count}, HR={hr.Count}, " +
                                  $"IBI={ibi.Count}, ACC={acc.Count}, TEMP={temp.Count}");

                // 2) Otvori WCF proxy. Standardni Close/Abort obrazac, kao u KT1 z.4.
                ChannelFactory<IGalaxyPpgService> factory = null;
                IGalaxyPpgService proxy = null;
                LatencyLogger latency = null;
                try
                {
                    factory = new ChannelFactory<IGalaxyPpgService>("GalaxyPPGClient");
                    proxy = factory.CreateChannel();
                    latency = new LatencyLogger(latencyPath);

                    var sessionId = proxy.StartSession(new SessionMeta
                    {
                        ParticipantId = participantId,
                        DeviceId = "E4",
                        SampleRateHz = 64, // BVP rate kao master clock
                        StartTimestampUnix = bvp.Count > 0 ? bvp[0].TimestampMicro / 1_000_000.0 : NowUnix()
                    });
                    Console.WriteLine($"[Client] Session started: {sessionId}");

                    // 3) Merge + sekvencijalni streaming. Po jedan PushSample po redu,
                    //    sa lokalnim merenjem latencije i status izveštajem na konzoli.
                    long sent = 0, rejected = 0;
                    var swTotal = Stopwatch.StartNew();
                    foreach (var sample in SampleMerger.Merge(bvp, hr, ibi, acc, temp, participantId, maxRows))
                    {
                        var sendStart = DateTime.UtcNow;
                        var sw = Stopwatch.StartNew();
                        try
                        {
                            var ack = proxy.PushSample(sample);
                            sw.Stop();
                            latency.LogSend(sample.RowIndex, sendStart, sw.Elapsed.TotalMilliseconds);
                            if (ack.Accepted) sent++;
                        }
                        catch (FaultException<ValidationFault> vf)
                        {
                            sw.Stop();
                            latency.LogSend(sample.RowIndex, sendStart, sw.Elapsed.TotalMilliseconds);
                            rejected++;
                            if (rejected <= 5)
                            {
                                Console.WriteLine($"  Row {sample.RowIndex}: REJECTED " +
                                                  $"({vf.Detail.Code} on {vf.Detail.Field})");
                            }
                        }

                        // Status na svakih 500 redova (smanjuje šum u konzoli za velike sesije).
                        if ((sample.RowIndex + 1) % 500 == 0)
                        {
                            Console.WriteLine($"  [progress] sent={sent} rejected={rejected} " +
                                              $"elapsed={swTotal.Elapsed.TotalSeconds:F1}s");
                        }
                    }
                    swTotal.Stop();

                    Console.WriteLine($"[Client] Streaming finished: sent={sent} rejected={rejected} " +
                                      $"totalElapsed={swTotal.Elapsed.TotalSeconds:F2}s");
                    proxy.EndSession();
                    Console.WriteLine($"[Client] Latency log: {Path.GetFullPath(latencyPath)}");
                }
                catch (FaultException<ValidationFault> vf)
                {
                    Console.WriteLine($"[VALIDATION] {vf.Detail.Code} on '{vf.Detail.Field}': {vf.Detail.Message}");
                }
                catch (CommunicationException ce)
                {
                    Console.WriteLine("[COMM ERROR] " + ce.Message);
                }
                catch (TimeoutException te)
                {
                    Console.WriteLine("[TIMEOUT] " + te.Message);
                }
                finally
                {
                    // Standardni redosled: prvo proxy, pa factory, pa logger.
                    CloseProxy(proxy);
                    CloseFactory(factory);
                    latency?.Dispose();
                }
            }
            finally
            {
                rejects?.Dispose();
                if (rejects != null)
                    Console.WriteLine($"[Client] Rejected client rows: {Path.GetFullPath(rejectedPath)}");
            }
        }

        // -----------------------------------------------------------------
        // Scenario 2: demo - 3 validna + 1 nevalidan
        // -----------------------------------------------------------------
        private static void RunDemoSession()
        {
            ChannelFactory<IGalaxyPpgService> factory = null;
            IGalaxyPpgService proxy = null;

            try
            {
                factory = new ChannelFactory<IGalaxyPpgService>("GalaxyPPGClient");
                proxy = factory.CreateChannel();

                var sessionId = proxy.StartSession(new SessionMeta
                {
                    ParticipantId = "P01",
                    DeviceId = "E4",
                    SampleRateHz = 64,
                    StartTimestampUnix = NowUnix()
                });
                Console.WriteLine($"[Client] Session started: {sessionId}");

                SendDemo(proxy, MakeSample(0, bvp: 100));
                SendDemo(proxy, MakeSample(1, bvp: -50));
                SendDemo(proxy, MakeSample(2, skinTemp: 33.0));
                SendDemo(proxy, MakeSample(3, bvp: 999_999)); // van opsega -> rejects

                proxy.EndSession();
                Console.WriteLine("[Client] EndSession OK");
            }
            catch (FaultException<ValidationFault> vf)
            {
                Console.WriteLine($"[VALIDATION] {vf.Detail.Code} on '{vf.Detail.Field}': {vf.Detail.Message}");
            }
            catch (CommunicationException ce)
            {
                Console.WriteLine("[COMM ERROR] " + ce.Message);
            }
            catch (TimeoutException te)
            {
                Console.WriteLine("[TIMEOUT] " + te.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] " + ex.Message);
            }
            finally
            {
                CloseProxy(proxy);
                CloseFactory(factory);
            }
        }

        // -----------------------------------------------------------------
        // Scenario 3: simulacija prekida prenosa (KT1 z.4)
        // -----------------------------------------------------------------
        private static void RunBrokenTransfer()
        {
            ChannelFactory<IGalaxyPpgService> factory = null;
            IGalaxyPpgService proxy = null;

            try
            {
                factory = new ChannelFactory<IGalaxyPpgService>("GalaxyPPGClient");
                proxy = factory.CreateChannel();

                proxy.StartSession(new SessionMeta
                {
                    ParticipantId = "P02",
                    DeviceId = "E4",
                    SampleRateHz = 64,
                    StartTimestampUnix = NowUnix()
                });
                Console.WriteLine("[Client] Session started for broken-transfer demo.");

                SendDemo(proxy, MakeSample(0, bvp: 100, participant: "P02"));
                SendDemo(proxy, MakeSample(1, bvp: 110, participant: "P02"));

                Console.WriteLine("[Client] Throwing simulated transfer break BEFORE EndSession...");
                throw new InvalidOperationException("Simulated transfer break.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Caught] " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                CloseProxy(proxy);
                CloseFactory(factory);
                Console.WriteLine("[Client] finally: proxy and factory released.");
            }
        }

        // -----------------------------------------------------------------
        // Pomoćne metode
        // -----------------------------------------------------------------

        private static void SendDemo(IGalaxyPpgService proxy, E4Sample sample)
        {
            try
            {
                var ack = proxy.PushSample(sample);
                Console.WriteLine($"  Row {ack.RowIndex}: {ack.Status}");
            }
            catch (FaultException<ValidationFault> vf)
            {
                Console.WriteLine($"  Row {sample.RowIndex}: REJECTED " +
                                  $"({vf.Detail.Code} - {vf.Detail.Message})");
            }
        }

        private static E4Sample MakeSample(
            long row,
            double? bvp = 100,
            double? accX = 0,
            double? accY = 0,
            double? accZ = 1,
            double? hr = 70,
            double? ibi = 850,
            double? skinTemp = 32,
            string participant = "P01")
        {
            return new E4Sample
            {
                ParticipantId = participant,
                RowIndex = row,
                TimestampUnix = NowUnix() + row * (1.0 / 64),
                BVP = bvp,
                AccX = accX,
                AccY = accY,
                AccZ = accZ,
                HeartRate = hr,
                IBI_ms = ibi,
                SkinTemp = skinTemp
            };
        }

        private static double NowUnix()
            => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        private static void CloseProxy(IGalaxyPpgService proxy)
        {
            if (!(proxy is ICommunicationObject co)) return;
            try
            {
                if (co.State == CommunicationState.Faulted) co.Abort();
                else co.Close();
            }
            catch
            {
                co.Abort();
            }
        }

        private static void CloseFactory<T>(ChannelFactory<T> factory)
        {
            if (factory == null) return;
            try
            {
                if (factory.State == CommunicationState.Faulted) factory.Abort();
                else factory.Close();
            }
            catch
            {
                factory.Abort();
            }
        }
    }
}
