using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using PolytoriaCamera = Polytoria.Datamodel.Camera;
using PolytoriaPlayer = Polytoria.Datamodel.Player;
using PolytoriaGame = Polytoria.Datamodel.Game;
using PolytoriaNetworkEvent = Polytoria.Datamodel.NetworkEvent;
using PolytoriaNetMessage = Polytoria.Datamodel.NetMessage;

namespace PolytoriaVR
{
    public class VRManager : MonoBehaviour
    {
        private bool initializing;
        private Camera vrCamera;
        private Transform originalParent;
        private int framesToWait = 120;
        private bool renderLoopRunning;

        private Vector3 latestHmdPos;
        private Quaternion latestHmdRot = Quaternion.identity;

        private RenderTexture leftEyeRT;
        private RenderTexture rightEyeRT;

        private PolytoriaCamera polytoriaCamera;
        private PolytoriaPlayer localPlayer;
        private float smoothTurnYaw;
        private const float BodyTurnThreshold = 45f;
        private const float BodyTurnSpeed = 6f;
        internal static float SmoothTurnSpeed = 120f;
        private const float JoystickDeadzone = 0.15f;
        private const float DefaultMoveSpeed = 16f;

        internal static float VRScale = 1f;

        private static float VRWorldScale => 6f * VRScale;

        private Rigidbody flyRb;
        private bool flyInitialized;

        private float savedMaxDistance = 50f;
        private bool savedCameraState;

        private GameObject leftHand;
        private GameObject rightHand;
        private int leftControllerIdx = -1;
        private int rightControllerIdx = -1;
        private float controllerSearchTimer;

        private PolytoriaNetworkEvent vrHandSyncEvent;
        private bool networkEventSearched;
        private float networkSyncTimer;
        private const float NetworkSyncInterval = 0.011f;
        private float hideOwnHandsTimer;
        private string localPlayerName;
        private static float HandNetworkScale => 6f * VRScale;

        private float leftGripStrength;
        private float rightGripStrength;
        private float leftTriggerStrength;
        private float rightTriggerStrength;

        private float[] leftFingerCurls = new float[5];
        private float[] rightFingerCurls = new float[5];
        private bool hasLeftFingerData;
        private bool hasRightFingerData;

        internal static volatile bool LocalHandsVisible = true;

        internal static volatile bool FlyEnabled = true;
        internal static volatile float FlySpeedMultiplier = 1f;

        private float handScaleTimer;

        void Update()
        {
            if (framesToWait > 0) { framesToWait--; return; }

            if (Plugin.VRActivationRequested && !Plugin.VRActive && !initializing)
            {
                Plugin.VRActivationRequested = false;
                initializing = true;
                Plugin.LastStatus = "Initializing OpenVR...";
                StartCoroutine(InitializeVR().WrapToIl2Cpp());
            }

            if (Plugin.VRDeactivationRequested && Plugin.VRActive)
            {
                Plugin.VRDeactivationRequested = false;
                DisableVR();
            }
        }

        void LateUpdate()
        {
            if (Plugin.VRActive && vrCamera != null)
            {
                ApplyTracking();
            }
        }

        private IEnumerator InitializeVR()
        {
            Plugin.Log.LogInfo("[VR] OpenVR initializing..");
            bool initOK = false;
            try { initOK = OpenVR.Init(); }
            catch (Exception e) { Plugin.Log.LogError($"[VR] Init: {e.Message}"); }

            if (!initOK)
            {
                Plugin.LastStatus = "Failed: OpenVR init error (SteamVR might not be running)";
                initializing = false;
                yield break;
            }

            yield return null;

            Plugin.Log.LogInfo("[VR] Setting up camera..");
            SetupCamera();

            yield return null;

            Plugin.VRActive = true;
            Plugin.LastStatus = "Active";

            SetupHands();

            try
            {
                string pluginDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string manifestPath = System.IO.Path.Combine(pluginDir, "vr_actions.json");
                Plugin.Log.LogInfo($"[VR] Initializing VR input..");
                if (System.IO.File.Exists(manifestPath))
                    OpenVR.InitInput(manifestPath);
                else
                    Plugin.Log.LogWarning($"[VR] vr_actions.json not found at {manifestPath} - VR input won't work!");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[VR] VR input init error: {e.Message}");
            }

            if (OpenVR.CanSubmitFrames && vrCamera != null)
            {
                renderLoopRunning = true;
                StartCoroutine(EndOfFrameLoop().WrapToIl2Cpp());
                Plugin.LastStatus = "Active";
                Plugin.Log.LogInfo("[VR] VR load successful!");
            }
            else
            {
                Plugin.LastStatus = "VR Active, tracking only";
                Plugin.Log.LogWarning("[VR] Submit not available, tracking only mode!");
            }

            initializing = false;
        }

