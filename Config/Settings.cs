namespace BucketMonitor
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    using Amazon;
    using Amazon.Runtime;

    using YamlDotNet.Serialization;
    using YamlDotNet.Serialization.NamingConventions;

    using BucketMonitor.Config;

    public class Settings
    {
        public string BucketName { get; private set; }

        public int MaxDownloads { get; private set; }

        public string RootPath { get; private set; }

        public TimeSpan PollingInterval { get; private set; }

        public RegionEndpoint RegionEndpoint { get; private set; }

        public AmazonCredentials AmazonCredentials { get; private set; }

        public string DatabaseConnectionString { get; private set; }

        public List<string> IncludedPaths { get; private set; }

        public bool DebugMode { get; private set; }

        public AWSCredentials GetCredentials()
        {
            if (this.AmazonCredentials != null)
            {
                return new BasicAWSCredentials(
                    this.AmazonCredentials.AccessKey,
                    this.AmazonCredentials.SecretKey);
            }
            else
            {
                return new InstanceProfileAWSCredentials();
            }
        }

        public string Summarize()
        {
            var included = string.Join(
                $", ", this.IncludedPaths?.ToArray() ??
                    new string[] { });

            return
                $"bucket_name = {this.BucketName}\n" +
                $"max_downloads = {this.MaxDownloads}\n" +
                $"polling_interval = {this.PollingInterval}\n" +
                $"region_endpoint = {this.RegionEndpoint.SystemName}\n" +
                $"root_path = {this.RootPath}\n" +
                $"debug_mode = {this.DebugMode}\n" +
                $"amazon_credentials = [{(this.AmazonCredentials == null ? "INSTANCE_PROFILE" : "BASIC")}]\n" +
                $"included_paths = [{included}]";
        }

        private void Validate()
        {
            if (this.IncludedPaths == null)
            {
                this.IncludedPaths = new List<string>();
            }
            else
            {
                foreach (var path in this.IncludedPaths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        throw new Exception($"Settings Error: Invalid included path: {path}"); 
                    }
                    else if (Path.IsPathRooted(path))
                    {
                        throw new Exception($"Settings Error: Absolute included paths are not allowed: {path}"); 
                    }
                    else if (path.Contains("\\"))
                    {
                        throw new Exception($"Settings Error: '\\' not allowed in included path: {path}"); 
                    }
                    else if (path.EndsWith("/"))
                    {
                        throw new Exception($"Settings Error: Included path cannot end with '/': {path}"); 
                    }
                }
            }

            if (this.RootPath == null)
            {
                throw new Exception("Settings Error: drive is null"); 
            }
            else if (this.RootPath.EndsWith("/"))
            {
                throw new Exception("Settings Error: drive cannot end in '/'"); 
            }
        }

        public static bool TryLoad(string path, out Settings settings)
        {
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithTypeConverter(new RegionEndpointTypeConverter())
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build();

                using (var reader = new StreamReader(path))
                {
                    settings = deserializer.Deserialize<Settings>(reader);
                    settings.Validate();
                    return true;
                }
            }
            catch
            {
                settings = null;
                return false;
            }
        }
    }
}
