using Newtonsoft.Json;

namespace Aevatar.Application.Grains.Twitter.Dtos;

public class TwitterUserInfoResponseDto
{
    [JsonProperty("data")]
    public TwitterUserInfoResponseDataDto Data { get; set; }
}

public class TwitterUserInfoResponseDataDto
{
    [JsonProperty("id")] 
    public string Id { get; set; }
    [JsonProperty("username")] 
    public string Username { get; set; }
    [JsonProperty("name")] 
    public string Name { get; set; }
    [JsonProperty("profile_image_url")] 
    public string ProfileImageUrl { get; set; }
}