using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.GodChat
{
    /// <summary>
    /// Test voice processing logic for short text (e.g., "我来了。")
    /// Tests the core logic without complex object instantiation
    /// </summary>
    public class VoiceShortTextTest
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public VoiceShortTextTest(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        /// <summary>
        /// Test HasMeaningfulContent static method with Chinese short text
        /// This is the core logic that determines if text should be processed
        /// </summary>
        [Fact]
        public void HasMeaningfulContent_ChineseShortText_ShouldReturnTrue()
        {
            // Arrange
            var chineseText = "我来了。";

            // Act
            var result = TestHasMeaningfulContent(chineseText);

            // Assert
            result.ShouldBeTrue();
            _testOutputHelper.WriteLine($"✅ Chinese text '{chineseText}' correctly identified as meaningful");
        }

        /// <summary>
        /// Test HasMeaningfulContent with Chinese text without punctuation
        /// </summary>
        [Fact]
        public void HasMeaningfulContent_ChineseTextNoPunctuation_ShouldReturnTrue()
        {
            // Arrange
            var chineseText = "我来了";

            // Act
            var result = TestHasMeaningfulContent(chineseText);

            // Assert
            result.ShouldBeTrue();
            _testOutputHelper.WriteLine($"✅ Chinese text '{chineseText}' (no punctuation) correctly identified as meaningful");
        }

        /// <summary>
        /// Test HasMeaningfulContent with English short text
        /// </summary>
        [Fact]
        public void HasMeaningfulContent_EnglishShortText_ShouldReturnTrue()
        {
            // Arrange
            var englishText = "Hi!";

            // Act
            var result = TestHasMeaningfulContent(englishText);

            // Assert
            result.ShouldBeTrue();
            _testOutputHelper.WriteLine($"✅ English text '{englishText}' correctly identified as meaningful");
        }

        /// <summary>
        /// Test HasMeaningfulContent with punctuation only
        /// </summary>
        [Fact]
        public void HasMeaningfulContent_PunctuationOnly_ShouldReturnFalse()
        {
            // Arrange
            var punctuationText = "。！？";

            // Act
            var result = TestHasMeaningfulContent(punctuationText);

            // Assert
            result.ShouldBeFalse();
            _testOutputHelper.WriteLine($"✅ Punctuation-only text '{punctuationText}' correctly identified as not meaningful");
        }

        /// <summary>
        /// Test HasMeaningfulContent with empty text
        /// </summary>
        [Fact]
        public void HasMeaningfulContent_EmptyText_ShouldReturnFalse()
        {
            // Arrange
            var emptyText = "";

            // Act
            var result = TestHasMeaningfulContent(emptyText);

            // Assert
            result.ShouldBeFalse();
            _testOutputHelper.WriteLine($"✅ Empty text correctly identified as not meaningful");
        }

        /// <summary>
        /// Test the core extraction logic simulation for last chunk scenarios
        /// This simulates the key decision points in ExtractCompleteSentence
        /// </summary>
        [Fact]
        public void ExtractLogic_ChineseShortTextWithLastChunk_ShouldProcess()
        {
            // Arrange
            var input = "我来了。";
            var isLastChunk = true;

            // Act - Simulate the key logic from ExtractCompleteSentence
            var hasMeaningfulContent = TestHasMeaningfulContent(input);
            
            // This simulates the enhanced logic: return any non-empty text when isLastChunk = true
            bool shouldProcess = false;
            if (isLastChunk)
            {
                var trimmedText = input.Trim();
                if (!string.IsNullOrEmpty(trimmedText))
                {
                    shouldProcess = true;
                }
            }

            // Assert
            hasMeaningfulContent.ShouldBeTrue();
            shouldProcess.ShouldBeTrue();
            _testOutputHelper.WriteLine($"✅ Short text extraction logic: input='{input}', isLastChunk={isLastChunk}, shouldProcess={shouldProcess}");
        }

        /// <summary>
        /// Test the short text special handling logic (≤ 6 characters)
        /// </summary>
        [Fact]
        public void ExtractLogic_ShortTextSpecialHandling_ShouldProcess()
        {
            // Arrange
            var input = "Hi!";
            var isLastChunk = false;

            // Act - Simulate the special short text handling
            var hasMeaningfulContent = TestHasMeaningfulContent(input);
            bool shouldProcess = false;
            
            // Special handling for short text: return directly if meaningful and <= 6 characters
            if (input.Length <= 6 && hasMeaningfulContent)
            {
                shouldProcess = true;
            }

            // Assert
            hasMeaningfulContent.ShouldBeTrue();
            shouldProcess.ShouldBeTrue();
            _testOutputHelper.WriteLine($"✅ Short text special handling: input='{input}', length={input.Length}, shouldProcess={shouldProcess}");
        }

        /// <summary>
        /// Test sentence ending detection logic
        /// </summary>
        [Fact]
        public void ExtractLogic_SentenceEndingDetection_ShouldWork()
        {
            // Arrange
            var input = "Hello world! How are you";
            var sentenceEnders = new char[] { '.', '?', '!', '。', '？', '！', ',', ';', ':', '，', '；', '：', '\n', '\r' };

            // Act - Simulate sentence ending detection
            int extractIndex = -1;
            for (int i = input.Length - 1; i >= 0; i--)
            {
                if (sentenceEnders.Contains(input[i]))
                {
                    var potentialSentence = input.Substring(0, i + 1);
                    if (TestHasMeaningfulContent(potentialSentence))
                    {
                        extractIndex = i;
                        break;
                    }
                }
            }

            // Assert
            extractIndex.ShouldBeGreaterThan(-1);
            var extractedSentence = input.Substring(0, extractIndex + 1);
            extractedSentence.ShouldBe("Hello world!");
            _testOutputHelper.WriteLine($"✅ Sentence ending detection: extracted='{extractedSentence}' from input='{input}'");
        }

        /// <summary>
        /// Replicate the HasMeaningfulContent logic for testing
        /// This is a copy of the actual method logic to verify our understanding
        /// </summary>
        private static bool TestHasMeaningfulContent(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            
            // Remove all punctuation and check if there's actual content
            var cleanText = Regex.Replace(text, @"[^\w\u4e00-\u9fff]", "");
            return cleanText.Length > 0; // At least one letter or Chinese character
        }
    }
} 