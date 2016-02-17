using UnityEngine;
using System.Collections;
using UnityEngine.Networking;

/// <summary>
/// A Script to log RTT to the clients (or server clients)
/// </summary>
public class RTTDebug : NetworkBehaviour {

    private NetworkManager nwManager;
    private System.Text.StringBuilder debugString = new System.Text.StringBuilder();

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
            debugString.Remove(0, debugString.Length);
            if (client != null) {
                debugString.Append("RTT: ");
                debugString.Append(client.GetRTT().ToString());
            } else {
                debugString.Append("Null client");
            }
            GUI.Label(new Rect(20, 20, 100, 30), debugString.ToString());
        }
    }
}
