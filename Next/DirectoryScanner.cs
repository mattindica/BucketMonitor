namespace BucketMonitor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using Amazon.S3.Model;
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
            var counter = new DirectoryCounter($"Scanning {this.Settings.RootPath}");

            if (this.Settings.IncludedPaths.Count() > 0)
            {
                return this.Settings.IncludedPaths.SelectMany(path =>
                {
                    var directory = new DirectoryInfo(Path.Combine(this.Settings.RootPath, path));
                    if (directory.Exists)
                    {
                        return this.EnumerateFiles(directory, counter);
                    }
                    else
                    {
                        return Enumerable.Empty<FileInfo>();
                    }
                });
            }
            else
            {
                return this.EnumerateFiles(this.Root, counter);
            }
        }

        private IEnumerable<FileInfo> EnumerateFiles(
            DirectoryInfo directory,
            DirectoryCounter counter)
        {
            var output = new List<FileInfo>();
            var files = directory.EnumerateFiles("*", SearchOption.TopDirectoryOnly);
            output.AddRange(files);
            counter.AddScannedFile(files.Count());
            foreach (var dir in directory.EnumerateDirectories("*"))
            {
                try
                {
                    output.AddRange(this.EnumerateFiles(dir, counter));
                }
                catch
                {
                    counter.AddFailedDirectory();
                    //Console.WriteLine("Exception loading directory: {0}", dir.FullName);
                }
            }
            counter.AddScannedDirectory();
            return output;
        }


        internal class DirectoryCounter
        {
            public DirectoryCounter(string message)
            {
                this.Message = message;
            }

            public long ScannedFiles { get; private set; } = 0;

            public long ScannedDirectories { get; private set; } = 0;

            public long FailedDirectories { get; private set; } = 0;

            private ConsoleString ConsoleString { get; } = new ConsoleString();

            private string Message { get; }

            public void AddScannedFile(int count = 1)
            {
                this.ScannedFiles += count;
                this.ConsoleString.Update(this.ToString());
            }

            public void AddFailedDirectory(int count = 1)
            {
                this.FailedDirectories += count;
                this.ConsoleString.Update(this.ToString());
            }

            public void AddScannedDirectory(int count = 1)
            {
                this.ScannedDirectories += count;
                this.ConsoleString.Update(this.ToString());
            }

            public override string ToString() => string.Format("{0} -> ScannedFiles={1:n0}. ScannedDirectories={2:n0}. FailedDirectories={3:n0}.",
                this.Message,
                this.ScannedFiles,
                this.ScannedDirectories,
                this.FailedDirectories);
        }  
    }
}