        private void SetupCamera()
        {
            var mainCam = Camera.main;
            if (mainCam == null) mainCam = FindObjectOfType<Camera>();
            if (mainCam == null)
            {
                Plugin.Log.LogWarning("[VR] No camera found!");
                return;
            }

            vrCamera = mainCam;
            originalParent = mainCam.transform.parent;

            mainCam.nearClipPlane = 0.01f;

            uint w = OpenVR.RenderWidth;
            uint h = OpenVR.RenderHeight;
            if (w == 0 || h == 0) { w = 1440; h = 1600; }

            leftEyeRT = new RenderTexture((int)w, (int)h, 24);
            leftEyeRT.antiAliasing = 1;
            leftEyeRT.Create();

            rightEyeRT = new RenderTexture((int)w, (int)h, 24);
            rightEyeRT.antiAliasing = 1;
            rightEyeRT.Create();

            Plugin.Log.LogInfo($"[VR] Render targets created, resolution is: {w}x{h}");
        }

        private void SetupHands()
        {
            try
            {
                leftHand = GameObject.CreatePrimitive(PrimitiveType.Cube);
                leftHand.name = "VR_LeftHand";
                leftHand.transform.localScale = new Vector3(0.15f * VRWorldScale, 0.15f * VRWorldScale, 0.4f * VRWorldScale);
                leftHand.GetComponent<Collider>().enabled = false;
                leftHand.SetActive(false);

                rightHand = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rightHand.name = "VR_RightHand";
                rightHand.transform.localScale = new Vector3(0.15f * VRWorldScale, 0.15f * VRWorldScale, 0.4f * VRWorldScale);
                rightHand.GetComponent<Collider>().enabled = false;
                rightHand.SetActive(false);

                Plugin.Log.LogInfo("[VR] Local hands created");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[VR] SetupHands error: {e.Message}");
            }
        }

        private IEnumerator EndOfFrameLoop()
        {
            var waitEndOfFrame = new WaitForEndOfFrame();

            while (Plugin.VRActive && renderLoopRunning)
            {
                yield return waitEndOfFrame;

                if (!Plugin.VRActive || vrCamera == null) break;

                try
                {
                    var origPosition = vrCamera.transform.position;
                    var origRotation = vrCamera.transform.rotation;
                    var origTargetTexture = vrCamera.targetTexture;

                    var hmdRot = GetEffectiveRotation();
                    var headPosition = origPosition;

                    float nearClip = vrCamera.nearClipPlane;
                    float farClip = vrCamera.farClipPlane;

                    {
                        OpenVR.GetEyeToHeadTransform(0, out Vector3 eyePos, out _);
                        vrCamera.transform.position = headPosition + hmdRot * (eyePos * VRWorldScale);
                        vrCamera.transform.rotation = hmdRot;
                        vrCamera.projectionMatrix = OpenVR.GetProjectionMatrix(0, nearClip, farClip);
                        vrCamera.targetTexture = leftEyeRT;
                        vrCamera.Render();
                    }

                    {
                        OpenVR.GetEyeToHeadTransform(1, out Vector3 eyePos, out _);
                        vrCamera.transform.position = headPosition + hmdRot * (eyePos * VRWorldScale);
                        vrCamera.transform.rotation = hmdRot;
                        vrCamera.projectionMatrix = OpenVR.GetProjectionMatrix(1, nearClip, farClip);
                        vrCamera.targetTexture = rightEyeRT;
                        vrCamera.Render();
                    }

                    vrCamera.transform.position = origPosition;
                    vrCamera.transform.rotation = origRotation;
                    vrCamera.targetTexture = origTargetTexture;
                    vrCamera.ResetProjectionMatrix();

                    int errL = OpenVR.SubmitFrame(0, leftEyeRT.GetNativeTexturePtr());
                    int errR = OpenVR.SubmitFrame(1, rightEyeRT.GetNativeTexturePtr());

                    if (errL != 0 || errR != 0)
                        Plugin.Log.LogWarning($"[VR] Submit errors: L={errL} R={errR}");

                    OpenVR.PostPresentHandoff();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[VR] Render loop error: {e.Message}");
                }
            }

            renderLoopRunning = false;
            Plugin.Log.LogInfo("[VR] Render loop stopped.");
        }

