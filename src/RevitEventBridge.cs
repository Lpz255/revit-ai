using System;
using System.Threading;
using Autodesk.Revit.UI;

namespace RevitAI
{
    internal sealed class RevitJob
    {
        public readonly Func<UIApplication, object> Work;
        public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        public object Result;
        public Exception Error;

        public RevitJob(Func<UIApplication, object> work)
        {
            Work = work;
        }
    }

    internal sealed class RevitEventBridge : IExternalEventHandler
    {
        private readonly ExternalEvent _externalEvent;
        private readonly object _gate = new object();
        private readonly object _runLock = new object();
        private RevitJob _pending;

        public RevitEventBridge()
        {
            _externalEvent = ExternalEvent.Create(this);
        }

        public object Run(Func<UIApplication, object> work, int timeoutMs)
        {
            lock (_runLock)
            {
                return RunCore(work, timeoutMs);
            }
        }

        private object RunCore(Func<UIApplication, object> work, int timeoutMs)
        {
            var job = new RevitJob(work);
            lock (_gate) { _pending = job; }

            int deadline = Environment.TickCount + timeoutMs;
            ExternalEventRequest request = _externalEvent.Raise();

            while (request != ExternalEventRequest.Accepted && Environment.TickCount < deadline)
            {
                Thread.Sleep(50);
                request = _externalEvent.Raise();
            }

            if (request != ExternalEventRequest.Accepted)
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_pending, job)) _pending = null;
                }
                throw new TimeoutException("Revit is busy (" + request + "); retry when the UI is idle");
            }

            if (!job.Done.Wait(timeoutMs))
            {
                throw new TimeoutException("Revit did not finish the command within " + timeoutMs + " ms");
            }

            if (job.Error != null)
            {
                throw new InvalidOperationException(job.Error.Message, job.Error);
            }

            return job.Result;
        }

        public void Execute(UIApplication app)
        {
            RevitJob job;
            lock (_gate)
            {
                job = _pending;
                _pending = null;
            }

            if (job == null) return;

            try
            {
                job.Result = job.Work(app);
            }
            catch (Exception ex)
            {
                job.Error = ex;
                Log.Error("job failed: " + ex.Message);
            }
            finally
            {
                job.Done.Set();
            }
        }

        public string GetName()
        {
            return "RevitAI Bridge";
        }
    }
}
