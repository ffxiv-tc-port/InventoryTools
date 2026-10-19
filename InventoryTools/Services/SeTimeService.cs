using System;
using AllaganLib.Shared.Time;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using GameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace InventoryTools.Services;

/// <summary>
/// 取代 AllaganLib.Shared 的 <see cref="SeTime"/>：位址都解得出時逐行等同原實作；
/// GetServerTime 解不出時改用本機時鐘、Framework 解不出時艾歐澤亞時間改由伺服器時間推算，不讓插件載入失敗。
/// 原實作從不觸發 <see cref="WeatherChanged"/>，這裡照舊。
/// </summary>
public sealed class SeTimeService : ISeTime
{
    private static readonly bool ServerTimeAvailable = GameFramework.Addresses.GetServerTime.Value != 0;
    private static readonly bool FrameworkAvailable = GameFramework.Addresses.Instance.Value != 0;

    private readonly IFramework _framework;

    public TimeStamp ServerTime { get; private set; }
    public TimeStamp EorzeaTime { get; private set; }
    public long EorzeaTotalMinute { get; private set; }
    public long EorzeaTotalHour { get; private set; }
    public short EorzeaMinuteOfDay { get; private set; }
    public byte EorzeaHourOfDay { get; private set; }
    public byte EorzeaMinuteOfHour { get; private set; }

    public event Action? Updated;
    public event Action? HourChanged;
#pragma warning disable CS0067 // 介面要求；原實作同樣從不觸發
    public event Action? WeatherChanged;
#pragma warning restore CS0067

    public SeTimeService(IFramework framework, ILogger<SeTimeService> logger)
    {
        _framework = framework;
        if (!ServerTimeAvailable || !FrameworkAvailable)
        {
            logger.LogWarning(
                "遊戲改版後 Framework 的位址對不上（GetServerTime {ServerTime}、Instance {Instance}）：伺服器與艾歐澤亞時間改用本機時鐘推算，可能差幾秒；其餘功能照常。",
                ServerTimeAvailable ? "有" : "無", FrameworkAvailable ? "有" : "無");
        }
        Update(null);
        _framework.Update += Update;
    }

    public void Dispose()
    {
        _framework.Update -= Update;
    }

    private static TimeStamp GetServerTime()
        => ServerTimeAvailable
            ? new TimeStamp(GameFramework.GetServerTime() * 1000)
            : new TimeStamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1000);

    private unsafe TimeStamp GetEorzeaTime()
    {
        var framework = FrameworkAvailable ? GameFramework.Instance() : null;
        if (framework == null)
        {
            return ServerTime.ConvertToEorzea();
        }
        if (Math.Abs(new TimeStamp(framework->UtcTime.Timestamp * 1000) - ServerTime) >= 5000)
        {
            return ServerTime.ConvertToEorzea();
        }
        return new TimeStamp(framework->ClientTime.EorzeaTime * 1000);
    }

    private void Update(IFramework? _)
    {
        ServerTime = GetServerTime();
        EorzeaTime = GetEorzeaTime();
        var totalMinutes = EorzeaTime.TotalMinutes;
        if (totalMinutes != EorzeaTotalMinute)
        {
            EorzeaTotalMinute = totalMinutes;
            EorzeaMinuteOfDay = (short)(EorzeaTotalMinute % 1440);
            EorzeaMinuteOfHour = (byte)(EorzeaMinuteOfDay % 60);
        }
        var hour = EorzeaTotalMinute / 60;
        if (hour != EorzeaTotalHour)
        {
            EorzeaTotalHour = hour;
            EorzeaHourOfDay = (byte)(EorzeaMinuteOfDay / 60);
            HourChanged?.Invoke();
        }
        Updated?.Invoke();
    }
}
