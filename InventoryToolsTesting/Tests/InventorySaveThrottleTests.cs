using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InventoryTools.Services;
using Lumina.Data;
using Lumina.Excel;
using LuminaSupplemental.Excel.Model;
using NUnit.Framework;

namespace InventoryToolsTesting.Tests
{
    // 不需要遊戲資料：節流只吃假時鐘，落地測試只用暫存目錄裡的真檔案。
    [TestFixture]
    public class InventorySaveThrottleTests
    {
        private sealed class Row : ICsv
        {
            public string[] Fields = Array.Empty<string>();
            public bool Include = true;
            public bool Throw;

            public void FromCsv(string[] lineData) => Fields = lineData;

            public string[] ToCsv() => Throw ? throw new InvalidOperationException("row failed") : Fields;

            public bool IncludeInCsv() => Include;

            public void PopulateData(ExcelModule gameData, Language language)
            {
            }
        }

        private TimeSpan _now;
        private string _dir = null!;

        private InventorySaveThrottle NewThrottle()
        {
            _now = TimeSpan.FromHours(5);
            return new InventorySaveThrottle(() => _now);
        }

        private void Advance(double seconds) => _now += TimeSpan.FromSeconds(seconds);

        private static List<Row> Rows(string tag, int count = 400)
        {
            return Enumerable.Range(0, count)
                .Select(i => new Row { Fields = new[] { tag, i.ToString(), "道具,\"引號\"", "", "a;b" }, Include = i % 7 != 3 })
                .ToList();
        }

        private static byte[] Legacy(List<Row> rows, string path)
        {
            Assert.That(new ContainerAwareCsvLoader(null!, null!).ToCsvRaw(rows, path), Is.True);
            return File.ReadAllBytes(path);
        }

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "it_save_throttle_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_dir, true);
        }

        [Test]
        public void NoChangeNeverWrites()
        {
            var throttle = NewThrottle();
            for (var i = 0; i < 100; i++)
            {
                Advance(60);
                throttle.RequestFlush();
                Assert.That(throttle.TryBeginWrite(), Is.False);
            }

            Assert.That(throttle.TryTakeReport(true, out _), Is.False);
        }

        [Test]
        public void WritesOnlyAfterQuietPeriod()
        {
            var throttle = NewThrottle();
            throttle.MarkDirty();
            Advance(1);
            throttle.MarkDirty();
            Advance(InventorySaveThrottle.QuietSeconds - 0.1);
            Assert.That(throttle.TryBeginWrite(), Is.False);
            Advance(0.2);
            Assert.That(throttle.TryBeginWrite(), Is.True);
            Assert.That(throttle.IsDirty, Is.False);
            Assert.That(throttle.EndWrite(InventorySaveThrottle.WriteResult.Written), Is.EqualTo(0));
            Advance(600);
            Assert.That(throttle.TryBeginWrite(), Is.False);
        }

        [Test]
        public void ContinuousChangesAreCappedAtMaxDirtyAge()
        {
            var throttle = NewThrottle();
            var writes = new List<double>();
            var start = _now;
            for (var tick = 0; tick < 1000; tick++)
            {
                if (tick % 10 == 0)
                {
                    throttle.MarkDirty();
                }

                if (throttle.TryBeginWrite())
                {
                    writes.Add((_now - start).TotalSeconds);
                    throttle.EndWrite(InventorySaveThrottle.WriteResult.Written);
                }

                Advance(0.1);
            }

            Assert.That(writes.Count, Is.EqualTo(3));
            Assert.That(writes[0], Is.EqualTo(30).Within(0.11));
            Assert.That(writes[1], Is.EqualTo(61).Within(0.11));
            Assert.That(writes[2], Is.EqualTo(92).Within(0.11));
        }

        [Test]
        public void FlushWritesImmediatelyOnlyWhenDirty()
        {
            var throttle = NewThrottle();
            throttle.RequestFlush();
            throttle.MarkDirty();
            Assert.That(throttle.TryBeginWrite(), Is.False);
            throttle.RequestFlush();
            Assert.That(throttle.TryBeginWrite(), Is.True);
            throttle.EndWrite(InventorySaveThrottle.WriteResult.Written);
            Assert.That(throttle.TryBeginWrite(), Is.False);
        }

        [Test]
        public void DoesNotOverlapWhileWriting()
        {
            var throttle = NewThrottle();
            throttle.MarkDirty();
            Advance(3);
            Assert.That(throttle.TryBeginWrite(), Is.True);
            throttle.MarkDirty();
            throttle.RequestFlush();
            Advance(100);
            Assert.That(throttle.TryBeginWrite(), Is.False);
            Assert.That(throttle.IsWriting, Is.True);
            throttle.EndWrite(InventorySaveThrottle.WriteResult.Written);
            Assert.That(throttle.TryBeginWrite(), Is.True);
        }

