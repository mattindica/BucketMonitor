namespace BucketMonitor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Amazon.Runtime;
    using Amazon.S3;
    using Amazon.S3.Model;

    using ConsoleTables;
    using Microsoft.Extensions.Logging;

    public class BucketManager
    {
        private static readonly string TAG_NAME_PROCESSED = "processed";
        public BucketManager(
            ILogger logger,
            Settings settings)
        {
            this.Client = new AmazonS3Client(
                credentials: settings.GetCredentials(), 
                region: settings.RegionEndpoint);

            this.BucketName = settings.BucketName;
            this.DriveLetter = settings.DriveLetter;
            this.PollingInterval = settings.PollingInterval;
            this.Cache = new S3ObjectCache(logger, settings.DriveLetter);
            this.DebugMode = settings.DebugMode;
            this.Logger = logger;
        }

        public TimeSpan PollingInterval { get; }

        public string BucketName { get; }

        public char DriveLetter { get; }

        private AmazonS3Client Client { get; }

        private S3ObjectCache Cache { get; } 

        private bool DebugMode { get; } 

        private ILogger Logger { get; }

        public async Task<IEnumerable<SourceImage>> ListPendingAsync()
        {
            return (await this.ListImagesAsync())
                .Where(x => x.Status == ImageStatus.Pending)
                .OrderBy(obj => obj.LastModified);
        }

        private SourceImage LoadSourceImage(string key, DateTime lastModified, long totalBytes)
        {
            SourceImage image;

            if (this.TryConvertPath(key, out var file))
            {
                /*var tags = await this.ListTagsAsync(key);
                ImageStatus status;

                if (tags.TryGetValue(TAG_NAME_PROCESSED, out var value) && value == true.ToString())
                {
                    status = ImageStatus.Completed;
                }
                else
                {
                    status = ImageStatus.Pending;
                }*/

                image = new SourceImage(
                    key: key,
                    file: file,
                    lastModified: lastModified,
                    totalBytes: totalBytes,
                    status: ImageStatus.Pending);

            }
            else
            {
                image = new SourceImage(
                    key: key,
                    file: null,
                    lastModified: lastModified,
                    totalBytes: totalBytes,
                    status: ImageStatus.Skipped);
            }

            this.Cache.Put(image);
            return image;
        }

        public async Task MonitorAsync()
        {
            DateTime? lastCheck = null;

            this.Logger.LogInformation("Monitoring Bucket {0}", this.BucketName);
            Console.Out.Flush();
            for (; ; ) {
                try
                {
                    var now = DateTime.Now;
                    if (!lastCheck.HasValue || DateTime.Now.Subtract(lastCheck.Value) > this.PollingInterval)
                    {
                        Console.WriteLine();
                        Console.WriteLine();
                        Console.Out.Flush();
                        this.Logger.LogInformation("Scanning Bucket {0}", this.BucketName);
                        Console.Out.Flush();
                        lastCheck = DateTime.Now;

                        var pending = await this.ListPendingAsync();
                        var count = pending.Count();
                        var tracker = new DownloadTracker(count, pending.Select(x => x.TotalBytes).Sum());

                        if (count > 0)
                        {
                            this.Logger.LogInformation("Downloading {0} Pending Images", pending.Count());
                            var tasks = pending.Select(image => this.DownloadAsync(image, tracker));
                            await Task.WhenAll(tasks);
                            this.Logger.LogInformation("Downloads Complete");
                            await this.Cache.SaveAsync();
                        }
                        else
                        {
                            this.Logger.LogInformation("No Pending Images");
                        }
                    }
                    else
                    {
                        await Task.Delay(1000);
                    }
                }
                catch (Exception e)
                {
                    this.Logger.LogError("Exception: {0}", e.ToString());
                }
            }
        }

        public async Task Summarize()
        {
            var table = new ConsoleTable("Status", "Total Count");
            table.Options.EnableCount = false;

            var images = (await this.ListImagesAsync())
                .OrderBy(x => x.Status)
                .ThenBy(x => x.LastModified)
                .GroupBy(x => x.Status)
                .ToDictionary(x => x.Key, x => x.ToList());


            table.AddRow(this.ToName(ImageStatus.Completed), (images.GetValueOrDefault(ImageStatus.Completed)?.Count ?? 0).ToString());
            table.AddRow(this.ToName(ImageStatus.Pending), (images.GetValueOrDefault(ImageStatus.Pending)?.Count ?? 0).ToString());
            table.AddRow(this.ToName(ImageStatus.Failed), (images.GetValueOrDefault(ImageStatus.Failed)?.Count ?? 0).ToString());
            table.AddRow(this.ToName(ImageStatus.Skipped), (images.GetValueOrDefault(ImageStatus.Skipped)?.Count ?? 0).ToString());

            table.Write();
        }

        private string ToName(ImageStatus status) => Enum.GetName(typeof(ImageStatus), status);

        public async Task DisplayCompleted()
        {
            var table = new ConsoleTable("Key", "Last Modified", "Completed", "Status", "Path");
            table.Options.EnableCount = false;

            var images = (await this.ListImagesAsync())
                .OrderBy(x => x.Status)
                .ThenBy(x => x.LastModified);

            var tasks = images.Select(x => this.IsProcessed(x.Key));
            this.Logger.LogInformation("Looking up processing status for {0} files", images.Count());

            /*foreach (var (image, task) in Enumerable.Zip(images, tasks))
            {
                var result = await task;
                this.Logger.LogInformation("{0} - {1}", image.Key, result);
            }*/

            var statuses = await Task.WhenAll(tasks);

            foreach (var (image, completed) in Enumerable.Zip(images, statuses))
            {
                if (completed)
                {
                    this.Logger.LogInformation("Detected Processed Object: {0}", image.Key);
                    image.MarkCompleted();
                    this.Cache.Put(image);
                }
                table.AddRow(
                    image.Key,
                    image.LastModified,
                    completed,
                    Enum.GetName(typeof(ImageStatus), image.Status),
                    image.File?.FullName ?? "-"); 
            }

            table.Write();
            Console.WriteLine();
            await this.Cache.SaveAsync();
        }

        public async Task DisplayImages()
        {
            var table = new ConsoleTable("Key", "Last Modified", "Status", "Path");
            table.Options.EnableCount = false;

            var images = (await this.ListImagesAsync())
                .OrderBy(x => x.Status)
                .ThenBy(x => x.LastModified);

            this.Logger.LogInformation("Showing {0} objects...", images.Count());

            foreach (var image in images)
            {
                table.AddRow(
                    image.Key,
                    image.LastModified,
                    Enum.GetName(typeof(ImageStatus), image.Status),
                    image.File?.FullName ?? "-"); 
            }

            table.Write();
            Console.WriteLine();
        }

        private SourceImage LoadObject(S3Object obj)
        {
            if (this.Cache.TryGet(obj.Key, obj.LastModified, out var cached))
            {
                return cached;
            }
            else
            {
                return this.LoadSourceImage(obj.Key, obj.LastModified, obj.Size);
            }
        } 

        private async Task<IEnumerable<SourceImage>> ListImagesAsync()
        {
            var results = new List<SourceImage>();
            ListObjectsV2Request request = new ListObjectsV2Request
            {
                BucketName = this.BucketName
            };

            var console = new ConsoleString();

            int count = 0;
            ListObjectsV2Response response;
            do
            {
                console.Update($"Querying {results.Count()} Objects...");
                response = await this.Client.ListObjectsV2Async(request);

                var images = response.S3Objects.Select(obj => this.LoadObject(obj));
                results.AddRange(images);
                request.ContinuationToken = response.NextContinuationToken;
                count++;

            }
            while (response.IsTruncated);

            console.Update("");
            Console.WriteLine();
            this.Logger.LogInformation($"Queried {results.Count()} Objects");

            await this.Cache.SaveAsync();
            Console.Out.Flush();

            return results;
        }

        private async Task<bool> IsProcessed(string key)
        {
            try
            {
                var tags = await this.ListTagsAsync(key);
                return tags.ContainsKey(TAG_NAME_PROCESSED);
            }
            catch (Exception ec)
            {
                this.Logger.LogError("Error checking processing status for {0}: {1}", key, ec);
                return false;
            }
        }

        private async Task<IDictionary<string, string>> ListTagsAsync(string key)
        {
            GetObjectTaggingRequest request = new GetObjectTaggingRequest
            {
                BucketName = this.BucketName,
                Key = key
            };
            var response = await this.Client.GetObjectTaggingAsync(request);
            return response.Tagging.ToDictionary(x => x.Key, x => x.Value);
        }

        /*public async Task UpdateImageStatusAsync(
          string key,
          bool processed)
        {
            await this.Client.PutObjectTaggingAsync(new PutObjectTaggingRequest
            {
                BucketName = this.BucketName,
                Key = key, 
                Tagging = new Tagging
                {
                    TagSet = new List<Tag>
                    {
                        new Tag()
                        {
                            Key = TAG_NAME_PROCESSED,
                            Value = processed.ToString()
                        }
                    }
                }
            });
        }*/

        public bool TryConvertPath(string key, out FileInfo file)
        {
            return TryConvertPath(key, this.DriveLetter, out file);
        }

        public static bool TryConvertPath(string key, char driveLetter, out FileInfo file)
        {
            if (key.EndsWith("/") || Path.IsPathRooted(key))
            {
                file = default(FileInfo);
                return false;
            }
            else
            {
                var path = Path.Combine($"{driveLetter}:\\", key);
                file = new FileInfo(path); 
                return true;
            }
        }

        public async Task<FileInfo> DownloadAsync(
            SourceImage image,
            DownloadTracker tracker)
        {
            var file = image.File;

            /*if (!file.Exists)
            {
                Console.WriteLine("Skipping Download. File doesn't exist: {0}", file.FullName);

                image.MarkFailed();
                this.Cache.Put(image);
                return null;
            }*/
            var started = false;
            long downloaded = 0;

            try
            {
                var response = await this.Client.GetObjectAsync(new GetObjectRequest()
                {
                    BucketName = this.BucketName,
                    Key = image.Key
                });

                var tmp = Path.GetTempFileName();

                long transferred = 0;
                response.WriteObjectProgressEvent += (sender, p) =>
                {
                    if (!started)
                    {
                        tracker.Start();
                    }
                    started = true;

                    downloaded = p.TransferredBytes;
                    tracker.Downloaded(downloaded - transferred);
                    transferred = downloaded;
                };

                await response.WriteResponseStreamToFileAsync(tmp, false, CancellationToken.None);

                if (this.DebugMode)
                {
                    File.Delete(tmp);
                }
                else
                {
                    if (!this.SafeMove(tmp, file.FullName))
                    {
                        image.MarkFailed();
                        this.Cache.Put(image);//, skipEnqueue: true);
                        tracker.Fail();
                        return null;
                    }
                }

                // await this.UpdateImageStatusAsync(image.Key, true);

                this.Logger.LogDebug("Download Complete: {0} -> {1}", image.Key, file.FullName);

                image.MarkCompleted();
                this.Cache.Put(image);
                if (started)
                {
                    tracker.Complete();
                }

                return file;
            }
            catch (Exception ec)
            {
                this.Logger.LogDebug("Download Failed:  {0} -> {1}", image.Key, file.FullName);
                this.Logger.LogDebug("Exception: {0}", ec.ToString());

                image.MarkFailed();
                this.Cache.Put(image);

                if (started)
                {
                    tracker.Fail();
                    tracker.Downloaded(image.TotalBytes - downloaded);
                }

                return null;
            }
        }

        private bool SafeMove(string source, string destination)
        {
            try
            {
                var sourceFile = new FileInfo(source); 
                var destinationFile = new FileInfo(destination); 

                if (sourceFile.Length == destinationFile.Length)
                {
                    File.Move(source, destination, overwrite: true);
                }
                else
                {
                    this.Logger.LogDebug(
                        "Cancelling Move Operation. Source file length ({0}) does not match destination ({1}): {2}",
                        sourceFile.Length,
                        destinationFile.Length,
                        destination);

                    File.Delete(source);
                }

                return true;
            }
            catch (Exception ec)
            {
                this.Logger.LogDebug("Exception Moving File {0} -> {1}: {2}", source, destination, ec.ToString());
                if (File.Exists(source))
                {
                    File.Delete(source);
                }

                return false;
            }
        }
    }
}
