using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace InduLink.Simulation
{
    /// <summary>Serializes service lifecycle and owns the optional periodic value generator.</summary>
    public abstract class ProtocolSimulator : IAsyncDisposable
    {
        private readonly SemaphoreSlim _lifecycle = new SemaphoreSlim(1, 1);
        private readonly object _disposeSync = new object();
        private readonly Dictionary<string, SimulationPoint> _points;
        private readonly TimeSpan _interval;
        private CancellationTokenSource _changesCancellation;
        private Task _changesTask = Task.CompletedTask;
        private Task _disposeTask;
        private int _disposed;
        private int _running;
        private string _lastError;

        protected ProtocolSimulator(IEnumerable<SimulationPoint> points, StringComparer comparer, TimeSpan? interval)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            _points = new Dictionary<string, SimulationPoint>(comparer);
            foreach (var point in points)
            {
                if (point == null) throw new ArgumentException("Points cannot contain null entries.", nameof(points));
                if (!_points.TryAdd(point.Address, point)) throw new ArgumentException("Duplicate point: " + point.Address);
            }
            if (_points.Count == 0) throw new ArgumentException("At least one simulator point is required.", nameof(points));
            _interval = interval ?? TimeSpan.FromSeconds(1);
            if (_interval < TimeSpan.FromMilliseconds(20) || _interval.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(interval), "Update interval must be between 20 ms and Int32.MaxValue ms.");
        }

        protected IEnumerable<SimulationPoint> Points => _points.Values;
        public bool IsRunning => Volatile.Read(ref _running) != 0;
        public string LastError => Volatile.Read(ref _lastError);
        public abstract string Endpoint { get; }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (IsRunning) return;
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    foreach (var point in Points) SetPointValue(point, point.ParseValue(point.InitialValue));
                    await StartTransportAsync(cancellationToken).ConfigureAwait(false);
                    _changesCancellation = new CancellationTokenSource();
                    Volatile.Write(ref _lastError, null);
                    Volatile.Write(ref _running, 1);
                    _changesTask = ChangeValuesAsync(_changesCancellation.Token);
                }
                catch
                {
                    await StopTransportAsync().ConfigureAwait(false);
                    throw;
                }
            }
            finally { _lifecycle.Release(); }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await StopCoreAsync().ConfigureAwait(false); }
            finally { _lifecycle.Release(); }
        }

        private async Task StopCoreAsync()
        {
            Volatile.Write(ref _running, 0);
            if (_changesCancellation != null)
            {
                _changesCancellation.Cancel();
                await _changesTask.ConfigureAwait(false);
                _changesCancellation.Dispose();
                _changesCancellation = null;
            }
            await StopTransportAsync().ConfigureAwait(false);
        }

        public IReadOnlyList<SimulationPointValue> ReadValues()
        {
            ThrowIfDisposed();
            return Points.Select(point => new SimulationPointValue(point, ReadPointValue(point))).ToArray();
        }

        public void SetValue(string address, string value)
        {
            ThrowIfDisposed();
            if (!_points.TryGetValue(address, out var point)) throw new ArgumentException("Unknown simulator point: " + address);
            SetPointValue(point, point.ParseValue(value));
        }

        private async Task ChangeValuesAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(_interval);
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                    foreach (var point in Points)
                    {
                        if (point.Behavior == SimulationBehavior.Fixed) continue;
                        try { AdvancePoint(point); }
                        catch (Exception ex) when (ex is OverflowException || ex is FormatException)
                        {
                            // Keep the last valid value if a generator reaches its data type limit.
                            Volatile.Write(ref _lastError, point.Address + ": " + ex.Message);
                        }
                    }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }

        public ValueTask DisposeAsync()
        {
            lock (_disposeSync)
            {
                if (_disposeTask == null)
                {
                    Interlocked.Exchange(ref _disposed, 1);
                    _disposeTask = StopAsync();
                }
                return new ValueTask(_disposeTask);
            }
        }

        protected void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(GetType().Name);
        }
        protected abstract Task StartTransportAsync(CancellationToken cancellationToken);
        protected abstract Task StopTransportAsync();
        protected abstract object ReadPointValue(SimulationPoint point);
        protected abstract void SetPointValue(SimulationPoint point, object value);
        protected abstract void AdvancePoint(SimulationPoint point);
    }
}