        [Test]
        public void FailureStaysDirtyAndRetriesNoFasterThanMinInterval()
        {
            var throttle = NewThrottle();
            throttle.MarkDirty();
            Advance(3);
            var attempts = new List<double>();
            var start = _now;
            for (var frame = 0; frame < 60 * 21; frame++)
            {
                if (throttle.TryBeginWrite())
                {
                    attempts.Add((_now - start).TotalSeconds);
                    Assert.That(throttle.EndWrite(InventorySaveThrottle.WriteResult.Failed), Is.EqualTo(attempts.Count));
                    Assert.That(throttle.IsDirty, Is.True);
                }

                Advance(1.0 / 60);
            }

            Assert.That(attempts.Count, Is.EqualTo(5));
            for (var i = 1; i < attempts.Count; i++)
            {
                Assert.That(attempts[i] - attempts[i - 1], Is.GreaterThanOrEqualTo(InventorySaveThrottle.MinRetrySeconds));
            }

            Advance(InventorySaveThrottle.MinRetrySeconds);
            Assert.That(throttle.TryBeginWrite(), Is.True);
            Assert.That(throttle.EndWrite(InventorySaveThrottle.WriteResult.Written), Is.EqualTo(0));
            Assert.That(throttle.IsDirty, Is.False);
        }

        [Test]
        public void FailedFlushKeepsFlushAndOriginalDirtyAge()
        {
            var throttle = NewThrottle();
            throttle.MarkDirty();
            throttle.RequestFlush();
            Assert.That(throttle.TryBeginWrite(), Is.True);
            throttle.EndWrite(InventorySaveThrottle.WriteResult.Failed);
            Advance(InventorySaveThrottle.MinRetrySeconds - 0.5);
            throttle.MarkDirty();
            Assert.That(throttle.TryBeginWrite(), Is.False);
            Advance(0.6);
            Assert.That(throttle.TryBeginWrite(), Is.True, "flush request survives the failure");
            throttle.EndWrite(InventorySaveThrottle.WriteResult.Failed);

            var aged = NewThrottle();
            aged.MarkDirty();
            Advance(29);
            aged.MarkDirty();
            Advance(1);
            Assert.That(aged.TryBeginWrite(), Is.True);
            aged.MarkDirty();
            aged.EndWrite(InventorySaveThrottle.WriteResult.Failed);
            Advance(InventorySaveThrottle.MinRetrySeconds - 2.5);
            aged.MarkDirty();
            Advance(2.5);
            Assert.That(aged.TryBeginWrite(), Is.True, "age is counted from the first unsaved change");
        }

        [Test]
        public void ReportIsHourlyOnlyWithEventsAndHasFixedFormat()
        {
            var throttle = NewThrottle();
            Advance(7200);
            Assert.That(throttle.TryTakeReport(false, out _), Is.False);

            throttle.MarkDirty();
            Advance(0.5);
            throttle.MarkDirty();
            Advance(1);
            throttle.MarkDirty();
            Advance(5);
            throttle.MarkDirty();
            Advance(20);
            throttle.MarkDirty();
            Assert.That(throttle.TryBeginWrite(), Is.False);
            Advance(3);
            Assert.That(throttle.TryBeginWrite(), Is.True);
            throttle.EndWrite(InventorySaveThrottle.WriteResult.Failed);
            Assert.That(throttle.TryBeginWrite(), Is.False);
            Assert.That(throttle.TryBeginWrite(), Is.False);
            Advance(5);
            Assert.That(throttle.TryBeginWrite(), Is.True);
            throttle.EndWrite(InventorySaveThrottle.WriteResult.Written);
            throttle.MarkDirty();
            throttle.RequestFlush();
            Assert.That(throttle.TryBeginWrite(), Is.True);
            throttle.EndWrite(InventorySaveThrottle.WriteResult.Superseded);
            throttle.RecordStopWrite(true);

            Advance(InventorySaveThrottle.ReportSeconds - 34.5 - 0.4);
            Assert.That(throttle.TryTakeReport(false, out _), Is.False);
            Advance(0.6);
            Assert.That(throttle.TryTakeReport(false, out var line), Is.True);
            Assert.That(line, Is.EqualTo("[InvSaveDiag] window=3600s events=6 gap<0.75s=1 gap<2s=1 gap<10s=2 gap>=10s=1 writes=2 (quiet=1 cap=0 flush=0 stop=1) failed=1 deferred=1"));
            Assert.That(throttle.TryTakeReport(true, out _), Is.False);

            throttle.MarkDirty();
            Advance(12);
            Assert.That(throttle.TryTakeReport(true, out line), Is.True);
            Assert.That(line, Is.EqualTo("[InvSaveDiag] window=12s events=1 gap<0.75s=0 gap<2s=0 gap<10s=0 gap>=10s=1 writes=0 (quiet=0 cap=0 flush=0 stop=0) failed=0 deferred=0"));
        }

