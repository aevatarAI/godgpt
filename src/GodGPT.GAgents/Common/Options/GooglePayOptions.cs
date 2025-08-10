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
