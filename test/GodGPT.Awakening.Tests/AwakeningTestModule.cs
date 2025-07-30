using Aevatar;
using Aevatar.Application.Grains;
using GodGPT.GAgents.Awakening.Options;
using GodGPT.GAgents.SpeechChat;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Volo.Abp.BlobStoring;
using Volo.Abp.Modularity;

namespace GodGPT.Awakening.Tests;

[DependsOn(
    typeof(AevatarOrleansTestBaseModule),
    typeof(GodGPTGAgentModule)
)]
public class AwakeningTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // Mock IBlobContainer for testing
        var mockBlobContainer = new Mock<IBlobContainer>();
        services.AddSingleton(mockBlobContainer.Object);

        // Configure AwakeningOptions for testing
        services.Configure<AwakeningOptions>(options =>
        {
            options.EnableAwakening = true;
            options.MaxRetryAttempts = 2; // Reduced for faster tests
            options.TimeoutSeconds = 10; // Reduced for faster tests
            options.Temperature = 0.8;
            options.EnableLanguageSpecificPrompt = false; // Disabled by default
            options.PromptTemplate = "Based on the user's recent conversation content: {CONTENT_SUMMARY}, please generate a personalized awakening level (1-10) and an inspiring awakening sentence in {LANGUAGE}. The response should be motivational and reflect the user's current state and interests. Context: {USER_CONTEXT}. Date: {DATE}. Format your response as JSON: {{\"level\": number, \"message\": \"string\"}}";
            options.LanguageInstructions = new Dictionary<VoiceLanguageEnum, string>
            {
                { VoiceLanguageEnum.Chinese, "Use Chinese for the message" },
                { VoiceLanguageEnum.English, "Use English for the message" },
                { VoiceLanguageEnum.Spanish, "Use Spanish for the message" }
            };
        });
    }
}
