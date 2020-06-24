namespace BucketMonitor.CLI
{
    using System.Threading.Tasks;

    using Microsoft.Extensions.DependencyInjection;
    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.Extensions.Logging;
    using System;

    [HelpOption("-h|--help")]
    public abstract class BaseCommandLine<T>
        where T : BaseCommandLine<T>
    {
        protected Program Parent { get; set; }

        protected abstract Task<int> ExecuteAsync(CommandLineApplication app, ServiceProvider provider);

        protected BucketManager BuildBucketManager(ServiceProvider provider)
        {
            return new BucketManager(
                logger: provider.GetService<ILogger<T>>(),
                settings: this.Parent.Settings);
        }

        protected virtual async Task<int> OnExecuteAsync(CommandLineApplication app)
        {
            using (var provider = this.Parent.CreateProvider())
            {
                Console.WriteLine("Loaded Config {0}:", Program.ConfigFile);
                Console.WriteLine();
                Console.WriteLine(Parent.Settings.Summarize());
                Console.WriteLine();

                return await this.ExecuteAsync(app, provider);
            }
        }
    }
}
