using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using Valve.VR;

namespace PolytoriaVR
{
    internal static class OpenVR
    {
        private static CVRSystem system;
        private static CVRCompositor compositor;
        private static CVRInput input;

        public static bool IsInitialized { get; private set; }
        public static bool InputInitialized { get; private set; }

        static OpenVR()
        {
            try { RegisterDllResolver(); }
            catch (Exception e) { Plugin.Log.LogWarning($"[OpenVR] DLL resolver setup: {e.Message}"); }
        }

        private static void RegisterDllResolver()
        {

            var nativeLibType = Type.GetType("System.Runtime.InteropServices.NativeLibrary, System.Runtime.InteropServices");
            if (nativeLibType == null) return;

            var setResolver = nativeLibType.GetMethod("SetDllImportResolver", BindingFlags.Public | BindingFlags.Static);
            var tryLoadPath = nativeLibType.GetMethod("TryLoad", new[] { typeof(string), typeof(IntPtr).MakeByRefType() });
            var tryLoadFull = nativeLibType.GetMethod("TryLoad", new[] { typeof(string), typeof(Assembly), typeof(DllImportSearchPath?), typeof(IntPtr).MakeByRefType() });
            if (setResolver == null || tryLoadPath == null) return;

            var resolverType = setResolver.GetParameters()[1].ParameterType;
            var resolverMethod = typeof(OpenVR).GetMethod(nameof(ResolveOpenVR), BindingFlags.NonPublic | BindingFlags.Static);
            var resolver = Delegate.CreateDelegate(resolverType, resolverMethod);

            setResolver.Invoke(null, new object[] { typeof(OpenVRInterop).Assembly, resolver });
            Plugin.Log.LogInfo("[OpenVR] Native library resolver registered");
        }

        private static IntPtr ResolveOpenVR(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName != "openvr_api") return IntPtr.Zero;

            var nativeLibType = Type.GetType("System.Runtime.InteropServices.NativeLibrary, System.Runtime.InteropServices");
            var tryLoadPath = nativeLibType?.GetMethod("TryLoad", new[] { typeof(string), typeof(IntPtr).MakeByRefType() });
            var tryLoadFull = nativeLibType?.GetMethod("TryLoad", new[] { typeof(string), typeof(Assembly), typeof(DllImportSearchPath?), typeof(IntPtr).MakeByRefType() });

            if (tryLoadFull != null)
            {
                var args = new object[] { libraryName, assembly, searchPath, IntPtr.Zero };
                if ((bool)tryLoadFull.Invoke(null, args))
                    return (IntPtr)args[3];
            }

            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            string[] libNames = isWindows ? new[] { "openvr_api.dll" }
                              : isMac     ? new[] { "libopenvr_api.dylib", "openvr_api.dylib" }
                              :             new[] { "libopenvr_api.so", "openvr_api.so" };

            string[] searchDirs = {
                Path.Combine(Application.dataPath, "Plugins", "x86_64"),
                Path.GetDirectoryName(Application.dataPath) ?? "",
                Path.Combine(Application.dataPath, ".."),
            };

            foreach (var dir in searchDirs)
            {
                foreach (var lib in libNames)
                {
                    var p = Path.Combine(dir, lib);
                    if (!File.Exists(p) || tryLoadPath == null) continue;
                    var args = new object[] { p, IntPtr.Zero };
                    if ((bool)tryLoadPath.Invoke(null, args))
                    {
                        Plugin.Log.LogInfo($"[OpenVR] Loaded native library: {p}");
                        return (IntPtr)args[1];
                    }
                }
            }

            Plugin.Log.LogError($"[OpenVR] Could not find openvr_api native library! Copy it to the game directory.");
            return IntPtr.Zero;
        }

