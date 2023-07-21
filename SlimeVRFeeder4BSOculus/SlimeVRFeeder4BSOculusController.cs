using IPA.Utilities;
using Messages;
using SlimeVRFeeder4BSOculus.SlimeVRFeeder;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using UnityEngine;
using UnityEngine.XR;

namespace SlimeVRFeeder4BSOculus
{
    /// <summary>
    /// Monobehaviours (scripts) are added to GameObjects.
    /// For a full list of Messages a Monobehaviour can receive from the game, see https://docs.unity3d.com/ScriptReference/MonoBehaviour.html.
    /// </summary>
    public class SlimeVRFeeder4BSOculusController : MonoBehaviour
    {
        enum SlimeVRTrackerIndex
        {
            HEAD = 0,
            LEFT_HAND = 1,
            RIGHT_HAND = 2,
        };
        enum UserAction
        {
            RESET,
            FAST_RESET,
        }

        public static SlimeVRFeeder4BSOculusController Instance { get; private set; }

        private bool isOculusDevice;

        // These methods are automatically called by Unity, you should remove any you aren't using.
        #region Monobehaviour Messages
        /// <summary>
        /// Only ever called once, mainly used to initialize variables.
        /// </summary>
        private void Awake()
        {
            // For this particular MonoBehaviour, we only want one instance to exist at any time, so store a reference to it in a static property
            //   and destroy any that are created while one already exists.
            if (Instance != null)
            {
                Plugin.Log?.Warn($"Instance of {GetType().Name} already exists, destroying.");
                GameObject.DestroyImmediate(this);
                return;
            }
            GameObject.DontDestroyOnLoad(this); // Don't destroy this object on scene changes
            Instance = this;
            Plugin.Log?.Debug($"{name}: Awake()");
        }

        

        /// <summary>
        /// Only ever called once on the first frame the script is Enabled. Start is called after any other script's Awake() and before Update().
        /// </summary>
        private void Start()
        {

            isOculusDevice = XRSettings.loadedDeviceName.IndexOf("oculus", StringComparison.OrdinalIgnoreCase) >= 0;
            if(!isOculusDevice)
            {
                Plugin.Log?.Info($"the game is not oculus mode, don't connect to slime vr server");

                enabled = false;
                return;
            }
        }


        private bool SendMessage(ProtobufMessage message)
        {
            bool succeed = SlimeVRBridge.getInstance().sendMessage(message);
            if (!succeed)
            {
                needAddTracker = true;
                needSendStatus = true;
                Plugin.Log?.Info($"Send Failed");
                if (needTryConnect < 0)
                    needTryConnect = 600;
            }
            return succeed;
        }

        int needTryConnect = 0;

        bool needAddTracker = true;
        bool needSendStatus = true;

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
            return SendMessage(message);
        }

        /// <summary>
        /// Called every frame if the script is enabled.
        /// </summary>
        private void Update()
        {
            if(needTryConnect == 0)
            {
                Plugin.Log?.Info($"connecting to slime vr driver");
                SlimeVRBridge.getInstance().connect();
                needTryConnect = -1;
            }else if(needTryConnect > 0)
            {
                needTryConnect--;
            }
            try
            {
                ProtobufMessage message = new ProtobufMessage();

                if (needAddTracker)
                {
                    Plugin.Log?.Info($"will add tracker");
                    var added = message.TrackerAdded = new TrackerAdded();
                    added.TrackerId = (int)SlimeVRTrackerIndex.HEAD;
                    added.TrackerRole = (int)SlimeVRBridge.SlimeVRPosition.Head;
                    added.TrackerName = SlimeVRBridge.PositionNames[(int)SlimeVRBridge.SlimeVRPosition.Head];
                    added.TrackerSerial = "quest_headset";
                    if (!SendMessage(message)) return;

                    added.TrackerId = (int)SlimeVRTrackerIndex.LEFT_HAND;
                    added.TrackerRole = (int)SlimeVRBridge.SlimeVRPosition.LeftHand;
                    added.TrackerName = SlimeVRBridge.PositionNames[(int)SlimeVRBridge.SlimeVRPosition.LeftController];
                    added.TrackerSerial = "quest_left_hand";
                    if (!SendMessage(message)) return;

                    added.TrackerId = (int)SlimeVRTrackerIndex.RIGHT_HAND;
                    added.TrackerRole = (int)SlimeVRBridge.SlimeVRPosition.RightHand;
                    added.TrackerName = SlimeVRBridge.PositionNames[(int)SlimeVRBridge.SlimeVRPosition.RightController];
                    added.TrackerSerial = "quest_right_hand";
                    if (!SendMessage(message)) return;

                    needAddTracker = false;
                    message = new ProtobufMessage();
                }

                if (needSendStatus)
                {
                    //SetStatus only one time
                    var status = message.TrackerStatus = new TrackerStatus();
                    status.Status = TrackerStatus.Types.Status.Ok;
                    status.TrackerId = (int)SlimeVRTrackerIndex.HEAD;
                    if (!SendMessage(message)) return;

                    status.TrackerId = (int)SlimeVRTrackerIndex.LEFT_HAND;
                    if (!SendMessage(message)) return;
                    status.TrackerId = (int)SlimeVRTrackerIndex.RIGHT_HAND;
                    if (!SendMessage(message)) return;

                    needSendStatus = false;
                    message = new ProtobufMessage();
                }

                message.Position = new Position();
                var headpos = OVRPlugin.GetNodePose(OVRPlugin.Node.Head, OVRPlugin.Step.Physics);
                var lhandpos = OVRPlugin.GetNodePose(OVRPlugin.Node.HandLeft, OVRPlugin.Step.Physics);
                var rhandpos = OVRPlugin.GetNodePose(OVRPlugin.Node.HandRight, OVRPlugin.Step.Physics);
                bool SendPos(OVRPlugin.Posef pose, SlimeVRTrackerIndex index)
                {
                    var pos = message.Position;
                    pos.X = pose.Position.x;
                    pos.Y = pose.Position.y;
                    pos.Z = pose.Position.z;
                    pos.Qw = pose.Orientation.w;
                    pos.Qx = pose.Orientation.x;
                    pos.Qy = pose.Orientation.y;
                    pos.Qz = pose.Orientation.z;
                    pos.TrackerId = (int)index;
                    pos.DataSource = Position.Types.DataSource.Full;
                    return SendMessage(message);
                }
                if (!SendPos(headpos, SlimeVRTrackerIndex.HEAD)) return;
                if (!SendPos(lhandpos, SlimeVRTrackerIndex.LEFT_HAND)) return;
                if (!SendPos(rhandpos, SlimeVRTrackerIndex.RIGHT_HAND)) return;
                if (!SlimeVRBridge.getInstance().flush()) return;

            }
            catch (Exception e)
            {
                MessageBox.Show("This is a exception that should never triggered, the SlimeVRFeederPlugin should fix this bug:\n" + e.ToString());
            }
        }

        /// <summary>
        /// Called when the script is being destroyed.
        /// </summary>
        private void OnDestroy()
        {
            SlimeVRBridge.getInstance().close();
            if (Instance == this)
                Instance = null; // This MonoBehaviour is being destroyed, so set the static instance property to null.

        }
        #endregion
    }
}