        [Test]
        public void SaverWritesSnapshotTakenAtWriteTime()
        {
            var throttle = NewThrottle();
            var value = "v1";
            var written = new List<string>();
            using var done = new ManualResetEventSlim();
            var saver = new ThrottledSnapshotSaver<string>(throttle, () => value, s =>
            {
                lock (written)
                {
                    written.Add(s);
                }

                done.Set();
                return null;
            });

            saver.MarkDirty();
            Assert.That(saver.Tick(), Is.False);
            value = "v2";
            Advance(3);
            Assert.That(saver.Tick(), Is.True);
            Assert.That(saver.Tick(), Is.False);
            Assert.That(done.Wait(5000), Is.True);
            SpinWait.SpinUntil(() => !throttle.IsWriting, 5000);
            Assert.That(written, Is.EqualTo(new[] { "v2" }));
            Advance(60);
            Assert.That(saver.Tick(), Is.False);
        }

        [Test]
        public void StopWaitsForInFlightWriteThenWritesLatest()
        {
            var throttle = NewThrottle();
            var value = "old";
            var written = new List<string>();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var saver = new ThrottledSnapshotSaver<string>(throttle, () => value, s =>
            {
                if (s == "old")
                {
                    entered.Set();
                    release.Wait();
                }

                lock (written)
                {
                    written.Add(s);
                }

                return null;
            });

            saver.MarkDirty();
            Advance(3);
            Assert.That(saver.Tick(), Is.True);
            Assert.That(entered.Wait(5000), Is.True);
            value = "new";
            saver.MarkDirty();

            var stop = Task.Run(() => saver.FlushOnStop(TimeSpan.FromSeconds(10)));
            Assert.That(stop.Wait(300), Is.False, "stop must wait for the in-flight write");
            lock (written)
            {
                Assert.That(written, Is.Empty);
            }

            release.Set();
            Assert.That(stop.Wait(5000), Is.True);
            Assert.That(stop.Result, Is.True);
            Assert.That(written, Is.EqualTo(new[] { "old", "new" }));

            // 卸載之後才輪到的舊快照不可以蓋掉較新的。
            typeof(ThrottledSnapshotSaver<string>)
                .GetMethod("WriteQueued", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(saver, new object[] { 1L, "stale" });
            Assert.That(written, Is.EqualTo(new[] { "old", "new" }));
        }

        [Test]
        public void StopGivesUpAfterBoundWhenInFlightWriteHangs()
        {
            var throttle = NewThrottle();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var calls = 0;
            var failures = new List<Exception>();
            var saver = new ThrottledSnapshotSaver<string>(throttle, () => "x", _ =>
            {
                Interlocked.Increment(ref calls);
                entered.Set();
                release.Wait();
                return null;
            }, (e, _) => failures.Add(e));

            saver.MarkDirty();
            Advance(3);
            Assert.That(saver.Tick(), Is.True);
            Assert.That(entered.Wait(5000), Is.True);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            Assert.That(saver.FlushOnStop(TimeSpan.FromMilliseconds(400)), Is.False);
            Assert.That(watch.ElapsedMilliseconds, Is.InRange(300, 2000));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(failures.Single(), Is.InstanceOf<TimeoutException>());
            release.Set();
            SpinWait.SpinUntil(() => !throttle.IsWriting, 5000);
        }

        [Test]
        public void AtomicOutputIsByteIdenticalToLegacyWriter()
        {
            var rows = Rows("甲");
            var legacy = Legacy(rows, Path.Combine(_dir, "legacy.csv"));
            var target = Path.Combine(_dir, "atomic.csv");
            Assert.That(ContainerAwareCsvLoader.ToCsvRawAtomic(rows, target), Is.Null);
            Assert.That(legacy.Length, Is.GreaterThan(1000));
            Assert.That(File.ReadAllBytes(target), Is.EqualTo(legacy));

            File.WriteAllText(target, new string('x', legacy.Length * 3));
            Assert.That(ContainerAwareCsvLoader.ToCsvRawAtomic(rows, target), Is.Null);
            Assert.That(File.ReadAllBytes(target), Is.EqualTo(legacy));
            Assert.That(Directory.GetFiles(_dir).Select(Path.GetFileName), Is.EquivalentTo(new[] { "legacy.csv", "atomic.csv" }));
        }

        [Test]
        public void ReaderHoldingFileMakesReplaceFailAndKeepsOldFileUntilRetry()
        {
            var target = Path.Combine(_dir, "inventories.csv");
            var oldBytes = Legacy(Rows("old"), target);
            var newBytes = Legacy(Rows("new"), Path.Combine(_dir, "expected.csv"));
            File.Delete(Path.Combine(_dir, "expected.csv"));

            var throttle = NewThrottle();
            var current = Rows("new");
            var failures = new ConcurrentQueue<(Exception Error, int Count)>();
            var saver = new ThrottledSnapshotSaver<List<Row>>(
                throttle,
                () => current,
                rows => ContainerAwareCsvLoader.ToCsvRawAtomic(rows, target),
                (e, n) => failures.Enqueue((e, n)));

            using (var reader = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                saver.MarkDirty();
                Advance(3);
                Assert.That(saver.Tick(), Is.True);
                SpinWait.SpinUntil(() => failures.Count == 1, 5000);
                Assert.That(failures.TryPeek(out var failure), Is.True);
                Assert.That(failure.Count, Is.EqualTo(1));
                Assert.That(failure.Error, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
                TestContext.Out.WriteLine("replace failed with " + failure.Error.GetType().Name + ": " + failure.Error.Message.Replace(_dir, "<dir>"));
                Assert.That(throttle.IsDirty, Is.True);
                Assert.That(Directory.GetFiles(_dir).Select(Path.GetFileName), Is.EqualTo(new[] { "inventories.csv" }));

                var seen = new byte[oldBytes.Length + 1];
                var read = 0;
                int n;
                while ((n = reader.Read(seen, read, seen.Length - read)) > 0)
                {
                    read += n;
                }

                Assert.That(seen.Take(read).ToArray(), Is.EqualTo(oldBytes));

                Advance(InventorySaveThrottle.MinRetrySeconds - 0.5);
                Assert.That(saver.Tick(), Is.False);
            }

            Assert.That(File.ReadAllBytes(target), Is.EqualTo(oldBytes));
            Advance(0.6);
            Assert.That(saver.Tick(), Is.True);
            SpinWait.SpinUntil(() => !throttle.IsWriting, 5000);
            Assert.That(failures.Count, Is.EqualTo(1));
            Assert.That(throttle.IsDirty, Is.False);
            Assert.That(File.ReadAllBytes(target), Is.EqualTo(newBytes));
            Assert.That(Directory.GetFiles(_dir).Select(Path.GetFileName), Is.EqualTo(new[] { "inventories.csv" }));
        }

        // 讀者帶 FileShare.Delete 時 File.Move 覆蓋在 Windows 10 仍被拒；別的系統若放行也算對，
        // 這裡只釘「不是成功換成新檔，就是舊檔完整且沒有殘留」。
        [Test]
        public void ReaderSharingDeleteNeverLeavesPartialState()
        {
            var target = Path.Combine(_dir, "inventories.csv");
            var oldBytes = Legacy(Rows("old"), target);
            Exception? error;
            using (var reader = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                error = ContainerAwareCsvLoader.ToCsvRawAtomic(Rows("new"), target);
                using var copy = new MemoryStream();
                reader.CopyTo(copy);
                Assert.That(copy.ToArray(), Is.EqualTo(oldBytes));
            }

            TestContext.Out.WriteLine("reader with FileShare.Delete: " + (error == null ? "replace succeeded" : "replace failed with " + error.GetType().Name));
            if (error == null)
            {
                Assert.That(Encoding.UTF8.GetString(File.ReadAllBytes(target)), Does.StartWith("new,0,"));
            }
            else
            {
                Assert.That(File.ReadAllBytes(target), Is.EqualTo(oldBytes));
            }

            Assert.That(Directory.GetFiles(_dir).Select(Path.GetFileName), Is.EqualTo(new[] { "inventories.csv" }));
        }

        [Test]
        public void ExceptionMidWriteKeepsOldFileAndLeavesNoTempFile()
        {
            var target = Path.Combine(_dir, "inventories.csv");
            var oldBytes = Legacy(Rows("old"), target);
            var rows = Rows("new", 5000);
            var fullLength = Legacy(rows, Path.Combine(_dir, "full.csv")).Length;
            File.Delete(Path.Combine(_dir, "full.csv"));
            Assert.That(rows[4001].Include, Is.True);
            rows[4001].Throw = true;
            var error = ContainerAwareCsvLoader.ToCsvRawAtomic(rows, target);
            Assert.That(error, Is.InstanceOf<InvalidOperationException>());
            Assert.That(File.ReadAllBytes(target), Is.EqualTo(oldBytes));
            Assert.That(Directory.GetFiles(_dir).Select(Path.GetFileName), Is.EqualTo(new[] { "inventories.csv" }));

            // 對照：舊寫法在同樣的中途例外下把正式檔截斷。
            Assert.That(new ContainerAwareCsvLoader(null!, null!).ToCsvRaw(rows, target), Is.False);
            var truncated = File.ReadAllBytes(target);
            Assert.That(truncated, Is.Not.EqualTo(oldBytes));
            Assert.That(truncated.Length, Is.LessThan(fullLength));
        }
    }
}
