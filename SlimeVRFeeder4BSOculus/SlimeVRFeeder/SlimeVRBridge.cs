using Google.Protobuf;
using Messages;
using System;
using System.IO;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;
using UnityEngine.XR;

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

        private static SlimeVRBridge FeederInstance;

        public static SlimeVRBridge getFeederInstance()
        {
            if (FeederInstance == null)
                FeederInstance = new NamedPipeBridge("\\\\.\\pipe\\SlimeVRInput");
            return FeederInstance;
        }

        private static SlimeVRBridge DriverInstance;

        public abstract void start();

        public abstract void close();

        public abstract void connect();
        public abstract void reset();
    }

    sealed class NamedPipeBridge : SlimeVRBridge
    {
        enum SlimeVRTrackerIndex
        {
            HEAD = 0,
            LEFT_HAND = 1,
            RIGHT_HAND = 2,
        };

        [DllImport("kernel32.dll")]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, [Out] byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);
        [DllImport("kernel32.dll")]
        private static extern bool PeekNamedPipe(IntPtr hNamedPipe, [Out] byte[] lpBuffer, uint nBufferSize, out uint lpBytesRead, IntPtr lpTotalBytesAvail, IntPtr lpBytesLeftThisMessage);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr CreateFileA(
             [MarshalAs(UnmanagedType.LPStr)] string filename,
             [MarshalAs(UnmanagedType.U4)] FileAccess access,
             [MarshalAs(UnmanagedType.U4)] FileShare share,
             IntPtr securityAttributes,
             [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
             [MarshalAs(UnmanagedType.U4)] FileAttributes flagsAndAttributes,
             IntPtr templateFile);
        [DllImport("kernel32.dll", SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        [SuppressUnmanagedCodeSecurity]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr hObject);


        byte[] sendBuffer = new byte[4096];
        int sendBufferDataCount = 0;

        private string pipe_name;

        IntPtr pipe = IntPtr.Zero;
        private static IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        public NamedPipeBridge(string pipe_name)
        {
            this.pipe_name = pipe_name;
        }

        byte[] size_byte = new byte[4];
        byte[] received_message_buffer = new byte[256];
        public override ProtobufMessage getNextMessage()
        {
            uint read_bytes;

            if (pipe == IntPtr.Zero)
                return null;

            if (!PeekNamedPipe(pipe, size_byte, 4, out read_bytes, IntPtr.Zero, IntPtr.Zero))
                return null;
            if (read_bytes != 4)
                return null;

            int size = (size_byte[0]) | (size_byte[1] << 8) | (size_byte[2] << 16) | (size_byte[3] << 24);

            if(received_message_buffer.Length < size)
            {
                received_message_buffer = new byte[size];
            }

            if(size < 4 || size > 4096)
            {
                Plugin.Log?.Error($"Invalid data length from message({size}).");
                return null;
            }

            if (!ReadFile(pipe,received_message_buffer,(uint)size,out read_bytes, IntPtr.Zero))
            {
                return null;
            }
            if(read_bytes != size)
            {
                Plugin.Log?.Error($"can't read enough data from pipe, {read_bytes} read, {size} expected.");
                return null;
            }
            ProtobufMessage message = ProtobufMessage.Parser.ParseFrom(received_message_buffer, 4, size - 4);
            return message;
        }

        public override bool sendMessage(ProtobufMessage msg)
        {
            if (pipe == IntPtr.Zero) return false;
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

            if (pipe != IntPtr.Zero)
            {
                reset();
            }

            pipe = CreateFileA(pipe_name, FileAccess.ReadWrite, FileShare.None, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
            if (pipe == INVALID_HANDLE_VALUE)
            {
                pipe = IntPtr.Zero;
                //Plugin.Log?.Warn("slime-vr feeder pipe create failed.");
            }
            needAddTracker = true;
            needSendStatus = true;
        }

        public override void reset()
        {
            if (pipe != IntPtr.Zero)
            {
                CloseHandle(pipe);
                pipe = IntPtr.Zero;
            }
        }

        public override void close()
        {
            reset();
            closed = true;
        }

        public override bool flush()
        {
            try
            {
                uint _written = 0;

                WriteFile(pipe, sendBuffer, (uint)sendBufferDataCount, out _written,IntPtr.Zero);
                if(_written != sendBufferDataCount)
                {
                    Plugin.Log?.Error($"pipe sent {_written}(expected {sendBufferDataCount})");
                }
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
        bool closed = false;
        bool needAddTracker = false;
        bool needSendStatus = false;
        public override void start()
        {
            bool isOculusDevice = XRSettings.loadedDeviceName.IndexOf("oculus", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isOculusDevice)
            {
                Plugin.Log?.Info($"the game is not oculus mode, don't connect to slime vr server");
                return;
            }
            new System.Threading.Thread(() =>
            {
                while (true)
                {
                    connect();
                    while (!closed && UpdateFrame())
                        System.Threading.Thread.Sleep(10);
                    if (closed)
                        break;
                    System.Threading.Thread.Sleep(3000);
                }

            }).Start();
        }
        enum UserAction
        {
            RESET,
            FAST_RESET,
        }

        bool SendUserAction(UserAction action)
        {
            ProtobufMessage message = new ProtobufMessage();
            var act = message.UserAction;
            switch (action)
            {
                case UserAction.RESET:
                    act.Name = "reset";
                    break;
                case UserAction.FAST_RESET:
                    act.Name = "fast_reset";
                    break;
            }
            return sendMessage(message);
        }

        private bool UpdateFrame()
        {
            try
            {
                while (getNextMessage() != null)
                    continue;

                ProtobufMessage message = new ProtobufMessage();

                if (needAddTracker)
                {
                    var added = message.TrackerAdded = new TrackerAdded();
                    added.TrackerId = (int)SlimeVRTrackerIndex.HEAD;
                    added.TrackerRole = (int)SlimeVRBridge.SlimeVRPosition.Head;
                    added.TrackerName = SlimeVRBridge.PositionNames[(int)SlimeVRBridge.SlimeVRPosition.Head];
                    added.TrackerSerial = "quest_headset";
                    if (!sendMessage(message)) return false;

                    added.TrackerId = (int)SlimeVRTrackerIndex.LEFT_HAND;
                    added.TrackerRole = (int)SlimeVRBridge.SlimeVRPosition.LeftHand;
                    added.TrackerName = SlimeVRBridge.PositionNames[(int)SlimeVRBridge.SlimeVRPosition.LeftController];
                    added.TrackerSerial = "quest_left_hand";
                    if (!sendMessage(message)) return false;

                    added.TrackerId = (int)SlimeVRTrackerIndex.RIGHT_HAND;
                    added.TrackerRole = (int)SlimeVRBridge.SlimeVRPosition.RightHand;
                    added.TrackerName = SlimeVRBridge.PositionNames[(int)SlimeVRBridge.SlimeVRPosition.RightController];
                    added.TrackerSerial = "quest_right_hand";
                    if (!sendMessage(message)) return false;

                    needAddTracker = false;
                    message = new ProtobufMessage();
                }

                if (needSendStatus)
                {
                    //SetStatus only one time
                    var status = message.TrackerStatus = new TrackerStatus();
                    status.Status = TrackerStatus.Types.Status.Ok;
                    status.TrackerId = (int)SlimeVRTrackerIndex.HEAD;
                    if (!sendMessage(message)) return false;

                    status.TrackerId = (int)SlimeVRTrackerIndex.LEFT_HAND;
                    if (!sendMessage(message)) return false;
                    status.TrackerId = (int)SlimeVRTrackerIndex.RIGHT_HAND;
                    if (!sendMessage(message)) return false;

                    needSendStatus = false;
                    message = new ProtobufMessage();
                }

                message.Position = new Position();
                var headpos = OVRPlugin.GetNodePose(OVRPlugin.Node.Head, OVRPlugin.Step.Render).ToOVRPose();
                var lhandpos = OVRPlugin.GetNodePose(OVRPlugin.Node.HandLeft, OVRPlugin.Step.Render).ToOVRPose();
                var rhandpos = OVRPlugin.GetNodePose(OVRPlugin.Node.HandRight, OVRPlugin.Step.Render).ToOVRPose();
                bool SendPos(OVRPose pose, SlimeVRTrackerIndex index)
                {
                    var pos = message.Position;
                    pos.X = pose.position.x;
                    pos.Y = pose.position.y;
                    pos.Z = -pose.position.z;
                    pos.Qw = -pose.orientation.w;
                    pos.Qx = pose.orientation.x;
                    pos.Qy = pose.orientation.y;
                    pos.Qz = -pose.orientation.z;
                    pos.TrackerId = (int)index;
                    pos.DataSource = Position.Types.DataSource.Full;
                    return sendMessage(message);
                }
                if (!SendPos(headpos, SlimeVRTrackerIndex.HEAD)) return false;
                if (!SendPos(lhandpos, SlimeVRTrackerIndex.LEFT_HAND)) return false;
                if (!SendPos(rhandpos, SlimeVRTrackerIndex.RIGHT_HAND)) return false;
                if (!flush()) return false;
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show("This is a exception that should never triggered, the SlimeVRFeederPlugin should fix this bug:\n" + e.ToString());
                return false;
            }
        }
    }
}
