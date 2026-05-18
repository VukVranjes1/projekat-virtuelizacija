using System.ServiceModel;

namespace GalaxyPPG.Common
{
    /// <summary>
    /// Ugovor servisa po specifikaciji KT1 zadatka 1 i 2:
    ///   StartSession(meta) -> id
    ///   PushSample(sample) -> AckResult
    ///   EndSession()
    ///
    /// SessionMode.Required + InstanceContextMode.PerSession na implementaciji
    /// znači da svaki klijent dobija svoju instancu servisa, pa se per-session
    /// stanje (meta, RejectsWriter) drži u poljima instance umesto u eksternoj
    /// mapi - jednostavnije i bezbednije po pitanju paralelizma.
    /// </summary>
    [ServiceContract(
        Namespace = "http://galaxyppg.local/v1",
        SessionMode = SessionMode.Required)]
    public interface IGalaxyPpgService
    {
        // IsInitiating=true: ovo je prva operacija u sesiji.
        [OperationContract(IsInitiating = true, IsTerminating = false)]
        [FaultContract(typeof(ValidationFault))]
        string StartSession(SessionMeta meta);

        [OperationContract(IsInitiating = false, IsTerminating = false)]
        [FaultContract(typeof(ValidationFault))]
        AckResult PushSample(E4Sample sample);

        // IsTerminating=true: ova operacija zatvara sesiju i instance servisa
        // ide na dispose preko WCF runtime-a.
        [OperationContract(IsInitiating = false, IsTerminating = true)]
        void EndSession();
    }
}
