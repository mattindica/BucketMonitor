namespace BucketMonitor
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;

    using Amazon;
    using Amazon.Runtime;

    using McMaster.Extensions.CommandLineUtils;

    public class Program
    {
        public static int Main(string[] args)
        {
            var app = new CommandLineApplication();

            app.HelpOption();

            var bucketName = app.Argument<string>(
                name: "BUCKET_NAME",
                description: "The Amazon S3 Bucket",
                configuration: argument =>
                {
                    argument.OnValidate(
                        ctx =>
                        {
                            if (argument.Value == null)
                            {
                                throw new ArgumentException($"no <BUCKET_NAME> was provided.");
                            }
                            return ValidationResult.Success;
                        });
                });

            var regionEndpoint = app.Argument<string>(
                name: "REGION_ENDPOINT",
                description: "The Amazon S3 Region Endpoint",
                configuration: argument =>
                {
                    argument.OnValidate(
                        ctx =>
                        {
                            if (argument.Value == null)
                            {
                                throw new ArgumentException($"no <REGION_ENDPOINT> was provided.");
                            }
                            else if (!RegionEndpoint.EnumerableAllRegions.Any(x => x.SystemName == argument.Value))
                            {
                                throw new ArgumentException($"Invalid <REGION_ENDPOINT>: {argument.Value}");
                            }

                            return ValidationResult.Success;
                        });
                });

            var driveLetter = app.Argument<char>(
                name: "DRIVE_LETTER",
                description: "The storage gateway drive letter",
                configuration: argument =>
                {
                    argument.OnValidate(
                        ctx =>
                        {
                            if (argument.Value == null)
                            {
                                throw new ArgumentException($"no <DRIVE_LETTER> was provided.");
                            }
                            else if (argument.Value.Length != 1)
                            {
                                throw new ArgumentException($"Invalid Drive Letter: {argument.Value}");
                            }

                            return ValidationResult.Success;
                        });
                });

            var command = app.Argument<string>(
                name: "COMMAND",
                description: "list | monitor",
                configuration: argument =>
                {
                    argument.OnValidate(
                        ctx =>
                        {
                            if (argument.Value == null)
                            {
                                throw new ArgumentException($"no <COMMAND> was provided.");
                            }

                            return ValidationResult.Success;
                        });
                });


            app.OnValidate(
                ctx =>
                {
                    if (app.Arguments.Count < 4 || app.RemainingArguments.Count > 0)
                    {
                        throw new ArgumentException("Incorrect number of arguments.");
                    }
                    else
                    {
                        return ValidationResult.Success;
                    }
                });


            app.OnExecuteAsync(async (cancellationToken) =>
            {
                var endpoint = RegionEndpoint.GetBySystemName(regionEndpoint.ParsedValue);

                BucketManager manager = new BucketManager(
                    bucketName: bucketName.ParsedValue,
                    driveLetter: driveLetter.ParsedValue,
                    pollingInterval: TimeSpan.FromSeconds(30),
                    region: endpoint,
                    credentials: new InstanceProfileAWSCredentials());

                switch (command.ParsedValue) {
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

                return 0;
            });

            return app.Execute(args);
        }
    }
}
