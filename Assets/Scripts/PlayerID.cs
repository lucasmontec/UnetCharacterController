using UnityEngine;
using System.Collections;
using UnityEngine.Networking;

/// <summary>
/// This class handles the Player ID creation.
/// This helps to interact with NetworkInstanceId.
/// NetworkInstanceId provides a single ID unique to each entity,
/// we use that to build player UIDs to use in our game.
/// 
/// This changes the player transform name to match the generated ID.
/// </summary>
public class PlayerID : NetworkBehaviour {

    //The UID for each player
	[SyncVar]
	public string
		playerUID;

    //The ID provider from the UNET frmrk
	private NetworkInstanceId playerNetworkID;
    //The player transform to change name
	private Transform playerTransform;

	public override void OnStartLocalPlayer () {
		GetNetIdentity ();
	}

	// Use this for initialization
	void Awake () {
		playerTransform = transform;
	}
	
	// Update is called once per frame
	void Update () {
		if (playerTransform.name == "" || playerTransform.name == "WBPlayer(Clone)") {
			SetUID ();
		}
	}
	
	void SetUID () {
		if (!isLocalPlayer) {
			playerTransform.name = playerUID;
		} else {
			playerTransform.name = "Player_" + playerNetworkID.ToString ();
		}
	}

    //This is a method that can only be called client side.
    //This method gets the ID from the NetworkIdentity class and sets the ID on the server
    [Client]
	void GetNetIdentity () {
		playerNetworkID = GetComponent<NetworkIdentity> ().netId;
		CmdSetUID ("Player_" + playerNetworkID.ToString ());
	}

    //When the ID is set here, since it is a SyncVar, it gets broadcasted to all other copies of this
    //player object on all clients.
	[Command]
	void CmdSetUID (string name) {
		playerUID = name;
	}
}
