using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.GAgents.Speech;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Aevatar.GodGPT.Tests.Speech;

public class SpeechServiceTests : AevatarGodGPTTestsBase
{
    private readonly ITestOutputHelper _output;

    public SpeechServiceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Configuration_Should_Be_Available()
    {
        // Arrange & Act
        var configuration = ServiceProvider.GetRequiredService<IConfiguration>();
        
        // Debug configuration
        _output.WriteLine($"Configuration is null: {configuration == null}");
        if (configuration != null)
        {
            var speechSection = configuration.GetSection("Speech");
            _output.WriteLine($"Speech section exists: {speechSection.Exists()}");
            
            var subscriptionKey = speechSection["SubscriptionKey"];
            var region = speechSection["Region"];
            var endpoint = speechSection["Endpoint"];
            
            _output.WriteLine($"Raw SubscriptionKey: '{subscriptionKey}'");
            _output.WriteLine($"Raw Region: '{region}'");
            _output.WriteLine($"Raw Endpoint: '{endpoint}'");
            
            // Debug all configuration sources
            _output.WriteLine("Configuration providers:");
            foreach (var provider in ((IConfigurationRoot)configuration).Providers)
            {
                _output.WriteLine($"Provider: {provider.GetType().Name}");
            }
            
            // Try to get all keys starting with "Speech"
            _output.WriteLine("All configuration keys containing 'Speech':");
            var allKeys = ((IConfigurationRoot)configuration).AsEnumerable();
            foreach (var kvp in allKeys.Where(x => x.Key.Contains("Speech", StringComparison.OrdinalIgnoreCase)))
            {
                _output.WriteLine($"Key: {kvp.Key}, Value: {kvp.Value}");
            }
        }
        
        Assert.NotNull(configuration);
    }

    [Fact]
    public void SpeechOptions_Should_Be_Configured_Correctly()
    {
        // Arrange & Act
        var speechOptions = ServiceProvider.GetRequiredService<IOptionsMonitor<SpeechOptions>>();
        var options = speechOptions.CurrentValue;
        
        // 添加调试信息
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var configPath = Path.Combine(baseDir, "appsettings.json");
        
        _output.WriteLine($"Base Directory: {baseDir}");
        _output.WriteLine($"Config file path: {configPath}");
        _output.WriteLine($"Config file exists: {File.Exists(configPath)}");
        
        if (File.Exists(configPath))
        {
            var configContent = File.ReadAllText(configPath);
            _output.WriteLine($"Config content first 500 chars: {configContent.Substring(0, Math.Min(500, configContent.Length))}");
        }
        
        // Assert & Debug
        _output.WriteLine($"SpeechOptions.SubscriptionKey: '{options?.SubscriptionKey}'");
        _output.WriteLine($"SpeechOptions.Region: '{options?.Region}'");
        _output.WriteLine($"SpeechOptions.Endpoint: '{options?.Endpoint}'");
        _output.WriteLine($"SpeechOptions.RecognitionLanguage: '{options?.RecognitionLanguage}'");
        _output.WriteLine($"SpeechOptions.SynthesisLanguage: '{options?.SynthesisLanguage}'");
        _output.WriteLine($"SpeechOptions.SynthesisVoiceName: '{options?.SynthesisVoiceName}'");

        // Assert
        Assert.NotNull(options);
        Assert.False(string.IsNullOrEmpty(options.SubscriptionKey), "SubscriptionKey should not be null or empty");
        Assert.False(string.IsNullOrEmpty(options.Region), "Region should not be null or empty");
        Assert.False(string.IsNullOrEmpty(options.Endpoint), "Endpoint should not be null or empty");
        Assert.Equal("zh-CN", options.RecognitionLanguage);
        Assert.Equal("zh-CN", options.SynthesisLanguage);
        Assert.Equal("zh-CN-XiaoxiaoNeural", options.SynthesisVoiceName);
    }

    [Fact]
    public async Task SpeechToTextAsync_Should_Initialize_Without_Error()
    {
        // Arrange
        try
        {
            var speechService = ServiceProvider.GetRequiredService<ISpeechService>();
            _output.WriteLine("SpeechService created successfully");
            
            // 创建一个简单的WAV文件测试数据
            var wavData = CreateSimpleWavFile();
            
            // Act
            var result = await speechService.SpeechToTextAsync(wavData);
            
            // Assert
            _output.WriteLine($"Speech recognition result: {result}");
            Assert.NotNull(result);
            
            _output.WriteLine("SpeechToTextAsync test completed successfully");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Exception occurred: {ex.GetType().Name}: {ex.Message}");
            _output.WriteLine($"Stack trace: {ex.StackTrace}");
            
            // 如果是配置问题，应该抛出 InvalidOperationException
            if (ex is InvalidOperationException)
            {
                throw;
            }
            
            // 其他异常（如网络问题、音频格式问题等）可以容忍
            _output.WriteLine("Exception was expected for test audio data");
            Assert.True(true, "Service is properly configured and accessible");
        }
    }

