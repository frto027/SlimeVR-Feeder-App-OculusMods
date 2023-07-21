using Google.Protobuf;
using Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using UnityEngine.Assertions;

namespace SlimeVRFeeder4BSOculus.SlimeVRFeeder
{


    abstract class SlimeVRBridge
    {
        public enum SlimeVRPosition
        {
            None = 0,
            Waist,
            LeftFoot,
            RightFoot,
            Chest,
            LeftKnee,
            RightKnee,
            LeftElbow,
            RightElbow,
            LeftShoulder,
            RightShoulder,
            LeftHand,
            RightHand,
            LeftController,
            RightController,
            Head,
            Neck,
            Camera,
            Keyboard,
            HMD,
            Beacon,
            GenericController
        }

        public static string[] PositionNames = new string[]{
            "None",
            "Waist",
            "LeftFoot",
            "RightFoot",
            "Chest",
            "LeftKnee",
            "RightKnee",
            "LeftElbow",
            "RightElbow",
            "LeftShoulder",
            "RightShoulder",
            "LeftHand",
            "RightHand",
            "LeftController",
            "RightController",
            "Head",
            "Neck",
            "Camera",
            "Keyboard",
            "HMD",
            "Beacon",
            "GenericController"
        };

        public abstract ProtobufMessage getNextMessage();
        public abstract bool sendMessage(ProtobufMessage msg);//the message WILL delay send for speed up.
        //WARNING: the data is buffered, you MUST call flush after add message via sendMessage.
        public abstract bool flush();

        private static SlimeVRBridge Instance;

        public static SlimeVRBridge getInstance()
        {
            if (Instance == null)
                Instance = new NamedPipeBridge();
            return Instance;
        }

        public abstract void close();

        public abstract void connect();
        public abstract void reset();
    }

    sealed class NamedPipeBridge : SlimeVRBridge
    {
        byte[] sendBuffer = new byte[4096];
        int sendBufferDataCount = 0;

        private readonly static string pipe_name = "SlimeVRInput";

        NamedPipeClientStream pipe = null;

        public NamedPipeBridge()
        {
        }

        public override ProtobufMessage getNextMessage()
        {
            // We don't read the pipe because the following fact:
            // 1. slime-vr server doesn't send anything
            // 2. read pipe will block the write, and C# don't provide peek api
            return null;
        }

        public override bool sendMessage(ProtobufMessage msg)
        {
            if (pipe == null || !pipe.IsConnected || !pipe.CanWrite) return false;
            var size = msg.CalculateSize() + 4;

            if (size + sendBufferDataCount >= sendBuffer.Length)
            {
                if(!flush())
                    return false;
            }

            sendBuffer[sendBufferDataCount] = (byte)(size & 0xFF);
            sendBuffer[sendBufferDataCount + 1] = (byte)((size >> 8) & 0xFF);
            sendBuffer[sendBufferDataCount + 2] = (byte)((size >> 16) & 0xFF);
            sendBuffer[sendBufferDataCount + 3] = (byte)((size >> 24) & 0xFF);

            msg.WriteTo(new MemoryStream(sendBuffer, sendBufferDataCount + 4, size -  4));

            sendBufferDataCount += size;
            return true;
        }

        public override void connect()
        {
            try
            {
                pipe = new NamedPipeClientStream(pipe_name);
                pipe.Connect();
                Assert.IsTrue(pipe.IsConnected);
            }catch (Exception e)
            {
                MessageBox.Show(e.ToString());
                pipe = null;
            }
        }

        public override void reset()
        {
            if (pipe != null)
            {
                pipe.Close();
                pipe = null;
            }
        }

        public override void close()
        {
            reset();
        }

        public override bool flush()
        {
            try
            {
                pipe.Write(sendBuffer, 0, sendBufferDataCount);
                pipe.Flush();
                sendBufferDataCount = 0;
                return true;
            }
            catch (Exception e)
            {
                //unable to send, we will clear all data.
                MessageBox.Show(e.ToString());
                sendBufferDataCount = 0;
                return false;
            }
        }
    }
}
