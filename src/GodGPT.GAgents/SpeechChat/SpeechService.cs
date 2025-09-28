using Microsoft.Extensions.Logging;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Options;
using Aevatar.Application.Grains.Agents.ChatManager;

namespace GodGPT.GAgents.SpeechChat;

public class SpeechService : ISpeechService
{
    private readonly SpeechConfig _speechConfig;
    private readonly ILogger<SpeechService> _logger;

    public SpeechService(IOptions<SpeechOptions> speechOptions,
        ILogger<SpeechService> logger)
    {
        _speechConfig = SpeechConfig.FromSubscription(speechOptions.Value.SubscriptionKey, speechOptions.Value.Region);
        _logger = logger;
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
                _logger.LogDebug("[SpeechService][SpeechToTextAsync] - Cancellation Reason:{0}",cancellation.Reason);
                if (cancellation.Reason == CancellationReason.Error)
                {
                    _logger.LogDebug("[SpeechService][SpeechToTextAsync] - Error Code:{0},Error Details:{1}",cancellation.ErrorCode,cancellation.ErrorDetails);
                }
            }
            
            return result.Text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,"[SpeechService][SpeechToTextAsync] - Speech recognition error:{errMsg}",ex.Message);
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
            // Validate audio data
            if (audioData == null || audioData.Length == 0)
            {
                throw new ArgumentException("Audio data cannot be null or empty", nameof(audioData));
            }

            // Detect audio format
            var audioFormat = DetectAudioFormat(audioData);
            var tempSpeechConfig = SpeechConfig.FromSubscription(_speechConfig.SubscriptionKey, _speechConfig.Region);
            tempSpeechConfig.SpeechRecognitionLanguage = GetLanguageCode(language);

            // Special handling for WebM/Opus format
            if (IsWebMFormat(audioData))
            {
                return await HandleWebMOpusFormat(audioData, tempSpeechConfig);
            }

            // Special handling for M4A format
            if (IsM4AFormat(audioData))
            {
                return await HandleM4AFormat(audioData, tempSpeechConfig);
            }

            // For compressed formats (Opus, MP3, etc.), try compressed format first
            if (audioFormat.HasValue)
            {
                try
                {
                    _logger.LogDebug("[SpeechService][SpeechToTextAsync-language] - Attempting recognition with compressed format:{0}",audioFormat.Value);
                    var result = await TryRecognizeWithFormat(audioData, tempSpeechConfig, audioFormat.Value);
                    if (!string.IsNullOrEmpty(result))
                    {
                        _logger.LogDebug("[SpeechService][SpeechToTextAsync-language] - Successfully recognition with audioFormat:{0},format:{1}",audioFormat.Value, result);
                        return result;
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("SPXERR_GSTREAMER_NOT_FOUND_ERROR"))
                {
                    _logger.LogError(ex, "[SpeechService][SpeechToTextAsync-language] - InvalidOperationException GStreamer not available for:{0} format. Note: Microsoft Speech SDK requires GStreamer for compressed audio formats.}",audioFormat.Value);

                    // Provide specific guidance for WebM/Opus
                    if (audioFormat.Value == AudioStreamContainerFormat.OGG_OPUS)
                    {
                        return "⚠️ WebM/Opus format detected but requires GStreamer support. " +
                               "Solutions: 1) Install GStreamer with Opus plugin, " +
                               "2) Convert to WAV format first, " +
                               "3) Use browser-based Web Audio API for Opus decoding.";
                    }
                    
                    return $"⚠️ {audioFormat.Value} format detected but GStreamer unavailable. Please convert to WAV format for speech recognition.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SpeechService][SpeechToTextAsync-language] - Exception Failed to recognize with:{0} format:{1}",audioFormat.Value,ex.Message);
                }
            }

            // Fallback: Try with default format
            try
            {
                _logger.LogDebug("[SpeechService][SpeechToTextAsync-language] -Attempting recognition with default format (fallback)...");

                var result = await TryRecognizeWithFormat(audioData, tempSpeechConfig, null);
                if (!string.IsNullOrEmpty(result))
                {
                    _logger.LogDebug("[SpeechService][SpeechToTextAsync-language] -Successfully recognized with default format:{0}", result);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SpeechService][SpeechToTextAsync-language] -Failed to recognize with default format:{0}", ex.Message);
            }

            // If we reach here, all attempts failed
            var formatName = audioFormat?.ToString() ?? (DetectAudioFormat(audioData)?.ToString() ?? "WAV/PCM");
            throw new InvalidOperationException($"Unable to process audio data. Format: {formatName}. " +
                                              "Consider converting to WAV format for better compatibility.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SpeechService][SpeechToTextAsync-language] - Exception SpeechToTextAsync error:{0}", ex.Message);
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
            _logger.LogError("[VOICE_SERVICE_DEBUG] Text cannot be null or empty");
            throw new ArgumentException("Text cannot be null or empty", nameof(text));
        }
        
        _logger.LogInformation($"[VOICE_SERVICE_DEBUG] Starting TTS - Text: '{text}', Language: {language}");
        
        var tempSpeechConfig = SpeechConfig.FromSubscription(_speechConfig.SubscriptionKey, _speechConfig.Region);
        var languageCode = GetLanguageCode(language);
        var voiceName = GetVoiceName(language);
        
        tempSpeechConfig.SpeechSynthesisLanguage = languageCode;
        tempSpeechConfig.SpeechSynthesisVoiceName = voiceName;
        
        _logger.LogInformation($"[VOICE_SERVICE_DEBUG] Config - LanguageCode: {languageCode}, VoiceName: {voiceName}, Region: {_speechConfig.Region}");
        
        // Configure MP3 format with 16kHz sample rate
        tempSpeechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3);
        
        // Create audio config for memory output instead of system audio to avoid SPXERR_AUDIO_SYS_LIBRARY_NOT_FOUND error
        using var audioConfig = AudioConfig.FromStreamOutput(AudioOutputStream.CreatePullStream());
        using var tempSynthesizer = new SpeechSynthesizer(tempSpeechConfig, audioConfig);
        
        _logger.LogInformation($"[VOICE_SERVICE_DEBUG] Calling SpeakTextAsync...");
        using var result = await tempSynthesizer.SpeakTextAsync(text);
        
        _logger.LogInformation($"[VOICE_SERVICE_DEBUG] TTS Result - Reason: {result.Reason}, AudioData: {result.AudioData?.Length ?? 0} bytes");
        
        if (result.Reason != ResultReason.SynthesizingAudioCompleted)
        {
            _logger.LogError($"[VOICE_SERVICE_DEBUG] TTS Failed - Reason: {result.Reason}");
            if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                _logger.LogError($"[VOICE_SERVICE_DEBUG] TTS Canceled - Reason: {cancellation.Reason}, ErrorCode: {cancellation.ErrorCode}, ErrorDetails: {cancellation.ErrorDetails}");
            }
        }
        
        // Calculate approximate duration based on text length and speech rate
        // Average speech rate is ~150 words per minute or ~2.5 words per second
        var wordCount = text.Split(' ').Length;
        var estimatedDuration = wordCount / 2.5; // seconds
        
        var metadata = new AudioMetadata
        {
            Duration = estimatedDuration,
            SizeBytes = result.AudioData?.Length ?? 0,
            SampleRate = 16000, // 16kHz as configured
            BitRate = 32000, // 32kbps as configured
            Format = "mp3",
            LanguageType = language
        };
        
        _logger.LogInformation($"[VOICE_SERVICE_DEBUG] TTS Success - Metadata: Duration={metadata.Duration}s, Size={metadata.SizeBytes} bytes");
        
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

    /// <summary>
    /// Detects the audio format based on file header bytes
    /// </summary>
    /// <param name="audioData">Audio data bytes</param>
    /// <returns>Detected AudioStreamContainerFormat</returns>
    private  AudioStreamContainerFormat? DetectAudioFormat(byte[] audioData)
    {
        if (audioData == null || audioData.Length < 4)
            return null;

        // Check for common audio format signatures
        var header = audioData.Take(Math.Min(32, audioData.Length)).ToArray();
        
        // WebM format: starts with EBML header (0x1A, 0x45, 0xDF, 0xA3)
        if (header.Length >= 4 && header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
        {
            _logger.LogDebug("[SpeechService][DetectAudioFormat] - WebM format detected - contains Opus audio codec");
            // WebM files typically contain Opus or Vorbis audio
            return AudioStreamContainerFormat.OGG_OPUS; // Use OGG_OPUS as closest match for WebM/Opus
        }
        
        // Opus in OGG container: "OggS" signature
        if (header.Length >= 4 && header[0] == 0x4F && header[1] == 0x67 && header[2] == 0x67 && header[3] == 0x53)
        {
            // Further check for Opus magic signature in OGG pages
            // Look for "OpusHead" signature within the first few hundred bytes
            for (int i = 0; i < Math.Min(audioData.Length - 8, 200); i++)
            {
                if (audioData[i] == 0x4F && audioData[i + 1] == 0x70 && audioData[i + 2] == 0x75 && audioData[i + 3] == 0x73 &&
                    audioData[i + 4] == 0x48 && audioData[i + 5] == 0x65 && audioData[i + 6] == 0x61 && audioData[i + 7] == 0x64)
                {
                    _logger.LogDebug("[SpeechService][DetectAudioFormat] - OGG/Opus format detected");
                    return AudioStreamContainerFormat.OGG_OPUS;
                }
            }
            // Default to OGG if no Opus signature found
            _logger.LogDebug("[SpeechService][DetectAudioFormat] - OGG format detected (non-Opus)");
            return AudioStreamContainerFormat.OGG_OPUS;
        }

        // MP3 format: FF FB or FF FA (MPEG Audio Layer III)
        if (header.Length >= 2 && header[0] == 0xFF && (header[1] == 0xFB || header[1] == 0xFA))
        {
            _logger.LogDebug("[SpeechService][DetectAudioFormat] - MP3 format detected");
            return AudioStreamContainerFormat.MP3;
        }

        // WAV format: "RIFF" + "WAVE"
        if (header.Length >= 12 && 
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && // "RIFF"
            header[8] == 0x57 && header[9] == 0x41 && header[10] == 0x56 && header[11] == 0x45)   // "WAVE"
        {
            _logger.LogDebug("[SpeechService][DetectAudioFormat] - WAV format detected");
            return null; // WAV uses default format
        }

        // AAC format: FF F1 or FF F9 (ADTS)
        if (header.Length >= 2 && header[0] == 0xFF && (header[1] == 0xF1 || header[1] == 0xF9))
        {
            _logger.LogDebug("[SpeechService][DetectAudioFormat] - AAC format detected");
            return AudioStreamContainerFormat.ALAW; // Use as closest match
        }

        // FLAC format: "fLaC"
        if (header.Length >= 4 && header[0] == 0x66 && header[1] == 0x4C && header[2] == 0x61 && header[3] == 0x43)
        {
            _logger.LogDebug("[SpeechService][DetectAudioFormat] - FLAC format detected");

            return AudioStreamContainerFormat.FLAC;
        }

        // M4A format: MPEG-4 container with "ftyp" box
        // M4A files start with a 4-byte size followed by "ftyp"
        if (header.Length >= 8 && 
            header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70) // "ftyp"
        {
            _logger.LogDebug("[SpeechService][DetectAudioFormat] - M4A format detected");
            return AudioStreamContainerFormat.ALAW; // Use ALAW as closest match for M4A
        }

        _logger.LogDebug("[SpeechService][DetectAudioFormat] - Unknown audio format, will try as PCM/WAV");

        // Default to null for unknown formats (will be treated as PCM/WAV)
        return null;
    }

    /// <summary>
    /// Attempts to recognize speech using specified audio format
    /// </summary>
    /// <param name="audioData">Audio data bytes</param>
    /// <param name="speechConfig">Speech configuration</param>
    /// <param name="containerFormat">Audio container format, null for default</param>
    /// <returns>Recognized text</returns>
    private static async Task<string> TryRecognizeWithFormat(byte[] audioData, SpeechConfig speechConfig, AudioStreamContainerFormat? containerFormat)
    {
        AudioConfig audioConfig;
        PushAudioInputStream pushStream;

        // Create appropriate audio configuration based on format
        if (containerFormat.HasValue)
        {
            // Use specified compressed format
            var streamFormat = AudioStreamFormat.GetCompressedFormat(containerFormat.Value);
            pushStream = AudioInputStream.CreatePushStream(streamFormat);
            audioConfig = AudioConfig.FromStreamInput(pushStream);
        }
        else
        {
            // Use default format for WAV/PCM
            pushStream = AudioInputStream.CreatePushStream();
            audioConfig = AudioConfig.FromStreamInput(pushStream);
        }

        using (pushStream)
        using (audioConfig)
        using (var recognizer = new SpeechRecognizer(speechConfig, audioConfig))
        {
            var recognitionResults = new List<string>();
            var recognitionCompleted = new TaskCompletionSource<bool>();
            var sessionStopped = new TaskCompletionSource<bool>();

            recognizer.Recognized += (s, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech)
                {
                    recognitionResults.Add(e.Result.Text);
                }
            };
            recognizer.Recognizing += (s, e) => { };
            recognizer.Canceled += (s, e) =>
            {
                if (e.Reason == CancellationReason.Error)
                    recognitionCompleted.TrySetException(new InvalidOperationException($"Speech recognition failed: {e.ErrorCode} - {e.ErrorDetails}"));
                else
                    recognitionCompleted.TrySetException(new OperationCanceledException("Speech recognition was canceled"));
            };
            recognizer.SessionStopped += (s, e) => sessionStopped.TrySetResult(true);

            await recognizer.StartContinuousRecognitionAsync();
            pushStream.Write(audioData);
            pushStream.Close();
            
            try
            {
                await Task.WhenAny(sessionStopped.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            }
            catch (Exception ex)
            {
            }
            
            await recognizer.StopContinuousRecognitionAsync();

            if (recognitionResults.Count > 0)
            {
                return string.Join(" ", recognitionResults);
            }

            // 2. fallback: RecognizeOnceAsync
            using var pushStream2 = containerFormat.HasValue
                ? AudioInputStream.CreatePushStream(AudioStreamFormat.GetCompressedFormat(containerFormat.Value))
                : AudioInputStream.CreatePushStream();
            using var audioConfig2 = AudioConfig.FromStreamInput(pushStream2);
            using var recognizer2 = new SpeechRecognizer(speechConfig, audioConfig2);
            pushStream2.Write(audioData);
            pushStream2.Close();
            var result = await recognizer2.RecognizeOnceAsync();
            if (result.Reason == ResultReason.RecognizedSpeech)
            {
                return result.Text;
            }
            else if (result.Reason == ResultReason.NoMatch)
            {
                using var pushStream3 = AudioInputStream.CreatePushStream();
                using var audioConfig3 = AudioConfig.FromStreamInput(pushStream3);
                using var recognizer3 = new SpeechRecognizer(SpeechConfig.FromSubscription(speechConfig.SubscriptionKey, speechConfig.Region), audioConfig3);
                pushStream3.Write(audioData);
                pushStream3.Close();
                var result3 = await recognizer3.RecognizeOnceAsync();
                if (result3.Reason == ResultReason.RecognizedSpeech)
                {
                    return result3.Text;
                }
                throw new InvalidOperationException("No speech could be recognized from the audio data");
            }
            else if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = CancellationDetails.FromResult(result);
                if (cancellation.Reason == CancellationReason.Error)
                {
                    throw new InvalidOperationException($"Speech recognition failed: {cancellation.ErrorCode} - {cancellation.ErrorDetails}");
                }
                throw new OperationCanceledException("Speech recognition was canceled");
            }
            else
            {
                throw new InvalidOperationException($"Unexpected recognition result: {result.Reason}");
            }
        }
    }

    /// <summary>
    /// Checks if the audio data is in WebM format
    /// </summary>
    /// <param name="audioData">Audio data bytes</param>
    /// <returns>True if WebM format is detected</returns>
    private static bool IsWebMFormat(byte[] audioData)
    {
        if (audioData == null || audioData.Length < 4)
            return false;

        // WebM format: starts with EBML header (0x1A, 0x45, 0xDF, 0xA3)
        return audioData[0] == 0x1A && audioData[1] == 0x45 && audioData[2] == 0xDF && audioData[3] == 0xA3;
    }

    /// <summary>
    /// Checks if the audio data is in M4A format
    /// </summary>
    /// <param name="audioData">Audio data bytes</param>
    /// <returns>True if M4A format is detected</returns>
    private static bool IsM4AFormat(byte[] audioData)
    {
        if (audioData == null || audioData.Length < 8)
            return false;

        // M4A format: MPEG-4 container with "ftyp" box
        // M4A files start with a 4-byte size followed by "ftyp"
        return audioData[4] == 0x66 && audioData[5] == 0x74 && audioData[6] == 0x79 && audioData[7] == 0x70; // "ftyp"
    }

    /// <summary>
    /// Handles WebM/Opus format audio processing
    /// </summary>
    /// <param name="audioData">WebM audio data</param>
    /// <param name="speechConfig">Speech configuration</param>
    /// <returns>Recognition result or error message</returns>
    private  async Task<string> HandleWebMOpusFormat(byte[] audioData, SpeechConfig speechConfig)
    {
        string tempWebMFile = null;
        string tempWavFile = null;
        
        try
        {
            _logger.LogDebug("[SpeechService][HandleWebMOpusFormat] - Processing WebM/Opus format - converting to WAV using FF mpeg..");
            // Create temporary files
            tempWebMFile = Path.GetTempFileName() + ".webm";
            tempWavFile = Path.GetTempFileName() + ".wav";
            
            // Write WebM data to temporary file
            await File.WriteAllBytesAsync(tempWebMFile, audioData);
            _logger.LogDebug("[SpeechService][HandleWebMOpusFormat] - WebM data written to temporary file:{0}",tempWebMFile);

            // Convert WebM/Opus to WAV using FFmpeg
            var ffmpegResult = await ConvertWebMToWavAsync(tempWebMFile, tempWavFile);
            if (!ffmpegResult.Success)
            {
                throw new InvalidOperationException($"FFmpeg conversion failed: {ffmpegResult.Error}");
            }
            
            _logger.LogDebug("[SpeechService][HandleWebMOpusFormat] - WebM successfully converted to WAV:{0}",tempWebMFile);

            // Read converted WAV data
            var wavData = await File.ReadAllBytesAsync(tempWavFile);
            _logger.LogDebug("[SpeechService][HandleWebMOpusFormat] - WAV data size:{0} bytes",wavData.Length);

            // Use converted WAV data for speech recognition
            var result = await TryRecognizeWithFormat(wavData, speechConfig, null);
            
            if (!string.IsNullOrEmpty(result))
            {
                _logger.LogDebug("[SpeechService][HandleWebMOpusFormat] - WebM/Opus recognition successful:{0}",result);
                return result;
            }
            else
            {
                throw new InvalidOperationException("No speech could be recognized from the converted WAV audio data");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SpeechService][HandleWebMOpusFormat] - WebM/Opus processing error:{0}",ex.Message);
            throw new InvalidOperationException($"Failed to process WebM/Opus audio: {ex.Message}", ex);
        }
        finally
        {
            // Clean up temporary files
            try
            {
                if (!string.IsNullOrEmpty(tempWebMFile) && File.Exists(tempWebMFile))
                {
                    File.Delete(tempWebMFile);
                    _logger.LogDebug("[SpeechService][HandleWebMOpusFormat] - Cleaned up temporary WebM file:{0}",tempWebMFile);
                }
                if (!string.IsNullOrEmpty(tempWavFile) && File.Exists(tempWavFile))
                {
                    File.Delete(tempWavFile);
                    _logger.LogDebug("[SpeechService][HandleWebMOpusFormat] - Cleaned up temporary WAV file:{0}",tempWebMFile);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "[SpeechService][HandleWebMOpusFormat] - Warning: Failed to clean up temporary files:{0}",cleanupEx.Message);
            }
        }
    }

    /// <summary>
    /// Converts WebM/Opus file to WAV format using FFmpeg
    /// </summary>
    /// <param name="inputWebMPath">Input WebM file path</param>
    /// <param name="outputWavPath">Output WAV file path</param>
    /// <returns>Conversion result</returns>
    private async Task<(bool Success, string Error)> ConvertWebMToWavAsync(string inputWebMPath, string outputWavPath)
    {
        try
        {
            _logger.LogDebug("[SpeechService][ConvertWebMToWavAsync] - Starting FFmpeg conversion:{0},inputWebMPath:{1}",inputWebMPath, inputWebMPath);
            // FFmpeg command to convert WebM/Opus to WAV
            // -i: input file
            // -ar 16000: set audio sample rate to 16kHz (required by many speech services)
            // -ac 1: set audio channels to mono
            // -f wav: output format WAV
            // -y: overwrite output file if exists
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{inputWebMPath}\" -ar 16000 -ac 1 -f wav -y \"{outputWavPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new System.Diagnostics.Process { StartInfo = processInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && File.Exists(outputWavPath))
            {
                _logger.LogDebug("[SpeechService][ConvertWebMToWavAsync] - FFmpeg conversion successful. Output file size:{0} bytes",new FileInfo(outputWavPath).Length);
                return (true, null);
            }
            else
            {
                var errorMessage = $"FFmpeg failed with exit code {process.ExitCode}. Error: {error}";
                _logger.LogDebug("[SpeechService][ConvertWebMToWavAsync] - FFmpeg conversion failed:{0}",errorMessage);
                return (false, errorMessage);
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"Exception during FFmpeg conversion: {ex.Message}";
            _logger.LogError(ex, "[SpeechService][ConvertWebMToWavAsync] - FFmpeg conversion exception:{0}",errorMessage);
            return (false, errorMessage);
        }
    }

    /// <summary>
    /// Handles M4A format audio processing
    /// </summary>
    /// <param name="audioData">M4A audio data</param>
    /// <param name="speechConfig">Speech configuration</param>
    /// <returns>Recognition result or error message</returns>
    private async Task<string> HandleM4AFormat(byte[] audioData, SpeechConfig speechConfig)
    {
        string tempM4AFile = null;
        string tempWavFile = null;
        
        try
        {
            _logger.LogDebug("[SpeechService][HandleM4AFormat] - Processing M4A format - converting to WAV using FFmpeg..");
            // Create temporary files
            tempM4AFile = Path.GetTempFileName() + ".m4a";
            tempWavFile = Path.GetTempFileName() + ".wav";
            
            // Write M4A data to temporary file
            await File.WriteAllBytesAsync(tempM4AFile, audioData);
            _logger.LogDebug("[SpeechService][HandleM4AFormat] - M4A data written to temporary file:{0}", tempM4AFile);

            // Convert M4A to WAV using FFmpeg
            var ffmpegResult = await ConvertM4AToWavAsync(tempM4AFile, tempWavFile);
            if (!ffmpegResult.Success)
            {
                throw new InvalidOperationException($"FFmpeg conversion failed: {ffmpegResult.Error}");
            }
            
            _logger.LogDebug("[SpeechService][HandleM4AFormat] - M4A successfully converted to WAV:{0}", tempWavFile);

            // Read converted WAV data
            var wavData = await File.ReadAllBytesAsync(tempWavFile);
            _logger.LogDebug("[SpeechService][HandleM4AFormat] - WAV data size:{0} bytes", wavData.Length);

            // Use converted WAV data for speech recognition
            var result = await TryRecognizeWithFormat(wavData, speechConfig, null);
            
            if (!string.IsNullOrEmpty(result))
            {
                _logger.LogDebug("[SpeechService][HandleM4AFormat] - M4A recognition successful:{0}", result);
                return result;
            }
            else
            {
                throw new InvalidOperationException("No speech could be recognized from the converted WAV audio data");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SpeechService][HandleM4AFormat] - M4A processing error:{0}", ex.Message);
            throw new InvalidOperationException($"Failed to process M4A audio: {ex.Message}", ex);
        }
        finally
        {
            // Clean up temporary files
            try
            {
                if (!string.IsNullOrEmpty(tempM4AFile) && File.Exists(tempM4AFile))
                {
                    File.Delete(tempM4AFile);
                    _logger.LogDebug("[SpeechService][HandleM4AFormat] - Cleaned up temporary M4A file:{0}", tempM4AFile);
                }
                if (!string.IsNullOrEmpty(tempWavFile) && File.Exists(tempWavFile))
                {
                    File.Delete(tempWavFile);
                    _logger.LogDebug("[SpeechService][HandleM4AFormat] - Cleaned up temporary WAV file:{0}", tempWavFile);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "[SpeechService][HandleM4AFormat] - Warning: Failed to clean up temporary files:{0}", cleanupEx.Message);
            }
        }
    }

    /// <summary>
    /// Converts M4A file to WAV format using FFmpeg
    /// </summary>
    /// <param name="inputM4APath">Input M4A file path</param>
    /// <param name="outputWavPath">Output WAV file path</param>
    /// <returns>Conversion result</returns>
    private async Task<(bool Success, string Error)> ConvertM4AToWavAsync(string inputM4APath, string outputWavPath)
    {
        try
        {
            _logger.LogDebug("[SpeechService][ConvertM4AToWavAsync] - Starting FFmpeg conversion:{0},inputM4APath:{1}", inputM4APath, inputM4APath);
            // FFmpeg command to convert M4A to WAV
            // -i: input file
            // -ar 16000: set audio sample rate to 16kHz (required by many speech services)
            // -ac 1: set audio channels to mono
            // -f wav: output format WAV
            // -y: overwrite output file if exists
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{inputM4APath}\" -ar 16000 -ac 1 -f wav -y \"{outputWavPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new System.Diagnostics.Process { StartInfo = processInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && File.Exists(outputWavPath))
            {
                _logger.LogDebug("[SpeechService][ConvertM4AToWavAsync] - FFmpeg conversion successful. Output file size:{0} bytes", new FileInfo(outputWavPath).Length);
                return (true, null);
            }
            else
            {
                var errorMessage = $"FFmpeg failed with exit code {process.ExitCode}. Error: {error}";
                _logger.LogDebug("[SpeechService][ConvertM4AToWavAsync] - FFmpeg conversion failed:{0}", errorMessage);
                return (false, errorMessage);
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"Exception during FFmpeg conversion: {ex.Message}";
            _logger.LogError(ex, "[SpeechService][ConvertM4AToWavAsync] - FFmpeg conversion exception:{0}", errorMessage);
            return (false, errorMessage);
        }
    }
}