using System;
using System.Threading;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.Ai
{
    public sealed class AiRealtimeUnderstandingScheduler
    {
        private readonly AiAsrSemanticWindow _window;
        private readonly object _gate = new();
        private bool _isRunning;
        private int _lastProcessedVersion;
        private Task _runnerTask = Task.CompletedTask;

        public event Action<Exception> ProcessingFailed;

        public AiRealtimeUnderstandingScheduler(AiAsrSemanticWindow window = null)
        {
            _window = window ?? new AiAsrSemanticWindow();
        }

        public Task EnqueueAsync(
            AiAsrTurnEnvelope turn,
            Func<AiAsrSemanticWindowSnapshot, CancellationToken, Task> processor,
            CancellationToken cancellationToken = default)
        {
            if (processor == null)
            {
                throw new ArgumentNullException(nameof(processor));
            }

            if (turn != null)
            {
                _window.AddTurn(turn);
            }

            lock (_gate)
            {
                if (!_isRunning)
                {
                    _isRunning = true;
                    _runnerTask = Task.Run(() => RunLoopAsync(processor, cancellationToken), CancellationToken.None);
                }
            }

            return Task.CompletedTask;
        }

        public async Task WaitForIdleAsync(TimeSpan timeout)
        {
            var started = DateTimeOffset.Now;
            while (DateTimeOffset.Now - started < timeout)
            {
                Task task;
                bool isRunning;
                lock (_gate)
                {
                    task = _runnerTask;
                    isRunning = _isRunning;
                }

                if (!isRunning && (task == null || task.IsCompleted))
                {
                    return;
                }

                try
                {
                    if (task != null)
                    {
                        await Task.WhenAny(task, Task.Delay(25)).ConfigureAwait(false);
                    }
                    else
                    {
                        await Task.Delay(25).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Failures are surfaced via ProcessingFailed; tests can still wait for idle.
                }
            }

            throw new TimeoutException("AI实时理解调度器等待空闲超时");
        }

        private async Task RunLoopAsync(
            Func<AiAsrSemanticWindowSnapshot, CancellationToken, Task> processor,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                AiAsrSemanticWindowSnapshot snapshot;
                lock (_gate)
                {
                    snapshot = _window.CreateSnapshot();
                    if (snapshot.Version <= _lastProcessedVersion)
                    {
                        _isRunning = false;
                        return;
                    }
                }

                try
                {
                    await processor(snapshot, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ProcessingFailed?.Invoke(ex);
                }
                catch (OperationCanceledException ex)
                {
                    ProcessingFailed?.Invoke(ex);
                }

                lock (_gate)
                {
                    if (snapshot.Version > _lastProcessedVersion)
                    {
                        _lastProcessedVersion = snapshot.Version;
                    }
                }
            }
        }
    }
}
