using System;
using System.Reflection;
using HarmonyLib;

namespace PolytoriaVR
{

    internal static class NpcSafetyPatch
    {
        public static void Apply(Harmony harmony)
        {
            var prefix = new HarmonyMethod(typeof(NpcSafetyPatch), nameof(SafePrefix));
            var finalizer = new HarmonyMethod(typeof(NpcSafetyPatch), nameof(SafeFinalizer));

            TryPatch(harmony, typeof(Polytoria.Datamodel.NPC), "SetAnimatorActive", prefix, finalizer);
            TryPatch(harmony, typeof(Polytoria.Datamodel.NPC), "UserCode_RpcSetAnimatorActive__Boolean", prefix, finalizer);
            TryPatch(harmony, typeof(Polytoria.Datamodel.NPC), "InvokeUserCode_RpcSetAnimatorActive__Boolean", null, finalizer);

            TryPatch(harmony, typeof(Polytoria.Datamodel.Player), "UserCode_RpcSetAnimatorActive__Boolean", prefix, finalizer);
            TryPatch(harmony, typeof(Polytoria.Datamodel.Player), "InvokeUserCode_RpcSetAnimatorActive__Boolean", null, finalizer);
        }

        private static void TryPatch(Harmony harmony, Type type, string methodName, HarmonyMethod prefix, HarmonyMethod finalizer)
        {
            try
            {
                var method = AccessTools.Method(type, methodName);
                if (method == null)
                {
                    Plugin.Log.LogWarning($"Could not find {type.Name}.{methodName} to patch");
                    return;
                }
                harmony.Patch(method, prefix: prefix, finalizer: finalizer);
                Plugin.Log.LogInfo($"Patched {type.Name}.{methodName}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to patch {type.Name}.{methodName}: {ex.Message}");
            }
        }

        static bool SafePrefix(object __instance)
        {
            try
            {
                if (__instance == null) return false;

                if (__instance is Il2CppSystem.Object il2cppObj)
                {
                    if (il2cppObj.Pointer == IntPtr.Zero) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        static Exception SafeFinalizer(Exception __exception)
        {
            if (__exception != null)
                Plugin.Log.LogWarning($"Suppressed SetAnimatorActive error: {__exception.Message}");
            return null;
        }
    }
}
