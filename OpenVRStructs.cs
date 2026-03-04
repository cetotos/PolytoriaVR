using System;
using System.Runtime.InteropServices;

namespace PolytoriaVR
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct HmdMatrix34_t
    {
        public float m00, m01, m02, m03;
        public float m10, m11, m12, m13;
        public float m20, m21, m22, m23;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HmdVector3_t
    {
        public float x, y, z;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TrackedDevicePose_t
    {
        public HmdMatrix34_t mDeviceToAbsoluteTracking;
        public HmdVector3_t vVelocity;
        public HmdVector3_t vAngularVelocity;
        public int eTrackingResult;
        [MarshalAs(UnmanagedType.I1)] public bool bPoseIsValid;
        [MarshalAs(UnmanagedType.I1)] public bool bDeviceIsConnected;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HmdMatrix44_t
    {
        public float m00, m01, m02, m03;
        public float m10, m11, m12, m13;
        public float m20, m21, m22, m23;
        public float m30, m31, m32, m33;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Texture_t
    {
        public IntPtr handle;
        public int eType;
        public int eColorSpace;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VRTextureBounds_t
    {
        public float uMin, vMin;
        public float uMax, vMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VRControllerAxis_t
    {
        public float x, y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VRControllerState_t
    {
        public uint unPacketNum;
        public ulong ulButtonPressed;
        public ulong ulButtonTouched;
        public VRControllerAxis_t rAxis0;
        public VRControllerAxis_t rAxis1;
        public VRControllerAxis_t rAxis2;
        public VRControllerAxis_t rAxis3;
        public VRControllerAxis_t rAxis4;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InputAnalogActionData_t
    {
        [MarshalAs(UnmanagedType.I1)]
        public bool bActive;
        public ulong activeOrigin;
        public float x, y, z;
        public float deltaX, deltaY, deltaZ;
        public float fUpdateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VRActiveActionSet_t
    {
        public ulong ulActionSet;
        public ulong ulRestrictedToDevice;
        public ulong ulSecondaryActionSet;
        public uint unPadding;
        public int nPriority;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InputSkeletalActionData_t
    {
        [MarshalAs(UnmanagedType.I1)]
        public bool bActive;
        public ulong activeOrigin;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VRSkeletalSummaryData_t
    {
        public fixed float flFingerCurl[5];
        public fixed float flFingerSplay[4];
    }
}
