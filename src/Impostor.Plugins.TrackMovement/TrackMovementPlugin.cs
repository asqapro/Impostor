using Impostor.Api.Plugins;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Impostor.Plugins.TrackMovement
{
    [ImpostorPlugin(
        package: "gg.impostor.example",
        name: "Example",
        author: "AeonLucid",
        version: "1.0.0")]
    public class TrackMovementPlugin : PluginBase
    {
        private readonly ILogger<TrackMovementPlugin> _logger;

        public TrackMovementPlugin(ILogger<TrackMovementPlugin> logger)
        {
            _logger = logger;
        }

        public override ValueTask EnableAsync()
        {
            _logger.LogInformation("Example is being enabled.");
            return default;
        }

        public override ValueTask DisableAsync()
        {
            _logger.LogInformation("Example is being disabled.");
            return default;
        }
    }
}
