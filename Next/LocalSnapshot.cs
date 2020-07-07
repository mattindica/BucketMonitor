namespace BucketMonitor
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    public class LocalSnapshot : ILocalSnapshot
    {
        public LocalSnapshot()
        {
        }

        private IDictionary<string, FileInfo> Cache { get; } = new Dictionary<string, FileInfo>();

        public void Register(FileInfo file)
        {
            var key = this.GetNormalizedKey(file);
            if (!this.Cache.ContainsKey(key))
            {
                this.Cache[key] = file;
            }
        }

        public bool Exists(RemoteImage image) 
        {
            return this.Cache.ContainsKey(this.GetNormalizedKey(image.File));
        }

        public IEnumerable<FileInfo> ToList()
        {
            return this.Cache.Values.OrderBy(x => x.FullName);
        }

        private string GetNormalizedKey(FileInfo file) => 
            this.GetNormalizedKey(file.FullName);

        private string GetNormalizedKey(string path) => path.ToLower();
    }
}
