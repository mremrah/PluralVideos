using PluralVideos.Helpers;
using PluralVideos.Services.Video;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PluralVideos.Services
{
    // Thank to https://codereview.stackexchange.com/questions/145938/semaphore-based-concurrent-work-queue
    // https://markheath.net/post/constraining-concurrent-threads-csharp
    public class DownloadEventArgs : EventArgs
    {
        public DownloadEventArgs(bool succeeded, int moduleId, string moduleTitle, int clipId, string clipTitle, int duration, int totalSize, ApiError error = null)
        {
            Succeeded = succeeded;
            ModuleId = moduleId;
            ModuleTitle = moduleTitle;
            ClipId = clipId;
            ClipTitle = clipTitle;
            Duration = duration;
            TotalSize = totalSize;
            Error = error;
        }

        public int TotalClips { get; set; }

        public int DownloadedClips { get; set; }

        public bool Succeeded { get; set; }

        public int ModuleId { get; }

        public string ModuleTitle { get; set; }

        public int ClipId { get; set; }

        public string ClipTitle { get; set; }

        public int Duration { get; }

        public int TotalSize { get; }

        public ApiError Error { get; }

        public Header CourseHeader { get; set; }
    }

    public sealed class TaskQueue : IDisposable
    {
        private readonly SemaphoreSlim semaphore;

        private readonly ConcurrentQueue<DownloadClient> clients = new ConcurrentQueue<DownloadClient>();

        public EventHandler<DownloadEventArgs> ProcessCompleteEvent;

        public EventHandler<string> FileAlreadyDownloadedEvent;

        public TaskQueue()
        {
            var concurrentDownloads = Math.Max(5, Environment.ProcessorCount - 3);
            semaphore = new SemaphoreSlim(concurrentDownloads);
        }

        public void Enqueue(DownloadClient client)
        {
            clients.Enqueue(client);
        }

        readonly object sync = new object();
        public async Task Execute()
        {
            var clips = clients.Count;
            var downloadedClips = 0;
            var downloadDuration = 0L;
            var downloadedBytes = 0L;
            var tasks = new List<Task>();

            string[] completedFiles = null;

            while (clients.TryDequeue(out var client))
            {
                if (client != null)
                {
                    if (completedFiles == null && File.Exists(client.LogFile))
                    {
                        completedFiles = File.ReadAllLines(client.LogFile);
                    }

                    if (completedFiles != null && completedFiles.Contains(client.FilePath))
                    {
                        OnFileAlreadyDownloadedEvent(client.FilePath);
                        downloadedClips++;
                        continue;
                    }

                    await semaphore.WaitAsync();
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var (completed, duration, totalSize, error) = await client.Download();
                            if (completed && error == null)
                            {
                                Interlocked.Add(ref downloadDuration, duration);
                                Interlocked.Add(ref downloadedBytes, totalSize);
                                Interlocked.Increment(ref downloadedClips);
                                if (!string.IsNullOrEmpty(client.FilePath))
                                {
                                    lock (sync)
                                    {
                                        File.AppendAllText(client.LogFile, Environment.NewLine + client.FilePath);
                                    }
                                }
                            }
                            else
                            {
                                clients.Enqueue(client);
                            }


                            OnRaiseDownloadEvent(new DownloadEventArgs(completed, client.ModuleId, client.ModuleTitle, client.Clip.Index, client.Clip.Title, duration, totalSize, error)
                            {
                                TotalClips = clips,
                                DownloadedClips = downloadedClips,
                                CourseHeader = client.Course
                            });
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }
            }

            await Task.WhenAll(tasks);
        }

        public void Dispose()
        {
            semaphore.Dispose();
        }

        private void OnRaiseDownloadEvent(DownloadEventArgs e)
        {
            ProcessCompleteEvent?.Invoke(this, e);
        }

        private void OnFileAlreadyDownloadedEvent(string e)
        {
            FileAlreadyDownloadedEvent?.Invoke(this, e);
        }
    }
}
