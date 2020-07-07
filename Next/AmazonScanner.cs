namespace BucketMonitor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Extensions.Logging;

    using Amazon.S3;
    using Amazon.S3.Model;

    public class AmazonScanner
    {
        public static int MAX_PATH_LENGTH { get; } = 185;

        public AmazonScanner(
            ILoggerFactory factory,
            Settings settings)
        {
            this.Client = new AmazonS3Client(
                credentials: settings.GetCredentials(),
                region: settings.RegionEndpoint);

            this.BucketName = settings.BucketName;
            this.Logger = factory.CreateLogger<AmazonScanner>();
            this.Settings = settings;
        }

        public string BucketName { get; }

        private AmazonS3Client Client { get; }

        private ILogger Logger { get; }

        private Settings Settings { get; }

        public async Task<IEnumerable<RemoteImage>> QueryIncludedAsync(ScanNotifier notifier = null)
        {
            if (this.Settings.IncludedPaths != null &&
                this.Settings.IncludedPaths.Count() > 0)
            {
                var tasks = this.Settings.IncludedPaths.Select(path => this.QueryAsync($"{path}/", notifier));
                return (await Task.WhenAll(tasks))
                    .SelectMany(x => x)
                    .Where(x => x != null); // TODO: Is this needed?
            }
            else
            {
                return await this.QueryAsync(notifier: notifier);
            }
        }

        private async Task<IEnumerable<RemoteImage>> QueryAsync(string prefix = null, ScanNotifier notifier = null)
        {
            notifier?.Start();
            var results = new List<RemoteImage>();
            ListObjectsV2Request request = new ListObjectsV2Request
            {
                BucketName = this.BucketName,
                Prefix = prefix
            };

            int count = 0;
            ListObjectsV2Response response;
            long total = 0;
            do
            {
                response = await this.Client.ListObjectsV2Async(request);

                var images = new List<RemoteImage>();
                var toAdd = new List<ImageEntry>();

                total += response.S3Objects.Count();

                var filtered = response.S3Objects
                    .Where(x => x.Key.Length <= MAX_PATH_LENGTH);
                    //.Where(x => this.IsIncludedPath(x.Key));

                foreach (var obj in filtered)
                {
                    if (RemoteImage.TryLoad(obj, this.Settings, out var image))
                    {
                        results.Add(image);
                    }
                }

                notifier?.ScannedBatch(total, results.Count());
                request.ContinuationToken = response.NextContinuationToken;
                count++;
            }
            while (response.IsTruncated);
            notifier?.Finish(total, results.Count());
            return results;
        }

        public async Task<IEnumerable<RemoteImage>> DiffAsync(
            ILocalSnapshot snapshot,
            ScanNotifier notifier = null)
        {
            var remote = await this.QueryIncludedAsync(notifier: notifier);
            return remote.Where(x => !snapshot.Exists(x))
                .OrderBy(x => x.Key);
        }

        public async Task<RemoteImage> LoadAsync(
            string key)
        {
            var results = await this.Client.ListObjectsV2Async(new ListObjectsV2Request()
            {
                BucketName = this.Settings.BucketName,
                Prefix = key
            });

            var count = results.S3Objects.Count();

            if (count == 0)
            {
                throw new Exception($"Error: Could not find remote image: {key}");
            }
            else if (count > 1)
            {
                throw new Exception($"Error: Found multiple ({count}) remote images: {key}");
            }
            else if (RemoteImage.TryLoad(results.S3Objects.Single(), this.Settings, out var image))
            {
                return image;
            }
            else
            {
                throw new Exception($"Error: Invalid Remote Imag: {key}");
            }
        }

        public async Task<FileInfo> SyncAsync(
            RemoteImage image,
            SyncTracker tracker)
        {
            var file = image.File;
            try
            {
                file.Refresh();
                if (!file.Exists)
                {
                    var response = await this.Client.GetObjectAsync(new GetObjectRequest()
                    {
                        BucketName = this.BucketName,
                        Key = image.Key
                    });

                    var tmp = Path.GetTempFileName();

                    response.WriteObjectProgressEvent += (sender, p) =>
                    {
                        tracker.Transferred(image, p.TransferredBytes);
                    };

                    await response.WriteResponseStreamToFileAsync(tmp, false, CancellationToken.None);

                    if (this.Settings.DebugMode)
                    {
                        File.Delete(tmp);
                    }
                    else
                    {
                        if (!this.SafeMove(tmp, file.FullName))
                        {
                            tracker.Fail(image);
                            return null;
                        }
                    }

                    this.Logger.LogDebug("Download Complete: {0} -> {1}", image.Key, file?.FullName ?? "-");
                    tracker.Complete(image);
                }
                else
                {
                    this.Logger.LogDebug("Skipping Existing File: {0} -> {1}", image.Key, file?.FullName ?? "-");
                    tracker.Skip(image);
                }

                return file;
            }
            catch (Exception ec)
            {
                this.Logger.LogDebug("Download Failed:  {0} -> {1}", image.Key, file?.FullName ?? "-");
                this.Logger.LogDebug("Exception: {0}", ec.ToString());

                tracker.Fail(image);
                return null;
            }
        }

        private bool SafeMove(string source, string destination)
        {
            try
            {
                var sourceFile = new FileInfo(source); 
                var destinationFile = new FileInfo(destination); 

                if (!destinationFile.Directory.Exists)
                {
                    Directory.CreateDirectory(destinationFile.Directory.FullName);
                    this.Logger.LogDebug($"Created Directory: {destinationFile.Directory.FullName}");
                }

                File.Move(source,
                    destination,
                    overwrite: false); // Be explicit about never overwriting

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
