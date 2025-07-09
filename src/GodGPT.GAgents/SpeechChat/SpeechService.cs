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
    
    // SSML volume control configuration - hardcoded for enhanced audio output
    private const string DEFAULT_VOLUME_BOOST = "+40%";  // Increase volume by 40%
    private const string BACKUP_VOLUME_BOOST = "+30%";   // Fallback if +40% fails
    
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
        
        // Use SSML with volume control for enhanced audio output
        string ssmlText = WrapTextWithVolumeSSML(text, language, DEFAULT_VOLUME_BOOST);
        SpeechSynthesisResult result;
        
        try
        {
            // Try with primary volume boost
            result = await tempSynthesizer.SpeakSsmlAsync(ssmlText);
            
            // Check if synthesis failed, fallback to backup volume or plain text
            if (result.Reason == ResultReason.Canceled)
            {
                Console.WriteLine($"Primary SSML synthesis failed, trying backup volume");
                ssmlText = WrapTextWithVolumeSSML(text, language, BACKUP_VOLUME_BOOST);
                result = await tempSynthesizer.SpeakSsmlAsync(ssmlText);
                
                if (result.Reason == ResultReason.Canceled)
                {
                    Console.WriteLine($"Backup SSML synthesis failed, using plain text");
                    result = await tempSynthesizer.SpeakTextAsync(text);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SSML synthesis error: {ex.Message}, falling back to plain text");
            result = await tempSynthesizer.SpeakTextAsync(text);
        }
        
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
        
        result.Dispose();
        return (result.AudioData, metadata);
    }
    
    /// <summary>
    /// Wraps plain text with SSML prosody tags for volume control
    /// </summary>
    /// <param name="text">Plain text to synthesize</param>
    /// <param name="language">Target language for proper SSML configuration</param>
    /// <param name="volumeBoost">Volume boost percentage (e.g., "+40%")</param>
    /// <returns>SSML formatted string with volume control</returns>
    private static string WrapTextWithVolumeSSML(string text, VoiceLanguageEnum language, string volumeBoost)
    {
        // Escape any XML special characters in the text
        var escapedText = text.Replace("&", "&amp;")
                             .Replace("<", "&lt;")
                             .Replace(">", "&gt;")
                             .Replace("\"", "&quot;")
                             .Replace("'", "&apos;");
        
        var languageCode = GetLanguageCode(language);
        var voiceName = GetVoiceName(language);
        
        return $@"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{languageCode}'>
    <voice name='{voiceName}'>
        <prosody volume='{volumeBoost}'>
            {escapedText}
        </prosody>
    </voice>
</speak>";
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