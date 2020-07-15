namespace BucketMonitor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
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

        public async Task<ILocalSnapshot> ScanAsync()
        {
            var counter = new DirectoryCounter($"Scanning {this.Settings.RootPath}");
            var files = this.EnumerateIncludedFiles(counter)
                .Aggregate(new LocalSnapshot(), (result, file) =>
                {
                    result.Register(file);
                    return result;
                });
            Console.WriteLine();

            await counter.WriteErrorsToFileAsync();
            return files;
        }

        private IEnumerable<FileInfo> EnumerateAllFiles()
        {
            return this.Root.EnumerateFiles("*", SearchOption.AllDirectories);
        }

        private IList<FileInfo> EnumerateIncludedFiles(DirectoryCounter counter)
        {
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
                        counter.AddFailedDirectory(dir.FullName);
                        this.Logger.LogDebug($"Failed to scan directory '{dir.FullName}': {ec.ToString()}");
                    }
                }
                counter.AddScannedDirectory();
            }
            catch (Exception ec)
            {
                counter.AddFailedDirectory(directory.FullName);
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

            private IList<string> Failed = new List<string>();

            private string Message { get; }

            public void AddScannedFile(int count = 1)
            {
                this.ScannedFiles += count;
                this.ConsoleString.Update(this.ToString());
            }

            public void AddFailedDirectory(string path)
            {
                this.FailedDirectories += 1;
                this.Failed.Add(path);
                this.ConsoleString.Update(this.ToString());
            }

            public void AddScannedDirectory(int count = 1)
            {
                this.ScannedDirectories += count;
                this.ConsoleString.Update(this.ToString());
            }

            public async Task WriteErrorsToFileAsync()
            {
                if (this.Failed.Count() > 0)
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMddTHHmmss");
                    var path = Path.Combine(Program.LoggingDirectory, $"{timestamp}.failed_files.log");
                    await File.WriteAllLinesAsync(path, this.Failed);
                    Console.WriteLine("{0} failed directories saved to {1}", this.Failed.Count(), path);
                }
            }

            public override string ToString() => string.Format("{0} - ScannedFiles={1:n0}. ScannedDirectories={2:n0}. FailedDirectories={3:n0}.",
                this.Message,
                this.ScannedFiles,
                this.ScannedDirectories,
                this.FailedDirectories);
        }  
    }
}