        private Quaternion GetEffectiveRotation()
        {
            return Quaternion.Euler(0f, smoothTurnYaw, 0f) * latestHmdRot;
        }

        private void ApplyTracking()
        {
            if (!OpenVR.IsInitialized) return;

            if (OpenVR.WaitGetPosesAndGetHMD(out Vector3 hmdPos, out Quaternion hmdRot))
            {
                latestHmdPos = hmdPos;
                latestHmdRot = hmdRot;
            }

            OpenVR.UpdateInput();
            ForceFirstPerson();
            if (localPlayer == null) FindLocalPlayer();
            HandleSmoothTurn();
            ApplyVRToGameCamera();
            SyncPlayerRotation();
            HandleMovement();
            UpdateHands();
            UpdateGripStrength();
            UpdateFingerTracking();
            SyncHandsToNetwork();
            HideOwnNetworkHands();
        }

        private void ForceFirstPerson()
        {
            try
            {
                if (polytoriaCamera == null)
                {
                    polytoriaCamera = PolytoriaCamera.Instance;
                    if (polytoriaCamera == null) return;

                    if (!savedCameraState)
                    {
                        savedMaxDistance = polytoriaCamera.MaxDistance;
                        savedCameraState = true;
                        Plugin.Log.LogInfo($"[VR] Found Polytoria Camera, saved MaxDistance={savedMaxDistance}");
                    }
                }

                polytoriaCamera.MinDistance = 0f;
                polytoriaCamera.MaxDistance = 0f;
                polytoriaCamera.Distance = 0f;

                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            catch { }
        }

        private unsafe void ApplyVRToGameCamera()
        {
            try
            {
                if (polytoriaCamera == null) return;

                var effectiveRot = GetEffectiveRotation();
                var euler = effectiveRot.eulerAngles;
                float yaw = euler.y;
                float pitch = euler.x;
                if (pitch > 180f) pitch -= 360f;

                var ptr = polytoriaCamera.Pointer;
                *(float*)(ptr + 0x258) = yaw;
                *(float*)(ptr + 0x25C) = pitch;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[VR] ApplyVRToGameCamera error: {e.Message}");
            }
        }

        private void SyncPlayerRotation()
        {
            try
            {
                if (localPlayer == null) return;

                float effectiveYaw = GetEffectiveRotation().eulerAngles.y;
                localPlayer.Rotation = new Vector3(0f, effectiveYaw, 0f);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[VR] SyncPlayerRotation error: {e.Message}");
            }
        }

        private void FindLocalPlayer()
        {
            try
            {
                var players = UnityEngine.Object.FindObjectsOfType<PolytoriaPlayer>();
                if (players == null) return;

                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (p != null && p.IsLocalPlayer)
                    {
                        localPlayer = p;
                        Plugin.Log.LogInfo("[VR] Found local player for rotation sync");
                        return;
                    }
                }
            }
            catch { }
        }

