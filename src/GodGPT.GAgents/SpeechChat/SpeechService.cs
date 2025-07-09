using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Aevatar.Application.Grains.Agents.ChatManager;

namespace GodGPT.GAgents.SpeechChat;

public class SpeechService : ISpeechService
{
    private readonly SpeechConfig _speechConfig;
    private readonly AudioGainProcessor _audioGainProcessor;
    private readonly ILogger<SpeechService> _logger;
    
    public SpeechService(IOptions<SpeechOptions> speechOptions, ILogger<SpeechService> logger)
    {
        _speechConfig = SpeechConfig.FromSubscription(speechOptions.Value.SubscriptionKey, speechOptions.Value.Region);
        
        // Configure for MP3 output format - 16kHz, 32kbps, mono
        _speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3);
        
        _audioGainProcessor = new AudioGainProcessor();
        _logger = logger;
        
        _logger.LogInformation("SpeechService initialized with MP3 output format: 16kHz, 32kbps, mono");
    }

    public async Task<(byte[] AudioData, AudioMetadata Metadata)> TextToSpeechWithMetadataAsync(string text, VoiceLanguageEnum voiceLanguage)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("Text cannot be null or empty", nameof(text));
        }

        try
        {
            using var synthesizer = new SpeechSynthesizer(_speechConfig);
            
            // Configure voice based on language
            var voiceName = GetVoiceName(voiceLanguage);
            _speechConfig.SpeechSynthesisVoiceName = voiceName;
            
            _logger.LogDebug("Starting TTS synthesis for text length: {TextLength}, voice: {Voice}", 
                text.Length, voiceName);

            // Perform text-to-speech synthesis
            using var result = await synthesizer.SpeakTextAsync(text);
            
            // Check synthesis result
            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                _logger.LogDebug("TTS synthesis completed successfully, audio data size: {DataSize} bytes", 
                    result.AudioData.Length);
                
                // Apply 3x audio gain amplification for volume enhancement
                var amplifiedAudioData = await _audioGainProcessor.ApplyGainAsync(result.AudioData, 3.0f);
                
                _logger.LogDebug("Audio gain processing completed, final size: {FinalSize} bytes", 
                    amplifiedAudioData.Length);
                
                // Create metadata for MP3 format
                var metadata = new AudioMetadata
                {
                    Format = "mp3",
                    SampleRate = 16000,  // 16kHz
                    BitRate = 32000,     // 32kbps  
                    SizeBytes = amplifiedAudioData.Length,
                    Duration = EstimateDurationInSeconds(amplifiedAudioData.Length, 32000), // Estimate based on bitrate
                    LanguageType = voiceLanguage
                };
                
                return (amplifiedAudioData, metadata);
            }
            else if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                var errorMessage = $"TTS synthesis was cancelled: {cancellation.Reason}";
                
                if (cancellation.Reason == CancellationReason.Error)
                {
                    errorMessage += $" - Error: {cancellation.ErrorDetails}";
                }
                
                _logger.LogError("TTS synthesis failed: {ErrorMessage}", errorMessage);
                throw new InvalidOperationException(errorMessage);
            }
            else
            {
                var errorMessage = $"TTS synthesis failed with reason: {result.Reason}";
                _logger.LogError("TTS synthesis failed: {ErrorMessage}", errorMessage);
                throw new InvalidOperationException(errorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during TTS synthesis");
            throw;
        }
    }

    public async Task<string> SpeechToTextAsync(byte[] audioData)
    {
        try
        {
            //support wav、mp3、aac、ogg、flac、opus
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

    private string GetVoiceName(VoiceLanguageEnum voiceLanguage)
    {
        return voiceLanguage switch
        {
            VoiceLanguageEnum.English => "en-US-AriaNeural",
            VoiceLanguageEnum.Chinese => "zh-CN-XiaoxiaoNeural",
            VoiceLanguageEnum.Spanish => "es-ES-ElviraNeural",
            _ => "en-US-AriaNeural" // Default to English
        };
    }
    
    private double EstimateDurationInSeconds(int audioDataLength, int bitrate)
    {
        // Estimate duration: (data size in bytes * 8 bits/byte) / bitrate = duration in seconds
        return (audioDataLength * 8.0) / bitrate;
    }
}