namespace Aevatar.Application.Grains.Twitter.Dtos;

[GenerateSerializer]
public class TwitterAuthParamsDto
{
    [Id(1)] public string ClientId { get; set; }
    [Id(2)] public string GrantType { get; set; }
    [Id(3)] public string CodeChallenge { get; set; }
    [Id(4)] public string CodeChallengeMethod { get; set; }
    [Id(5)] public string State { get; set; }
    [Id(6)] public string Scope { get; set; }
    [Id(7)] public string ResponseType { get; set; }
}
