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
    public Vector3 calculatedPosition;

    public double timeStamp;
}

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(AudioSource))]
//[NetworkSettings(channel = 0, sendInterval = 0.02f)]
[NetworkSettings(channel = 0, sendInterval = 0.5f)]
public class UNETFirstPersonController : NetworkBehaviour {
    private bool m_IsWalking;
    [SerializeField] private float m_WalkSpeed;
    [SerializeField] private float m_RunSpeed;
    [SerializeField] [Range(0f, 1f)]    private float m_RunstepLenghten;
    [SerializeField] private float m_CrouchSpeed = 3f;
    [SerializeField] private float m_CrouchHeightDelta = 0.6f;
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
    // The delta center of hitbox when crouched
    private Vector3 m_CrouchedHitboxCenterDelta;
    // The delta of the camera's position when crouched
    private Vector3 m_CameraCrouchPosDelta;

    // The square root of two.
    private const float sqrt2 = 1.414213562373f;
    [SerializeField]
    [Tooltip("Player desacceleration factor (the lower, the faster it becomes 0)")]
    private const float m_SlowdownFactor = 0.6f;
    [SerializeField]
    [Tooltip("Player strafe factor (the higher, more control he has while on air)")]
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
    private double currentStamp = 0;

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
    private double lastMassageTime = 0;
    private const double maxDelayBeforeServerSimStop = 10000;

    //Local reconciliation (authority player)
    private struct ReconciliationEntry {
        public Inputs inputs;
        public Vector3 position;
        public float rotationYaw;
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
    }

    //Network initialization
    public void Awake() {
        NetworkServer.RegisterHandler(InputListMessage.MSGID, ServerProcessInput);
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

        m_CrouchedHitboxCenterDelta = new Vector3(0f, m_CrouchHeightDelta * 0.5f, 0f);
        m_CameraCrouchPosDelta = new Vector3(0f, m_CrouchHeightDelta * 1.5f, 0f);
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
            //This must be before move to check if move grounded the character
            m_PreviouslyGrounded = m_CharacterController.isGrounded;

            double timestamp = Network.time;
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
            //We need to store this here because player movement will clear m_Jump
            bool sendJump = m_Jump;

            // Store transform values
            //This is also used for the host to know if it moved to send change messages
            Vector3 prevPosition = transform.position;
            Quaternion prevRotation = transform.rotation;

            // Store collision values
            CollisionFlags lastFlag = m_CollisionFlags;

            String pre = "", post = "";

            //If we have predicion, we use the input here to move the character
            if (prediction || isServer) {
                if (isClient && !isServer) {
                    pre = "\n[" + timestamp + "] Client state (pre movement) :\n" + getState();
                }
                //Move the player object
                PlayerMovement(speed);
                if (isClient && !isServer) {
                    post = "\n[" + timestamp + "] Client state (post movement) :\n" + getState();
                }
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
                bool moved = Vector3.Distance(prevPosition, transform.position) > 0 || m_Input[0] || m_Input[1]
                    || m_Input[2] || m_Input[3];
                if (moved || sendJump || crouchChange || rotationChanged) {
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
                    inputs.calculatedPosition = transform.position;
                    inputs.timeStamp = timestamp;
                    inputsList.Enqueue(inputs);
                    debugMovement dePos = new debugMovement();

                    if (isClient) {
                        FileDebug.Log(pre, "ClientLog");
                        FileDebug.Log(post, "ClientLog");
                    }

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
                        entry.rotationYaw = transform.rotation.eulerAngles.y;
                        entry.grounded = m_PreviouslyGrounded;
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
                    if (inputsList.Count > 0) {
                        InputListMessage messageToSend = new InputListMessage();
                        messageToSend.inputsList = new List<Inputs>(inputsList);
                        connectionToServer.Send(InputListMessage.MSGID, messageToSend);

                        //Clear the input list
                        inputsList.Clear();
                    }
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

                //Store state
                Vector3 lastPosition = transform.position;
                Quaternion lastCharacterRotation = transform.rotation;
                Quaternion lastCameraRotation = m_firstPersonCharacter.rotation;

                //Create the struct to read possible input to calculate
                Inputs inputs;
                inputs.rotate = false;

                //If we have inputs, get them and simulate on the server
                if (inputsList.Count > 0) {
                    inputs = inputsList.Dequeue();

                    m_IsWalking = inputs.walk;
                    m_Input = inputs.wasd;
                    m_isCrouching = inputs.crouch;
                    m_Jump = inputs.jump;
                    currentStamp = inputs.timeStamp;

                    //If need to, apply rotation
                    if (inputs.rotate) {
                        transform.rotation = Quaternion.Euler(transform.rotation.x, inputs.yaw, transform.rotation.z);
                        m_firstPersonCharacter.rotation = Quaternion.Euler(inputs.pitch, m_firstPersonCharacter.rotation.eulerAngles.y, m_firstPersonCharacter.rotation.eulerAngles.z);
                    }
                    
                    //If need to, simulate movement
                    if (inputs.move) { 
                        CalcSpeed(out speed); //Server-side method to the speed out of input from clients

                        //Debug state
                        FileDebug.Log("\n[" + currentStamp + "] Server state (pre movement):\n" + getState(), "ServerLog");

                        //Move the player object
                        PlayerMovement(speed);

                        //Debug state
                        FileDebug.Log("\n[" + currentStamp + "] Server state (post movement):\n" + getState(), "ServerLog");
                    }

                    //Position acceptance
                    //TO-DO this is hardcoded and is a fix for a weird behavior. This is wrong.
                    /*if (Vector3.Distance(transform.position, inputs.calculatedPosition) < 0.4f) {
                        transform.position = inputs.calculatedPosition;
                    }*/
                }

                //If its time to send messages
                if (dataStep > GetNetworkSendInterval()) {
                    //If we have any changes in position or rotation, we send a messsage
                    if (Vector3.Distance(transform.position, lastPosition) > 0 || inputs.rotate) {
                        RpcClientReceivePosition(currentStamp, transform.position, m_MoveDir);
                        //Debug.Log("Sent client pos "+dataStep + ", stamp: " + currentReconciliationStamp);
                    }
                    dataStep = 0;
                   
                }
                dataStep += Time.fixedDeltaTime;
                m_PreviouslyGrounded = m_CharacterController.isGrounded;
            }
        }
    }
    //>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>FIXED UPDATE

