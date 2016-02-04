using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityStandardAssets.CrossPlatformInput;
using UnityStandardAssets.Utility;
using System.Linq;
using Random = UnityEngine.Random;

// Input and results structs
public struct Inputs {
    public float yaw; // Y
    public float pitch; // X
    public bool[] wasd;
    public bool move;
    public bool walk;
    public bool crouch;
    public bool jump;
    public bool rotate;

    public long timeStamp;
}

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(AudioSource))]
//[NetworkSettings(channel = 0, sendInterval = 0.02f)]
[NetworkSettings(channel = 0, sendInterval = 0.08f)]
public class UNETFirstPersonController : NetworkBehaviour {
    private bool m_IsWalking;
    [SerializeField] private float m_WalkSpeed;
    [SerializeField] private float m_RunSpeed;
    [SerializeField] [Range(0f, 1f)]    private float m_RunstepLenghten;
    [SerializeField] private float m_CrouchSpeed = 3f;
    [SerializeField] private float m_CrouchCharacterHeight = 1.4f;
    [SerializeField] private float m_JumpSpeed = 10f;
    [SerializeField] private float m_StickToGroundForce;
    [SerializeField] private float m_GravityMultiplier;
    [SerializeField] private UnityStandardAssets.Characters.FirstPerson.MouseLook m_MouseLook;
    [SerializeField] private bool m_UseFovKick;
    [SerializeField] private FOVKick m_FovKick = new FOVKick();
    [SerializeField] private LerpControlledBob m_JumpBob = new LerpControlledBob();
    [SerializeField] private float m_StepInterval;
    [SerializeField] private AudioClip[] m_FootstepSounds;    // an array of footstep sounds that will be randomly selected from.
    [SerializeField] private AudioClip m_JumpSound;           // the sound played when character leaves the ground.
    [SerializeField] private AudioClip m_LandSound;           // the sound played when character touches back on ground.
    [SerializeField] private Transform m_firstPersonCharacter; //The gameObject that contains the camera and applies the yaw rotations

    // Player desacceleration factor (the lower, the faster it becomes 0)
    private const float m_SlowdownFactor = 0.6f;
    // Player strafe factor (the higher, more control he has while on air)
    private const float m_StrafeSpeed = 0.9f;

    [SerializeField]
    [Tooltip("Turn of to remove client side prediction")]
    private Boolean prediction = false;
    [SerializeField]
    [Tooltip("Turn of to remove client side reconciliation")]
    private Boolean reconciliation = false;
    [SerializeField]
    [Tooltip("Turn on input simulation instead of reading user input")]
    private Boolean inputSimulation = false;

    private Camera m_Camera;
    private bool m_Jump;
    private bool m_isCrouching;
    private bool m_PreviouslyCrouching;
    private bool[] m_Input = new bool[4]; // W, A, S, D
    private Vector3 m_MoveDir = Vector3.zero;
    private CharacterController m_CharacterController;
    private CollisionFlags m_CollisionFlags;
    private bool m_PreviouslyGrounded;
    private Vector3 m_OriginalCameraPosition;
    private float m_StepCycle;
    private float m_NextStep;
    private bool m_Jumping;
    private AudioSource m_AudioSource;

    //Reconciliation list - Client side (excludes the host client)
    private int maxReconciliationEntries = 250;
    private long currentReconciliationStamp = 0;

    //Local interpolation
    //The local authority on clients interpolate when correcting large diffs
    [SerializeField]
    [Tooltip("Turn on to add interpolation correction for local players that are clients.")]
    private Boolean useLocalInterpolation = true;
    private Vector3 targetPosition;
    private float localInterpolationFactor = 10f;

    // Velocity struct
    //public struct Velocity {
    //    public Vector3 velocity;
    //    public float timeStamp;
    //}

    //The list of inputs sent from the player to the server
    //This is server side
    private Queue<Inputs> inputsList = new Queue<Inputs>();
    private const int maxInputs = 500;

    //This is to only send data to the players in an interval
    private float dataStep;
    private bool rotationChanged = false;

    //Reduce server and client simulation mismatch
    //This variable prevents the server from simulating a client when messages
    //are too late
    private long lastMassageTime = 0;
    private const float maxDelayBeforeServerSimStop = 0.2f;

    //Local reconciliation (authority player)
    private struct ReconciliationEntry {
        public Inputs inputs;
        public Vector3 position;
        public Quaternion rotation;
        public bool grounded;
        public bool prevCrouching;
        public CollisionFlags lastFlags;
    }
    private List<ReconciliationEntry> reconciliationList = new List<ReconciliationEntry>();

