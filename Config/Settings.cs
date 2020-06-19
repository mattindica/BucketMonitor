namespace BucketMonitor
{
    using Amazon;
    using Amazon.Runtime;
    using BucketMonitor.Config;
    using Microsoft.Extensions.Logging;
    using System;
    using System.IO;
    using YamlDotNet.Serialization;
    using YamlDotNet.Serialization.NamingConventions;

    public class Settings
    {
        public string BucketName { get; set; }

        public char DriveLetter { get; set; }

        public TimeSpan PollingInterval { get; set; }

        public RegionEndpoint RegionEndpoint { get; set; }

        public AmazonCredentials AmazonCredentials { get; set; }

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
            return
                $"bucket_name = {this.BucketName}\n" +
                $"drive_letter = {this.DriveLetter}\n" +
                $"polling_interval = {this.PollingInterval}\n" +
                $"region_endpoint = {this.RegionEndpoint.SystemName}\n" +
                $"debug_mode = {this.DebugMode}\n" +
                $"amazon_credentials = [{(this.AmazonCredentials == null ? "INSTANCE_PROFILE" : "BASIC")}]\n";
        }

        public static bool TryLoad(string path, out Settings settings, ILogger logger)
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
            catch (Exception ec)
            {
                logger.LogError("Error Loading Config File {0}: {1}", path, ec.ToString());
                settings = null;
                return false;
            }
        }
    }
}