    /// <summary>
    /// This is a debug method that gets all state variables that are involved in player movement.
    /// </summary>
    /// <returns>A string with all variables involved in player movement</returns>
    private string getState() {
        string state = "";
        state += "current position (" + transform.position.x + ", " + transform.position.y + ", " + transform.position.z + ")\n";
        state += "current rotation (" + transform.rotation.x + ", " + transform.rotation.y + ", " + transform.rotation.z + ", " + transform.rotation.w + ")\n";
        state += "m_IsWalking "+m_IsWalking + "\n";
        state += "m_RunSpeed " + m_RunSpeed + "\n";
        state += "m_CrouchSpeed " + m_CrouchSpeed + "\n";
        state += "m_CrouchHeightDelta " + m_CrouchHeightDelta + "\n";
        state += "m_JumpSpeed " + m_JumpSpeed + "\n";
        state += "m_StickToGroundForce " + m_StickToGroundForce + "\n";
        state += "m_GravityMultiplier " + m_GravityMultiplier + "\n";
        state += "m_firstPersonCharacter.rotation " + m_firstPersonCharacter.rotation + "\n";
        state += "m_CrouchedHitboxCenterDelta " + m_CrouchedHitboxCenterDelta + "\n";
        state += "m_CameraCrouchPosDelta " + m_CameraCrouchPosDelta + "\n";
        state += "m_SlowdownFactor " + m_SlowdownFactor + "\n";
        state += "m_StrafeSpeed " + m_StrafeSpeed + "\n";
        state += "m_Jump " + m_Jump + "\n";
        state += "m_isCrouching " + m_isCrouching + "\n";
        state += "m_PreviouslyCrouching " + m_PreviouslyCrouching + "\n";
        state += "m_Input " + String.Join(", ", m_Input.ToList<Boolean>().Select(p => p.ToString()).ToArray()) + "\n";
        state += "m_MoveDir " + m_MoveDir + "\n";
        state += "m_CollisionFlags " + m_CollisionFlags + "\n";
        state += "m_PreviouslyGrounded " + m_PreviouslyGrounded + "\n";
        state += "m_Jumping " + m_Jumping + "\n";
        state += "Physics.gravity " + Physics.gravity + "\n";
        state += "Time.fixedDeltaTime " + Time.fixedDeltaTime + "\n";
        return state;
    }

    /// <summary>
    /// Receives the movements and rotations input and process it. 
    /// </summary>
    [Server]
    private void ServerProcessInput(NetworkMessage netMsg) {
        //Get the msg
        InputListMessage msg = netMsg.ReadMessage<InputListMessage>();

        //Add all inputs
        msg.inputsList.ForEach(e => inputsList.Enqueue(e) );

        //Debug.Log("Stamp of last input: " + stamp);
        //Debug.Log("Current input list size: " + inputsList.Count);

        //Store the last received message
        lastMassageTime = msg.stamp;
    }

