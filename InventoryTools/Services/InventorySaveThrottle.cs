using System;
using System.Diagnostics;
using System.Globalization;

namespace InventoryTools.Services;

/// <summary>
/// inventories.csv 的存檔節流：只決定「現在該不該寫」並累計統計，不碰檔案也不起執行緒。
/// 時間一律取自建構時給的時鐘（須單調遞增）。
/// 所有成員由內部鎖保護、可從任何執行緒呼叫；鎖內不呼叫外部程式碼。
/// </summary>
public sealed class InventorySaveThrottle
{
    /// <summary>最後一次變動後安靜這麼多秒才寫。</summary>
    public const double QuietSeconds = 3;

    /// <summary>連續變動時，從第一次變髒起最多這麼多秒一定寫一次。</summary>
    public const double MaxDirtySeconds = 30;

    /// <summary>寫失敗後至少隔這麼多秒才重試。</summary>
    public const double MinRetrySeconds = 5;

    /// <summary>統計行的視窗長度；視窗由該段時間的第一個變動事件開啟，沒有事件就沒有視窗。</summary>
    public const double ReportSeconds = 3600;

    public enum WriteReason
    {
        Quiet,
        MaxAge,
        Flush,
    }

    public enum WriteResult
    {
        Written,
        Failed,

        /// <summary>沒寫：已有更新的快照落地。</summary>
        Superseded,
    }

    private readonly Func<TimeSpan> _clock;
    private readonly object _gate = new();

    private bool _dirty;
    private TimeSpan _firstDirtyAt;
    private TimeSpan _lastChangeAt;
    private bool _flushRequested;

    private bool _writing;
    private WriteReason _writingReason;
    private TimeSpan _writingFirstDirtyAt;
    private TimeSpan _writingLastChangeAt;

    private bool _retryGated;
    private TimeSpan _retryNotBefore;
    private bool _deferLatched;
    private int _consecutiveFailures;

    private bool _windowOpen;
    private TimeSpan _windowStart;
    private bool _hasLastEvent;
    private TimeSpan _lastEventAt;
    private int _events;
    private int _gapUnder075;
    private int _gapUnder2;
    private int _gapUnder10;
    private int _gapLonger;
    private int _writesQuiet;
    private int _writesMaxAge;
    private int _writesFlush;
    private int _writesStop;
    private int _failed;
    private int _deferred;

    public InventorySaveThrottle()
        : this(static () => Stopwatch.GetElapsedTime(0))
    {
    }

    public InventorySaveThrottle(Func<TimeSpan> clock)
    {
        _clock = clock;
    }

    public bool IsDirty
    {
        get
        {
            lock (_gate)
            {
                return _dirty;
            }
        }
    }

    public bool IsWriting
    {
        get
        {
            lock (_gate)
            {
                return _writing;
            }
        }
    }

    /// <summary>庫存有變動（每個變動事件呼叫一次）。</summary>
    public void MarkDirty()
    {
        lock (_gate)
        {
            var now = _clock();
            if (!_windowOpen)
            {
                _windowOpen = true;
                _windowStart = now;
            }

            _events++;
            if (_hasLastEvent)
            {
                var gap = (now - _lastEventAt).TotalSeconds;
                if (gap < 0.75)
                {
                    _gapUnder075++;
                }
                else if (gap < 2)
                {
                    _gapUnder2++;
                }
                else if (gap < 10)
                {
                    _gapUnder10++;
                }
                else
                {
                    _gapLonger++;
                }
            }

            _hasLastEvent = true;
            _lastEventAt = now;

            if (!_dirty)
            {
                _dirty = true;
                _firstDirtyAt = now;
            }

            _lastChangeAt = now;
        }
    }

    /// <summary>登出／換角：有沒寫的變動就不等安靜期。沒有變動時不做事。</summary>
    public void RequestFlush()
    {
        lock (_gate)
        {
            if (_dirty)
            {
                _flushRequested = true;
            }
        }
    }

