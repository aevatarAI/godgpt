using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Options;
using Aevatar.Application.Grains.Agents.ChatManager;

namespace GodGPT.GAgents.SpeechChat;

public class SpeechService : ISpeechService
{
    private readonly SpeechConfig _speechConfig;

    public SpeechService(IOptions<SpeechOptions> speechOptions)
    {
        _speechConfig = SpeechConfig.FromSubscription(speechOptions.Value.SubscriptionKey, speechOptions.Value.Region);
    }

    public async Task<string> SpeechToTextAsync(byte[] audioData)
    {
        try
        {
            //support mav、mp3、aac、ogg、flac、opus
            using var pushStream = AudioInputStream.CreatePushStream();
            using var audioConfig = AudioConfig.FromStreamInput(pushStream);
            using var recognizer = new SpeechRecognizer(_speechConfig, audioConfig);
            
            pushStream.Write(audioData);
            pushStream.Close();

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
        catch (Exception ex)
        {
            Console.WriteLine($"Speech recognition error: {ex.Message}");
            throw;
        }
    }

    public async Task<byte[]> TextToSpeechAsync(string text)
    {
        // Create audio config for memory output instead of system audio
        using var audioConfig = AudioConfig.FromStreamOutput(AudioOutputStream.CreatePullStream());
        using var synthesizer = new SpeechSynthesizer(_speechConfig, audioConfig);
        using var result = await synthesizer.SpeakTextAsync(text);
        return result.AudioData;
    }

    public async Task<string> SpeechToTextAsync(byte[] audioData, VoiceLanguageEnum language)
    {
        try
        {
            var tempSpeechConfig = SpeechConfig.FromSubscription(_speechConfig.SubscriptionKey, _speechConfig.Region);
            tempSpeechConfig.SpeechRecognitionLanguage = GetLanguageCode(language);

            using var pushStream = AudioInputStream.CreatePushStream();
            using var audioConfig = AudioConfig.FromStreamInput(pushStream);
            using var recognizer = new SpeechRecognizer(tempSpeechConfig, audioConfig);
            
            pushStream.Write(audioData);
            pushStream.Close();

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
        catch (Exception ex)
        {
            Console.WriteLine($"Speech recognition error: {ex.Message}");
            throw;
        }
    }

    public async Task<byte[]> TextToSpeechAsync(string text, VoiceLanguageEnum language)
    {
        var tempSpeechConfig = SpeechConfig.FromSubscription(_speechConfig.SubscriptionKey, _speechConfig.Region);
        tempSpeechConfig.SpeechSynthesisLanguage = GetLanguageCode(language);
        tempSpeechConfig.SpeechSynthesisVoiceName = GetVoiceName(language);
        
        // Create audio config for memory output instead of system audio
        using var audioConfig = AudioConfig.FromStreamOutput(AudioOutputStream.CreatePullStream());
        using var tempSynthesizer = new SpeechSynthesizer(tempSpeechConfig, audioConfig);
        using var result = await tempSynthesizer.SpeakTextAsync(text);
        return result.AudioData;
    }

    public async Task<(byte[] AudioData, AudioMetadata Metadata)> TextToSpeechWithMetadataAsync(string text, VoiceLanguageEnum language)
    {
        // Validate input parameters
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("Text cannot be null or empty", nameof(text));
        }
        
        var tempSpeechConfig = SpeechConfig.FromSubscription(_speechConfig.SubscriptionKey, _speechConfig.Region);
        tempSpeechConfig.SpeechSynthesisLanguage = GetLanguageCode(language);
        tempSpeechConfig.SpeechSynthesisVoiceName = GetVoiceName(language);
        
        // Configure MP3 format with 16kHz sample rate
        tempSpeechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3);
        
        // Create audio config for memory output instead of system audio to avoid SPXERR_AUDIO_SYS_LIBRARY_NOT_FOUND error
        using var audioConfig = AudioConfig.FromStreamOutput(AudioOutputStream.CreatePullStream());
        using var tempSynthesizer = new SpeechSynthesizer(tempSpeechConfig, audioConfig);
        using var result = await tempSynthesizer.SpeakTextAsync(text);
        
        // Calculate approximate duration based on text length and speech rate
        // Average speech rate is ~150 words per minute or ~2.5 words per second
        var wordCount = text.Split(' ').Length;
        var estimatedDuration = wordCount / 2.5; // seconds
        
        var metadata = new AudioMetadata
        {
            Duration = estimatedDuration,
            SizeBytes = result.AudioData.Length,
            SampleRate = 16000, // 16kHz as configured
            BitRate = 32000, // 32kbps as configured
            Format = "mp3",
            LanguageType = language
        };
        
        return (result.AudioData, metadata);
    }

    private static string GetLanguageCode(VoiceLanguageEnum language)
    {
        return language switch
        {
            VoiceLanguageEnum.English => "en-US",
            VoiceLanguageEnum.Chinese => "zh-CN",
            VoiceLanguageEnum.Spanish => "es-ES",
            _ => "en-US"
        };
    }

    private static string GetVoiceName(VoiceLanguageEnum language)
    {
        return language switch
        {
            VoiceLanguageEnum.English => "en-US-JennyNeural",
            VoiceLanguageEnum.Chinese => "zh-CN-XiaoxiaoNeural",
            VoiceLanguageEnum.Spanish => "es-ES-ElviraNeural",
            _ => "en-US-JennyNeural"
        };
    }
}