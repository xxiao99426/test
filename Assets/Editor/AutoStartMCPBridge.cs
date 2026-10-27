using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 保证 MCP for Unity 的 stdio 桥接可用：
/// 1. 把 UseHttpTransport 置为 false —— 否则包自带的 StdioBridgeReloadHandler
///    会在每次重载时判定 shouldResume=false，不恢复 stdio 桥接。
/// 2. 桥接未运行时拉起（首次启动 / 兜底）。
/// 通过反射调用，避免 Assembly-CSharp-Editor 依赖 MCPForUnity.Editor 程序集。
/// </summary>
[InitializeOnLoad]
public static class AutoStartMCPBridge
{
    private const string UseHttpTransportKey = "MCPForUnity.UseHttpTransport";
    private const string AutoStartOnLoadKey = "MCPForUnity.AutoStartOnLoad";

    static AutoStartMCPBridge()
    {
        EditorApplication.delayCall += TryStart;
    }

    private static void TryStart()
    {
        // 关键：与 stdio 传输保持一致，让包内置的重载恢复逻辑生效
        if (EditorPrefs.GetBool(UseHttpTransportKey, true))
        {
            EditorPrefs.SetBool(UseHttpTransportKey, false);
            Debug.Log("[AutoMCP] 已将 UseHttpTransport 置为 false，启用 stdio 重载恢复");
        }
        EditorPrefs.SetBool(AutoStartOnLoadKey, true);

        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a =>
            {
                try { return a.GetType("MCPForUnity.Editor.Services.Transport.Transports.StdioBridgeHost"); }
                catch { return null; }
            })
            .FirstOrDefault(t => t != null);

        if (type == null)
        {
            // 没装 MCP for Unity 包时静默跳过：这是开发期辅助脚本，
            // 不该让只是打开工程评审的人在 Console 里看到一条警告。
            return;
        }

        var isRunningProp = type.GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Static);
        if (isRunningProp != null && (bool)isRunningProp.GetValue(null))
        {
            var p = type.GetMethod("GetCurrentPort", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            Debug.Log($"[AutoMCP] 桥接已在运行，端口 {p}");
            return;
        }

        var start = type.GetMethod("StartAutoConnect", BindingFlags.Public | BindingFlags.Static);
        if (start == null)
        {
            Debug.LogWarning("[AutoMCP] 未找到 StartAutoConnect 方法");
            return;
        }

        try
        {
            start.Invoke(null, null);
            var running = isRunningProp?.GetValue(null);
            var port = type.GetMethod("GetCurrentPort", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            Debug.Log($"[AutoMCP] MCP stdio 桥接已启动 IsRunning={running} Port={port}");
        }
        catch (Exception e)
        {
            Debug.LogError("[AutoMCP] 启动失败: " + e);
        }
    }
}
