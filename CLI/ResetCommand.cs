﻿namespace BucketMonitor.CLI
{
    using System;
    using System.Threading.Tasks;

    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.Extensions.DependencyInjection;

    [Command(Description = "Removes all cached image entries.")]
    public class ResetCommand : BaseCommandLine<ResetCommand>
    {
        protected override async Task<int> ExecuteAsync(
            CommandLineApplication app,
            ServiceProvider provider)
        {
            if (this.GetUserConfirmation("Are you sure you want to delete all image statuses?"))
            {
                await this.BuildBucketManager(provider)
                    .ResetAsync(provider);

                return 0;
            }
            else
            {
                return 1;
            }
        }
    }
}
