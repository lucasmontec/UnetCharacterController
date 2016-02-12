using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;

public class NetworkDebug : NetworkBehaviour {

    private static NetworkDebug _instance;
    private short NWDebugMsgType = MsgType.Highest + 2;

    public void Awake() {
        _instance = this;
        NetworkServer.RegisterHandler(NWDebugMsgType, ServerLog);
    }

    public static void Log(string data) {
        _instance.ILog(data);
    }

    /// <summary>
    /// This prints the log data to the unity editor console.
    /// This will find the console across the network.
    /// </summary>
    /// <param name="data"></param>
    private void ILog(string data) {

        //If we are not the editor we need to find the editor
        //Clients will send the message to the server (could be the editor)
        //If the server receives a msg and is not the editor, he will broadcast the log
        //Because on of the clients may be the editor
        if (isServer) {
#if UNITY_EDITOR
            Debug.Log(data);
#else
            RpcClientLog(data);
#endif
        } else if (isClient) {
#if UNITY_EDITOR
            Debug.Log(data);
#else
            var smsg = new StringMessage(data);
            connectionToServer.Send(NWDebugMsgType, smsg);
#endif
        }

    }

    [Server]
    private void ServerLog(NetworkMessage message) {
        StringMessage smsg = message.ReadMessage<StringMessage>();
        Log(smsg.value);
    }

    [ClientRpc]
    private void RpcClientLog(string data) {
#if UNITY_EDITOR
        Debug.Log(data);
#endif
    }
}