        private void UpdateHands()
        {
            try
            {
                if (leftHand == null || rightHand == null) return;

                controllerSearchTimer -= Time.deltaTime;
                if (controllerSearchTimer <= 0f)
                {
                    bool hadControllers = leftControllerIdx >= 0 || rightControllerIdx >= 0;
                    OpenVR.FindControllerIndices(out leftControllerIdx, out rightControllerIdx);
                    controllerSearchTimer = (leftControllerIdx >= 0 || rightControllerIdx >= 0) ? 5f : 0.5f;

                }

                var headWorldPos = vrCamera != null ? vrCamera.transform.position : Vector3.zero;
                var playSpaceRot = Quaternion.Euler(0f, smoothTurnYaw, 0f);

                handScaleTimer -= Time.deltaTime;
                if (handScaleTimer <= 0f)
                {
                    handScaleTimer = 1f;
                    var handScale = new Vector3(0.15f * VRWorldScale, 0.15f * VRWorldScale, 0.4f * VRWorldScale);
                    leftHand.transform.localScale = handScale;
                    rightHand.transform.localScale = handScale;
                }

                var leftRenderer = leftHand.GetComponent<Renderer>();
                var rightRenderer = rightHand.GetComponent<Renderer>();
                if (leftRenderer != null) leftRenderer.enabled = LocalHandsVisible;
                if (rightRenderer != null) rightRenderer.enabled = LocalHandsVisible;

                if (leftControllerIdx >= 0 && OpenVR.GetTrackedDevicePose(leftControllerIdx, out Vector3 lPos, out Quaternion lRot))
                {
                    leftHand.SetActive(true);
                    leftHand.transform.position = headWorldPos + playSpaceRot * ((lPos - latestHmdPos) * VRWorldScale);
                    leftHand.transform.rotation = playSpaceRot * lRot;
                }
                else
                {
                    leftHand.SetActive(false);
                }

                if (rightControllerIdx >= 0 && OpenVR.GetTrackedDevicePose(rightControllerIdx, out Vector3 rPos, out Quaternion rRot))
                {
                    rightHand.SetActive(true);
                    rightHand.transform.position = headWorldPos + playSpaceRot * ((rPos - latestHmdPos) * VRWorldScale);
                    rightHand.transform.rotation = playSpaceRot * rRot;
                }
                else
                {
                    rightHand.SetActive(false);
                }

            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[VR] UpdateHands error: {e.Message}");
            }
        }

        private void UpdateGripStrength()
        {
            if (!OpenVR.InputInitialized) return;
            try
            {
                leftGripStrength = OpenVR.GetGripStrength(true);
                rightGripStrength = OpenVR.GetGripStrength(false);
                leftTriggerStrength = OpenVR.GetTriggerStrength(true);
                rightTriggerStrength = OpenVR.GetTriggerStrength(false);
            }
            catch { }
        }

        private void UpdateFingerTracking()
        {
            if (!OpenVR.InputInitialized)
            {
                hasLeftFingerData = false;
                hasRightFingerData = false;
                return;
            }

            try
            {
                hasLeftFingerData = OpenVR.GetFingerCurls(true, out leftFingerCurls);
                hasRightFingerData = OpenVR.GetFingerCurls(false, out rightFingerCurls);
            }
            catch { }
        }

        private void FindNetworkEvent()
        {
            if (networkEventSearched) return;
            networkEventSearched = true;

            try
            {
                var game = PolytoriaGame.singleton;
                if (game == null)
                {
                    Plugin.Log.LogWarning("[VR] Game.singleton is null, will retry next frame");
                    networkEventSearched = false;
                    return;
                }

                var scriptService = game.FindChild("ScriptService");
                if (scriptService == null)
                {
                    Plugin.Log.LogWarning("[VR] ScriptService not found");
                    return;
                }

                var eventInstance = scriptService.FindChild("VRHandSync");
                if (eventInstance == null)
                {
                    Plugin.Log.LogInfo("[VR] VRHandSync NetworkEvent not found in ScriptService (hand sync disabled)");
                    return;
                }

                vrHandSyncEvent = eventInstance.TryCast<PolytoriaNetworkEvent>();
                if (vrHandSyncEvent != null)
                    Plugin.Log.LogInfo("[VR] Found VRHandSync NetworkEvent - network hand sync enabled!");
                else
                    Plugin.Log.LogWarning("[VR] VRHandSync found but is not a NetworkEvent");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[VR] FindNetworkEvent error: {e.Message}");
            }
        }

