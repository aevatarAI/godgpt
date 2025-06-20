using System.Security.Cryptography;
using System.Text;
using Aevatar.Application.Grains.Common.Services;
using Aevatar.Application.Grains.MineAI;
using GodGPT.GAgents.MineAI.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Aevatar.Application.Grains.Tests.MineAI;

public class MineAIAwakeningGAgentTests : AevatarOrleansTestBase<AevatarGodGPTTestsMoudle>
{
    private const string SystemId = "MineAI";

    [Fact]
    public async Task ValidateSystemRequestAsync_ValidRequest_ReturnsTrue()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var requestId = Guid.NewGuid().ToString();
        var prompt = "test prompt";
        
        var contentToSign = $"{SystemId}{timestamp}{requestId}{prompt}";
        var signature = await SignData(contentToSign);
        
        var request = new AwakeningScoreRequest
        {
            SystemId = SystemId,
            Timestamp = timestamp,
            Id = requestId,
            Prompt = prompt,
            Signature = Convert.ToBase64String(signature)
        };
        
        var mineAiAwakening = Cluster.GrainFactory.GetGrain<IMineAIAwakening>(Guid.NewGuid());
        var result = await mineAiAwakening.CalculateScoreAsync(request);

        result.ShouldNotBeNull();
    }

    private async Task<byte[]> SignData(string content)
    {
        using (var privateKey = RSA.Create())
        {
            var systemAuthentication = Application.ServiceProvider.GetRequiredService<ISystemAuthenticationService>();
            var privateKeyAsync = await systemAuthentication.GetPrivateKeyAsync(SystemId);
            var privateKeyBytes = Convert.FromBase64String(privateKeyAsync);
            privateKey.ImportPkcs8PrivateKey(privateKeyBytes, out _);
            
            var dataToSign = Encoding.UTF8.GetBytes(content);
            return privateKey.SignData(dataToSign, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }
} 