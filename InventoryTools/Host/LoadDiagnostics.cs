using System;
using System.Diagnostics;
using AllaganLib.GameSheets.Service;
using Autofac;
using Autofac.Core;
using Autofac.Core.Resolving.Pipeline;
using CriticalCommonLib.Helpers;
using Dalamud.Plugin.Services;

namespace InventoryTools.Host;

/// <summary>
/// 在容器建立實例的那一刻記下耗時與配置（<see cref="LoadDiag"/>），只包裝、不改解析結果。
/// 要在被量的元件註冊之前掛上 <c>Registered</c> 事件，之後的註冊才看得到。
/// </summary>
internal static class LoadTimingMiddleware
{
    public static void Attach(IComponentRegistration registration)
    {
        var limitType = registration.Activator.LimitType;
        var label = limitType == typeof(SheetManager) ? "遊戲資料表快取（AllaganLib）"
            : limitType == typeof(InventoryToolsConfiguration) ? "主設定"
            : null;
        if (label == null)
        {
            return;
        }

        registration.PipelineBuilding += (_, pipeline) =>
            pipeline.Use(PipelinePhase.Activation, MiddlewareInsertionMode.StartOfPhase, (context, next) =>
            {
                var timer = LoadTimer.Start();
                next(context);
                LoadDiag.Record(label, timer);
            });
    }
}

/// <summary>
/// 載入後一段時間內，本插件的 <c>Framework.Update</c> 處理器每幀合計花多久，結束時寫一行 <c>[LoadDiag]</c> 並退訂。
/// 同一插件的處理器依訂閱順序呼叫：<see cref="LoadFrameMeter(IFramework, IPluginLog)"/> 要在任何服務訂閱之前建立、
/// <see cref="Arm"/> 在全部服務啟動之後呼叫；之後才訂閱的處理器不在量測內。只在框架執行緒上讀寫欄位。
/// </summary>
internal sealed class LoadFrameMeter
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly IFramework _framework;
    private readonly IPluginLog _pluginLog;
    private long _armedAt;
    private long _frameStart;
    private TimeSpan _gcPauseAtFrameStart;
    private bool _inFrame;
    private int _frames;
    private int _slowestFrame;
    private double _totalMs;
    private double _slowestMs;
    private double _slowestGcMs;

    public LoadFrameMeter(IFramework framework, IPluginLog pluginLog)
    {
        _framework = framework;
        _pluginLog = pluginLog;
        _framework.Update += Begin;
    }

    public void Arm()
    {
        _armedAt = Stopwatch.GetTimestamp();
        _framework.Update += End;
    }

    private void Begin(IFramework framework)
    {
        _frameStart = Stopwatch.GetTimestamp();
        _gcPauseAtFrameStart = GC.GetTotalPauseDuration();
        _inFrame = true;
    }

    private void End(IFramework framework)
    {
        if (!_inFrame)
        {
            return;
        }

        _inFrame = false;
        var ms = Stopwatch.GetElapsedTime(_frameStart).TotalMilliseconds;
        _frames++;
        _totalMs += ms;
        if (ms > _slowestMs)
        {
            _slowestMs = ms;
            _slowestFrame = _frames;
            _slowestGcMs = (GC.GetTotalPauseDuration() - _gcPauseAtFrameStart).TotalMilliseconds;
        }

        if (Stopwatch.GetElapsedTime(_armedAt) < Window)
        {
            return;
        }

        _framework.Update -= Begin;
        _framework.Update -= End;
        _pluginLog.Information(
            $"[LoadDiag] 載入後 {Window.TotalSeconds:F0} 秒內本插件每幀更新：{_frames} 幀、合計 {_totalMs:F1} ms、最慢一幀 {_slowestMs:F1} ms（第 {_slowestFrame} 幀，其中 GC 暫停 {_slowestGcMs:F1} ms）");
    }
}