    /// <summary>
    /// 該寫就回 true 並進入「寫入中」；呼叫端要在同一條執行緒上立刻取快照，寫完呼叫 <see cref="EndWrite"/>。
    /// 寫入中或失敗後的重試間隔內一律回 false。
    /// </summary>
    public bool TryBeginWrite()
    {
        lock (_gate)
        {
            if (!_dirty)
            {
                return false;
            }

            var now = _clock();
            WriteReason reason;
            if (_flushRequested)
            {
                reason = WriteReason.Flush;
            }
            else if ((now - _lastChangeAt).TotalSeconds >= QuietSeconds)
            {
                reason = WriteReason.Quiet;
            }
            else if ((now - _firstDirtyAt).TotalSeconds >= MaxDirtySeconds)
            {
                reason = WriteReason.MaxAge;
            }
            else
            {
                return false;
            }

            if (_writing || (_retryGated && now < _retryNotBefore))
            {
                if (!_deferLatched)
                {
                    _deferLatched = true;
                    _deferred++;
                }

                return false;
            }

            _writing = true;
            _writingReason = reason;
            _writingFirstDirtyAt = _firstDirtyAt;
            _writingLastChangeAt = _lastChangeAt;
            _dirty = false;
            _flushRequested = false;
            _deferLatched = false;
            return true;
        }
    }

    /// <summary>
    /// 一次寫入結束。失敗時把髒狀態還原成寫入前的樣子並啟動重試間隔。
    /// 回傳目前連續失敗的次數（成功為 0）。
    /// </summary>
    public int EndWrite(WriteResult result)
    {
        lock (_gate)
        {
            _writing = false;
            if (result != WriteResult.Failed)
            {
                if (result == WriteResult.Written)
                {
                    switch (_writingReason)
                    {
                        case WriteReason.Quiet:
                            _writesQuiet++;
                            break;
                        case WriteReason.MaxAge:
                            _writesMaxAge++;
                            break;
                        default:
                            _writesFlush++;
                            break;
                    }
                }

                _retryGated = false;
                _consecutiveFailures = 0;
                return 0;
            }

            _failed++;
            _consecutiveFailures++;
            _retryGated = true;
            _retryNotBefore = _clock() + TimeSpan.FromSeconds(MinRetrySeconds);
            if (!_dirty)
            {
                _lastChangeAt = _writingLastChangeAt;
            }

            _dirty = true;
            _firstDirtyAt = _writingFirstDirtyAt;
            if (_writingReason == WriteReason.Flush)
            {
                _flushRequested = true;
            }

            return _consecutiveFailures;
        }
    }

    /// <summary>卸載時那一次不經節流的寫入，只進統計。</summary>
    public void RecordStopWrite(bool success)
    {
        lock (_gate)
        {
            if (success)
            {
                _writesStop++;
                _dirty = false;
                _flushRequested = false;
            }
            else
            {
                _failed++;
            }
        }
    }

    /// <summary>
    /// 視窗滿 <see cref="ReportSeconds"/>（或 <paramref name="final"/>）且期間有變動事件時，給出一行統計並重設。
    /// </summary>
    public bool TryTakeReport(bool final, out string line)
    {
        lock (_gate)
        {
            line = string.Empty;
            if (!_windowOpen)
            {
                return false;
            }

            var elapsed = (_clock() - _windowStart).TotalSeconds;
            if (!final && elapsed < ReportSeconds)
            {
                return false;
            }

            var writes = _writesQuiet + _writesMaxAge + _writesFlush + _writesStop;
            line = string.Create(
                CultureInfo.InvariantCulture,
                $"[InvSaveDiag] window={elapsed:F0}s events={_events} gap<0.75s={_gapUnder075} gap<2s={_gapUnder2} gap<10s={_gapUnder10} gap>=10s={_gapLonger} writes={writes} (quiet={_writesQuiet} cap={_writesMaxAge} flush={_writesFlush} stop={_writesStop}) failed={_failed} deferred={_deferred}");

            _windowOpen = false;
            _events = 0;
            _gapUnder075 = 0;
            _gapUnder2 = 0;
            _gapUnder10 = 0;
            _gapLonger = 0;
            _writesQuiet = 0;
            _writesMaxAge = 0;
            _writesFlush = 0;
            _writesStop = 0;
            _failed = 0;
            _deferred = 0;
            return true;
        }
    }
}