    //Player position simulator
    private float simStep = 0f;
    private float maxSimDelay = 2f;
    private float minSimDelay = 0.01f;
    private float currentSimStep = 0.5f;

    //Reconciliation methods
    private void AddReconciliation(ReconciliationEntry entry) {
        reconciliationList.Add(entry);

        //Limit the list size
        if (reconciliationList.Count > maxReconciliationEntries)
            reconciliationList.RemoveAt(0);

        //Debug.Log("Current reconciliation list size: " + reconciliationList.Count);
    }

    // Use this for initialization
    private void Start() {
        //Client initialization
        if (isLocalPlayer) {
            m_CharacterController = GetComponent<CharacterController>();
            m_Camera = GetComponentInChildren<Camera>();
            m_OriginalCameraPosition = m_Camera.transform.localPosition;
            m_FovKick.Setup(m_Camera);
            m_StepCycle = 0f;
            m_NextStep = m_StepCycle / 2f;
            m_Jumping = false;
            m_AudioSource = GetComponent<AudioSource>();
            m_MouseLook.Init(transform, m_Camera.transform);
        }
        if (isServer) { // Server side initialization
            m_CharacterController = GetComponent<CharacterController>();
            m_Jumping = false;
        }
    }

    /*
    * SHARED
    */
    private void Update() {

        //Client side jump
        if (isLocalPlayer) {
            Quaternion lastCharacterRotation = transform.rotation;
            Quaternion lastCameraRotation = m_firstPersonCharacter.rotation;
            RotateView();
            if (Quaternion.Angle(lastCharacterRotation, transform.rotation) > 0 || Quaternion.Angle(lastCameraRotation, m_firstPersonCharacter.rotation) > 0) {
                rotationChanged = true;
            }

            // The jump state needs to read here to make sure it is not missed
            if (m_CharacterController.isGrounded)
                m_Jump |= CrossPlatformInputManager.GetButtonDown("Jump");
        }
    }

    string serverDebug = String.Empty;
    string clientDebug = String.Empty;
    struct debugMovement {
        public Vector3 position;
        public Vector3 velocity;
    }
    Queue<debugMovement> debugClientPos = new Queue<debugMovement>();