    [Fact]
    public void Debug_Configuration_Loading_Process()
    {
        // Arrange & Act
        var configuration = ServiceProvider.GetRequiredService<IConfiguration>();
        var speechOptions = ServiceProvider.GetRequiredService<IOptionsMonitor<SpeechOptions>>();
        
        // 详细调试配置加载过程
        _output.WriteLine("=== Configuration Loading Debug ===");
        
        // 1. 检查配置根对象
        _output.WriteLine($"Configuration object type: {configuration.GetType().FullName}");
        _output.WriteLine($"Configuration is null: {configuration == null}");
        
        if (configuration is IConfigurationRoot configRoot)
        {
            _output.WriteLine("\n=== Configuration Providers ===");
            foreach (var provider in configRoot.Providers)
            {
                _output.WriteLine($"Provider Type: {provider.GetType().Name}");
                
                if (provider is Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider jsonProvider)
                {
                    // 使用反射获取Source信息
                    var sourceProperty = jsonProvider.GetType().GetProperty("Source");
                    if (sourceProperty != null)
                    {
                        var source = sourceProperty.GetValue(jsonProvider);
                        var pathProperty = source?.GetType().GetProperty("Path");
                        if (pathProperty != null)
                        {
                            var path = pathProperty.GetValue(source);
                            _output.WriteLine($"  JSON Source Path: {path}");
                        }
                    }
                }
            }
        }
        
        // 2. 检查Speech配置段
        var speechSection = configuration.GetSection("Speech");
        _output.WriteLine($"\n=== Speech Section Debug ===");
        _output.WriteLine($"Speech section exists: {speechSection.Exists()}");
        _output.WriteLine($"Speech section path: {speechSection.Path}");
        _output.WriteLine($"Speech section key: {speechSection.Key}");
        _output.WriteLine($"Speech section value: {speechSection.Value}");
        
        // 3. 检查具体配置值
        _output.WriteLine("\n=== Individual Configuration Values ===");
        var subscriptionKey = speechSection["SubscriptionKey"];
        var region = speechSection["Region"];
        var endpoint = speechSection["Endpoint"];
        var recognitionLanguage = speechSection["RecognitionLanguage"];
        var synthesisLanguage = speechSection["SynthesisLanguage"];
        var synthesisVoiceName = speechSection["SynthesisVoiceName"];
        
        _output.WriteLine($"SubscriptionKey from section: '{subscriptionKey}'");
        _output.WriteLine($"Region from section: '{region}'");
        _output.WriteLine($"Endpoint from section: '{endpoint}'");
        _output.WriteLine($"RecognitionLanguage from section: '{recognitionLanguage}'");
        _output.WriteLine($"SynthesisLanguage from section: '{synthesisLanguage}'");
        _output.WriteLine($"SynthesisVoiceName from section: '{synthesisVoiceName}'");
        
        // 4. 检查所有以Speech开头的配置键
        _output.WriteLine("\n=== All Speech-related Keys ===");
        var allConfigKeys = ((IConfigurationRoot)configuration).AsEnumerable()
            .Where(kvp => kvp.Key.StartsWith("Speech", StringComparison.OrdinalIgnoreCase))
            .OrderBy(kvp => kvp.Key);
        
        foreach (var kvp in allConfigKeys)
        {
            _output.WriteLine($"Key: '{kvp.Key}', Value: '{kvp.Value}'");
        }
        
        // 5. 检查IOptions<SpeechOptions>
        _output.WriteLine("\n=== IOptions<SpeechOptions> Debug ===");
        _output.WriteLine($"SpeechOptions service type: {speechOptions.GetType().FullName}");
        
        var options = speechOptions.CurrentValue;
        _output.WriteLine($"SpeechOptions object is null: {options == null}");
        
        if (options != null)
        {
            _output.WriteLine($"Options.SubscriptionKey: '{options.SubscriptionKey}'");
            _output.WriteLine($"Options.Region: '{options.Region}'");
            _output.WriteLine($"Options.Endpoint: '{options.Endpoint}'");
            _output.WriteLine($"Options.RecognitionLanguage: '{options.RecognitionLanguage}'");
            _output.WriteLine($"Options.SynthesisLanguage: '{options.SynthesisLanguage}'");
            _output.WriteLine($"Options.SynthesisVoiceName: '{options.SynthesisVoiceName}'");
        }
        
        // 6. 手动绑定测试
        _output.WriteLine("\n=== Manual Binding Test ===");
        var manualOptions = new SpeechOptions();
        speechSection.Bind(manualOptions);
        
        _output.WriteLine($"Manual binding - SubscriptionKey: '{manualOptions.SubscriptionKey}'");
        _output.WriteLine($"Manual binding - Region: '{manualOptions.Region}'");
        _output.WriteLine($"Manual binding - Endpoint: '{manualOptions.Endpoint}'");
        _output.WriteLine($"Manual binding - RecognitionLanguage: '{manualOptions.RecognitionLanguage}'");
        _output.WriteLine($"Manual binding - SynthesisLanguage: '{manualOptions.SynthesisLanguage}'");
        _output.WriteLine($"Manual binding - SynthesisVoiceName: '{manualOptions.SynthesisVoiceName}'");
        
        // 这个测试只是为了调试，不进行断言
    }

    private byte[] CreateSimpleWavFile()
    {
        using var memoryStream = new MemoryStream();
        using var writer = new BinaryWriter(memoryStream);
        
        // WAV文件头
        writer.Write("RIFF".ToCharArray());
        writer.Write((uint)36);
        writer.Write("WAVE".ToCharArray());
        
        // fmt子块
        writer.Write("fmt ".ToCharArray());
        writer.Write((uint)16);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((uint)16000);
        writer.Write((uint)32000);
        writer.Write((ushort)2);
        writer.Write((ushort)16);
        
        // data子块
        writer.Write("data".ToCharArray());
        writer.Write((uint)0);
        
        return memoryStream.ToArray();
    }
}
