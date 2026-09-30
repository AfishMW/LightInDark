using HarmonyLib;
using InnerNet;
using LightInDark.Utilities;

namespace LightInDark.Patches;

[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.DisconnectInternal))]
public static class DisconnectInternalPatch
{
    public static void Prefix(InnerNetClient __instance, ref DisconnectReasons reason)
    {
        if (reason == DisconnectReasons.Kicked)
        {
            string pendingReason = KickHelper.ConsumePendingReason(__instance.ClientId);
            if (!string.IsNullOrEmpty(pendingReason))
            {
                __instance.LastCustomDisconnect = pendingReason;
                reason = DisconnectReasons.Custom;
            }
        }
    }
}