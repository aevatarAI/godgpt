using GodGPT.GAgents.SpeechChat;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.GodGPT.Tests.Speech;

public class SpeechServiceTests : AevatarGodGPTTestsBase
{
    private readonly ITestOutputHelper _output;
    private const string TEST_MP3_BASE64="";
    public SpeechServiceTests(ITestOutputHelper output)
    {
        _output = output;
    }
    

    [Fact]
    public async Task SpeechToTextAsync_Should_Initialize_Without_Error()
    {
        var speechService = ServiceProvider.GetRequiredService<ISpeechService>();
        var audioData = Convert.FromBase64String(SpeechConstants.WAW_BASE64);

        // Act & Assert
        var result = await speechService.SpeechToTextAsync(audioData, VoiceLanguageEnum.Chinese);
            
        // For WAV format, expect actual recognition (would work with real Azure Speech service)
        // For Opus format on macOS without GStreamer, expect our informative message
        Assert.True(!string.IsNullOrEmpty(result), "Result should not be null or empty");
            
        // Log the result for debugging
        Console.WriteLine($"Speech recognition result: '{result}'");
            
        // Normalize the result by removing punctuation and spaces for comparison
        var normalizedResult = result.Replace("。", "").Replace("，", "").Replace(" ", "").Replace(".", "").Replace(",", "");
        normalizedResult.ShouldContain("123456");
    }
    [Fact]
    public async Task SpeechToTextByLanguageAsync_Should_Initialize_Without_Error()
    {
        var speechService = ServiceProvider.GetRequiredService<ISpeechService>();
        // Convert base64 string to byte array
        var wavData = ConvertBase64ToByteArray(SpeechConstants.WAW_BASE64);
            
        // Act
        var result = await speechService.SpeechToTextAsync(wavData, VoiceLanguageEnum.Chinese);
        Assert.NotNull(result);
        
        // Normalize the result by removing punctuation and spaces for comparison
        var normalizedResult = result.Replace("。", "").Replace("，", "").Replace(" ", "").Replace(".", "").Replace(",", "");
        normalizedResult.ShouldContain("123456");
    }

    [Fact]
    public async Task TextToSpeechWithMetadataAsync_Should_Return_AudioData_And_Metadata()
    {
        // Arrange
        var speechService = ServiceProvider.GetRequiredService<ISpeechService>();
        var testText = "Hello, this is a test message for text-to-speech conversion.";
        var language = VoiceLanguageEnum.English;

        // Act
        var result = await speechService.TextToSpeechWithMetadataAsync(testText, language);

        // Assert
        result.AudioData.ShouldNotBeNull();
        result.AudioData.Length.ShouldBeGreaterThan(0);
        
        result.Metadata.ShouldNotBeNull();
        result.Metadata.Format.ShouldBe("mp3");
        result.Metadata.SampleRate.ShouldBe(16000);
        result.Metadata.BitRate.ShouldBeGreaterThan(0);
        result.Metadata.SizeBytes.ShouldBe(result.AudioData.Length);
        result.Metadata.Duration.ShouldBeGreaterThan(0);
        result.Metadata.LanguageType.ShouldBe(language);

        _output.WriteLine($"Generated audio: {result.AudioData.Length} bytes");
        _output.WriteLine($"Duration: {result.Metadata.Duration} seconds");
        _output.WriteLine($"Sample Rate: {result.Metadata.SampleRate} Hz");
        _output.WriteLine($"Bit Rate: {result.Metadata.BitRate} bps");
    }

