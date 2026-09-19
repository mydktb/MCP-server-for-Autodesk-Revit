using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace MKRevitMCP
{
    public class RevitTask : IExternalEventHandler
    {
        private static RevitTask _instance;
        private static ExternalEvent _externalEvent;

        private readonly ConcurrentQueue<Job> _jobs = new ConcurrentQueue<Job>();

        private class Job
        {
            public Func<UIApplication, string> Work;
            public TaskCompletionSource<string> Completion;
        }

        public static void Initialize()
        {
            _instance = new RevitTask();
            _externalEvent = ExternalEvent.Create(_instance);
            Log.Write("RevitTask initialized.");
        }

        public static Task<string> RunAsync(Func<UIApplication, string> work)
        {
            if (_instance == null || _externalEvent == null)
            {
                Log.Write("ERROR: RevitTask not initialized.");
                throw new InvalidOperationException("RevitTask not initialized.");
            }

            var job = new Job
            {
                Work = work,
                Completion = new TaskCompletionSource<string>(
                    TaskCreationOptions.RunContinuationsAsynchronously)
            };

            _instance._jobs.Enqueue(job);
            Log.Write("Job enqueued. Raising ExternalEvent...");

            var request = _externalEvent.Raise();
            Log.Write($"Raise() returned: {request}");

            return job.Completion.Task;
        }

        public void Execute(UIApplication app)
        {
            Log.Write($"Execute called on Revit thread. Queue depth: {_jobs.Count}");

            while (_jobs.TryDequeue(out var job))
            {
                try
                {
                    var result = job.Work(app);
                    Log.Write($"Work completed: {result}");
                    job.Completion.SetResult(result);
                }
                catch (Exception ex)
                {
                    Log.Write($"Work threw: {ex.Message}");
                    job.Completion.SetException(ex);
                }
            }
        }

        public string GetName() => "MKRevitMCP Revit Task";
    }
}