namespace BucketMonitor
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using Microsoft.Extensions.Logging;

    public class DirectoryScanner
    {
        public DirectoryScanner(
            ILoggerFactory factory,
            Settings settings)
        {
            this.Settings = settings;
            this.Logger = factory.CreateLogger<DirectoryScanner>();
            this.Root = new DirectoryInfo(settings.RootPath);
        }

        private Settings Settings { get; }

        private ILogger<DirectoryScanner> Logger { get; }

        private DirectoryInfo Root { get; }

        public ILocalSnapshot Scan()
        {
            return this.EnumerateIncludedFiles()
                .Aggregate(new LocalSnapshot(), (result, file) =>
                {
                    result.Register(file);
                    return result;
                });
        }

        private IEnumerable<FileInfo> EnumerateAllFiles()
        {
            return this.Root.EnumerateFiles("*", SearchOption.AllDirectories);
        }

        private IEnumerable<FileInfo> EnumerateIncludedFiles()
        {
            if (this.Settings.IncludedPaths.Count() > 0)
            {
                return this.Settings.IncludedPaths.SelectMany(path =>
                {
                    var directory = new DirectoryInfo(Path.Combine(this.Settings.RootPath, path));
                    if (directory.Exists)
                    {
                        return this.EnumerateFiles(directory);
                    }
                    else
                    {
                        return Enumerable.Empty<FileInfo>();
                    }
                });
            }
            else
            {
                return this.EnumerateFiles(this.Root);
            }
        }

        private IEnumerable<FileInfo> EnumerateFiles(DirectoryInfo directory) => directory.EnumerateFiles("*", SearchOption.AllDirectories);
    }
}
