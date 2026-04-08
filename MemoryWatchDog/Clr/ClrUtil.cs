namespace MemoryWatchDog
{
    using System;
    using System.Collections.Generic;
    using System.Runtime;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Diagnostics.NETCore.Client;

    public static class ClrUtil
    {
        public static void ForceGC()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // This is for compression the LOH (Large Object Heap) - this is not done by defualt and could fragment your memory and and memory could grow
                // https://web.archive.org/web/20201027035717/https://www.wintellect.com/hey-who-stole-all-my-memory/
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();
            }
            catch
            {
                // ignore
            }
        }

        public static void ForceRemoteGC(int processId)
        {
            var client = new DiagnosticsClient(processId);
            var providers = new List<EventPipeProvider>
            {
                new EventPipeProvider(
                    "Microsoft-Windows-DotNETRuntime",
                    System.Diagnostics.Tracing.EventLevel.Informational,
                    (long)0x800000) // GCHeapCollect keyword - induces a GC on the target process
            };

            EventPipeSession session = null;
            try
            {
                session = client.StartEventPipeSession(providers, requestRundown: false);

                // Drain the event stream on a background thread to prevent Stop() from deadlocking.
                // Without this, the pipe buffer fills up and Stop() blocks forever waiting for the
                // runtime to acknowledge the stop command.
                var drainTask = Task.Run(() =>
                {
                    try
                    {
                        var buffer = new byte[4096];
                        while (session.EventStream.Read(buffer, 0, buffer.Length) > 0)
                        {
                        }
                    }
                    catch
                    {
                        // Stream will throw when session is stopped, which is expected
                    }
                });

                // Give the runtime time to execute the induced GC
                Thread.Sleep(1000);

                session.Stop();
                drainTask.Wait(TimeSpan.FromSeconds(5));
            }
            finally
            {
                session?.Dispose();
            }
        }
    }
}
