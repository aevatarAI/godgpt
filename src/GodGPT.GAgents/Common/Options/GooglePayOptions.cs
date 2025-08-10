using System.Collections.Generic;

namespace Aevatar.Application.Grains.Common.Options
{
    public class GooglePayOptions
    {
        public string PackageName { get; set; }
        public string ServiceAccountJson { get; set; }
        public List<GooglePayProduct> Products { get; set; } = new();
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
