using System;
using System.Threading;
using System.Threading.Tasks;

namespace InventoryTools.Services;

/// <summary>
/// 把 <see cref="InventorySaveThrottle"/> 的決定落實成寫檔。
/// 執行緒：<see cref="MarkDirty"/>／<see cref="RequestFlush"/>／<see cref="Tick"/> 在框架執行緒（快照在這裡取）；
/// 寫檔在執行緒池；<see cref="FlushOnStop"/> 在卸載的那條執行緒、且在不再有 Tick 之後。
/// 寫檔之間以 _writeLock 排隊，鎖內只取快照與做檔案 I/O；序號保證較舊的快照不會蓋掉較新的，
/// 卸載開始後還沒輪到的背景寫入一律不寫。
/// </summary>
public sealed class ThrottledSnapshotSaver<TSnapshot>
{
    /// <summary>卸載時最後一次寫入失敗後再試的次數與間隔（正式檔剛好被別人開著讀的情況）。</summary>
    public const int StopRetryCount = 2;

    public const int StopRetryDelayMilliseconds = 150;

    private readonly InventorySaveThrottle _throttle;
    private readonly Func<TSnapshot> _takeSnapshot;
    private readonly Func<TSnapshot, Exception?> _write;
    private readonly Action<Exception, int>? _onFailure;
    private readonly object _writeLock = new();
    private long _nextSequence;
    private long _writtenSequence;
    private volatile bool _stopped;

    /// <param name="write">成功回 null、失敗回例外；失敗時不得動到正式檔。會在執行緒池與卸載的執行緒上被呼叫。</param>
    /// <param name="onFailure">每次寫入失敗時呼叫（例外、連續失敗次數；0＝卸載時那一次）；可能在執行緒池上。</param>
    public ThrottledSnapshotSaver(
        InventorySaveThrottle throttle,
        Func<TSnapshot> takeSnapshot,
        Func<TSnapshot, Exception?> write,
        Action<Exception, int>? onFailure = null)
    {
        _throttle = throttle;
        _takeSnapshot = takeSnapshot;
        _write = write;
        _onFailure = onFailure;
    }

    public InventorySaveThrottle Throttle => _throttle;

    public void MarkDirty() => _throttle.MarkDirty();

    public void RequestFlush() => _throttle.RequestFlush();

    /// <summary>每幀呼叫；該寫時取快照並交給執行緒池。回傳這一幀有沒有起一次寫入。</summary>
    public bool Tick()
    {
        if (_stopped || !_throttle.TryBeginWrite())
        {
            return false;
        }

        TSnapshot snapshot;
        try
        {
            snapshot = _takeSnapshot();
        }
        catch (Exception e)
        {
            Complete(InventorySaveThrottle.WriteResult.Failed, e);
            return false;
        }

        var sequence = Interlocked.Increment(ref _nextSequence);
        QueueWrite(sequence, snapshot);
        return true;
    }

    // lambda 放在這裡而不是 Tick 裡：留在 Tick 時，閉包物件在方法開頭每幀配置一個（不寫檔的那些幀也是）。
    private void QueueWrite(long sequence, TSnapshot snapshot)
    {
        Task.Run(() => WriteQueued(sequence, snapshot));
    }

    /// <summary>
    /// 卸載時必寫：等正在寫的那一份寫完，再把當下最新的快照寫出去。
    /// 等鎖加重試合計不超過 <paramref name="maxWait"/>（自己那幾次寫檔的 I/O 時間不計）。
    /// </summary>
    public bool FlushOnStop(TimeSpan maxWait)
    {
        _stopped = true;
        var deadline = Environment.TickCount64 + (long)maxWait.TotalMilliseconds;
        Exception? error = null;
        for (var attempt = 0; attempt <= StopRetryCount; attempt++)
        {
            var remaining = deadline - Environment.TickCount64;
            if (attempt > 0)
            {
                if (remaining <= StopRetryDelayMilliseconds)
                {
                    break;
                }

                Thread.Sleep(StopRetryDelayMilliseconds);
                remaining -= StopRetryDelayMilliseconds;
            }

            if (!Monitor.TryEnter(_writeLock, TimeSpan.FromMilliseconds(Math.Max(0, remaining))))
            {
                error = new TimeoutException("The previous inventory save did not finish in time.");
                break;
            }

            try
            {
                var sequence = Interlocked.Increment(ref _nextSequence);
                try
                {
                    error = _write(_takeSnapshot());
                }
                catch (Exception e)
                {
                    error = e;
                }

                if (error == null)
                {
                    _writtenSequence = sequence;
                }
            }
            finally
            {
                Monitor.Exit(_writeLock);
            }

            if (error == null)
            {
                break;
            }
        }

        _throttle.RecordStopWrite(error == null);
        if (error != null)
        {
            _onFailure?.Invoke(error, 0);
        }

        return error == null;
    }

    private void WriteQueued(long sequence, TSnapshot snapshot)
    {
        Exception? error = null;
        var result = InventorySaveThrottle.WriteResult.Written;
        lock (_writeLock)
        {
            if (_stopped || sequence < _writtenSequence)
            {
                result = InventorySaveThrottle.WriteResult.Superseded;
            }
            else
            {
                try
                {
                    error = _write(snapshot);
                }
                catch (Exception e)
                {
                    error = e;
                }

                if (error == null)
                {
                    _writtenSequence = sequence;
                }
                else
                {
                    result = InventorySaveThrottle.WriteResult.Failed;
                }
            }
        }

        Complete(result, error);
    }

    private void Complete(InventorySaveThrottle.WriteResult result, Exception? error)
    {
        var consecutiveFailures = _throttle.EndWrite(result);
        if (error != null)
        {
            _onFailure?.Invoke(error, consecutiveFailures);
        }
    }
}
