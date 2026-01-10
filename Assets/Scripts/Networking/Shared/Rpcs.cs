using Unity.Collections;
using Unity.NetCode;

namespace Unity.FPSSample_2
{

    /// <summary>
    /// Client to server to request joinning current session. 
    /// </summary>
    public struct ClientJoinRequestRpc : IRpcCommand
    {
        public FixedString64Bytes PlayerName;
        public int CharacterIndex;
    }

    /// <summary>
    /// Client request a death + respawn from the server. For testing purposes.
    /// </summary>
    public struct ClientRequestRespawnRpc : IRpcCommand
    {
    }
}