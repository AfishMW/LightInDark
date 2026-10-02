using System;
using LightInDark;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Roles;

namespace Light.Config;

/// <summary>
/// 职业配置自动注册器：为每个可生成的职业生成配置块，
/// 含通用"数量/概率"配置与职业专属配置（BuildConfigurations）。
/// </summary>
internal static class RoleConfigRegistrar
{
    private static bool _registered;

    /// <summary>注册全部职业配置块（幂等，插件启动时调用一次）。</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        try
        {
            foreach (var role in RoleRegistry.AllRoles)
            {
                if (!role.CanSpawnIn()) continue;   // 占位职业（Vanilla 等）不出配置

                var block = new ConfigBlock(
                    $"lid.role.{role.CodeName}", role.Name, ToCategory(role.Category))
                    .SetHeaderColor(role.Color.ToUnityColor());

                // 通用配置：出现数量 / 出现概率
                block.AddConfiguration(
                    $"role.{role.CodeName}.count", role.Allocation.MaxCount, 0, 15, 1,
                    $"{role.Name} 数量", $"{role.Name} 的最大出现数量");
                block.AddConfiguration(
                    $"role.{role.CodeName}.chance", role.Allocation.Chance, 0, 100, 5,
                    $"{role.Name} 概率", $"{role.Name} 的出现概率")
                    .WithSuffix(ConfigSuffix.Percent);

                // 职业专属配置
                role.BuildConfigurations(block);
            }
            LightLogger.Log("[RoleConfigRegistrar] 职业配置注册完成");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RoleConfigRegistrar.Register]", ex);
        }
    }

    /// <summary>阵营枚举映射到配置分类。</summary>
    private static ConfigCategory ToCategory(RoleCategory category) => category switch
    {
        RoleCategory.Crewmate => ConfigCategory.Crewmate,
        RoleCategory.Impostor => ConfigCategory.Impostor,
        _ => ConfigCategory.Neutral,
    };
}
