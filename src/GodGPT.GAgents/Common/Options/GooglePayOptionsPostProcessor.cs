using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Application.Grains.Common.Options
{
    /// <summary>
    /// Post processor for GooglePayOptions to support flat configuration structure
    /// </summary>
    public class GooglePayOptionsPostProcessor : IPostConfigureOptions<GooglePayOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<GooglePayOptionsPostProcessor> _logger;

        public GooglePayOptionsPostProcessor(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            _configuration = configuration;
            _logger = loggerFactory.CreateLogger<GooglePayOptionsPostProcessor>();
        }

        public void PostConfigure(string name, GooglePayOptions options)
        {
            _logger.LogDebug("PostConfigure called for GooglePayOptions: name={Name}", name);
            
            // If ServiceAccountJson is not already set, try to read from flat configuration
            if (string.IsNullOrEmpty(options.ServiceAccountJson))
            {
                _logger.LogDebug("ServiceAccountJson is empty, attempting to read from GoogleServiceAccount section");
                
                var serviceAccountSection = _configuration.GetSection("GoogleServiceAccount");
                if (serviceAccountSection.Exists())
                {
                    // Create a proper service account JSON object
                    var serviceAccountObj = new
                    {
                        type = serviceAccountSection["type"],
                        project_id = serviceAccountSection["project_id"],
                        private_key_id = serviceAccountSection["private_key_id"],
                        private_key = serviceAccountSection["private_key"],
                        client_email = serviceAccountSection["client_email"],
                        client_id = serviceAccountSection["client_id"],
                        auth_uri = serviceAccountSection["auth_uri"],
                        token_uri = serviceAccountSection["token_uri"],
                        auth_provider_x509_cert_url = serviceAccountSection["auth_provider_x509_cert_url"],
                        client_x509_cert_url = serviceAccountSection["client_x509_cert_url"],
                        universe_domain = serviceAccountSection["universe_domain"]
                    };
                    
                    // Serialize to JSON
                    options.ServiceAccountJson = JsonSerializer.Serialize(serviceAccountObj);
                    _logger.LogDebug("ServiceAccountJson populated from GoogleServiceAccount section");
                }
                else
                {
                    _logger.LogWarning("GoogleServiceAccount section not found in configuration");
                }
            }
            else
            {
                _logger.LogDebug("ServiceAccountJson already set, skipping flat configuration processing");
            }
            
            _logger.LogDebug("PostConfigure completed. PackageName: {PackageName}, ServiceAccountJson length: {Length}", 
                options.PackageName, options.ServiceAccountJson?.Length ?? 0);
        }
    }
}