        private static ulong mainActionSetHandle;
        private static ulong leftSkeletonHandle;
        private static ulong rightSkeletonHandle;
        private static ulong leftJoystickHandle;
        private static ulong rightJoystickHandle;
        private static ulong leftGripHandle;
        private static ulong rightGripHandle;
        private static ulong leftTriggerHandle;
        private static ulong rightTriggerHandle;

        private static TrackedDevicePose_t[] poses = new TrackedDevicePose_t[1];
        private static TrackedDevicePose_t[] renderPoses = new TrackedDevicePose_t[16];
        private static TrackedDevicePose_t[] gamePoses = new TrackedDevicePose_t[16];

        public static uint RenderWidth { get; private set; }
        public static uint RenderHeight { get; private set; }

        public static bool Init()
        {
            try
            {
                bool present = OpenVRInterop.IsHmdPresent();
                Plugin.Log.LogInfo($"[OpenVR] HMD present: {present}");
                if (!present)
                {
                    Plugin.Log.LogError("[OpenVR] No HMD detected! Is SteamVR running?");
                    return false;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[OpenVR] IsHmdPresent check failed: {e.Message}");
            }

            EVRInitError error = EVRInitError.None;
            try
            {
                OpenVRInterop.InitInternal(ref error, EVRApplicationType.VRApplication_Scene);

                if (error != EVRInitError.None)
                {
                    Plugin.Log.LogError($"[OpenVR] VR_Init failed: {error}");

                    // don't really need this, but it can stay
                    Plugin.Log.LogInfo("[OpenVR] Retrying as Background application...");
                    error = EVRInitError.None;
                    OpenVRInterop.InitInternal(ref error, EVRApplicationType.VRApplication_Background);

                    if (error != EVRInitError.None)
                    {
                        Plugin.Log.LogError($"[OpenVR] Background init also failed: {error}");
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[OpenVR] VR_InitInternal exception: {e.Message}");
                return false;
            }

            try
            {
                error = EVRInitError.None;
                var systemPtr = OpenVRInterop.GetGenericInterface("FnTable:" + Valve.VR.OpenVR.IVRSystem_Version, ref error);
                if (systemPtr != IntPtr.Zero && error == EVRInitError.None)
                {
                    system = new CVRSystem(systemPtr);
                    Plugin.Log.LogInfo($"[OpenVR] IVRSystem resolved ({Valve.VR.OpenVR.IVRSystem_Version})");

                    uint w = 0, h = 0;
                    system.GetRecommendedRenderTargetSize(ref w, ref h);
                    RenderWidth = w;
                    RenderHeight = h;
                    Plugin.Log.LogInfo($"[OpenVR] Got recommended resolution: {w}x{h}");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[OpenVR] IVRSystem setup failed: {e.Message}");
            }

            try
            {
                error = EVRInitError.None;
                var compositorPtr = OpenVRInterop.GetGenericInterface("FnTable:" + Valve.VR.OpenVR.IVRCompositor_Version, ref error);
                if (compositorPtr != IntPtr.Zero && error == EVRInitError.None)
                {
                    compositor = new CVRCompositor(compositorPtr);
                    Plugin.Log.LogInfo($"[OpenVR] IVRCompositor resolved ({Valve.VR.OpenVR.IVRCompositor_Version})");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[OpenVR] IVRCompositor setup: {e.Message}");
            }

            IsInitialized = (system != null);
            Plugin.Log.LogInfo($"[OpenVR] Initialization complete. Tracking available: {IsInitialized}");
            return IsInitialized;
        }

        public static bool InitInput(string manifestPath)
        {
            try
            {
                EVRInitError error = EVRInitError.None;
                var inputPtr = OpenVRInterop.GetGenericInterface("FnTable:" + Valve.VR.OpenVR.IVRInput_Version, ref error);

                if (inputPtr == IntPtr.Zero || error != EVRInitError.None)
                {
                    Plugin.Log.LogWarning("[OpenVR] IVRInput not available");
                    return false;
                }

                input = new CVRInput(inputPtr);
                Plugin.Log.LogInfo($"[OpenVR] IVRInput resolved ({Valve.VR.OpenVR.IVRInput_Version})");

                var err = input.SetActionManifestPath(manifestPath);
                if (err != EVRInputError.None)
                {
                    Plugin.Log.LogWarning($"[OpenVR] SetActionManifestPath failed: {err}");
                    return false;
                }

                input.GetActionSetHandle("/actions/main", ref mainActionSetHandle);
                input.GetActionHandle("/actions/main/in/LeftHand_Skeleton", ref leftSkeletonHandle);
                input.GetActionHandle("/actions/main/in/RightHand_Skeleton", ref rightSkeletonHandle);
                input.GetActionHandle("/actions/main/in/LeftJoystick", ref leftJoystickHandle);
                input.GetActionHandle("/actions/main/in/RightJoystick", ref rightJoystickHandle);
                input.GetActionHandle("/actions/main/in/LeftGrip", ref leftGripHandle);
                input.GetActionHandle("/actions/main/in/RightGrip", ref rightGripHandle);
                input.GetActionHandle("/actions/main/in/LeftTrigger", ref leftTriggerHandle);
                input.GetActionHandle("/actions/main/in/RightTrigger", ref rightTriggerHandle);

                InputInitialized = true;
                Plugin.Log.LogInfo("[OpenVR] IVRInput initialized!");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[OpenVR] InitInput error: {e.Message}");
                return false;
            }
        }

        private static VRActiveActionSet_t[] activeActionSets = new VRActiveActionSet_t[1];

        public static void UpdateInput()
        {
            if (!InputInitialized || input == null) return;

            try
            {
                activeActionSets[0].ulActionSet = mainActionSetHandle;
                activeActionSets[0].ulRestrictedToDevice = 0;
                activeActionSets[0].nPriority = 0;
                input.UpdateActionState(activeActionSets, (uint)Marshal.SizeOf<VRActiveActionSet_t>());
            }
            catch { }
        }

        public static void Shutdown()
        {
            try { OpenVRInterop.ShutdownInternal(); }
            catch { }
            system = null;
            compositor = null;
            input = null;
            IsInitialized = false;
            InputInitialized = false;
        }

        public static bool GetHMDPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (!IsInitialized || system == null)
                return false;

            try
            {
                system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0.0f, poses);
                if (poses[0].bPoseIsValid)
                {
                    MatrixToUnityPose(poses[0].mDeviceToAbsoluteTracking, out position, out rotation);
                    return true;
                }
            }
            catch { }

            return false;
        }

        public static bool WaitGetPosesAndGetHMD(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (compositor == null) return false;

            try
            {
                compositor.WaitGetPoses(renderPoses, gamePoses);
                if (renderPoses[0].bPoseIsValid)
                {
                    MatrixToUnityPose(renderPoses[0].mDeviceToAbsoluteTracking, out position, out rotation);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static void GetEyeToHeadTransform(int eye, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (system == null) return;

            try
            {
                var m = system.GetEyeToHeadTransform((EVREye)eye);
                MatrixToUnityPose(m, out position, out rotation);
            }
            catch { }
        }

        public static Matrix4x4 GetProjectionMatrix(int eye, float nearZ, float farZ)
        {
            if (system == null)
                return Matrix4x4.identity;

            try
            {
                var m = system.GetProjectionMatrix((EVREye)eye, nearZ, farZ);
                var mat = new Matrix4x4();
                mat.m00 = m.m0; mat.m01 = m.m1; mat.m02 = m.m2; mat.m03 = m.m3;
                mat.m10 = m.m4; mat.m11 = m.m5; mat.m12 = m.m6; mat.m13 = m.m7;
                mat.m20 = m.m8; mat.m21 = m.m9; mat.m22 = m.m10; mat.m23 = m.m11;
                mat.m30 = m.m12; mat.m31 = m.m13; mat.m32 = m.m14; mat.m33 = m.m15;
                return mat;
            }
            catch { return Matrix4x4.identity; }
        }

        private static VRTextureBounds_t flippedBounds = new VRTextureBounds_t
        {
            uMin = 0f, vMin = 1f,
            uMax = 1f, vMax = 0f
        };

        public static int SubmitFrame(int eye, IntPtr nativeTexturePtr)
        {
            if (compositor == null) return -1;

            var tex = new Texture_t
            {
                handle = nativeTexturePtr,
                eType = ETextureType.DirectX,
                eColorSpace = EColorSpace.Auto
            };

            var bounds = flippedBounds;
            var result = compositor.Submit((EVREye)eye, ref tex, ref bounds, EVRSubmitFlags.Submit_Default);
            return (int)result;
        }

        public static void PostPresentHandoff()
        {
            try { compositor?.PostPresentHandoff(); }
            catch { }
        }

        public static bool CanSubmitFrames => compositor != null;

        public static bool GetTrackedDevicePose(int deviceIndex, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (deviceIndex < 0 || deviceIndex >= renderPoses.Length) return false;
            if (!renderPoses[deviceIndex].bPoseIsValid) return false;

            MatrixToUnityPose(renderPoses[deviceIndex].mDeviceToAbsoluteTracking, out position, out rotation);
            return true;
        }

        public static void FindControllerIndices(out int leftIdx, out int rightIdx)
        {
            leftIdx = -1;
            rightIdx = -1;

            if (system != null)
            {
                try
                {
                    uint li = system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);
                    uint ri = system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);

                    if (li != Valve.VR.OpenVR.k_unTrackedDeviceIndexInvalid && li < (uint)renderPoses.Length)
                        leftIdx = (int)li;
                    if (ri != Valve.VR.OpenVR.k_unTrackedDeviceIndexInvalid && ri < (uint)renderPoses.Length)
                        rightIdx = (int)ri;

                    if (leftIdx >= 0 || rightIdx >= 0)
                        return;
                }
                catch { }
            }

            if (system == null) return;

            try
            {
                for (uint i = 1; i < 16; i++)
                {
                    var deviceClass = system.GetTrackedDeviceClass(i);
                    if (deviceClass != ETrackedDeviceClass.Controller) continue;

                    var role = system.GetControllerRoleForTrackedDeviceIndex(i);
                    if (role == ETrackedControllerRole.LeftHand && leftIdx < 0) leftIdx = (int)i;
                    else if (role == ETrackedControllerRole.RightHand && rightIdx < 0) rightIdx = (int)i;
                }
            }
            catch { }
        }

        public static bool GetControllerState(int deviceIndex, out VRControllerState_t state)
        {
            state = default;
            if (system == null || deviceIndex < 0) return false;

            try
            {
                return system.GetControllerState((uint)deviceIndex, ref state, (uint)Marshal.SizeOf<VRControllerState_t>());
            }
            catch { return false; }
        }

        public static Vector2 GetJoystick(int deviceIndex)
        {
            if (GetControllerState(deviceIndex, out var state))
                return new Vector2(state.rAxis0.x, state.rAxis0.y);
            return Vector2.zero;
        }

        public static Vector2 GetLeftJoystick()
        {
            if (InputInitialized && input != null && leftJoystickHandle != 0)
            {
                try
                {
                    var data = new InputAnalogActionData_t();
                    var err = input.GetAnalogActionData(leftJoystickHandle, ref data, (uint)Marshal.SizeOf<InputAnalogActionData_t>(), 0);
                    if (err == EVRInputError.None && data.bActive)
                        return new Vector2(data.x, data.y);
                }
                catch { }
            }
            return Vector2.zero;
        }

        public static Vector2 GetRightJoystick()
        {
            if (InputInitialized && input != null && rightJoystickHandle != 0)
            {
                try
                {
                    var data = new InputAnalogActionData_t();
                    var err = input.GetAnalogActionData(rightJoystickHandle, ref data, (uint)Marshal.SizeOf<InputAnalogActionData_t>(), 0);
                    if (err == EVRInputError.None && data.bActive)
                        return new Vector2(data.x, data.y);
                }
                catch { }
            }
            return Vector2.zero;
        }

        public static float GetGripStrength(bool leftHand)
        {
            if (!InputInitialized || input == null) return 0f;

            ulong handle = leftHand ? leftGripHandle : rightGripHandle;
            if (handle == 0) return 0f;

            try
            {
                var data = new InputAnalogActionData_t();
                var err = input.GetAnalogActionData(handle, ref data, (uint)Marshal.SizeOf<InputAnalogActionData_t>(), 0);
                if (err == EVRInputError.None && data.bActive)
                    return data.x;
            }
            catch { }
            return 0f;
        }

        public static float GetTriggerStrength(bool leftHand)
        {
            if (!InputInitialized || input == null) return 0f;

            ulong handle = leftHand ? leftTriggerHandle : rightTriggerHandle;
            if (handle == 0) return 0f;

            try
            {
                var data = new InputAnalogActionData_t();
                var err = input.GetAnalogActionData(handle, ref data, (uint)Marshal.SizeOf<InputAnalogActionData_t>(), 0);
                if (err == EVRInputError.None && data.bActive)
                    return data.x;
            }
            catch { }
            return 0f;
        }

        private static int skeletalFailCount;
        private static bool skeletalDisabled;
        private static float[] _cachedLeftCurls = new float[5];
        private static float[] _cachedRightCurls = new float[5];

        // we use skeletal when we don't really need to since this has a natural "curve" to the fingers
        // also the private version needs it for hand tracking

        public static unsafe bool GetFingerCurls(bool leftHand, out float[] curls)
        {
            curls = leftHand ? _cachedLeftCurls : _cachedRightCurls;
            if (!InputInitialized || input == null || skeletalDisabled) return false;

            try
            {
                ulong handle = leftHand ? leftSkeletonHandle : rightSkeletonHandle;
                if (handle == 0) return false;

                var actionData = new InputSkeletalActionData_t();
                var adErr = input.GetSkeletalActionData(handle, ref actionData, (uint)Marshal.SizeOf<InputSkeletalActionData_t>());
                if (adErr != EVRInputError.None || !actionData.bActive)
                {
                    skeletalFailCount++;
                    if (skeletalFailCount >= 120)
                    {
                        skeletalDisabled = true;
                        Plugin.Log.LogWarning("[OpenVR] Skeletal input not available - falling back to controller curls");
                    }
                    return false;
                }
                skeletalFailCount = 0;

                var summary = new VRSkeletalSummaryData_t();
                var err = input.GetSkeletalSummaryData(handle, EVRSummaryType.FromAnimation, ref summary);
                if (err != EVRInputError.None) return false;

                curls[0] = summary.flFingerCurl0;
                curls[1] = summary.flFingerCurl1;
                curls[2] = summary.flFingerCurl2;
                curls[3] = summary.flFingerCurl3;
                curls[4] = summary.flFingerCurl4;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void MatrixToUnityPose(HmdMatrix34_t pose, out Vector3 pos, out Quaternion rot)
        {
            pos = new Vector3(pose.m3, pose.m7, -pose.m11);

            var m = Matrix4x4.identity;
            m[0, 0] =  pose.m0;
            m[0, 1] =  pose.m1;
            m[0, 2] = -pose.m2;
            m[0, 3] =  pose.m3;

            m[1, 0] =  pose.m4;
            m[1, 1] =  pose.m5;
            m[1, 2] = -pose.m6;
            m[1, 3] =  pose.m7;

            m[2, 0] = -pose.m8;
            m[2, 1] = -pose.m9;
            m[2, 2] =  pose.m10;
            m[2, 3] = -pose.m11;

            m[3, 3] = 1f;

            rot = m.rotation;
        }
    }
}
