using System;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Winton.Extensions.Configuration.Consul.Website
{
    public class Startup
    {
        private readonly CancellationTokenSource _consulConfigCancellationTokenSource = new CancellationTokenSource();

        public Startup(IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory
                .AddConsole()
                .AddDebug();

            ILogger logger = loggerFactory.CreateLogger(nameof(Startup));

            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddConsul(
                    $"appsettings.json", 
                    _consulConfigCancellationTokenSource.Token,
                    options => {
                        options.ConsulConfigurationOptions = (cco) => {
                            cco.Address = new Uri("http://consul:8500");
                        };
                        options.Optional = true;
                        options.ReloadOnChange = true;
                        options.OnLoadException = (exceptionContext) => {
                            exceptionContext.Ignore = true;
                        };
                    })
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddSingleton<IConfigurationRoot>(Configuration)
                .AddMvc();
            services.AddSwaggerGen();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime appLifetime)
        {
            app
                .UseMvc()
                .UseSwagger()
                .UseSwaggerUi("api");

            appLifetime.ApplicationStopping.Register(_consulConfigCancellationTokenSource.Cancel);
        }
    }
}
