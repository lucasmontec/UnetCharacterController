using UnityEngine;
using System.Collections;
using UnityEngine.Networking;

/// <summary>
/// A Script to log RTT to the clients (or server clients)
/// </summary>
public class RTTDebug : NetworkBehaviour {

    private NetworkManager nwManager;

    void Start() {
        nwManager = GameObject.FindObjectOfType<NetworkManager>();
    }

	void Update () {
	
	}

    void OnGUI() {
        if (nwManager == null) {
            GUI.Label(new Rect(20, 20, 100, 30), "Network manager not found!");
        } else {
            NetworkClient client = nwManager.client;
            GUI.Label(new Rect(20, 20, 100, 30), (client==null?"Null client":("RTT: "+client.GetRTT().ToString())));
        }
    }
}
