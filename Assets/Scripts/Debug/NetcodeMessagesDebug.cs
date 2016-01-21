using UnityEngine;
using System.Collections;

/// <summary>
/// This class receives 
/// </summary>
public class NetcodeMessagesDebug : Singleton<NetcodeMessagesDebug> {
    // Number of messages clients sent and server received.
    private int serverMessages;
    private int clientMessages;

    // Inputs calculated in server and client.
    private int serverInputs;
    private int clientInputs;

    // If the game is over.
    private bool over;

    public void AddServerMessages(int messages = 1, int inputs = 1) {
        serverMessages += messages;
        serverInputs += inputs;
    }

    public void AddClientMessages(int messages = 1, int inputs = 1) {
        clientMessages += messages;
        clientInputs += inputs;
    }

    void PauseGameAndShowResults() {
        Time.timeScale = 0;
        over = true;

        Debug.LogWarning("GAME PAUSED. DEBUG INFO:");
        Debug.Log("Client messages sent: " + clientMessages + ", inputs calculated: " + clientInputs);
        Debug.Log("Server messages received: " + serverMessages + ", inputs calculated: " + serverInputs);
    }

    void Restart() {
        Time.timeScale = 1;
        over = false;

        serverInputs = serverMessages = clientInputs = clientMessages = 0;
    }

    void OnGUI() {
        if(over) {
            if(GUI.Button(new Rect(Screen.width / 2f, Screen.height / 2f, 100f, 40f), "Restart")) {
                Restart();
            }
        }
        else {
            if(GUI.Button(new Rect(Screen.width / 2f, Screen.height / 2f, 100f, 40f), "Pause")) {
                PauseGameAndShowResults();
            }
        }
    }
}