    /// <summary>
    /// This method processes the positions that the server sent to the client each update.
    /// </summary>
    /// <param name="pos">This is the position the server calculated for the input at that time.</param>
    /// <param name="inputStamp">This is the timestamp when the client sent this input originally.</param>
    [ClientRpc]
    private void RpcClientReceivePosition(double inputStamp, Vector3 pos, Vector3 movementVector) {
        if (reconciliation && isLocalPlayer) {// RECONCILIATION for owner players
            //Check if this stamp is in the list
            if (reconciliationList.Count == 0) {
                //Nothing to reconciliate, apply server position
                //Debug.Log(transform.position.ToString() + " : " + pos.ToString()); NUNCA REMOVER
                transform.position = pos;
            } else {
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

                float threshold = 0.005f;

                // Reapply all the inputs that aren't processed by the server yet.
                int count = 0;
                if (reconciliationList.Count > 0) {
                    //debugError += "The first position for reconciliation is: " + reconciliationList[0].position + "\n";
                    //Get the lastest collision flags
                    m_CollisionFlags = reconciliationList[0].lastFlags;

                    //We use the next stamp because we save state before move
                    //Debug
                    firstEntry = reconciliationList[0];
                    serverCalculationError = Vector3.Distance(firstEntry.position, pos);
                    //Debug.Log("SStamp: "+inputStamp+" CStamp: "+ clientForServerStamp.inputs.timeStamp);

                    float speed = 0f;
                    foreach (ReconciliationEntry e in reconciliationList) {
                        Inputs i = e.inputs;
                        m_Input = i.wasd;
                        m_IsWalking = i.walk;
                        m_isCrouching = i.crouch;
                        m_Jump = i.jump;
                        m_PreviouslyCrouching = e.prevCrouching;

                        CalcSpeed(out speed);

                        PlayerMovement(speed, e.grounded, e.position, e.rotationYaw);
                        debugError += "("+(count++)+")Intermediate rec position: " + transform.position+"\n";
                    }
                }

                debugError += "The final reconciliated position is: " + transform.position+"\n";
                debugError += "The predicted position was: " + predicted + "\n";

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
            transform.position = pos;
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
        PlayerMovement(speed, m_CharacterController.isGrounded, transform.position, transform.rotation.eulerAngles.y);
    }

    /// <summary>
    /// CLIENT-SIDE RECONCILIATION MOVEPLAYER
    /// Using a given input and gravity, moves the player object.
    /// </summary>
    private void PlayerMovement(float speed, bool grounded, Vector3 position, float rotationYaw) {
        //Builds the rotation
        Quaternion rotation = Quaternion.Euler(transform.rotation.x, rotationYaw, transform.rotation.z);

        //Calculate player local forward vector and right vector based on the rotation
        Vector3 right = rotation * Vector3.right;
        Vector3 forward = rotation * Vector3.forward;

        // Always move along the camera forward as it is the direction that it being aimed at
        Vector3 desiredMove = forward * VerticalMovement(m_Input[0], m_Input[2]) + right * HorizontalMovement(m_Input[1], m_Input[3]);
        // Normalizing diagonal movement
        if((m_Input[0] || m_Input[2]) && (m_Input[1] || m_Input[3])) {
            desiredMove.x /= sqrt2;
            desiredMove.z /= sqrt2;
        }
        //Calculate the side movement for strafing while in air
        Vector3 desiredStrafe = right * HorizontalMovement(m_Input[1], m_Input[3]);

        // Get a normal for the surface that is being touched to move along it
        /*RaycastHit hitInfo;
        Physics.SphereCast(position, m_CharacterController.radius, Vector3.down, out hitInfo,
                           m_CharacterController.height / 2f);
        desiredMove = Vector3.ProjectOnPlane(desiredMove, hitInfo.normal).normalized;*/

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

            //When going down things, we need to push the character down to avoid small jumps
            //10 is a good value
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
                Crouch();
            }
        }
        // If not crouching, set the desired speed to be walking or running
        else {
            speed = m_IsWalking ? m_WalkSpeed : m_RunSpeed;
            if (m_PreviouslyCrouching) {
                // If the player WAS crouching in the previous frame,
                // but is not crouching in the current, set his height to standard CharacterHeight
                Uncrouch();
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
                Crouch();
            }
        }
        // If not crouching, set the desired speed to be walking or running
        else {
            speed = m_IsWalking ? m_WalkSpeed : m_RunSpeed;
            if (m_PreviouslyCrouching) {
                Uncrouch();
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
                Crouch();
            }
        }
        // If not crouching, set the desired speed to be walking or running
        else {
            speed = m_IsWalking ? m_WalkSpeed : m_RunSpeed;
            if (m_PreviouslyCrouching) {
                Uncrouch();
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
    /// Sets the height and the center of the hitbox to be lower, to make the character crouch.
    /// </summary>
    private void Crouch() {
        m_CharacterController.height -= m_CrouchHeightDelta;
        m_CharacterController.center -= m_CrouchedHitboxCenterDelta;

        if(isLocalPlayer)
            m_Camera.transform.position -= m_CameraCrouchPosDelta;
    }

    /// <summary>
    /// Returns the height and the center of the hitbox to its default values.
    /// </summary>
    private void Uncrouch() {
        m_CharacterController.height += m_CrouchHeightDelta;
        m_CharacterController.center += m_CrouchedHitboxCenterDelta;

        if(isLocalPlayer)
            m_Camera.transform.position += m_CameraCrouchPosDelta;
    }

    /// <summary>
    /// Called when the controller hits a collider while performing a Move.
    /// </summary>
    /// <param name="hit"></param>
    //Shared
    /*private void OnControllerColliderHit(ControllerColliderHit hit) {
        Rigidbody body = hit.collider.attachedRigidbody;
        //dont move the rigidbody if the character is on top of it
        if (m_CollisionFlags == CollisionFlags.Below) {
            return;
        }

        if (body == null || body.isKinematic) {
            return;
        }
        body.AddForceAtPosition(m_CharacterController.velocity * 0.1f, hit.point, ForceMode.Impulse);
    }*/

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