    /*
    * SHARED
    */
    //>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>FIXED UPDATE
    private void FixedUpdate() {
        float speed = 0f;

        //If this is running at the local player (client with authoritative control or host client)
        //We run normal FPS controller (prediction)
        if (isLocalPlayer) {
            m_PreviouslyGrounded = m_CharacterController.isGrounded;

            long timestamp = System.DateTime.UtcNow.Ticks;
            //Store crouch input to send to server
            //We do this before reading input so that we can compare with the current crouch state
            bool sendCrouch = m_isCrouching;

            //Input from user or simulated
            if (inputSimulation) {
                SimInput(out speed);
            }
            else {
                GetInput(out speed);
            }

            //Store jump input to send to server
            bool sendJump = m_Jump;

            // Store transform values
            //This is also used for the host to know if it moved to send change messages
            Vector3 prevPosition = transform.position;
            Quaternion prevRotation = transform.rotation;
            bool prevGrounded = m_CharacterController.isGrounded;
            // Store collision values
            CollisionFlags lastFlag = m_CollisionFlags;

            //If we have predicion, we use the input here to move the character
            if (prediction || isServer) {
                //Move the player object
                PlayerMovement(speed);
            }
            
            //Client sound and camera
            ProgressStepCycle(speed);

            if(!m_PreviouslyGrounded && m_CharacterController.isGrounded) {
                StartCoroutine(m_JumpBob.DoBobCycle());
                PlayLandingSound();
                m_Jumping = false;
            }

            //OWNER CLIENTS THAT ARE NOT THE HOST
            //CLIENTS THAT ARE NOT THE SERVER
            if (!isServer) {
                bool crouchChange = m_isCrouching != sendCrouch;
                bool moved = m_CharacterController.velocity.sqrMagnitude > 0 || m_Input[0] || m_Input[1]
                    || m_Input[2] || m_Input[3];
                if (moved || sendJump || crouchChange || rotationChanged) {
                    //Debug.Log("W: " + m_Input[0] + " A: " + m_Input[1] + " S: " + m_Input[2] + " D: " + m_Input[3]);
                    //Store all inputs generated between msgs to send to server
                    Inputs inputs = new Inputs();
                    inputs.yaw = transform.rotation.eulerAngles.y;
                    inputs.pitch = m_firstPersonCharacter.rotation.eulerAngles.x;
                    inputs.wasd = m_Input;
                    inputs.move = moved;
                    inputs.walk = m_IsWalking;
                    inputs.rotate = rotationChanged;
                    inputs.jump = sendJump;
                    inputs.crouch = m_isCrouching;
                    inputs.timeStamp = timestamp;
                    inputsList.Enqueue(inputs);
                    debugMovement dePos = new debugMovement();
                    // DEBUG POSITION
                    dePos.velocity = m_CharacterController.velocity;
                    dePos.position = transform.position;
                    debugClientPos.Enqueue(dePos);

                    //If we moved, then we need to store reconciliation
                    if (moved || sendJump || crouchChange) {
                        // Create reconciliation entry
                        ReconciliationEntry entry = new ReconciliationEntry();
                        entry.inputs = inputs;
                        entry.lastFlags = lastFlag;
                        entry.position = prevPosition;
                        entry.rotation = prevRotation;
                        entry.grounded = prevGrounded;
                        entry.prevCrouching = m_PreviouslyCrouching;
                        AddReconciliation(entry);
                    }
                    
                    //Clear the jump to send
                    sendJump = false;

                    //Clear rotation flag
                    rotationChanged = false;

                    //Debug.Log("InLst sz is: "+ inputsList.Count+ " Moved is: "+moved);
                }

                //Only send input at the network send interval
                if (dataStep > GetNetworkSendInterval()) {
                    dataStep = 0;

                    //Debug.Log("Sending messages to server");
                    int toSend = inputsList.Count;
                    //Send input to the server
                    while (inputsList.Count > 0) {
                        //Send the inputs done locally
                        Inputs i = inputsList.Dequeue();
                        debugMovement d = debugClientPos.Dequeue();

                        clientDebug += "\n" + i.timeStamp;
                        clientDebug += "\nSending input: [" + String.Join(", ", i.wasd.ToList<Boolean>().Select(p => p.ToString()).ToArray()) + "],\nis walking: " + i.walk + ", is crouching: " + i.crouch + ", is jumping: " + i.jump +
                                        ", does rotate: " + i.rotate + "\nposition: (" + d.position.x + ", " + d.position.y + ", " + d.position.z + "), velocity: " + d.velocity + "\n";
                        if (i.move && i.rotate) {
                            //Debug.Log("Mov & Rot sent");
                           CmdProcessMovementAndRotation(i.timeStamp, i.wasd, i.walk, i.crouch, i.jump, i.pitch, i.yaw);
                        } else if (i.move) {
                            //Debug.Log("Mov sent");
                            CmdProcessMovement(i.timeStamp, i.wasd, i.walk, i.crouch, i.jump);
                        } else if (i.rotate) {
                            //Debug.Log("Rot sent");
                            CmdProcessRotation(i.timeStamp, i.pitch, i.yaw);
                        }
                    }
                    if (toSend > 0) {
                        using(System.IO.StreamWriter file =
                            new System.IO.StreamWriter(Application.persistentDataPath + @"\DebugClient.txt", true)) {
                            file.WriteLine("\n\n==================\nInput:\n\n" + clientDebug + "===================");

                            clientDebug = String.Empty;
                        }
                    }
                    //Clear the input list
                    inputsList.Clear();
                }

                dataStep += Time.fixedDeltaTime;
            }

            //This is for the host player to send its position to all other clients
            //HOST (THE CLIENT ON THE SERVER) CLIENT
            if (isServer) {
                if (dataStep > GetNetworkSendInterval()) {
                    dataStep = 0;
                    if (Vector3.Distance(transform.position, prevPosition) > 0 || rotationChanged) {
                        //Send the current server pos to all clients
                        RpcClientReceivePosition(timestamp, transform.position, m_MoveDir);
                        //Debug.Log("Sent host pos");
                    }
                }
                dataStep += Time.fixedDeltaTime;
            }
        }
        /*
        * SERVER SIDE
        */
        else { //If we are on the server, we process commands from the client instead, and generate update messages
            if (isServer) {
                if(!m_PreviouslyGrounded && m_CharacterController.isGrounded) {
                    m_Jumping = false;
                }

                Inputs inputs;
                if (inputsList.Count == 0) {
                    //Check if the message is late
                    //If the message is too late and the list is empty
                    //Stop server simulation of the player
                    if (lastMassageTime > maxDelayBeforeServerSimStop)
                        return;

                    inputs = new Inputs();
                    inputs.walk = true;
                    inputs.crouch = m_isCrouching;
                    inputs.wasd = new bool[] { false, false, false, false };
                    inputs.jump = false;
                    inputs.rotate = false;
                    inputs.timeStamp = System.DateTime.UtcNow.Ticks;
                }
                else {
                    inputs = inputsList.Dequeue();
                    //Debug.Log("Removing : "+inputs.timeStamp);
                }

                Vector3 lastPosition = transform.position;
                Quaternion lastCharacterRotation = transform.rotation;
                Quaternion lastCameraRotation = m_firstPersonCharacter.rotation;

                m_IsWalking = inputs.walk;
                m_Input = inputs.wasd;
                m_isCrouching = inputs.crouch;
                m_Jump = inputs.jump;
                if (inputs.rotate) {
                    transform.rotation = Quaternion.Euler(transform.rotation.x, inputs.yaw, transform.rotation.z);
                    m_firstPersonCharacter.rotation = Quaternion.Euler(inputs.pitch, m_firstPersonCharacter.rotation.eulerAngles.y, m_firstPersonCharacter.rotation.eulerAngles.z);
                }
                currentReconciliationStamp = inputs.timeStamp;

                CalcSpeed(out speed); //Server-side method to the speed out of input from clients

                //Move the player object
                PlayerMovement(speed);

                serverDebug += "\n" + currentReconciliationStamp;
                serverDebug += "\nProcessing input: [" + String.Join(", ", inputs.wasd.ToList<Boolean>().Select(p=>p.ToString()).ToArray()) + "],\nis walking: "+ inputs.walk+ ", is crouching: "+ inputs.crouch+", is jumping: "+ inputs.jump+", does rotate: "+ inputs.rotate+ ", velocity: " + m_CharacterController.velocity + "\n";

                serverDebug += "\nPosition after applying input: (" + transform.position.x + ", " + transform.position.y + ", " + transform.position.z + ")\n";

                if (dataStep > GetNetworkSendInterval()) {
                    if (Vector3.Distance(transform.position, lastPosition) > 0 || Quaternion.Angle(transform.rotation, lastCharacterRotation) > 0 || Quaternion.Angle(m_firstPersonCharacter.rotation, lastCameraRotation) > 0) {
                        RpcClientReceivePosition(currentReconciliationStamp, transform.position, m_MoveDir);
                        //Debug.Log("Sent client pos "+dataStep + ", stamp: " + currentReconciliationStamp);
                    }
                    dataStep = 0;
                   
                    using (System.IO.StreamWriter file =
                        new System.IO.StreamWriter(Application.persistentDataPath+@"\Debug.txt", true)) {
                                    file.WriteLine("\n\n==================\nInput timestamp: " + currentReconciliationStamp+"\n\n"+ serverDebug + "===================");
                    }
                    serverDebug = "";
                }
                dataStep += Time.fixedDeltaTime;
                m_PreviouslyGrounded = m_CharacterController.isGrounded;
            }
        }
    }
    //>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>FIXED UPDATE

