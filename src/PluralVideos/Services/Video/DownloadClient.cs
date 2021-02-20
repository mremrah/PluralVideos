using PluralVideos.Helpers;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace PluralVideos.Services.Video
{
    public class DownloadClient : BaseClient
    {
        private readonly string outputPath;
        private readonly Header course;

        public int ModuleId { get; }

        public string ModuleTitle { get; }

        public Clip Clip { get; }

        public string LogFile { get; }

        public string FilePath { get; }

        public Header Course => course;

        public string OutputPath => outputPath;

        public DownloadClient(string outputPath, Header course, int moduleId, string moduleTitle, Clip clip, HttpClient httpClientFactory, Func<bool, Task<string>> getAccessToken = null)
            : base(getAccessToken, httpClientFactory)
        {
            this.outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
            this.course = course ?? throw new ArgumentNullException(nameof(course));
            ModuleId = moduleId;
            ModuleTitle = moduleTitle ?? throw new ArgumentNullException(nameof(moduleTitle));
            Clip = clip ?? throw new ArgumentNullException(nameof(clip));
            LogFile = $@"{outputPath}\{course.Title.RemoveInvalidCharacter()}\CompletedFiles.txt";
            FilePath = $@"{outputPath}\{course.Title.RemoveInvalidCharacter()}\{moduleId}. {moduleTitle.RemoveInvalidCharacter()}\{clip.Index}. {clip.Title.RemoveInvalidCharacter()}.mp4";
        }

        public async Task<(bool completed, int duration, int size, ApiError error)> Download()
        {
            var clipsResponse = await GetClipUrls();
            if (!clipsResponse.Success)
                return (false, 0, 0, null);

            var completed = false;
            int totalSize = 0;
            int duration = 0;
            ApiError error = null;

            foreach (var item in clipsResponse.Data.RankedOptions)
            {
                var head = await HeadHttp(item.Url);
                if (!head.Success)
                    continue;

                //var filePath = DownloadFileHelper.GetVideoPath(outputPath, course.Title, ModuleId, ModuleTitle, Clip);
                using var fs = DownloadFileHelper.CreateFile(FilePath);
                duration = Environment.TickCount;
                var response = await GetFile(item.Url);
                if (response.Success)
                {
                    duration = Environment.TickCount - duration;
                    var buffer = new byte[4096 * 1024];
                    int read;
                    while ((read = await response.Data.ReadAsync(buffer, 0, buffer.Length)) != 0)
                    {
                        await fs.WriteAsync(buffer, 0, read);
                        totalSize += read;
                    }

                    response.Message.Dispose();
                    completed = true;
                    break;
                }
                else
                {
                    error = response.Error;
                }
            }

            return (completed, duration, totalSize, error);
        }

        private async Task<ApiResponse<ClipUrls>> GetClipUrls() =>
            await PostHttp<ClipUrls>($"library/videos/offline", new ClipUrlRequest(Clip.SupportsWidescreen) { ClipId = Clip.Id, CourseId = course.Id }, requiresAuthentication: true);

    }
}
