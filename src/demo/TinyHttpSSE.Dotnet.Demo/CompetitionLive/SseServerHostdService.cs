
using TinyHttpSSE.Server;

namespace CompetitionLive
{
    public class SseServerHostdService : IHostedService
    {
        private readonly HttpSseServer _server;
        public SseServerHostdService(HttpSseServer httpSseServer) { 
            _server = httpSseServer;
        }
        public async Task StartAsync(CancellationToken cancellationToken) {
            await Task.Run(() => {
                bool result= _server.Start();
            });
        }

        public async Task StopAsync(CancellationToken cancellationToken) {
            await _server.Stopping();
        }
    }
}