    [Theory]
    [InlineData(VoiceLanguageEnum.English, "Hello world, how are you today?")]
    [InlineData(VoiceLanguageEnum.Spanish, "Hola mundo, ¿cómo estás hoy?")]
    public async Task TextToSpeechWithMetadataAsync_Should_Support_Multiple_Languages(VoiceLanguageEnum language, string text)
    {
        // Arrange
        var speechService = ServiceProvider.GetRequiredService<ISpeechService>();

        // Act
        var result = await speechService.TextToSpeechWithMetadataAsync(text, language);

        // Assert
        result.AudioData.ShouldNotBeNull();
        result.AudioData.Length.ShouldBeGreaterThan(0);
        
        result.Metadata.ShouldNotBeNull();
        result.Metadata.LanguageType.ShouldBe(language);
        result.Metadata.Format.ShouldBe("mp3");
        result.Metadata.SampleRate.ShouldBe(16000);

        _output.WriteLine($"Language: {language}");
        _output.WriteLine($"Text: {text}");
        _output.WriteLine($"Audio size: {result.AudioData.Length} bytes");
        _output.WriteLine($"Duration: {result.Metadata.Duration} seconds");
    }

    [Fact]
    public async Task TextToSpeechWithMetadataAsync_Should_Handle_Empty_Text()
    {
        // Arrange
        var speechService = ServiceProvider.GetRequiredService<ISpeechService>();
        var emptyText = "";
        var language = VoiceLanguageEnum.English;

        // Act & Assert
        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await speechService.TextToSpeechWithMetadataAsync(emptyText, language);
        });
    }

    [Fact]
    public async Task TextToSpeechWithMetadataAsync_Should_Handle_Long_Text()
    {
        // Arrange
        var speechService = ServiceProvider.GetRequiredService<ISpeechService>();
        var longText = string.Join(" ", Enumerable.Repeat("This is a test sentence for long text conversion.", 10));
        var language = VoiceLanguageEnum.English;

        // Act
        var result = await speechService.TextToSpeechWithMetadataAsync(longText, language);

        // Assert
        result.AudioData.ShouldNotBeNull();
        result.AudioData.Length.ShouldBeGreaterThan(0);
        result.Metadata.Duration.ShouldBeGreaterThan(5); // Long text should have longer duration

        _output.WriteLine($"Long text length: {longText.Length} characters");
        _output.WriteLine($"Audio duration: {result.Metadata.Duration} seconds");
        _output.WriteLine($"Audio size: {result.AudioData.Length} bytes");
    }

    [Fact]
    public async Task TextToSpeechWithMetadataAsync_Should_Calculate_Correct_Duration()
    {
        // Arrange
        var speechService = ServiceProvider.GetRequiredService<ISpeechService>();
        var shortText = "Hello";
        var longText = "Hello world, this is a much longer text that should take more time to speak than the short one.";
        var language = VoiceLanguageEnum.English;

        // Act
        var shortResult = await speechService.TextToSpeechWithMetadataAsync(shortText, language);
        var longResult = await speechService.TextToSpeechWithMetadataAsync(longText, language);

        // Assert
        shortResult.Metadata.Duration.ShouldBeGreaterThan(0);
        longResult.Metadata.Duration.ShouldBeGreaterThan(shortResult.Metadata.Duration);

        _output.WriteLine($"Short text duration: {shortResult.Metadata.Duration} seconds");
        _output.WriteLine($"Long text duration: {longResult.Metadata.Duration} seconds");
    }

    private byte[] ConvertBase64ToByteArray(string base64String)
    {
        try
        {
            var data = Convert.FromBase64String(base64String);
            return data;
        }
        catch (FormatException formatEx)
        {
            throw new ArgumentException("Invalid base64 string format", nameof(base64String), formatEx);
        }
    }

    private byte[] CreateSimpleWavFile()
    {
        using var memoryStream = new MemoryStream();
        using var writer = new BinaryWriter(memoryStream);
        
        // WAV file header
        writer.Write("RIFF".ToCharArray());
        writer.Write((uint)36);
        writer.Write("WAVE".ToCharArray());
        
        // fmt sub-chunk
        writer.Write("fmt ".ToCharArray());
        writer.Write((uint)16);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((uint)16000);
        writer.Write((uint)32000);
        writer.Write((ushort)2);
        writer.Write((ushort)16);
        
        // data sub-chunk
        writer.Write("data".ToCharArray());
        writer.Write((uint)0);
        
        return memoryStream.ToArray();
    }
}
