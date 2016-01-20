using UnityEngine;
using System.Collections;
using UnityEngine.Networking;

/// <summary>
/// This class is just a simple controller that enables
/// certain client side components that need to be turned off for
/// multiplayer.
/// </summary>
public class NetworkTurnOn : NetworkBehaviour {

	/*
	 * Network setup
	 */
	[SerializeField]
	Camera fpsCharacterCam;
	[SerializeField]
	AudioListener audioListener;
	
	// Player network setup
	void Start () {
		if (isLocalPlayer) {
			//GetComponent<WBFirstPersonController> ().enabled = true; - This is now a shared script
			//If want to disable player to player collision, disable controller and enable only for local player
			//GetComponent<CharacterController>().enabled = true;
			fpsCharacterCam.enabled = true;
			audioListener.enabled = true;
		}
	}
}