        private void SyncHandsToNetwork()
        {
            try
            {
                if (vrHandSyncEvent == null)
                {
                    FindNetworkEvent();
                    if (vrHandSyncEvent == null) return;
                }

                networkSyncTimer -= Time.deltaTime;
                if (networkSyncTimer > 0f) return;
                networkSyncTimer = NetworkSyncInterval;

                bool leftActive = leftHand != null && leftHand.activeSelf;
                bool rightActive = rightHand != null && rightHand.activeSelf;

                var headPos = vrCamera != null ? vrCamera.transform.position : Vector3.zero;

                var msg = PolytoriaNetMessage.New();

                int activeFlags = 0;
                if (leftActive)
                {
                    activeFlags |= 1;
                    msg.AddVector3("lp", leftHand.transform.position);
                    msg.AddVector3("lr", leftHand.transform.rotation.eulerAngles);

                    activeFlags |= 4;
                    if (hasLeftFingerData)
                    {
                        for (int i = 0; i < 5; i++)
                            msg.AddNumber("lc" + i, leftFingerCurls[i]);
                    }
                    else
                    {
                        float lt = leftTriggerStrength;
                        float lg = leftGripStrength;
                        msg.AddNumber("lc0", Mathf.Max(0.0f, Mathf.Max(lt, lg) * 0.5f));
                        msg.AddNumber("lc1", Mathf.Max(0.12f, lt));
                        msg.AddNumber("lc2", Mathf.Max(0.13f, lg));
                        msg.AddNumber("lc3", Mathf.Max(0.14f, lg));
                        msg.AddNumber("lc4", Mathf.Max(0.15f, lg));
                    }
                }
                if (rightActive)
                {
                    activeFlags |= 2;
                    msg.AddVector3("rp", rightHand.transform.position);
                    msg.AddVector3("rr", rightHand.transform.rotation.eulerAngles);

                    activeFlags |= 8;
                    if (hasRightFingerData)
                    {
                        for (int i = 0; i < 5; i++)
                            msg.AddNumber("rc" + i, rightFingerCurls[i]);
                    }
                    else
                    {
                        float rt = rightTriggerStrength;
                        float rg = rightGripStrength;
                        msg.AddNumber("rc0", Mathf.Max(0.0f, Mathf.Max(rt, rg) * 0.5f));
                        msg.AddNumber("rc1", Mathf.Max(0.12f, rt));
                        msg.AddNumber("rc2", Mathf.Max(0.13f, rg));
                        msg.AddNumber("rc3", Mathf.Max(0.14f, rg));
                        msg.AddNumber("rc4", Mathf.Max(0.15f, rg));
                    }
                }
                msg.AddInt("a", activeFlags);

                msg.AddVector3("hp", headPos);
                var headEuler = GetEffectiveRotation().eulerAngles;
                msg.AddVector3("hrot", headEuler);

                msg.AddNumber("lg", leftGripStrength);
                msg.AddNumber("rg", rightGripStrength);
                msg.AddNumber("sc", VRScale);

                vrHandSyncEvent.InvokeServer(msg);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[VR] SyncHandsToNetwork error: {e.Message}");
                networkSyncTimer = 1f;
            }
        }

        private void HideOwnNetworkHands()
        {
            try
            {
                hideOwnHandsTimer -= Time.deltaTime;
                if (hideOwnHandsTimer > 0f) return;
                hideOwnHandsTimer = 1f;

                if (string.IsNullOrEmpty(localPlayerName))
                {
                    if (localPlayer != null)
                        localPlayerName = localPlayer.Name;
                    else
                        return;
                }

                var game = PolytoriaGame.singleton;
                if (game == null) return;

                var env = game.FindChild("Environment");
                if (env == null) return;

                var vrHandsFolder = env.FindChild("VRHands");
                if (vrHandsFolder == null) return;

                var children = vrHandsFolder.GetChildren();
                if (children == null) return;

                string leftName = "VR_L_" + localPlayerName;
                string rightName = "VR_R_" + localPlayerName;
                string headName = "VR_Head_" + localPlayerName;

                for (int i = 0; i < children.Length; i++)
                {
                    var child = children[i];
                    if (child == null) continue;

                    var n = child.Name;
                    if (n == leftName || n == rightName || n == headName)
                    {
                        var go = child.gameObject;
                        if (go != null)
                        {
                            var renderer = go.GetComponent<Renderer>();
                            if (renderer != null)
                                renderer.enabled = false;
                        }
                    }
                }
            }
            catch { }
        }

        private void HandleSmoothTurn()
        {
            try
            {
                if (rightControllerIdx < 0) return;

                var joy = OpenVR.InputInitialized ? OpenVR.GetRightJoystick() : OpenVR.GetJoystick(rightControllerIdx);
                if (Mathf.Abs(joy.x) < JoystickDeadzone) return;

                float turnAmount = joy.x * SmoothTurnSpeed * Time.deltaTime;
                smoothTurnYaw += turnAmount;

                if (smoothTurnYaw < 0f) smoothTurnYaw += 360f;
                if (smoothTurnYaw >= 360f) smoothTurnYaw -= 360f;
            }
            catch { }
        }

