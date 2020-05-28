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

    public class BucketManager
    {
        private static readonly string TAG_NAME_PROCESSED = "processed";

        public BucketManager(
            string bucketName,
            char driveLetter,
            TimeSpan pollingInterval,
            Amazon.RegionEndpoint region,
            AWSCredentials credentials)
        {
            this.Client = new AmazonS3Client(credentials, region);
            this.BucketName = bucketName;
            this.DriveLetter = driveLetter;
            this.PollingInterval = pollingInterval;
        }

        public TimeSpan PollingInterval { get; }

        public string BucketName { get; }

        public char DriveLetter { get; }

        private AmazonS3Client Client { get; }

        private S3ObjectCache Cache { get; } = new S3ObjectCache();

        public async Task<IEnumerable<SourceImage>> ListPendingAsync()
        {
            return (await this.ListImagesAsync())
                .Where(x => x.Status == ImageStatus.Pending)
                .OrderBy(obj => obj.LastModified);
        }

        private async Task<SourceImage> LoadSourceImageAsync(string key, DateTime lastModified)
        {
            SourceImage image;

            if (this.TryConvertPath(key, out var file))
            {
                var tags = await this.ListTagsAsync(key);
                ImageStatus status;

                if (tags.TryGetValue(TAG_NAME_PROCESSED, out var value) && value == true.ToString())
                {
                    status = ImageStatus.Completed;
                }
                else
                {
                    status = ImageStatus.Pending;
                }

                image = new SourceImage(
                    key: key,
                    file: file,
                    lastModified: lastModified,
                    status: status);

            }
            else
            {
                image = new SourceImage(
                    key: key,
                    file: null,
                    lastModified: lastModified,
                    status: ImageStatus.Skipped);
            }

            this.Cache.Put(image);
            return image;
        }

        public async Task MonitorAsync()
        {
            DateTime? lastCheck = null;

            Console.WriteLine("Monitoring Bucket {0}\n\n", this.BucketName);
            for (; ; ) {
                try
                {
                    var now = DateTime.Now;
                    if (!lastCheck.HasValue || DateTime.Now.Subtract(lastCheck.Value) > this.PollingInterval)
                    {
                        lastCheck = DateTime.Now;

                        var pending = await this.ListPendingAsync();
                        var count = pending.Count();

                        if (count > 0)
                        {
                            Console.WriteLine("Downloading {0} Pending Images", pending.Count());
                            var tasks = pending.Select(image => this.DownloadAsync(image));
                            await Task.WhenAll(tasks);
                        }
                    }
                    else
                    {
                        await Task.Delay(1000);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception: {0}", e);
                }
            }
        }

        public async Task DisplayImages()
        {
            var table = new ConsoleTable("Key", "Last Modified", "Status", "Path");
            table.Options.EnableCount = false;

            var images = (await this.ListImagesAsync())
                .OrderBy(x => x.Status)
                .ThenBy(x => x.LastModified);

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

        private async Task<SourceImage> LoadObjectAsync(S3Object obj)
        {
            if (this.Cache.TryGet(obj.Key, obj.LastModified, out var cached))
            {
                return cached;
            }
            else
            {
                var image = await this.LoadSourceImageAsync(obj.Key, obj.LastModified);
                if (image != null)
                {
                    this.Cache.Put(image);
                    return image;
                }
                else
                {
                    return null;
                }
            }
        } 

        private async Task<IEnumerable<SourceImage>> ListImagesAsync()
        {
            var results = new List<SourceImage>();
            ListObjectsV2Request request = new ListObjectsV2Request
            {
                BucketName = this.BucketName
            };

            int count = 0;
            ListObjectsV2Response response;
            do
            {
                Console.Write("\rQuerying Objects {0}", new string('.', count));
                response = await this.Client.ListObjectsV2Async(request);

                var tasks = response.S3Objects.Select(obj => this.LoadObjectAsync(obj));
                var images = await Task.WhenAll(tasks);
                results.AddRange(images);
                request.ContinuationToken = response.NextContinuationToken;
                count++;
            }
            while (response.IsTruncated);

            Console.WriteLine();

            return results;
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

        public async Task UpdateImageStatusAsync(
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
        }

        private bool TryConvertPath(string key, out FileInfo file)
        {
            if (Path.IsPathRooted(key))
            {
                file = default(FileInfo);
                return false;
            }
            else
            {
                var path = Path.Combine($"{this.DriveLetter}:\\", key);
                file = new FileInfo(path); 
                return true;
            }
        }

        public async Task<FileInfo> DownloadAsync(
            SourceImage image)
        {
            var file = image.File;
            if (!file.Directory.Exists)
            {
                file.Directory.Create();
            }

            try
            {
                var response = await this.Client.GetObjectAsync(new GetObjectRequest()
                {
                    BucketName = this.BucketName,
                    Key = image.Key
                });

                var tmp = Path.GetTempFileName();

                await response.WriteResponseStreamToFileAsync(tmp, false, CancellationToken.None);

                File.Move(tmp, file.FullName, overwrite: true);

                await this.UpdateImageStatusAsync(image.Key, true);

                Console.WriteLine("\rDownload Complete: {0} -> {1}", image.Key, file.FullName);

                image.MarkCompleted();
                this.Cache.Put(image);

                return file;
            }
            catch
            {
                Console.WriteLine("\rDownload Failed:  {0} -> {1}", image.Key, file.FullName);

                image.MarkFailed();
                this.Cache.Put(image);

                return null;
            }
        }
    }
}
