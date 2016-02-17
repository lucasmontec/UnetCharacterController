using UnityEngine;
using System.Collections;

public class GameOptions : MonoBehaviour {

    [SerializeField]
    [Range(0.05f, 2f)]
    private float timescale = 1f;

	void Awake () {
        Time.timeScale = timescale;
        //Application.targetFrameRate = 10;
    }
	
}
