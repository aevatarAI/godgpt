using System.Collections.Generic;

namespace Aevatar.Application.Grains.Common.Options
{
    public class GooglePayOptions
    {
        public string PackageName { get; set; }
        public string RsaPublicKey { get; set; }  // Google Play Console RSA public key for Pub/Sub signature verification
        public List<GooglePayProduct> Products { get; set; } = new();
        
        /// <summary>
        /// Google Service Account JSON string. 
        /// Can be set directly or automatically populated from GoogleServiceAccount configuration section via GooglePayOptionsPostProcessor.
        /// </summary>
        public string ServiceAccountJson { get; set; }
        
        /// <summary>
        /// RevenueCat API key for Google Play (starts with 'goog_')
        /// Used to query transaction information from RevenueCat API
        /// </summary>
        public string RevenueCatApiKey { get; set; }
        
        /// <summary>
        /// RevenueCat API base URL
        /// Default: https://api.revenuecat.com/v1
        /// </summary>
        public string RevenueCatBaseUrl { get; set; } = "https://api.revenuecat.com/v1";
    }

    public class GooglePayProduct
    {
        public string ProductId { get; set; }
        public int PlanType { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public bool IsSubscription { get; set; }
        public bool IsUltimate { get; set; }
    }
}
