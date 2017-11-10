using System;
using BlackMaple.MachineWatchInterface;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Swagger;

namespace MachineWatchApiServer
{
    public class StubBackend : BlackMaple.MachineWatchInterface.IServerBackend
    {
        public ICellConfiguration CellConfiguration()
        {
            throw new NotImplementedException();
        }

        public void Halt()
        {
            throw new NotImplementedException();
        }

        public void Init(string dataDirectory, IEvents events)
        {
            throw new NotImplementedException();
        }

        public IInspectionControl InspectionControl()
        {
            throw new NotImplementedException();
        }

        public IJobServerV2 JobServer()
        {
            throw new NotImplementedException();
        }

        public ILogServerV2 LogServer()
        {
            throw new NotImplementedException();
        }

        public IPalletServer PalletServer()
        {
            throw new NotImplementedException();
        }
    }

    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        public BlackMaple.MachineWatchInterface.IServerBackend FindBackend()
        {
            return new StubBackend();
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddSingleton<BlackMaple.MachineWatchInterface.IServerBackend>(FindBackend());
            services.AddMvc();
            services.AddSwaggerGen(c =>
                {
                    c.SwaggerDoc("v1", new Info { Title = "Machine Watch", Version = "v1" });
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            app.UseMvc();
            app.UseSwagger();
            app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Machine Watch V1");
                });
        }
    }
}
