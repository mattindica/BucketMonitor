namespace BucketMonitor
{
    using System;
    using System.IO;
    using System.Linq;

    using Amazon;

    using YamlDotNet.Core;
    using YamlDotNet.Core.Events;
    using YamlDotNet.Serialization;

    public class RegionEndpointTypeConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type)
        {
            return type == typeof(RegionEndpoint);
        }

        public object ReadYaml(IParser parser, Type type)
        {
            if (parser.TryConsume<Scalar>(out var scalar) && scalar != null)
            {
                if (!RegionEndpoint.EnumerableAllRegions.Any(x => x.SystemName == scalar.Value))
                {
                    throw new InvalidDataException($"Invalid region_endpoint: {scalar.Value}");
                }
                return RegionEndpoint.GetBySystemName(scalar.Value);
            }
            else
            {
                throw new InvalidDataException($"Failed to parse region_endpoint");
            }
        }

        public void WriteYaml(IEmitter emitter, object value, Type type)
        {
            throw new NotImplementedException();
        }
    }
}
