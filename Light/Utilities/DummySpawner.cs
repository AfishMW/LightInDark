using System;
using System.Collections;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InnerNet;
using UnityEngine;

namespace Light.Utilities;

/// <summary>假人生成器：房主本地生成 Dummy，补足人数便于测试。</summary>
public static class DummySpawner
{
    /// <summary>逐帧生成 count 个假人，完成时回调提示消息。</summary>
    public static IEnumerator CoSpawn(int count, Action<string> onMessage)
    {
        for (int i = 0; i < count; i++)
        {
            try { SpawnOne(); }
            catch (Exception ex)
            {
                LightInDark.Core.LightLogger.LogError("[DummySpawner]", ex);
                yield break;
            }
            yield return null;
        }
        onMessage?.Invoke($"已添加 {count} 个假人");
    }

    /// <summary>生成单个假人（参考 Nebula AmongUsUtil.SpawnDummy）。</summary>
    private static void SpawnOne()
    {
        var pc = UnityEngine.Object.Instantiate(AmongUsClient.Instance.PlayerPrefab);
        byte id = (byte)GameData.Instance.GetAvailableId();
        pc.PlayerId = id;
        pc.isDummy = true;

        var info = GameData.Instance.AddDummy(pc);
        pc.transform.position = PlayerControl.LocalPlayer.transform.position;
        pc.GetComponent<DummyBehaviour>().enabled = true;

        pc.SetName(AccountManager.Instance.GetRandomName());
        pc.SetColor(GetUnusedColor());
        pc.SetHat(CosmeticsLayer.EMPTY_HAT_ID, id);
        pc.SetVisor(CosmeticsLayer.EMPTY_VISOR_ID, id);
        pc.SetSkin(CosmeticsLayer.EMPTY_SKIN_ID, id);
        pc.SetPet(CosmeticsLayer.EMPTY_PET_ID, id);

        AmongUsClient.Instance.Spawn(pc, -2, SpawnFlags.None);
        info.RpcSetTasks(new Il2CppStructArray<byte>(0));
    }

    /// <summary>取一个未被占用的颜色，全部占用时随机。</summary>
    private static byte GetUnusedColor()
    {
        int count = Palette.PlayerColors.Length;
        for (byte c = 0; c < count; c++)
        {
            bool used = false;
            foreach (var p in PlayerControl.AllPlayerControls)
                if (p.cosmetics != null && p.cosmetics.ColorId == c) { used = true; break; }
            if (!used) return c;
        }
        return (byte)UnityEngine.Random.Range(0, count);
    }
}
