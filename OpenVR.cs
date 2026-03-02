using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PolytoriaVR
{
    internal static class OpenVR
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string lpFileName);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        private static IntPtr dllHandle;
        public static bool IsInitialized { get; private set; }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr d_VR_InitInternal(ref int peError, int eType);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void d_VR_ShutdownInternal();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr d_VR_GetGenericInterface([MarshalAs(UnmanagedType.LPStr)] string pchInterfaceVersion, ref int peError);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool d_VR_IsHmdPresent();

        private static d_VR_InitInternal fn_VR_InitInternal;
        private static d_VR_ShutdownInternal fn_VR_ShutdownInternal;
        private static d_VR_GetGenericInterface fn_VR_GetGenericInterface;
        private static d_VR_IsHmdPresent fn_VR_IsHmdPresent;

        private static IntPtr systemFnTable;
        private static IntPtr compositorFnTable;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void d_GetRecommendedRenderTargetSize(ref uint pnWidth, ref uint pnHeight);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate HmdMatrix44_t d_GetProjectionMatrix(int nEye, float fNearZ, float fFarZ);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate HmdMatrix34_t d_GetEyeToHeadTransform(int nEye);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void d_GetDeviceToAbsoluteTrackingPose(int eOrigin, float fPredicted, [In, Out] TrackedDevicePose_t[] pPoses, uint unCount);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint d_GetTrackedDeviceIndexForControllerRole(int unDeviceType);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int d_GetControllerRoleForTrackedDeviceIndex(uint unDeviceIndex);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int d_GetTrackedDeviceClass(uint unDeviceIndex);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool d_GetControllerState(uint unControllerDeviceIndex, ref VRControllerState_t pControllerState, uint unControllerStateSize);

        private static d_GetRecommendedRenderTargetSize fn_GetRecommendedRenderTargetSize;
        private static d_GetProjectionMatrix fn_GetProjectionMatrix;
        private static d_GetEyeToHeadTransform fn_GetEyeToHeadTransform;
        private static d_GetDeviceToAbsoluteTrackingPose fn_GetDeviceToAbsoluteTrackingPose;
        private static d_GetTrackedDeviceIndexForControllerRole fn_GetTrackedDeviceIndexForControllerRole;
        private static d_GetControllerRoleForTrackedDeviceIndex fn_GetControllerRoleForTrackedDeviceIndex;
        private static d_GetTrackedDeviceClass fn_GetTrackedDeviceClass;
        private static d_GetControllerState fn_GetControllerState;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int d_WaitGetPoses([In, Out] TrackedDevicePose_t[] pRenderPoses, uint unRenderPoseArrayCount, [In, Out] TrackedDevicePose_t[] pGamePoses, uint unGamePoseArrayCount);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int d_Submit(int nEye, ref Texture_t pTexture, IntPtr pBounds, int nSubmitFlags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void d_PostPresentHandoff();

        private static d_WaitGetPoses fn_WaitGetPoses;
        private static d_Submit fn_Submit;
        private static d_PostPresentHandoff fn_PostPresentHandoff;

        private static IntPtr inputFnTable;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int d_SetActionManifestPath([MarshalAs(UnmanagedType.LPStr)] string pchActionManifestPath);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int d_GetActionSetHandle([MarshalAs(UnmanagedType.LPStr)] string pchActionSetName, ref ulong pHandle);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int d_GetActionHandle([MarshalAs(UnmanagedType.LPStr)] string pchActionName, ref ulong pHandle);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int d_UpdateActionState([In] VRActiveActionSet_t[] pSets, uint unSizeOfVRSelectedActionSet_t, uint unSetCount);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int d_GetAnalogActionData(ulong action, ref InputAnalogActionData_t pActionData, uint unActionDataSize, ulong ulRestrictToDevice);

        private static d_SetActionManifestPath fn_SetActionManifestPath;
        private static d_GetActionSetHandle fn_GetActionSetHandle;
        private static d_GetActionHandle fn_GetActionHandle;
        private static d_UpdateActionState fn_UpdateActionState;
        private static d_GetAnalogActionData fn_GetAnalogActionData;

        private static ulong mainActionSetHandle;
        private static ulong leftJoystickHandle;
        private static ulong rightJoystickHandle;
        private static ulong leftGripHandle;
        private static ulong rightGripHandle;
        public static bool InputInitialized { get; private set; }

        private static TrackedDevicePose_t[] poses = new TrackedDevicePose_t[1];
        private static TrackedDevicePose_t[] renderPoses = new TrackedDevicePose_t[16];
        private static TrackedDevicePose_t[] gamePoses = new TrackedDevicePose_t[16];

        public static uint RenderWidth { get; private set; }
        public static uint RenderHeight { get; private set; }

        public static bool LoadDLL()
        {
            string[] searchPaths = {
                Path.Combine(Application.dataPath, "Plugins", "x86_64", "openvr_api.dll"),
                Path.Combine(Path.GetDirectoryName(Application.dataPath), "openvr_api.dll"),
                @"C:\Program Files (x86)\Steam\steamapps\common\SteamVR\bin\win64\openvr_api.dll",
                @"C:\Program Files (x86)\Steam\openvr_api.dll",
            };

            string dllPath = null;
            foreach (var p in searchPaths)
            {
                if (File.Exists(p)) { dllPath = p; break; }
            }

            if (dllPath == null)
            {
                Plugin.Log.LogError("[OpenVR] openvr_api.dll not found!");
                Plugin.Log.LogError("[OpenVR] Copy it from SteamVR to the game directory.");
                Plugin.Log.LogError("[OpenVR] Typical location: Steam\\steamapps\\common\\SteamVR\\bin\\win64\\openvr_api.dll");
                return false;
            }

            Plugin.Log.LogInfo($"[OpenVR] Loading: {dllPath}");
            dllHandle = LoadLibraryW(dllPath);
            if (dllHandle == IntPtr.Zero)
            {
                Plugin.Log.LogError("[OpenVR] LoadLibrary failed!");
                return false;
            }

            fn_VR_InitInternal = GetFunc<d_VR_InitInternal>("VR_InitInternal");
            fn_VR_ShutdownInternal = GetFunc<d_VR_ShutdownInternal>("VR_ShutdownInternal");
            fn_VR_GetGenericInterface = GetFunc<d_VR_GetGenericInterface>("VR_GetGenericInterface");
            fn_VR_IsHmdPresent = GetFunc<d_VR_IsHmdPresent>("VR_IsHmdPresent");

            if (fn_VR_InitInternal == null || fn_VR_GetGenericInterface == null)
            {
                Plugin.Log.LogError("[OpenVR] Failed to resolve core API functions!");
                return false;
            }

            Plugin.Log.LogInfo("[OpenVR] DLL loaded and API resolved");
            return true;
        }

        public static bool Init()
        {
            try
            {
                bool present = fn_VR_IsHmdPresent();
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

            int error = 0;
            try
            {
                IntPtr token = fn_VR_InitInternal(ref error, 1);

                if (error != 0)
                {
                    Plugin.Log.LogError($"[OpenVR] VR_Init failed with error code {error}");
                    Plugin.Log.LogInfo("[OpenVR] Retrying as Background application...");
                    error = 0;
                    token = fn_VR_InitInternal(ref error, 0);

                    if (error != 0)
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
                string[] systemVersions = { "FnTable:IVRSystem_023", "FnTable:IVRSystem_022", "FnTable:IVRSystem_021" };
                string usedSystemVersion = null;
                foreach (var ver in systemVersions)
                {
                    error = 0;
                    systemFnTable = fn_VR_GetGenericInterface(ver, ref error);
                    if (systemFnTable != IntPtr.Zero && error == 0)
                    {
                        usedSystemVersion = ver;
                        break;
                    }
                }

                if (systemFnTable != IntPtr.Zero && error == 0)
                {
                    fn_GetRecommendedRenderTargetSize = ReadFnTableEntry<d_GetRecommendedRenderTargetSize>(systemFnTable, 0);
                    fn_GetProjectionMatrix = ReadFnTableEntry<d_GetProjectionMatrix>(systemFnTable, 1);
                    fn_GetEyeToHeadTransform = ReadFnTableEntry<d_GetEyeToHeadTransform>(systemFnTable, 4);
                    fn_GetDeviceToAbsoluteTrackingPose = ReadFnTableEntry<d_GetDeviceToAbsoluteTrackingPose>(systemFnTable, 11);
                    fn_GetTrackedDeviceIndexForControllerRole = ReadFnTableEntry<d_GetTrackedDeviceIndexForControllerRole>(systemFnTable, 17);
                    fn_GetControllerRoleForTrackedDeviceIndex = ReadFnTableEntry<d_GetControllerRoleForTrackedDeviceIndex>(systemFnTable, 18);
                    fn_GetTrackedDeviceClass = ReadFnTableEntry<d_GetTrackedDeviceClass>(systemFnTable, 19);
                    fn_GetControllerState = ReadFnTableEntry<d_GetControllerState>(systemFnTable, 34);

                    Plugin.Log.LogInfo($"[OpenVR] IVRSystem functions resolved (version={usedSystemVersion}, GetControllerState={fn_GetControllerState != null})");

                    uint w = 0, h = 0;
                    fn_GetRecommendedRenderTargetSize?.Invoke(ref w, ref h);
                    RenderWidth = w;
                    RenderHeight = h;
                    Plugin.Log.LogInfo($"[OpenVR] Recommended render size: {w}x{h}");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[OpenVR] IVRSystem setup failed: {e.Message}");
            }

            try
            {
                string usedCompositorVersion = null;
                string[] compositorVersions = { "FnTable:IVRCompositor_029", "FnTable:IVRCompositor_028", "FnTable:IVRCompositor_027" };
                foreach (var ver in compositorVersions)
                {
                    error = 0;
                    compositorFnTable = fn_VR_GetGenericInterface(ver, ref error);
                    if (compositorFnTable != IntPtr.Zero && error == 0) { usedCompositorVersion = ver; break; }
                }

                if (compositorFnTable != IntPtr.Zero && error == 0)
                {
                    bool is029 = usedCompositorVersion != null && usedCompositorVersion.Contains("_029");
                    int submitIdx = is029 ? 6 : 5;
                    int postPresentIdx = is029 ? 9 : 8;

                    fn_WaitGetPoses = ReadFnTableEntry<d_WaitGetPoses>(compositorFnTable, 2);
                    fn_Submit = ReadFnTableEntry<d_Submit>(compositorFnTable, submitIdx);
                    fn_PostPresentHandoff = ReadFnTableEntry<d_PostPresentHandoff>(compositorFnTable, postPresentIdx);
                    Plugin.Log.LogInfo($"[OpenVR] IVRCompositor functions resolved (version={usedCompositorVersion}, Submit@{submitIdx}={fn_Submit != null}, PostPresentHandoff@{postPresentIdx}={fn_PostPresentHandoff != null})");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[OpenVR] IVRCompositor setup: {e.Message}");
            }

            IsInitialized = (fn_GetDeviceToAbsoluteTrackingPose != null);
            Plugin.Log.LogInfo($"[OpenVR] Initialization complete. Tracking available: {IsInitialized}");
            return IsInitialized;
        }

        public static bool InitInput(string manifestPath)
        {
            try
            {
                string[] inputVersions = { "FnTable:IVRInput_010", "FnTable:IVRInput_009", "FnTable:IVRInput_007" };
                int error = 0;
                string usedVersion = null;
                foreach (var ver in inputVersions)
                {
                    error = 0;
                    inputFnTable = fn_VR_GetGenericInterface(ver, ref error);
                    if (inputFnTable != IntPtr.Zero && error == 0) { usedVersion = ver; break; }
                }

                if (inputFnTable == IntPtr.Zero || error != 0)
                {
                    Plugin.Log.LogWarning("[OpenVR] IVRInput not available");
                    return false;
                }

                fn_SetActionManifestPath = ReadFnTableEntry<d_SetActionManifestPath>(inputFnTable, 0);
                fn_GetActionSetHandle = ReadFnTableEntry<d_GetActionSetHandle>(inputFnTable, 1);
                fn_GetActionHandle = ReadFnTableEntry<d_GetActionHandle>(inputFnTable, 2);
                fn_UpdateActionState = ReadFnTableEntry<d_UpdateActionState>(inputFnTable, 4);
                fn_GetAnalogActionData = ReadFnTableEntry<d_GetAnalogActionData>(inputFnTable, 6);

                Plugin.Log.LogInfo($"[OpenVR] IVRInput functions resolved (version={usedVersion})");

                int err = fn_SetActionManifestPath(manifestPath);
                if (err != 0)
                {
                    Plugin.Log.LogWarning($"[OpenVR] SetActionManifestPath failed: {err}");
                    return false;
                }

                err = fn_GetActionSetHandle("/actions/main", ref mainActionSetHandle);

                err = fn_GetActionHandle("/actions/main/in/LeftJoystick", ref leftJoystickHandle);

                err = fn_GetActionHandle("/actions/main/in/RightJoystick", ref rightJoystickHandle);

                err = fn_GetActionHandle("/actions/main/in/LeftGrip", ref leftGripHandle);

                err = fn_GetActionHandle("/actions/main/in/RightGrip", ref rightGripHandle);

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
            if (!InputInitialized || fn_UpdateActionState == null) return;

            try
            {
                activeActionSets[0].ulActionSet = mainActionSetHandle;
                activeActionSets[0].ulRestrictedToDevice = 0;
                activeActionSets[0].nPriority = 0;
                fn_UpdateActionState(activeActionSets, (uint)Marshal.SizeOf<VRActiveActionSet_t>(), 1);
            }
            catch { }
        }

        public static void Shutdown()
        {
            if (fn_VR_ShutdownInternal != null)
            {
                try { fn_VR_ShutdownInternal(); }
                catch { }
            }
            IsInitialized = false;
            InputInitialized = false;
        }

        public static bool GetHMDPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (!IsInitialized || fn_GetDeviceToAbsoluteTrackingPose == null)
                return false;

            try
            {
                fn_GetDeviceToAbsoluteTrackingPose(1, 0.0f, poses, 1);
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

            if (fn_WaitGetPoses == null) return false;

            try
            {
                fn_WaitGetPoses(renderPoses, (uint)renderPoses.Length, gamePoses, (uint)gamePoses.Length);
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

            if (fn_GetEyeToHeadTransform == null) return;

            try
            {
                var m = fn_GetEyeToHeadTransform(eye);
                MatrixToUnityPose(m, out position, out rotation);
            }
            catch { }
        }

        public static Matrix4x4 GetProjectionMatrix(int eye, float nearZ, float farZ)
        {
            if (fn_GetProjectionMatrix == null)
                return Matrix4x4.identity;

            try
            {
                var m = fn_GetProjectionMatrix(eye, nearZ, farZ);
                var mat = new Matrix4x4();
                mat.m00 = m.m00; mat.m01 = m.m01; mat.m02 = m.m02; mat.m03 = m.m03;
                mat.m10 = m.m10; mat.m11 = m.m11; mat.m12 = m.m12; mat.m13 = m.m13;
                mat.m20 = m.m20; mat.m21 = m.m21; mat.m22 = m.m22; mat.m23 = m.m23;
                mat.m30 = m.m30; mat.m31 = m.m31; mat.m32 = m.m32; mat.m33 = m.m33;
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
            if (fn_Submit == null) return -1;

            var tex = new Texture_t
            {
                handle = nativeTexturePtr,
                eType = 0,
                eColorSpace = 0
            };

            var bounds = flippedBounds;
            unsafe
            {
                return fn_Submit(eye, ref tex, (IntPtr)(&bounds), 0);
            }
        }

        public static void PostPresentHandoff()
        {
            try { fn_PostPresentHandoff?.Invoke(); }
            catch { }
        }

        public static bool CanSubmitFrames => fn_Submit != null && fn_PostPresentHandoff != null;

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

            if (fn_GetTrackedDeviceIndexForControllerRole != null)
            {
                try
                {
                    uint li = fn_GetTrackedDeviceIndexForControllerRole(1);
                    uint ri = fn_GetTrackedDeviceIndexForControllerRole(2);

                    const uint k_unTrackedDeviceIndexInvalid = 0xFFFFFFFF;
                    if (li != k_unTrackedDeviceIndexInvalid && li < (uint)renderPoses.Length)
                        leftIdx = (int)li;
                    if (ri != k_unTrackedDeviceIndexInvalid && ri < (uint)renderPoses.Length)
                        rightIdx = (int)ri;

                    if (leftIdx >= 0 || rightIdx >= 0)
                        return;
                }
                catch { }
            }

            if (fn_GetTrackedDeviceClass == null || fn_GetControllerRoleForTrackedDeviceIndex == null)
                return;

            try
            {
                for (uint i = 1; i < 16; i++)
                {
                    int deviceClass = fn_GetTrackedDeviceClass(i);
                    if (deviceClass != 2) continue;

                    int role = fn_GetControllerRoleForTrackedDeviceIndex(i);
                    if (role == 1 && leftIdx < 0) leftIdx = (int)i;
                    else if (role == 2 && rightIdx < 0) rightIdx = (int)i;
                }
            }
            catch { }
        }

        public static bool GetControllerState(int deviceIndex, out VRControllerState_t state)
        {
            state = default;
            if (fn_GetControllerState == null || deviceIndex < 0) return false;

            try
            {
                return fn_GetControllerState((uint)deviceIndex, ref state, (uint)Marshal.SizeOf<VRControllerState_t>());
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
            if (InputInitialized && fn_GetAnalogActionData != null && leftJoystickHandle != 0)
            {
                try
                {
                    var data = new InputAnalogActionData_t();
                    int err = fn_GetAnalogActionData(leftJoystickHandle, ref data, (uint)Marshal.SizeOf<InputAnalogActionData_t>(), 0);
                    if (err == 0 && data.bActive)
                        return new Vector2(data.x, data.y);
                }
                catch { }
            }
            return Vector2.zero;
        }

        public static Vector2 GetRightJoystick()
        {
            if (InputInitialized && fn_GetAnalogActionData != null && rightJoystickHandle != 0)
            {
                try
                {
                    var data = new InputAnalogActionData_t();
                    int err = fn_GetAnalogActionData(rightJoystickHandle, ref data, (uint)Marshal.SizeOf<InputAnalogActionData_t>(), 0);
                    if (err == 0 && data.bActive)
                        return new Vector2(data.x, data.y);
                }
                catch { }
            }
            return Vector2.zero;
        }

        public static float GetGripStrength(bool leftHand)
        {
            if (!InputInitialized || fn_GetAnalogActionData == null) return 0f;

            ulong handle = leftHand ? leftGripHandle : rightGripHandle;
            if (handle == 0) return 0f;

            try
            {
                var data = new InputAnalogActionData_t();
                int err = fn_GetAnalogActionData(handle, ref data, (uint)Marshal.SizeOf<InputAnalogActionData_t>(), 0);
                if (err == 0 && data.bActive)
                    return data.x;
            }
            catch { }
            return 0f;
        }

        private static void MatrixToUnityPose(HmdMatrix34_t pose, out Vector3 pos, out Quaternion rot)
        {
            pos = new Vector3(pose.m03, pose.m13, -pose.m23);

            var m = Matrix4x4.identity;
            m[0, 0] =  pose.m00;
            m[0, 1] =  pose.m01;
            m[0, 2] = -pose.m02;
            m[0, 3] =  pose.m03;

            m[1, 0] =  pose.m10;
            m[1, 1] =  pose.m11;
            m[1, 2] = -pose.m12;
            m[1, 3] =  pose.m13;

            m[2, 0] = -pose.m20;
            m[2, 1] = -pose.m21;
            m[2, 2] =  pose.m22;
            m[2, 3] = -pose.m23;

            m[3, 3] = 1f;

            rot = m.rotation;
        }

        private static T GetFunc<T>(string name) where T : Delegate
        {
            var ptr = GetProcAddress(dllHandle, name);
            if (ptr == IntPtr.Zero) { Plugin.Log.LogWarning($"[OpenVR] Export not found: {name}"); return null; }
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }

        private static T ReadFnTableEntry<T>(IntPtr table, int index) where T : Delegate
        {
            IntPtr fnPtr = Marshal.ReadIntPtr(table, index * IntPtr.Size);
            if (fnPtr == IntPtr.Zero) return null;
            return Marshal.GetDelegateForFunctionPointer<T>(fnPtr);
        }
    }
}
