using System;
using System.ServiceModel;

namespace GalaxyPPG.Server
{
    /// <summary>
    /// Konzolni host za WCF servis.
    ///
    /// KT1 zadatak 4: <see cref="ServiceHost"/> je ICommunicationObject -
    /// nije bezbedno staviti ga u <c>using</c> blok jer Close() može da baci
    /// izuzetak (kanal je u Faulted stanju). Standardni obrazac je
    /// try / catch / finally sa Close → Abort fallback-om.
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            Console.Title = "GalaxyPPG Server";

            ServiceHost host = null;
            try
            {
                // Tip se prosledjuje u konstruktor da bi WCF kreirao instance
                // po svojoj politici (PerSession u našem slučaju). Konfiguracija
                // (binding, endpoint) dolazi iz App.config - <service name="...">.
                host = new ServiceHost(typeof(GalaxyPpgService));
                host.Open();

                Console.WriteLine("GalaxyPPG WCF service is running.");
                foreach (var ep in host.Description.Endpoints)
                {
                    Console.WriteLine($"  Endpoint: {ep.Address}");
                    Console.WriteLine($"    Binding : {ep.Binding.Name}");
                    Console.WriteLine($"    Contract: {ep.Contract.ContractType.FullName}");
                }

                Console.WriteLine();
                Console.WriteLine("Press <Enter> to stop the service...");
                Console.ReadLine();

                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Fatal error: " + ex);
                Console.ResetColor();
                return 1;
            }
            finally
            {
                // Bez obzira na ishod, finally garantuje da host pravilno
                // oslobađa TCP slušaoca, kanale i instance servisa (preko Dispose).
                SafeCloseHost(host);
            }
        }

        /// <summary>
        /// Standardni obrazac zatvaranja ICommunicationObject (Close → Abort fallback).
        /// </summary>
        private static void SafeCloseHost(ServiceHost host)
        {
            if (host == null) return;
            try
            {
                if (host.State == CommunicationState.Faulted)
                {
                    host.Abort();
                }
                else
                {
                    host.Close();
                }
            }
            catch
            {
                // Ako Close baci, jedino što možemo je tvrdo prekinuti kanal.
                host.Abort();
            }
        }
    }
}
