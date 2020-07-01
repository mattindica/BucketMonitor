namespace BucketMonitor.CLI
{
    using System.Threading.Tasks;

    using Microsoft.Extensions.DependencyInjection;
    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.Extensions.Logging;
    using System;
    using Microsoft.EntityFrameworkCore;
    using System.Linq;

    [HelpOption("-h|--help")]
    public abstract class BaseCommandLine<T>
        where T : BaseCommandLine<T>
    {
        protected Program Parent { get; set; }

        protected virtual bool SkipMigrationCheck { get; } = false;

        protected abstract Task<int> ExecuteAsync(CommandLineApplication app, ServiceProvider provider);

        protected async Task<BucketManager> BuildBucketManager(ServiceProvider provider, bool validation = true)
        {
            var manager = new BucketManager(
                logger: provider.GetService<ILogger<T>>(),
                settings: this.Parent.Settings);

            if (validation)
            {
                await manager.ValidateBucket(provider.GetService<BucketMonitorContext>());
            }

            return manager;
        }

        protected virtual async Task<int> OnExecuteAsync(CommandLineApplication app)
        {
            using (var provider = this.Parent.CreateProvider())
            {
                Console.WriteLine("Loaded Config {0}:", Program.ConfigFile);
                Console.WriteLine();
                Console.WriteLine(Parent.Settings.Summarize());
                Console.WriteLine();

                if (!this.SkipMigrationCheck)
                {
                    var pending = await this.GetPendingMigrationCountAsync(provider);

                    if (pending > 0)
                    {
                        Console.WriteLine("Database Error: Missing {0} Migration(s)", pending);
                        return 1;
                    }
                }

                return await this.ExecuteAsync(app, provider);
            }
        }

        protected bool GetUserConfirmation(string message)
        {
            string input;
            do
            {
                Console.WriteLine("{0} (y/n)", message);
                input = Console.ReadLine().Trim().ToLower();
            }
            while (input != "y" && input != "n");
            return input == "y";
        }

        protected async Task<int> GetPendingMigrationCountAsync(ServiceProvider provider)
        {
            var context = provider.GetService<BucketMonitorContext>();

            var migrations = await context.Database.GetPendingMigrationsAsync();
            return migrations.Count();
        }
    }
}
