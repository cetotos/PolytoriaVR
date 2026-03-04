using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using HarmonyLib;
using UnityEngine;

namespace PolytoriaVR
{
    [BepInPlugin("com.cetotos.polytoriavr", "PolytoriaVR", "0.1.0")]
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource Log;
        internal static volatile bool VRActivationRequested;
        internal static volatile bool VRDeactivationRequested;
        internal static volatile bool VRActive;
        internal static string LastStatus = "Idle";

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("PolytoriaVR loading..");

            var harmony = new Harmony("com.cetotos.polytoriavr");
            NpcSafetyPatch.Apply(harmony);
            Log.LogInfo("[Harmony] Safety patches applied");

            ClassInjector.RegisterTypeInIl2Cpp<VRManager>();

            var go = new GameObject("PolytoriaVR_Manager");
            GameObject.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<VRManager>();

            var thread = new Thread(TcpListenerLoop) { IsBackground = true };
            thread.Start();

            Log.LogInfo("PolytoriaVR loaded");
        }

        private static void TcpListenerLoop()
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, 9999);
                listener.Start();
                Log.LogInfo("[TCP] Listening on 127.0.0.1:9999");
                while (true)
                {
                    TcpClient client = null;
                    try
                    {
                        client = listener.AcceptTcpClient();
                        var stream = client.GetStream();
                        var buffer = new byte[1024];
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        var msg = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                        string response;
                        switch (msg)
                        {
                            case "START_VR":
                                if (VRActive) response = "ALREADY_ACTIVE";
                                else { VRActivationRequested = true; response = "OK"; }
                                break;
                            case "STOP_VR":
                                if (!VRActive) response = "NOT_ACTIVE";
                                else { VRDeactivationRequested = true; response = "OK"; }
                                break;
                            case "STATUS":
                                response = $"VR={VRActive}|OpenVR={OpenVR.IsInitialized}|Status={LastStatus}|TurnSpeed={VRManager.SmoothTurnSpeed}|VRScale={VRManager.VRScale}|LocalHands={VRManager.LocalHandsVisible}|Fly={VRManager.FlyEnabled}|FlySpeed={VRManager.FlySpeedMultiplier}";
                                break;
                            default:
                                if (msg.StartsWith("SET_TURN_SPEED:"))
                                {
                                    if (float.TryParse(msg.Substring(15), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float speed))
                                    {
                                        VRManager.SmoothTurnSpeed = speed;
                                        response = $"OK:TurnSpeed={speed}";
                                    }
                                    else response = "ERROR:InvalidValue";
                                }
                                else if (msg.StartsWith("SET_FLY:"))
                                {
                                    var val = msg.Substring(8).Trim();
                                    VRManager.FlyEnabled = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                                    response = $"OK:Fly={VRManager.FlyEnabled}";
                                }
                                else if (msg.StartsWith("SET_FLY_SPEED:"))
                                {
                                    if (float.TryParse(msg.Substring(14), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fs) && fs >= 0.5f && fs <= 10f)
                                    {
                                        VRManager.FlySpeedMultiplier = fs;
                                        response = $"OK:FlySpeed={fs}";
                                    }
                                    else response = "ERROR:InvalidValue";
                                }
                                else if (msg.StartsWith("SET_LOCAL_HANDS:"))
                                {
                                    var val = msg.Substring(16).Trim();
                                    VRManager.LocalHandsVisible = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                                    response = $"OK:LocalHands={VRManager.LocalHandsVisible}";
                                }
                                else if (msg.StartsWith("SET_SCALE:"))
                                {
                                    if (float.TryParse(msg.Substring(10), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float scale) && scale > 0f)
                                    {
                                        VRManager.VRScale = scale;
                                        response = $"OK:VRScale={scale}";
                                    }
                                    else response = "ERROR:InvalidValue";
                                }
                                else response = "UNKNOWN_COMMAND";
                                break;
                        }
                        var responseBytes = Encoding.UTF8.GetBytes(response);
                        stream.Write(responseBytes, 0, responseBytes.Length);
                    }
                    catch (Exception e) { Log.LogWarning($"[TCP] Client error: {e.Message}"); }
                    finally { client?.Close(); }
                }
            }
            catch (Exception e) { Log.LogError($"[TCP] Fatal: {e.Message}"); }
            finally { listener?.Stop(); }
        }
    }
}
