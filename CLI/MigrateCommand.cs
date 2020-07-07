namespace BucketMonitor.CLI
{
    using System;
    using System.Threading.Tasks;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;

    using McMaster.Extensions.CommandLineUtils;

    [Command(Description = "Migrates the database.")]
    public class MigrateCommand : BaseCommandLine<StatusCommand>
    {
        protected override bool SkipMigrationCheck => true;

        protected override async Task<int> ExecuteAsync(
            CommandLineApplication app,
            ServiceProvider provider)
        {
            var pending = await this.GetPendingMigrationCountAsync(provider);

            if (pending > 0)
            {
                if (this.GetUserConfirmation($"Are you sure you want to run {pending} migrations?"))
                {
                    var context = provider.GetService<BucketMonitorContext>();
                    await context.Database.MigrateAsync();
                }
            }
            else
            {
                Console.WriteLine("No Migrations Required");
            }

            return 0;
        }
    }
}
