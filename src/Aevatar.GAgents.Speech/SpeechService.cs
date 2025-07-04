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

    public SpeechService(IOptions<SpeechOptions> speechOptions)
    {
        _speechConfig = SpeechConfig.FromSubscription(speechOptions.Value.SubscriptionKey, speechOptions.Value.Region);
        _speechConfig.SpeechRecognitionLanguage = "zh-CN";
        _speechConfig.SpeechSynthesisLanguage = "zh-CN";
        _speechConfig.SpeechSynthesisVoiceName = "zh-CN-XiaoxiaoNeural";
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

            // Add event handlers for better debugging
            recognizer.Recognizing += (s, e) => {
                Console.WriteLine($"RECOGNIZING: Text={e.Result.Text}");
            };

            recognizer.Recognized += (s, e) => {
                Console.WriteLine($"RECOGNIZED: Text={e.Result.Text}");
                Console.WriteLine($"RECOGNIZED: Reason={e.Result.Reason}");
                if (e.Result.Reason == ResultReason.RecognizedSpeech)
                {
                    Console.WriteLine($"RECOGNIZED: Final result: {e.Result.Text}");
                }
                else if (e.Result.Reason == ResultReason.NoMatch)
                {
                    Console.WriteLine($"NOMATCH: Speech could not be recognized.");
                }
            };

            recognizer.Canceled += (s, e) => {
                Console.WriteLine($"CANCELED: Reason={e.Reason}");
                if (e.Reason == CancellationReason.Error)
                {
                    Console.WriteLine($"CANCELED: ErrorCode={e.ErrorCode}");
                    Console.WriteLine($"CANCELED: ErrorDetails={e.ErrorDetails}");
                }
            };

            var result = await recognizer.RecognizeOnceAsync();
            if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = CancellationDetails.FromResult(result);
                Console.WriteLine($"  Cancellation Reason: {cancellation.Reason}");
                if (cancellation.Reason == CancellationReason.Error)
                {
                    Console.WriteLine($"  Error Code: {cancellation.ErrorCode}");
                    Console.WriteLine($"  Error Details: {cancellation.ErrorDetails}");
                }
            }
            
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