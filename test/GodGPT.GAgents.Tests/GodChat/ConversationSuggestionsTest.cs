using Xunit;
using Xunit.Abstractions;
using Shouldly;
using GodGPT.GAgents.Common.Constants;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aevatar.Application.Grains.Tests.GodChat
{
    /// <summary>
    /// Unit tests for conversation suggestions parsing and duplication prevention
    /// </summary>
    public class ConversationSuggestionsTest
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public ConversationSuggestionsTest(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public void ParseResponseWithSuggestions_CompleteFormat_ShouldParseCorrectly()
        {
            // Arrange
            var response = @"Hello! This is a sample response from the AI assistant.

This response contains some main content that should be preserved.

---CONVERSATION_SUGGESTIONS---
Design your personal greeting system
Explore language structure concepts  
Create voice ritual activators
---END_SUGGESTIONS---";

            // Act
            var (mainContent, suggestions) = ParseResponseWithSuggestions(response);

            // Assert
            suggestions.Count.ShouldBe(3);
            suggestions[0].ShouldBe("Design your personal greeting system");
            suggestions[1].ShouldBe("Explore language structure concepts");
            suggestions[2].ShouldBe("Create voice ritual activators");
            
            mainContent.ShouldNotContain("---CONVERSATION_SUGGESTIONS---");
            mainContent.ShouldNotContain("---END_SUGGESTIONS---");
            mainContent.ShouldContain("This response contains some main content");
            
            _testOutputHelper.WriteLine($"Main content length: {mainContent.Length}");
            _testOutputHelper.WriteLine($"Suggestions count: {suggestions.Count}");
        }

        [Fact]
        public void ParseResponseWithSuggestions_IncompleteEndMarker_ShouldStillParse()
        {
            // Arrange - This simulates the real issue where end marker was truncated
            var response = @"Hello! This is another response.

---CONVERSATION_SUGGESTIONS---

Design your personal greeting system
Explore language structure concepts
Create voice ritual activators
---END_SUGGE";

            // Act
            var (mainContent, suggestions) = ParseResponseWithSuggestions(response);

            // Assert
            suggestions.Count.ShouldBe(3);
            suggestions[0].ShouldBe("Design your personal greeting system");
            suggestions[1].ShouldBe("Explore language structure concepts");
            suggestions[2].ShouldBe("Create voice ritual activators");
            
            mainContent.ShouldNotContain("---CONVERSATION_SUGGESTIONS---");
            mainContent.ShouldNotContain("---END_SUGGE");
            mainContent.ShouldContain("Hello! This is another response.");
            
            _testOutputHelper.WriteLine($"Main content: {mainContent}");
            _testOutputHelper.WriteLine($"Suggestions: [{string.Join(", ", suggestions)}]");
        }

        [Fact]
        public void ParseResponseWithSuggestions_NoSuggestions_ShouldReturnOriginal()
        {
            // Arrange
            var response = "This is a simple response with no suggestions.";

            // Act
            var (mainContent, suggestions) = ParseResponseWithSuggestions(response);

            // Assert
            suggestions.Count.ShouldBe(0);
            mainContent.ShouldBe(response);
            
            _testOutputHelper.WriteLine($"Main content: {mainContent}");
            _testOutputHelper.WriteLine($"Suggestions count: {suggestions.Count}");
        }

        [Fact]
        public void ChatRegexPatterns_ConversationSuggestionsBlock_ShouldMatchVariousFormats()
        {
            // Test various end marker formats
            var testCases = new[]
            {
                "---CONVERSATION_SUGGESTIONS---\n1. test\n---END_SUGGESTIONS---",
                "---CONVERSATION_SUGGESTIONS---\n1. test\n---END_SUGGESTION---", 
                "---CONVERSATION_SUGGESTIONS---\n1. test\n---END_SUGGEST---",
                "---CONVERSATION_SUGGESTIONS---\n1. test\n---END_SUGGE",
                "---CONVERSATION_SUGGESTIONS---\n1. test", // No end marker at all
            };

            foreach (var testCase in testCases)
            {
                // Act
                var match = ChatRegexPatterns.ConversationSuggestionsBlock.Match(testCase);
                
                // Assert
                match.Success.ShouldBeTrue($"Should match format: {testCase}");
                match.Groups[1].Value.ShouldContain("1. test");
                
                _testOutputHelper.WriteLine($"✓ Matched: {testCase.Replace("\n", "\\n")}");
            }
        }

        [Fact]
        public void SimulateDuplicationScenario_LastChunkSafety()
        {
            // Arrange - Simulate the scenario that caused duplication
            var fullResponse = @"This is a long response with lots of content that simulates a full LLM response.
---CONVERSATION_SUGGESTIONS---
Suggestion 1
Suggestion 2  
Suggestion 3
---END_SUGGE"; // Incomplete end marker

            var lastChunkContent = "---END_SUGGE"; // Very small last chunk
            var (cleanMainContent, suggestions) = ParseResponseWithSuggestions(fullResponse);

            // Simulate the safety check logic
            var currentChunkLength = lastChunkContent.Length;
            var cleanContentLength = cleanMainContent.Length;
            var shouldReplace = suggestions.Count > 0 && 
                              (currentChunkLength == 0 || cleanContentLength <= currentChunkLength * 2);

            // Assert
            suggestions.Count.ShouldBe(3);
            shouldReplace.ShouldBeFalse("Should not replace small chunk with much larger content");
            
            _testOutputHelper.WriteLine($"Current chunk length: {currentChunkLength}");
            _testOutputHelper.WriteLine($"Clean content length: {cleanContentLength}");
            _testOutputHelper.WriteLine($"Should replace: {shouldReplace}");
            _testOutputHelper.WriteLine($"Ratio: {(double)cleanContentLength / currentChunkLength:F2}x");
        }

        #region Helper Methods

        /// <summary>
        /// Helper method that mimics the actual parsing logic in GodChatGAgent
        /// </summary>
        private (string mainContent, List<string> suggestions) ParseResponseWithSuggestions(string fullResponse)
        {
            if (string.IsNullOrEmpty(fullResponse))
            {
                return (fullResponse, new List<string>());
            }

            // Pattern to match conversation suggestions block using precompiled regex
            var match = ChatRegexPatterns.ConversationSuggestionsBlock.Match(fullResponse);
            
            if (match.Success)
            {
                // Extract main content by removing the suggestions section
                var mainContent = fullResponse.Replace(match.Value, "").Trim();
                var suggestionSection = match.Groups[1].Value;
                var suggestions = ExtractNumberedItems(suggestionSection);
                
                return (mainContent, suggestions);
            }
            
            return (fullResponse, new List<string>());
        }

        /// <summary>
        /// Extract numbered items from text (e.g., "1. item", "2. item", etc.)
        /// </summary>
        private List<string> ExtractNumberedItems(string text)
        {
            var items = new List<string>();
            if (string.IsNullOrEmpty(text)) return items;

            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine)) continue;

                var match = ChatRegexPatterns.NumberedItem.Match(trimmedLine);
                if (match.Success)
                {
                    items.Add(match.Groups[1].Value.Trim());
                }
                else if (!trimmedLine.StartsWith("---") && trimmedLine.Length > 0)
                {
                    // Handle items without numbers
                    items.Add(trimmedLine);
                }
            }

            return items;
        }

        #endregion
    }
} 