        private unsafe void HandleMovement()
        {
            try
            {
                if (localPlayer == null) return;

                if (FlyEnabled)
                {
                    if (!flyInitialized)
                    {
                        var rbPtr = *(IntPtr*)(localPlayer.Pointer + 0x188);
                        if (rbPtr == IntPtr.Zero) return;
                        flyRb = new Rigidbody(rbPtr);
                        flyRb.useGravity = false;
                        flyRb.isKinematic = true;
                        flyInitialized = true;
                        Plugin.Log.LogInfo("[VR] Fly mode initialized (kinematic=true)");
                    }
                }
                else
                {
                    if (flyInitialized && flyRb != null)
                    {
                        try { flyRb.useGravity = true; flyRb.isKinematic = false; } catch { }
                        flyRb = null;
                        flyInitialized = false;
                        Plugin.Log.LogInfo("[VR] Fly mode disabled (gravity restored)");
                    }
                }

                if (leftControllerIdx < 0) return;

                var joy = OpenVR.InputInitialized ? OpenVR.GetLeftJoystick() : OpenVR.GetJoystick(leftControllerIdx);
                if (Mathf.Abs(joy.x) < JoystickDeadzone) joy.x = 0f;
                if (Mathf.Abs(joy.y) < JoystickDeadzone) joy.y = 0f;
                if (joy.x == 0f && joy.y == 0f) return;

                var effectiveRot = GetEffectiveRotation();
                Vector3 forward, right;

                if (FlyEnabled)
                {
                    forward = effectiveRot * Vector3.forward;
                    right = effectiveRot * Vector3.right;
                }
                else
                {
                    float yaw = effectiveRot.eulerAngles.y;
                    var yawRot = Quaternion.Euler(0f, yaw, 0f);
                    forward = yawRot * Vector3.forward;
                    right = yawRot * Vector3.right;
                }

                var moveDir = (forward * joy.y + right * joy.x).normalized;

                float speed = DefaultMoveSpeed;
                try { speed = localPlayer.WalkSpeed; } catch { }

                var playerTransform = ((Component)localPlayer).transform;
                playerTransform.position += moveDir * speed * FlySpeedMultiplier * Time.deltaTime;
            }
            catch { }
        }

        private void DisableVR()
        {
            Plugin.Log.LogInfo("[VR] Disabling VR...");
            renderLoopRunning = false;
            localPlayer = null;
            smoothTurnYaw = 0f;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            try
            {
                if (polytoriaCamera != null)
                {
                    polytoriaCamera.MaxDistance = savedMaxDistance;
                    polytoriaCamera.MinDistance = 0f;
                    polytoriaCamera.Distance = 10f;
                    Plugin.Log.LogInfo($"[VR] Restored camera: MaxDistance={savedMaxDistance}");
                }
            }
            catch { }
            polytoriaCamera = null;
            savedCameraState = false;

            OpenVR.Shutdown();

            if (vrCamera != null)
            {
                vrCamera.targetTexture = null;
                vrCamera.ResetProjectionMatrix();
                vrCamera.transform.localPosition = Vector3.zero;
                vrCamera.transform.localRotation = Quaternion.identity;
            }

            if (leftHand != null) { UnityEngine.Object.Destroy(leftHand); leftHand = null; }
            if (rightHand != null) { UnityEngine.Object.Destroy(rightHand); rightHand = null; }
            leftControllerIdx = -1;
            rightControllerIdx = -1;
            if (flyRb != null)
            {
                try { flyRb.useGravity = true; flyRb.isKinematic = false; } catch { }
                flyRb = null;
            }
            flyInitialized = false;
            vrHandSyncEvent = null;
            networkEventSearched = false;
            localPlayerName = null;

            if (leftEyeRT != null) { leftEyeRT.Release(); UnityEngine.Object.Destroy(leftEyeRT); leftEyeRT = null; }
            if (rightEyeRT != null) { rightEyeRT.Release(); UnityEngine.Object.Destroy(rightEyeRT); rightEyeRT = null; }

            Plugin.VRActive = false;
            Plugin.LastStatus = "Disabled";
            Plugin.Log.LogInfo("[VR] VR disabled");
        }
    }
}
