using PluralVideos.Options;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using PluralVideos.Helpers;
using PluralVideos.Extensions;
using PluralVideos.Services.Video;
using PluralVideos.Services;

namespace PluralVideos
{
    public class Downloader : IDisposable
    {
        private readonly DownloaderOptions options;

        private readonly TaskQueue queue;

        private readonly PluralSightApi api;

        private readonly HttpClient httpClient = new HttpClient();
        private bool isDisposed;

        public Downloader(DownloaderOptions options)
        {
            this.options = options;
            api = new PluralSightApi(options.Timeout);
            queue = new TaskQueue();
            queue.ProcessCompleteEvent += ClipDownloaded;
            queue.FileAlreadyDownloadedEvent += FileAlreadyDownloaded;
        }

        public async Task Download()
        {
            if (options.ListClip || options.ListModule)
                Utils.WriteRedText("--list cannot be used with --clip or --module");

            var course = await GetCourseAsync(options.CourseId, list: false);
            Utils.WriteYellowText($"Downloading from course'{course.Header.Title}' started ...");

            if (options.DownloadClip)
            {
                var (clip, index, title) = course.GetClip(options.ClipId);
                GetClipAsync(clip, course.Header, index, title, list: false);
            }
            else if (options.DownloadModule)
            {
                var (module, index) = course.Modules.WithIndex()
                    .FirstOrDefault(i => i.item.Id == options.ModuleId);

                GetModuleAsync(course.Header, module, index, list: false);
            }
            else
            {
                foreach (var (module, index) in course.Modules.WithIndex())
                    GetModuleAsync(course.Header, module, index, options.ListCourse);
            }

            Utils.WriteGreenText($"[{DateTime.Now.ToShortDateString()}-{DateTime.Now.ToLongTimeString()}]\tDownloading has started ...");

            await queue.Execute();

            Utils.WriteYellowText($"{DateTime.Now.ToShortDateString()}-{DateTime.Now.ToLongTimeString()}]\tDownload completed");
        }

        private async Task<Course> GetCourseAsync(string courseName, bool list)
        {
            var courseResponse = await api.Video.GetCourse(courseName);
            if (!courseResponse.Success)
                throw new Exception($"Course was not found. Error: {courseResponse.ResponseBody}");

            var hasAccess = await api.Video.HasCourseAccess(courseResponse.Data.Header.Id);
            var noAccess = (!hasAccess.HasValue || !hasAccess.Value);
            if (noAccess && !list)
                throw new Exception("You do not have permission to download this course");
            else if (!noAccess && list)
                Utils.WriteRedText("Warning: You do not have permission to download this course");

            return courseResponse.Data;
        }

        private void GetModuleAsync(Header course, Module module, int index, bool list)
        {
            if (module == null)
                throw new Exception("The module was not found. Check the module and Try again.");

            if (list)
            {
                Utils.WriteGreenText($"\t{index}. {module.Title}", newLine: false);
                Utils.WriteBlueText($"  --  {module.Id}");
            }

            foreach (var clip in module.Clips)
                GetClipAsync(clip, course, index, module.Title, list);
        }

        private void GetClipAsync(Clip clip, Header course, int moduleId, string moduleTitle, bool list)
        {
            if (clip == null)
                throw new Exception("The clip was not found. Check the clip and Try again.");

            if (list)
            {
                Utils.WriteText($"\t\t{clip.Index}. {clip.Title}", newLine: false);
                Utils.WriteCyanText($"  --  {clip.Id}");
                return;
            }

            var client = new DownloadClient(options.OutputPath, course, moduleId, moduleTitle, clip, httpClient, api.GetAccessToken);
            queue.Enqueue(client);
        }

        private void ClipDownloaded(object sender, DownloadEventArgs e)
        {
            Utils.WriteGreenText($"\n[{DateTime.Now.ToShortDateString()}-{DateTime.Now.ToLongTimeString()}] - [{e.DownloadedClips}/{e.TotalClips}] {e.CourseHeader.Name} - {e.ModuleId}. {e.ModuleTitle}");
            if (e.Succeeded)
            {
                int duration = e.Duration;
                if (duration <= 0)
                {
                    duration = 1000;
                }

                int seconds = duration / 1000;
                int second =  seconds > 60 ? seconds % 60 : seconds;
                int minute = seconds / 60;
                float baudrate = (e.TotalSize / 1024) / (duration / 1000);

                Utils.WriteText($"\t\t{e.ClipId}. {e.ClipTitle}  --  downloaded in (HH:mm:ss.ms) {minute}:{second}.{duration}, {e.TotalSize / 1024} KB ({baudrate}/KBps)");
            }
            else
            {
                Utils.WriteRedText($"\t\t{e.ClipId}. {e.ClipTitle} --  Download failed. will retry again.");
                if (e.Error != null)
                {
                    Utils.WriteRedText($"\t\t{e.Error.Message}");
                }
            }
        }

        private void FileAlreadyDownloaded(object sender, string e)
        {
            Utils.WriteGreenText($"\n[{DateTime.Now.ToShortDateString()}-{DateTime.Now.ToLongTimeString()}] - File '{e}' Already downloaded!");
        }


        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    if (queue != null)
                    {
                        queue.ProcessCompleteEvent -= ClipDownloaded;
                    }
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                isDisposed = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~Downloader()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