    /// <summary>
    /// This is the server-side part of the controller.
    /// This receives input variables to move the character in the server using
    /// client input.
    /// The stamp is the input time stamp for reconciliation.
    /// </summary>
    [Command(channel = 1)]
    private void CmdProcessMovement(long stamp, bool[] wasd, bool walk, bool crouch, bool jump) {
        if (inputsList.Count > maxInputs)
            return;   
        ServerProcessInput(stamp, wasd, walk, crouch, jump, false, 0f, 0f);
    }

    /// <summary>
    /// Receives input variables to rotate the character in the server.
    /// </summary>
    [Command(channel = 1)]
    private void CmdProcessRotation(long stamp, float pitch, float yaw) {
        ServerProcessInput(stamp, new bool[]{ false, false, false, false }, false, false, false, true, pitch, yaw);
    }

    /// <summary>
    /// Receives input variables to move and rotate the character in the server.
    /// </summary>
    [Command(channel = 1)]
    private void CmdProcessMovementAndRotation(long stamp, bool[] wasd, bool walk, bool crouch, bool jump, float pitch, float yaw) {
        ServerProcessInput(stamp, wasd, walk, crouch, jump, true, pitch, yaw);
    }

    /// <summary>
    /// Receives the movements and rotations input and process it. 
    /// </summary>
    [Server]
    private void ServerProcessInput(long stamp, bool[] wasd, bool walk, bool crouch, bool jump, bool rotate, float pitch, float yaw) {
        //Create the inputs structure
        Inputs received = new Inputs();
        received.timeStamp = stamp;
        received.wasd = wasd;
        received.walk = walk;
        received.crouch = crouch;
        received.jump = jump;
        received.rotate = rotate;
        received.pitch = pitch;
        received.yaw = yaw;

        //Add all inputs
        inputsList.Enqueue(received);

        //Debug.Log("Stamp of last input: " + stamp);
        //Debug.Log("Current input list size: " + inputsList.Count);

        //Store the last received message
        lastMassageTime = System.DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// This method processes the positions that the server sent to the client each update.
    /// </summary>
    /// <param name="pos">This is the position the server calculated for the input at that time.</param>
    /// <param name="inputStamp">This is the timestamp when the client sent this input originally.</param>
    [ClientRpc]
    private void RpcClientReceivePosition(long inputStamp, Vector3 pos, Vector3 movementVector) {
        if (reconciliation && isLocalPlayer) {// RECONCILIATION for owner players
            //Check if this stamp is in the list
            if (reconciliationList.Count == 0) {
                //Nothing to reconciliate, apply server position
                //Debug.Log(transform.position.ToString() + " : " + pos.ToString()); NUNCA REMOVER
                if (useLocalInterpolation) {
                    targetPosition = pos;
                    if(Vector3.SqrMagnitude(transform.position - targetPosition) > 0) {
                        transform.position = Vector3.Lerp(transform.position, targetPosition, localInterpolationFactor);
                    }
                } else {
                    transform.position = pos;
                }
            }
            else {
                //Reconciliation starting
                //Debug.Log("Stamp received from server: "+inputStamp);

                //Get the oldest recorded input from the player
                ReconciliationEntry firstEntry = reconciliationList[0];

                //Debug.Log("The local reconciliation lists starts at: " + firstEntry.inputs.timeStamp);
                //Debug.Log("The current position (local start position - before prediction) is: " + firstEntry.trans.position);
                //Debug.Log("The position the server sent is: " + pos);
                //Debug.Log("The current position (local end position - after prediction) is: " + transform.position);
                string debugError = "";
                float serverCalculationError = 0f;
                Vector3 predicted = transform.position;

                //If the incoming position is too old, ignore
                if (inputStamp < firstEntry.inputs.timeStamp) {
                    debugError += "Ignored! " + inputStamp+" first in list was "+ firstEntry.inputs.timeStamp + "\n";
                    return;
                }

                int oldListSize = reconciliationList.Count;
                //Debug, get the client entry for the server stamp
                ReconciliationEntry clientForServerStamp = new ReconciliationEntry();
                reconciliationList.ForEach(
                    entry => {
                        if(entry.inputs.timeStamp == inputStamp) {
                            clientForServerStamp = entry;
                        }
                    }
                );
                //Remove all older stamps
                reconciliationList.RemoveAll(
                    entry => entry.inputs.timeStamp <= inputStamp
                );

                //debugError += "Removed: " + (reconciliationList.Count - oldListSize) + ", reconciliation list size: " + reconciliationList.Count + ", old list size: " + oldListSize + "\n";

                //Save current collision flags
                CollisionFlags cflags = m_CollisionFlags;
                //Save m_Jump
                bool prevJump = m_Jump;

                // Apply the received position
                transform.position = pos;
                //Apply 'de' received movement
                m_MoveDir = movementVector;

                // Reapply all the inputs that aren't processed by the server yet.
                int count = 0;
                if (reconciliationList.Count > 0) {
                    //debugError += "The first position for reconciliation is: " + reconciliationList[0].position + "\n";
                    //Get the lastest collision flags
                    m_CollisionFlags = reconciliationList[0].lastFlags;

                    serverCalculationError = Vector3.Distance(reconciliationList[0].position, pos);

                    float speed = 0f;
                    ReconciliationEntry first = reconciliationList[0];
                    foreach (ReconciliationEntry e in reconciliationList) {
                        Inputs i = e.inputs;
                        m_Input = i.wasd;
                        m_IsWalking = i.walk;
                        m_isCrouching = i.crouch;
                        m_Jump = i.jump;
                        m_PreviouslyCrouching = e.prevCrouching;

                        CalcSpeed(out speed);

                        PlayerMovement(speed, e.grounded, e.position, e.rotation);
                        debugError += "("+(count++)+")Intermediate rec position: " + transform.position+"\n";
                    }
                }

                debugError += "The final reconciliated position is: " + transform.position+"\n";
                debugError += "The predicted position was: " + predicted + "\n";

                float threshold = 0.005f;

                //Check if the server calculated the position in a wrong way

                if (serverCalculationError > threshold) {
                    Debug.Log("[Server position sim failure " + inputStamp + "] Error (distance): " + serverCalculationError);
                }

                //Check if predicted is different from renconciliated
                float recError = Vector3.Distance(predicted, transform.position);
                if (recError > threshold) {
                    debugError += "Total error: " + recError+"\n";
                    debugError += "(Logging only errors above: " + threshold+")";
                    if (serverCalculationError > threshold) {
                        Debug.Log("[Reconciliation error due to server error] Log:\n" + debugError);
                    } else {
                        Debug.Log("[Reconciliation error] Log:\n" + debugError);
                    }
                }
                
                //Restore collision flags
                m_CollisionFlags = cflags;
                //Restore jump state
                m_Jump = prevJump;
            }
        }
        else {
            //NO RECONCILIATION
            //When the position arrives from the server, since server is priority,
            //set the local pos to it
            if(useLocalInterpolation) {
                targetPosition = pos;
                if(Vector3.SqrMagnitude(transform.position - targetPosition) > 0) {
                    transform.position = Vector3.Lerp(transform.position, targetPosition, localInterpolationFactor);
                }
            } else {
                 transform.position = pos;
            }
        }
    }

    /// <summary>
    /// SHARED
    /// Using a given input and gravity, moves the player object.
    /// This can be used both in server side and client side.
    /// This needs that the variables m_Input, m_Jump, m_JumpSpeed are updated
    /// </summary>
    /// <param name="speed">The speed of the movement calculated on an input method. Changes if the player is running or crouching.</param>
    private void PlayerMovement(float speed) {
        PlayerMovement(speed, m_CharacterController.isGrounded, transform.position, transform.rotation);
    }

    /// <summary>
    /// CLIENT-SIDE RECONCILIATION MOVEPLAYER
    /// Using a given input and gravity, moves the player object.
    /// This needs that the variables m_Input, m_JumpSpeed are updated
    /// </summary>
    /// <param name="speed">The speed of the movement calculated on an input method. Changes if the player is running or crouching.</param>
    private void PlayerMovement(float speed, bool grounded, Vector3 position, Quaternion rotation) {
        //Calculate player local forward vector and right vector based on the rotation
        Vector3 right = rotation * Vector3.right;
        Vector3 forward = rotation * Vector3.forward;

        // Always move along the camera forward as it is the direction that it being aimed at
        Vector3 desiredMove = forward * VerticalMovement(m_Input[0], m_Input[2]) + right * HorizontalMovement(m_Input[1], m_Input[3]);
        //Calculate the side movement for strafing while in air
        Vector3 desiredStrafe = right * HorizontalMovement(m_Input[1], m_Input[3]);

        // Get a normal for the surface that is being touched to move along it
        RaycastHit hitInfo;
        Physics.SphereCast(position, m_CharacterController.radius, Vector3.down, out hitInfo,
                           m_CharacterController.height / 2f);
        desiredMove = Vector3.ProjectOnPlane(desiredMove, hitInfo.normal).normalized;

        if ( grounded ) { //ON GROUND
            /*
            * NORMALIZED MOVEMENT WITH SLOW DOWN
            */
            if (Math.Abs(desiredMove.x) > 0) {
                m_MoveDir.x = desiredMove.x * speed;
            } else {
                m_MoveDir.x = m_MoveDir.x * m_SlowdownFactor;
            }
            if (Math.Abs(desiredMove.z) > 0) {
                m_MoveDir.z = desiredMove.z * speed;
            } else {
                m_MoveDir.z = m_MoveDir.z * m_SlowdownFactor;
            }
            m_MoveDir.y = -m_StickToGroundForce;

            /*
            * JUMP
            */
            if (m_Jump) {
                m_MoveDir.y = m_JumpSpeed;
                m_Jump = false;
                m_Jumping = true;
            }
        } else { //ON AIR
            /*
            * STRAFE
            */
            //Strafe desire
            //The momevent dot the component of the (global) movement vector along the transform right vector
            float movementDot = Vector3.Dot(m_MoveDir, right);
            //THe desired strafe dot is also the component of the desired (global) strafe movement along the local right vector
            float desiredStrafeDot = Vector3.Dot(desiredStrafe, right);
            //Here we do this massive if to check if the strafe is valid
            if (
                /*Going right but not at full speed, and want to accelerate*/
                (movementDot < 5f && movementDot > 0f && desiredStrafeDot > 0f)
                ||
                /*Going left but not at full speed, and want to accelerate*/
                (movementDot > -5f && movementDot < 0f && desiredStrafeDot < 0f)
                ||
                /*Going right but want to go left*/
                (movementDot > 0f && desiredStrafeDot < 0f)
                ||
                /*Going left but want to go right*/
                (movementDot < 0f && desiredStrafeDot > 0f)
                ||
                /*Want to strafe*/
                (movementDot == 0f)
                ) {
                m_MoveDir.x += desiredStrafe.x * m_StrafeSpeed;
                m_MoveDir.z += desiredStrafe.z * m_StrafeSpeed;
            }

            /*
            * GRAVITY
            */
            m_MoveDir += Physics.gravity * m_GravityMultiplier * Time.fixedDeltaTime;
        }
        m_CollisionFlags = m_CharacterController.Move(m_MoveDir * Time.fixedDeltaTime);
    }

    /// <summary>
    /// This calculates the player speed on the server side.
    /// </summary>
    /// <param name="speed">Player speed.</param>
    private void CalcSpeed(out float speed) {
        // Set speed if crouching
        if (m_isCrouching) {
            speed = m_CrouchSpeed;
            if (!m_PreviouslyCrouching) {
                // If the player was NOT crouching in the previous frame,
                // but is crouching in the current, set his height to the CrouchHeight
                m_CharacterController.height = m_CrouchCharacterHeight;
            }
        }
        // If not crouching, set the desired speed to be walking or running
        else {
            speed = m_IsWalking ? m_WalkSpeed : m_RunSpeed;
            if (m_PreviouslyCrouching) {
                // If the player WAS crouching in the previous frame,
                // but is not crouching in the current, set his height to standard CharacterHeight
                m_CharacterController.height = 1.8f;
            }
        }

        m_PreviouslyCrouching = m_isCrouching;
    }


    /// <summary>
    /// This method updates the step cycle based on the player speed.
    /// The step cycle can be used to know when to play the step sound.
    /// </summary>
    /// <param name="speed"></param>
    [Client]
    private void ProgressStepCycle(float speed) {
        if (m_CharacterController.velocity.sqrMagnitude > 0 &&
            (HorizontalMovement(m_Input[1], m_Input[3]) != 0 ||
            VerticalMovement(m_Input[0], m_Input[2]) != 0)) {
            m_StepCycle += (m_CharacterController.velocity.magnitude + (speed * (m_IsWalking ? 1f : m_RunstepLenghten))) *
                         Time.fixedDeltaTime;
        }

        if (!(m_StepCycle > m_NextStep)) {
            return;
        }

        m_NextStep = m_StepCycle + m_StepInterval;

        PlayFootStepAudio();
    }

    /// <summary>
    /// Simulate random input for network capacity testing.
    /// </summary>
    /// <param name="speed"></param>
    [Client]
    private void SimInput(out float speed) {
        //Always advance sim step
        simStep += Time.deltaTime;

        //If not in time to change sim, return the speed
        if (simStep < currentSimStep) {
            if (m_isCrouching) {
                speed = m_CrouchSpeed;
            }
            else {
                speed = m_IsWalking ? m_WalkSpeed : m_RunSpeed;
            }
            return;
        }

        //If reached here, then reset the simulation step
        simStep = 0f;
        currentSimStep = Random.Range(minSimDelay, maxSimDelay);

        // Read input
        float horizontal = Random.Range(-1f, 1f);
        float vertical = Random.Range(-1f, 1f);

        bool waswalking = m_IsWalking;

#if !MOBILE_INPUT
        // On standalone builds, walk/run speed is modified by a key press.
        // keep track of whether or not the character is walking or running
        m_IsWalking = !(Random.value > 0.5f);
#endif
        m_isCrouching = (Random.value > 0.5f);

        // Set settings if crouching
        if (m_isCrouching) {
            speed = m_CrouchSpeed;
            if (!m_PreviouslyCrouching) {
                m_CharacterController.height = m_CrouchCharacterHeight;
            }
        }
        // If not crouching, set the desired speed to be walking or running
        else {
            speed = m_IsWalking ? m_WalkSpeed : m_RunSpeed;
            if (m_PreviouslyCrouching) {
                m_CharacterController.height = 1.8f;
            }
        }

        //                     W          A             S           D
        m_Input = new bool[] { vertical > 0, horizontal < 0, vertical < 0, horizontal > 0 };

        // handle speed change to give an fov kick
        // only if the player is going to a run, is running and the fovkick is to be used
        if (m_IsWalking != waswalking && m_UseFovKick && m_CharacterController.velocity.sqrMagnitude > 0) {
            StopAllCoroutines();
            StartCoroutine(!m_IsWalking ? m_FovKick.FOVKickUp() : m_FovKick.FOVKickDown());
        }

        m_PreviouslyCrouching = m_isCrouching;
    }

    /// <summary>
    /// This gets the player input (walk/crouch/run) and set the player speed accordingly.
    /// </summary>
    /// <param name="speed"></param>
    [Client]
    private void GetInput(out float speed) {
        // Read input
        float horizontal = CrossPlatformInputManager.GetAxisRaw("Horizontal");
        float vertical = CrossPlatformInputManager.GetAxisRaw("Vertical");

        bool waswalking = m_IsWalking;

#if !MOBILE_INPUT
        // On standalone builds, walk/run speed is modified by a key press.
        // keep track of whether or not the character is walking or running
        m_IsWalking = !CrossPlatformInputManager.GetButton("Run");
#endif
        m_isCrouching = CrossPlatformInputManager.GetButton("Crouch");

        // Set settings if crouching
        if (m_isCrouching) {
            speed = m_CrouchSpeed;
            if (!m_PreviouslyCrouching) {
                m_CharacterController.height = m_CrouchCharacterHeight;
            }
        }
        // If not crouching, set the desired speed to be walking or running
        else {
            speed = m_IsWalking ? m_WalkSpeed : m_RunSpeed;
            if (m_PreviouslyCrouching) {
                m_CharacterController.height = 1.8f;
            }
        }

        //                     W          A             S           D
        m_Input = new bool[] { vertical > 0, horizontal < 0, vertical < 0, horizontal > 0 };

        // handle speed change to give an fov kick
        // only if the player is going to a run, is running and the fovkick is to be used
        if (m_IsWalking != waswalking && m_UseFovKick && m_CharacterController.velocity.sqrMagnitude > 0) {
            StopAllCoroutines();
            StartCoroutine(!m_IsWalking ? m_FovKick.FOVKickUp() : m_FovKick.FOVKickDown());
        }

        m_PreviouslyCrouching = m_isCrouching;
    }

    /// <summary>
    /// This rotates the player view based on mouse movement.
    /// </summary>
    [Client]
    private void RotateView() {
        m_MouseLook.LookRotation(transform, m_Camera.transform);
    }

    /// <summary>
    /// Called when the controller hits a collider while performing a Move.
    /// </summary>
    /// <param name="hit"></param>
    //Shared
    private void OnControllerColliderHit(ControllerColliderHit hit) {
        Rigidbody body = hit.collider.attachedRigidbody;
        //dont move the rigidbody if the character is on top of it
        if (m_CollisionFlags == CollisionFlags.Below) {
            return;
        }

        if (body == null || body.isKinematic) {
            return;
        }
        body.AddForceAtPosition(m_CharacterController.velocity * 0.1f, hit.point, ForceMode.Impulse);
    }

    /// <summary>
    /// Checks if any vertical movement has been applied.
    /// Converts the bool values to a single byte.
    /// </summary>
    /// <param name="W"> The bool that indicates that W is pressed. </param>
    /// <param name="S"> The bool that indicates that S is pressed. </param>
    /// <returns></returns>
    private sbyte VerticalMovement(bool W, bool S) {
        if (W && !S)
            return 1;
        if (S && !W)
            return -1;

        return 0;
    }

    /// <summary>
    /// Returns the player current speed movement,
    /// considering if it is crouching, walking or running.
    /// </summary>
    /// <returns></returns>
    private float ReturnSpeed() {
        if (m_isCrouching)
            return m_CrouchSpeed;
        if (m_IsWalking)
            return m_WalkSpeed;

        return m_RunSpeed;
    }

    /// <summary>
    /// Checks if any horizontal movement has been applied.
    /// Converts the bool values to a single byte.
    /// </summary>
    /// <param name="A"> The bool that indicates that A is pressed. </param>
    /// <param name="D"> The bool that indicates that D is pressed. </param>
    /// <returns></returns>
    private sbyte HorizontalMovement(bool A, bool D) {
        if (D && !A)
            return 1;
        if (A && !D)
            return -1;

        return 0;
    }


    /// <summary>
    /// Plays the footstep audio.
    /// </summary>
    [Client]
    private void PlayFootStepAudio() {
        if (!m_CharacterController.isGrounded) {
            return;
        }
        // Pick & play a random footstep sound from the array,
        // excluding sound at index 0
        int n = Random.Range(1, m_FootstepSounds.Length);
        m_AudioSource.clip = m_FootstepSounds[n];
        m_AudioSource.PlayOneShot(m_AudioSource.clip);
        // Move picked sound to index 0 so it's not picked next time
        m_FootstepSounds[n] = m_FootstepSounds[0];
        m_FootstepSounds[0] = m_AudioSource.clip;
    }

    /// <summary>
    /// Plays the jump sound.
    /// </summary>
    [Client]
    private void PlayJumpSound() {
        m_AudioSource.clip = m_JumpSound;
        m_AudioSource.Play();
    }

    /// <summary>
    /// Play the landing sound
    /// </summary>
    [Client]
    private void PlayLandingSound() {
        m_AudioSource.clip = m_LandSound;
        m_AudioSource.Play();
        m_NextStep = m_StepCycle + .5f;
    }
}
