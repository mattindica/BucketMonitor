namespace BucketMonitor.CLI
{
    using System.Threading.Tasks;

    using Microsoft.Extensions.DependencyInjection;
    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.Extensions.Logging;

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
                return await this.ExecuteAsync(app, provider);
            }
        }
    }
}
