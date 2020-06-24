namespace BucketMonitor
{
    using Amazon;
    using Amazon.Runtime;
    using BucketMonitor.Config;

    using System;
    using System.Collections.Generic;
    using System.IO;
    using YamlDotNet.Serialization;
    using YamlDotNet.Serialization.NamingConventions;

    public class Settings
    {
        public string BucketName { get; set; }

        public int MaxDownloads { get; set; }

        public char DriveLetter { get; set; }

        public TimeSpan PollingInterval { get; set; }

        public RegionEndpoint RegionEndpoint { get; set; }

        public AmazonCredentials AmazonCredentials { get; set; }

        public string DatabaseConnectionString { get; set; }

        public List<string> ExcludedPaths { get; set; }

        public bool DebugMode { get; set; }

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
            var newline = Environment.NewLine;
            var excluded = string.Join(
                $", ", this.ExcludedPaths?.ToArray() ??
                    new string[] { });

            return
                $"bucket_name = {this.BucketName}\n" +
                $"max_downloads = {this.MaxDownloads}\n" +
                $"drive_letter = {this.DriveLetter}\n" +
                $"polling_interval = {this.PollingInterval}\n" +
                $"region_endpoint = {this.RegionEndpoint.SystemName}\n" +
                $"debug_mode = {this.DebugMode}\n" +
                $"amazon_credentials = [{(this.AmazonCredentials == null ? "INSTANCE_PROFILE" : "BASIC")}]\n" +
                $"excluded_paths = [{excluded}]";
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
