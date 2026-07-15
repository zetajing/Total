using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Runtime.Polling
{
    /// <summary>
    /// 按设备合并轮询的调度器。同一客户端仅运行一个后台循环，多个订阅到期时会合并重复点位，
    /// 减少重复请求，并使用固定节拍推进下一次轮询时间，避免“读取耗时 + Interval”造成累计漂移。
    /// </summary>
    /// <summary>订阅状态、调度时间和变更比较。</summary>
    public sealed partial class PollingScheduler : IPollingScheduler
    {        private sealed class SubscriptionRegistration
        {
            private readonly object _sync = new object();
            private string _lastFingerprint;
            private DateTimeOffset _nextDueUtc;

            public SubscriptionRegistration(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler)
            {
                Request = request;
                Handler = handler;
                _nextDueUtc = DateTimeOffset.UtcNow;
            }

            public SubscriptionRequest Request { get; private set; }
            public EventHandler<SubscriptionEvent> Handler { get; private set; }

            public bool IsDue(DateTimeOffset now)
            {
                lock (_sync)
                    return now >= _nextDueUtc;
            }

            public DateTimeOffset GetNextDueUtc()
            {
                lock (_sync)
                    return _nextDueUtc;
            }

            public void AdvanceSchedule(DateTimeOffset now)
            {
                lock (_sync)
                {
                    do
                    {
                        _nextDueUtc = _nextDueUtc.Add(Request.Interval);
                    }
                    while (_nextDueUtc <= now);
                }
            }

            public bool ShouldReport(IReadOnlyList<DataValue> values)
            {
                if (!Request.ReportOnChangeOnly)
                    return true;

                var fingerprint = BuildFingerprint(values);
                lock (_sync)
                {
                    if (string.Equals(_lastFingerprint, fingerprint, StringComparison.Ordinal))
                        return false;
                    _lastFingerprint = fingerprint;
                    return true;
                }
            }
        }
    }
}
