using System.Runtime.Serialization;

namespace GalaxyPPG.Common
{
  
    [DataContract]
    public enum WarningType
    {
        [EnumMember] BvpSpikeWarning,          
        [EnumMember] SkinTempOutOfRangeWarning, 
        [EnumMember] ExcessiveMotionWarning,  
        [EnumMember] IbiOutOfBandWarning      
    }
}
