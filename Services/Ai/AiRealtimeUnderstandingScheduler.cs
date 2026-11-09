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
        private CancellationTokenSource _sessionCancellation = new();
        private int _sessionGeneration;
        private bool _resettingSession;

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

            lock (_gate)
            {
                if (_resettingSession)
                {
                    return Task.CompletedTask;
                }

                if (turn != null)
                {
                    _window.AddTurn(turn);
                }

                if (!_isRunning)
                {
                    _isRunning = true;
                    int generation = _sessionGeneration;
                    var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessionCancellation.Token);
                    _runnerTask = Task.Run(async () =>
                    {
                        using (linked)
                        {
                            await RunLoopAsync(processor, linked.Token, generation).ConfigureAwait(false);
                        }
                    }, CancellationToken.None);
                }
            }

            return Task.CompletedTask;
        }

        public async Task ResetSessionAsync(CancellationToken cancellationToken = default)
        {
            Task runner;
            CancellationTokenSource previousCancellation;
            lock (_gate)
            {
                _resettingSession = true;
                _sessionGeneration++;
                runner = _runnerTask;
                previousCancellation = _sessionCancellation;
                _window.Clear();
                _lastProcessedVersion = _window.CreateSnapshot().Version;
            }

            try
            {
                // Finish the current request for settlement, but never start the queued window.
                await runner.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // A timed-out provider may still return; its token remains cancelled permanently.
                previousCancellation.Cancel();
                lock (_gate)
                {
                    _sessionCancellation = new CancellationTokenSource();
                    _runnerTask = Task.CompletedTask;
                    _isRunning = false;
                    _resettingSession = false;
                }
                _ = runner.ContinueWith(_ => previousCancellation.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
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
            CancellationToken cancellationToken,
            int generation)
        {
            while (true)
            {
                AiAsrSemanticWindowSnapshot snapshot;
                lock (_gate)
                {
                    if (generation != _sessionGeneration)
                    {
                        return;
                    }
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
                    if (!cancellationToken.IsCancellationRequested) ProcessingFailed?.Invoke(ex);
                }
                catch (OperationCanceledException ex)
                {
                    if (!cancellationToken.IsCancellationRequested) ProcessingFailed?.Invoke(ex);
                }

                lock (_gate)
                {
                    if (generation != _sessionGeneration)
                    {
                        return;
                    }
                    if (snapshot.Version > _lastProcessedVersion)
                    {
                        _lastProcessedVersion = snapshot.Version;
                    }
                }
            }
        }
    }
}
