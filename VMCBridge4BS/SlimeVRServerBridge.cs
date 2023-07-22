using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;

namespace VMCBridge4BS
{
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
        enum SlimeVRTrackerIndex
        {
            HEAD = 0,
            LEFT_HAND = 1,
            RIGHT_HAND = 2,
        };



        abstract class SlimeVRServerBridge
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

            private static SlimeVRServerBridge VMCInstance;

            public static SlimeVRServerBridge getVMCInstance()
            {
                if (VMCInstance == null)
                    VMCInstance = new NamedPipeServerBridge("\\\\.\\pipe\\VMCVRInput");
                return VMCInstance;
            }

            public abstract void start();
            public abstract void close();

            public abstract void create();
            public abstract void connect();

            public abstract bool is_connected();
            public abstract void reset();
        }

        sealed class NamedPipeServerBridge : SlimeVRServerBridge
        {
            [DllImport("kernel32.dll")]
            private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool ReadFile(IntPtr hFile, [Out] byte[] lpBuffer, uint nNumberOfBytesToRead, IntPtr lpNumberOfBytesRead, [In] ref System.Threading.NativeOverlapped lpOverlapped);
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
            private static extern bool CloseHandle(IntPtr hObject);


            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern IntPtr CreateNamedPipe(string lpName, uint dwOpenMode,
               uint dwPipeMode, uint nMaxInstances, uint nOutBufferSize, uint nInBufferSize,
               uint nDefaultTimeOut, IntPtr lpSecurityAttributes);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool DisconnectNamedPipe(IntPtr hNamedPipe);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool ConnectNamedPipe(IntPtr hNamedPipe,
               [In] ref System.Threading.NativeOverlapped lpOverlapped);
            [DllImport("kernel32.dll", SetLastError = true)]
            static extern bool GetOverlappedResult(IntPtr hFile,
               [In] ref System.Threading.NativeOverlapped lpOverlapped,
               out uint lpNumberOfBytesTransferred, bool bWait);

            [DllImport("kernel32.dll")]
            static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

            byte[] sendBuffer = new byte[4096];
            int sendBufferDataCount = 0;

            private string pipe_name;

            IntPtr pipe = IntPtr.Zero;
            private bool pipe_is_connected = false;
            private static IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

            private bool started = false;
            private bool closed = false;
            public NamedPipeServerBridge(string pipe_name)
            {
                this.pipe_name = pipe_name;
            }

            public override void start()
            {
                if (started)
                    return;
                started = true;
                var isOculusDevice = XRSettings.loadedDeviceName.IndexOf("oculus", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isOculusDevice)
                {
                    Plugin.Log?.Info($"the game is not oculus mode, don't connect to slime vr server");
                    return;
                }

                new System.Threading.Thread(() => {
                    while (!closed)
                    {
                        if (pipe_open_failed)
                            return;
                        connect();
                        needAddTracker = true;
                        while (!closed && UpdateFrame())
                        {
                            System.Threading.Thread.Sleep(10);
                        }
                        reset();
                        if (closed)
                            return;
                        System.Threading.Thread.Sleep(3000);
                    }
                }).Start();
            }

            bool needAddTracker = false;
            bool UpdateFrame()
            {
                var BridgeInstance = this;
                if (!BridgeInstance.is_connected())
                    return false;
                if (BridgeInstance.getNextMessage() != null)
                {
                    while (BridgeInstance.getNextMessage() != null)
                        continue;

                    var message = new ProtobufMessage();

                    if (needAddTracker)
                    {
                        var added = message.TrackerAdded = new TrackerAdded();
                        added.TrackerId = (int)SlimeVRTrackerIndex.HEAD;
                        added.TrackerRole = (int)SlimeVRServerBridge.SlimeVRPosition.HMD;
                        added.TrackerName = SlimeVRServerBridge.PositionNames[(int)SlimeVRServerBridge.SlimeVRPosition.HMD];
                        added.TrackerSerial = "quest_headset";
                        if (!sendMessage(message)) return false;

                        added.TrackerId = (int)SlimeVRTrackerIndex.LEFT_HAND;
                        added.TrackerRole = (int)SlimeVRServerBridge.SlimeVRPosition.LeftController;
                        added.TrackerName = SlimeVRServerBridge.PositionNames[(int)SlimeVRServerBridge.SlimeVRPosition.LeftController];
                        added.TrackerSerial = "quest_left_hand";
                        if (!sendMessage(message)) return false;

                        added.TrackerId = (int)SlimeVRTrackerIndex.RIGHT_HAND;
                        added.TrackerRole = (int)SlimeVRServerBridge.SlimeVRPosition.RightController;
                        added.TrackerName = SlimeVRServerBridge.PositionNames[(int)SlimeVRServerBridge.SlimeVRPosition.RightController];
                        added.TrackerSerial = "quest_right_hand";
                        if (!sendMessage(message)) return false;

                        needAddTracker = false;
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
                        pos.Z = pose.position.z;
                        pos.Qw = pose.orientation.w;
                        pos.Qx = pose.orientation.x;
                        pos.Qy = pose.orientation.y;
                        pos.Qz = pose.orientation.z;
                        pos.TrackerId = (int)index;
                        pos.DataSource = Position.Types.DataSource.Full;
                        return sendMessage(message);
                    }
                    if (!SendPos(headpos, SlimeVRTrackerIndex.HEAD)) return false;
                    if (!SendPos(lhandpos, SlimeVRTrackerIndex.LEFT_HAND)) return false;
                    if (!SendPos(rhandpos, SlimeVRTrackerIndex.RIGHT_HAND)) return false;
                    if (!BridgeInstance.flush()) return false;
                }
                return true;
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

                if (received_message_buffer.Length < size)
                {
                    received_message_buffer = new byte[size];
                }

                if (size < 4 || size > 4096)
                {
                    Plugin.Log?.Error($"Invalid data length from message({size}).");
                    return null;
                }

                System.Threading.NativeOverlapped nativeOverlapped = new System.Threading.NativeOverlapped()
                {
                    EventHandle = openEvent,
                };

                if (!ReadFile(pipe, received_message_buffer, (uint)size, IntPtr.Zero, ref nativeOverlapped))
                {
                    if(Marshal.GetLastWin32Error() != 997 /*ERROR_IO_PENDING*/ )
                        return null;
                }

                if (!GetOverlappedResult(pipe, ref nativeOverlapped, out read_bytes, true))
                    return null;

                if (read_bytes != size)
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
                    if (!flush())
                        return false;
                }

                sendBuffer[sendBufferDataCount] = (byte)(size & 0xFF);
                sendBuffer[sendBufferDataCount + 1] = (byte)((size >> 8) & 0xFF);
                sendBuffer[sendBufferDataCount + 2] = (byte)((size >> 16) & 0xFF);
                sendBuffer[sendBufferDataCount + 3] = (byte)((size >> 24) & 0xFF);

                msg.WriteTo(new MemoryStream(sendBuffer, sendBufferDataCount + 4, size - 4));

                sendBufferDataCount += size;
                return true;
            }

            public override void create()
            {

                if (pipe != IntPtr.Zero)
                {
                    reset();
                }

                pipe = CreateNamedPipe(pipe_name,
                    3 /*PIPE_ACCESS_DUPLEX*/ | 0x40000000 /*FILE_FLAG_OVERLAPPED*/,
                    0x00000000 /*PIPE_TYPE_BYTE*/| 0x00000000 /*PIPE_READMODE_BYTE */ | 0x00000000 /* PIPE_WAIT  */,
                    1,
                    1024 * 16,
                    1024 * 16,
                    0,
                    IntPtr.Zero
                    );
                if (pipe == INVALID_HANDLE_VALUE)
                {
                    pipe = IntPtr.Zero;
                    Plugin.Log?.Warn("vmc communicate pipe create failed.");
                }
            }

            public override void reset()
            {
                if (pipe != IntPtr.Zero)
                {
                    DisconnectNamedPipe(pipe);
                    pipe = IntPtr.Zero;
                }
                pipe_is_connected = false;
            }

            public override void close()
            {
                closed = true;
                reset();
            }

            public override bool flush()
            {
                try
                {
                    uint _written = 0;

                    WriteFile(pipe, sendBuffer, (uint)sendBufferDataCount, out _written, IntPtr.Zero);
                    if (_written != sendBufferDataCount)
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

            public override bool is_connected()
            {
                return pipe_is_connected;
            }
            IntPtr openEvent = CreateEvent(IntPtr.Zero, false, false, null);
            bool pipe_open_failed = false;
            public override void connect()
            {
                if (pipe == IntPtr.Zero)
                    create();
                if (pipe == IntPtr.Zero)
                    return;
                System.Threading.NativeOverlapped nativeOverlapped = new System.Threading.NativeOverlapped()
                {
                    EventHandle = openEvent,
                };
                var connected = ConnectNamedPipe(pipe, ref nativeOverlapped);
                var err = Marshal.GetLastWin32Error();
                if(!connected && err != 535 /* ERROR_PIPE_CONNECTED */)
                {
                    if(err != 997 /*ERROR_IO_PENDING*/)
                    {
                        pipe_open_failed = true;
                        MessageBox.Show("pipe connect failed, please restart the game to use VMC ");
                        return;
                    }
                    uint read;
                    if(!GetOverlappedResult(pipe, ref nativeOverlapped, out read, true))
                    {
                        pipe_open_failed = true;
                        MessageBox.Show("pipe connect failed(2), please restart the game to use VMC ");
                        return;
                    }
                }
                pipe_is_connected = true;
            }
        }
    }

}
