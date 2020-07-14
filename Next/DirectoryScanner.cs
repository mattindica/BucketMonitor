namespace BucketMonitor
{
    using System;
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
            var files = this.EnumerateIncludedFiles()
                .Aggregate(new LocalSnapshot(), (result, file) =>
                {
                    result.Register(file);
                    return result;
                });
            Console.WriteLine();
            return files;
        }

        private IEnumerable<FileInfo> EnumerateAllFiles()
        {
            return this.Root.EnumerateFiles("*", SearchOption.AllDirectories);
        }

        private IList<FileInfo> EnumerateIncludedFiles()
        {
            var counter = new DirectoryCounter($"Scanning {this.Settings.RootPath}");
            if (this.Settings.IncludedPaths.Count() > 0)
            {
                return this.Settings.IncludedPaths.SelectMany(path =>
                {
                    var directory = new DirectoryInfo(Path.Combine(this.Settings.RootPath, path));
                    if (directory.Exists)
                    {
                        return this.EnumerateFiles(directory, counter).ToList();
                    }
                    else
                    {
                        return new List<FileInfo>();
                    }
                }).ToList();
            }
            else
            {
                return this.EnumerateFiles(this.Root, counter).ToList();
            }
        }

        private IList<FileInfo> EnumerateFiles(
            DirectoryInfo directory,
            DirectoryCounter counter)
        {
            var output = new List<FileInfo>();
            try
            {
                var files = directory.EnumerateFiles("*", SearchOption.TopDirectoryOnly);
                output.AddRange(files);
                counter.AddScannedFile(files.Count());
                foreach (var dir in directory.EnumerateDirectories("*"))
                {
                    try
                    {
                        output.AddRange(this.EnumerateFiles(dir, counter).ToList());
                    }
                    catch (Exception ec)
                    {
                        counter.AddFailedDirectory();
                        this.Logger.LogDebug($"Failed to scan directory '{dir.FullName}': {ec.ToString()}");
                    }
                }
                counter.AddScannedDirectory();
            }
            catch (Exception ec)
            {
                counter.AddFailedDirectory();
                this.Logger.LogDebug($"Failed to scan directory '{directory.FullName}': {ec.ToString()}");
            }
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

            public override string ToString() => string.Format("{0} - ScannedFiles={1:n0}. ScannedDirectories={2:n0}. FailedDirectories={3:n0}.",
                this.Message,
                this.ScannedFiles,
                this.ScannedDirectories,
                this.FailedDirectories);
        }  
    }
}
