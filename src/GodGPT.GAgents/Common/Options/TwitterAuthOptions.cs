namespace Aevatar.Application.Grains.Common.Options;

[GenerateSerializer]
public class TwitterAuthOptions
{
    [Id(0)] public string ClientId { get; set; }
    [Id(1)] public string ClientSecret { get; set; }
    [Id(2)] public string TokenEndpoint { get; set; } = "https://api.x.com/2/oauth2/token";
    [Id(3)] public string UserInfoEndpoint { get; set; } = "https://api.twitter.com/2/users/me";
    [Id(4)] public string[] Scopes { get; set; } = new[] { "users.read" }; //tweet.read", "offline.access" 
    [Id(5)] public Dictionary<string, string> PostLoginRedirectUrls { get; set; } = new Dictionary<string, string>();
}