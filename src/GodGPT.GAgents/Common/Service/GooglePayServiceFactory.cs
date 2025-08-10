using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;

namespace Aevatar.Application.Grains.Common.Service
{
    public static class GooglePayServiceFactory
    {
        public static IServiceCollection AddGooglePayServices(
            this IServiceCollection services, 
            GooglePayOptions options)
        {
            services.AddTransient<IGooglePayService, GooglePayService>();
            services.AddSingleton<ILogger<GooglePayService>>(provider =>
                provider.GetRequiredService<ILoggerFactory>().CreateLogger<GooglePayService>());
            
            ValidateServiceConfiguration(options);
            
            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger("GooglePayServiceFactory");
            logger?.LogInformation("🔗 Using GooglePayService for integration testing");

            return services;
        }

        public static IGooglePayService CreateGooglePayService(
            GooglePayOptions options,
            ILoggerFactory loggerFactory)
        {
            ValidateServiceConfiguration(options);
            var logger = loggerFactory.CreateLogger<GooglePayService>();
            return new GooglePayService(logger, Microsoft.Extensions.Options.Options.Create(options));
        }
        
        private static void ValidateServiceConfiguration(GooglePayOptions options)
        {
            var errors = new List<string>();

            if (string.IsNullOrEmpty(options.PackageName))
            {
                errors.Add("PackageName is required for Google Play integration");
            }

            if (string.IsNullOrEmpty(options.ServiceAccountJson))
            {
                errors.Add("ServiceAccountJson must be provided");
            }
            
            if (errors.Any())
            {
                throw new InvalidOperationException(
                    "Google Pay service configuration is invalid:\n" + 
                    string.Join("\n", errors.Select(e => "- " + e)));
            }
        }
    }
}
