using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Options;

namespace Aevatar.GAgents.Speech;

public class SpeechService : ISpeechService
{
    private readonly SpeechConfig _speechConfig;
    private readonly SpeechSynthesizer _synthesizer;

    public SpeechService(IOptionsMonitor<SpeechOptions> speechOptions)
    {
        var options = speechOptions.CurrentValue;
        if (options == null)
        {
            throw new InvalidOperationException("SpeechOptions configuration is null. Please check your appsettings.json configuration.");
        }
        
        if (string.IsNullOrEmpty(options.SubscriptionKey))
        {
            throw new InvalidOperationException("SpeechOptions.SubscriptionKey is null or empty. Please check your appsettings.json configuration.");
        }
        
        if (string.IsNullOrEmpty(options.Region) && string.IsNullOrEmpty(options.Endpoint))
        {
            throw new InvalidOperationException("Both SpeechOptions.Region and SpeechOptions.Endpoint are null or empty. Please provide at least one.");
        }
        if (!string.IsNullOrEmpty(options.Endpoint))
        {
            var endpointUri = new Uri(options.Endpoint);
            _speechConfig = SpeechConfig.FromEndpoint(endpointUri, options.SubscriptionKey);
        }
        else
        {
            _speechConfig = SpeechConfig.FromSubscription(options.SubscriptionKey, options.Region);
        }
        
        _speechConfig.SpeechRecognitionLanguage = options.RecognitionLanguage;
        _speechConfig.SpeechSynthesisLanguage = options.SynthesisLanguage;
        _speechConfig.SpeechSynthesisVoiceName = options.SynthesisVoiceName;
        _synthesizer = new SpeechSynthesizer(_speechConfig);
    }

    public async Task<string> SpeechToTextAsync(byte[] audioData)
    {
        var tempFilePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFilePath, audioData);

            using var audioConfig = AudioConfig.FromWavFileInput(tempFilePath);
            using var recognizer = new SpeechRecognizer(_speechConfig, audioConfig);

            var result = await recognizer.RecognizeOnceAsync();
            return result.Text;
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    public async Task<byte[]> TextToSpeechAsync(string text)
    {
        using var result = await _synthesizer.SpeakTextAsync(text);
        return result.AudioData;
    }
}