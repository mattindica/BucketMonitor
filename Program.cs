namespace BucketMonitor
{
    using Amazon;
    using Amazon.Runtime;

    using System;
    using System.Threading.Tasks;

    public class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: dotnet run -- [monitor|list]");
                return;
            }

            // Customize AWS info here

            BucketManager manager = new BucketManager(
                bucketName: "source-bucket-name",
                driveLetter: 'M',
                pollingInterval: TimeSpan.FromSeconds(30),
                region: RegionEndpoint.USEast2,
                credentials: new InstanceProfileAWSCredentials());

            switch (args[0]) {
                case "list":
                    await manager.DisplayImages();
                    break;
                case "monitor":
                    await manager.MonitorAsync();
                    break;
                default:
                    Console.WriteLine("Invalid Command: {0}", args[0]);
                    break;
            }
        }
    }
}
