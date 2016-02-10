using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using System.Collections.Generic;

public class InputListMessage : MessageBase {

    public static short MSGID { get { return MsgType.Highest + 10; } }

    public List<Inputs> inputsList = null;
    public double stamp = 0;

    public override void Serialize(NetworkWriter writer) {
        writer.StartMessage(MSGID);
        //Send the number of elements
        writer.Write(inputsList.Count);//int32
        //Add the entire list
        foreach(Inputs i in inputsList) {
            writer.Write(i.crouch);//bool
            writer.Write(i.jump);//bool
            writer.Write(i.move);//bool
            writer.Write(i.pitch);//float
            writer.Write(i.rotate);//bool
            writer.Write(i.timeStamp);//long
            writer.Write(i.walk);//bool
            writer.Write(i.wasd[0]);//bool
            writer.Write(i.wasd[1]);//bool
            writer.Write(i.wasd[2]);//bool
            writer.Write(i.wasd[3]);//bool
            writer.Write(i.yaw);//float
        }
        writer.FinishMessage();
    }

    public override void Deserialize(NetworkReader reader) {
        //Get the no of inputs received
        int count = reader.ReadInt32();

        //Prepaare list
        if (inputsList != null) {
            inputsList.Clear(); //Detatch prev elements
        }
        inputsList = new List<Inputs>(); //Detatch prev mem

        //Build all inputs
        for (int i = 0; i < count; i++){
            //Create
            Inputs input = new Inputs();

            //Deserialize individually
            input.crouch = reader.ReadBoolean();
            input.jump = reader.ReadBoolean();
            input.move = reader.ReadBoolean();
            input.pitch = reader.ReadSingle();
            input.rotate = reader.ReadBoolean();
            input.timeStamp = reader.ReadDouble();
            input.walk = reader.ReadBoolean();
            bool[] wasd = new bool[4];
            wasd[0] = reader.ReadBoolean();
            wasd[1] = reader.ReadBoolean();
            wasd[2] = reader.ReadBoolean();
            wasd[3] = reader.ReadBoolean();
            input.wasd = wasd;
            input.yaw = reader.ReadSingle();

            //Add to the message list
            inputsList.Add(input);

            //Check if is the last input and register stamp
            if(i == count - 1) {
                stamp = input.timeStamp;
            }
        }
    }
}
