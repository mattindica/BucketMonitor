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

        public async Task<IEnumerable<SourceImage>> ListPendingAsync()
        {
            return (await this.ListImagesAsync())
                .Where(x => !x.Processed)
                .OrderBy(obj => obj.LastModified);
        }

        public async Task<IEnumerable<SourceImage>> ListImagesAsync()
        {
            var objects = await this.ListObjectsAsync();
            var images = new List<SourceImage>();

            foreach (var obj in objects)
            {
                if (this.TryConvertPath(obj, out var file))
                {
                    var tags = await this.ListTagsAsync(obj.Key);

                    bool isProcessed;

                    if (tags.TryGetValue(TAG_NAME_PROCESSED, out var value))
                    {
                        isProcessed = value == true.ToString();
                    }
                    else
                    {
                        isProcessed = false;
                    }

                    images.Add(new SourceImage(
                        key: obj.Key,
                        file: file,
                        lastModified: obj.LastModified,
                        processed: isProcessed));
                }
            }

            return images;
        }

        public async Task MonitorAsync()
        {
            DateTime? lastCheck = null;

            Console.WriteLine("Monitoring Bucket: {0}...", this.BucketName);
            for (; ; ) {
                try
                {
                    var now = DateTime.Now;
                    if (!lastCheck.HasValue || DateTime.Now.Subtract(lastCheck.Value) > this.PollingInterval)
                    {
                        lastCheck = DateTime.Now;

                        var pending = (await this.ListPendingAsync())
                            .FirstOrDefault();

                        if (pending != null)
                        {
                            await this.DownloadAsync(pending);
                        }
                    }
                    Thread.Sleep(1000);
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
                .OrderBy(x => x.Processed)
                .ThenBy(x => x.LastModified);

            foreach (var image in images)
            {
                table.AddRow(
                    image.Key,
                    image.LastModified,
                    image.Processed ? "Processed" : "Pending",
                    image.File.FullName);
            }

            table.Write();
            Console.WriteLine();
        }

        private async Task<IEnumerable<S3Object>> ListObjectsAsync()
        {
            var results = new List<S3Object>();
            ListObjectsV2Request request = new ListObjectsV2Request
            {
                BucketName = this.BucketName
            };

            ListObjectsV2Response response;
            do
            {
                response = await this.Client.ListObjectsV2Async(request);
                results.AddRange(response.S3Objects);
                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated);

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


        private bool TryConvertPath(S3Object obj, out FileInfo file)
        {
            if (Path.IsPathRooted(obj.Key))
            {
                Console.WriteLine("Error processing {0}: Expecting Relative Path", obj.Key);
                file = default(FileInfo);
                return false;
            }
            else
            {
                var path = Path.Combine($"{this.DriveLetter}:\\", obj.Key);
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

            var response = await this.Client.GetObjectAsync(new GetObjectRequest()
            {
                BucketName = this.BucketName,
                Key = image.Key
            });

            response.WriteObjectProgressEvent += (_, progress) =>
            {
                Console.Write("\rDownloading {0}: {1}%", image.Key, progress.PercentDone);
            };
            Console.WriteLine();

            var tmp = Path.GetTempFileName();

            await response.WriteResponseStreamToFileAsync(tmp, false, CancellationToken.None);

            File.Move(tmp, file.FullName);

            await this.UpdateImageStatusAsync(image.Key, true);

            Console.WriteLine("\rDownload Complete: {0}", image.Key);

            return file;
        }
    }
}
