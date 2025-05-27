using TinyHttpSSE.Server;

namespace CompetitionLive
{
    public class Program
    {
        public static void Main(string[] args) {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddSingleton((service) => {
                var config=service.GetService<IConfiguration>();
                return new HttpSseServer(config.GetValue<string>("SseServerUrl"));
            });
            builder.Services.AddHostedService<SseServerHostdService>();

            builder.Services.AddRazorPages();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment()) {
                app.UseExceptionHandler("/Error");
            }
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.MapRazorPages();

            app.Run();
        }
    }
}
