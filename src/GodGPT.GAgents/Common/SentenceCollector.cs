using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GodGPT.GAgents.SpeechChat;

namespace GodGPT.GAgents.Common
{
    /// <summary>
    /// Sentence collector for real-time text-to-speech conversion
    /// Detects complete sentences and triggers voice synthesis
    /// </summary>
    public class SentenceCollector
    {
        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly List<char> _sentenceEnders = new List<char> { '.', '?', '!', '。', '？', '！' };
        private readonly int _minSentenceLength = 10;
        private readonly Func<string, VoiceLanguageEnum, Task<(byte[] AudioData, Aevatar.Application.Grains.Agents.ChatManager.AudioMetadata Metadata)>> _synthesizeVoice;
        private readonly VoiceLanguageEnum _language;

        public SentenceCollector(VoiceLanguageEnum language, 
            Func<string, VoiceLanguageEnum, Task<(byte[] AudioData, Aevatar.Application.Grains.Agents.ChatManager.AudioMetadata Metadata)>> synthesizeVoice)
        {
            _language = language;
            _synthesizeVoice = synthesizeVoice;
        }

        /// <summary>
        /// Add text chunk and check for complete sentences
        /// </summary>
        /// <param name="textChunk">New text chunk to add</param>
        /// <returns>Voice synthesis result if complete sentence detected, null otherwise</returns>
        public async Task<(byte[] AudioData, Aevatar.Application.Grains.Agents.ChatManager.AudioMetadata Metadata)?> AddTextChunkAsync(string textChunk)
        {
            if (string.IsNullOrWhiteSpace(textChunk))
                return null;

            _buffer.Append(textChunk);
            var bufferText = _buffer.ToString();

            // Check for complete sentence
            var lastSentenceIndex = -1;
            for (int i = bufferText.Length - 1; i >= 0; i--)
            {
                if (_sentenceEnders.Contains(bufferText[i]))
                {
                    lastSentenceIndex = i;
                    break;
                }
            }

            if (lastSentenceIndex == -1)
                return null; // No complete sentence found

            // Extract complete sentence(s)
            var completeSentence = bufferText.Substring(0, lastSentenceIndex + 1).Trim();
            
            if (completeSentence.Length < _minSentenceLength)
                return null; // Sentence too short

            // Keep remaining text in buffer
            var remainingText = bufferText.Substring(lastSentenceIndex + 1);
            _buffer.Clear();
            _buffer.Append(remainingText);

            // Synthesize voice for complete sentence
            try
            {
                var result = await _synthesizeVoice(completeSentence, _language);
                return result;
            }
            catch (Exception ex)
            {
                // Log error but don't fail the entire process
                return null;
            }
        }

        /// <summary>
        /// Process any remaining text in buffer as final sentence
        /// </summary>
        /// <returns>Voice synthesis result if any remaining text, null otherwise</returns>
        public async Task<(byte[] AudioData, Aevatar.Application.Grains.Agents.ChatManager.AudioMetadata Metadata)?> FlushRemainingTextAsync()
        {
            var remainingText = _buffer.ToString().Trim();
            if (string.IsNullOrWhiteSpace(remainingText) || remainingText.Length < _minSentenceLength)
                return null;

            _buffer.Clear();

            try
            {
                var result = await _synthesizeVoice(remainingText, _language);
                return result;
            }
            catch (Exception ex)
            {
                // Log error but don't fail the entire process
                return null;
            }
        }

        /// <summary>
        /// Get current buffer content for debugging
        /// </summary>
        public string GetCurrentBuffer()
        {
            return _buffer.ToString();
        }
    }
} 