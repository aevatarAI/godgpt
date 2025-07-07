using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GodGPT.GAgents.SpeechChat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
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
        // Convert base64 string to byte array
        var wavData = ConvertBase64ToByteArray(SpeechConstants.WAW_BASE64);
            
        // Act
        var result = await speechService.SpeechToTextAsync(wavData);
        Assert.NotNull(result);
        result.ShouldContain("123456");
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
        result.ShouldContain("123456");
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
