using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Services;
using SmartStudyFunc.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartStudyFunc.Tests
{
    /// <summary>
    /// Unit tests for AiScoringService
    /// Tests AI evaluation, fallback scoring, and error handling
    /// </summary>
    public class AiScoringServiceTests
    {
        private readonly Mock<ILogger<AiScoringService>> _mockLogger;

        public AiScoringServiceTests()
        {
            _mockLogger = new Mock<ILogger<AiScoringService>>();
        }

        [Fact]
        public async Task ScoreAsync_WithValidInput_ReturnsScore()
        {
            // Arrange
            // NOTE: This test requires valid Azure OpenAI credentials
            // For true unit testing, mock OpenAIClient using interfaces
            
            var studentText = "The derivative of x^2 is 2x using the power rule.";
            var idealAnswer = "The derivative of x^2 is 2x. This is calculated using the power rule: d/dx(x^n) = n*x^(n-1).";
            var maxMarks = 10;
            var keywords = new List<string> { "derivative", "power rule", "2x" };

            // This test will use fallback scoring if OpenAI is not configured
            var service = new AiScoringService(_mockLogger.Object);

            // Act
            var result = await service.ScoreAsync(studentText, idealAnswer, maxMarks, keywords);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Score >= 0 && result.Score <= maxMarks);
            Assert.Equal(maxMarks, result.MaxMarks);
            Assert.NotEmpty(result.Feedback);
            Assert.Contains("derivative", result.KeywordsMatched);
            Assert.Contains("power rule", result.KeywordsMatched);
        }

        [Fact]
        public async Task ScoreAsync_WithEmptyInput_ThrowsArgumentException()
        {
            // Arrange
            var service = new AiScoringService(_mockLogger.Object);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.ScoreAsync("", "ideal answer", 10, new List<string>()));
        }

        [Fact]
        public async Task FallbackScoring_WithKeywords_ReturnsProportionalScore()
        {
            // Arrange
            var studentText = "The derivative concept involves rate of change.";
            var idealAnswer = "Differentiation finds the derivative which represents the rate of change.";
            var maxMarks = 10;
            var keywords = new List<string> { "derivative", "rate of change", "differentiation" };

            // Force fallback by using invalid/missing OpenAI credentials
            Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", "https://invalid.openai.azure.com");
            Environment.SetEnvironmentVariable("AZURE_OPENAI_KEY", "invalid-key");
            
            var service = new AiScoringService(_mockLogger.Object);

            // Act
            var result = await service.ScoreAsync(studentText, idealAnswer, maxMarks, keywords);

            // Assert
            Assert.True(result.UsedFallback, "Should use fallback when OpenAI fails");
            Assert.True(result.Score > 0, "Score should be > 0 with matched keywords");
            Assert.Contains("derivative", result.KeywordsMatched);
            Assert.Contains("rate of change", result.KeywordsMatched);
            Assert.Contains("differentiation", result.MissingKeywords);
        }

        [Fact]
        public async Task FallbackScoring_WithNoKeywords_ReturnsModerateScore()
        {
            // Arrange
            var studentText = "This is some answer text that doesn't match keywords.";
            var idealAnswer = "The correct answer involves specific mathematical concepts.";
            var maxMarks = 10;
            var keywords = new List<string> { "derivative", "calculus", "theorem" };

            Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", "https://invalid.openai.azure.com");
            Environment.SetEnvironmentVariable("AZURE_OPENAI_KEY", "invalid-key");
            
            var service = new AiScoringService(_mockLogger.Object);

            // Act
            var result = await service.ScoreAsync(studentText, idealAnswer, maxMarks, keywords);

            // Assert
            Assert.True(result.UsedFallback);
            Assert.Empty(result.KeywordsMatched);
            Assert.Equal(3, result.MissingKeywords.Count);
            Assert.True(result.Score >= 0 && result.Score <= maxMarks);
        }

        [Fact]
        public async Task ScoreAsync_CancellationRequested_ThrowsOperationCanceledException()
        {
            // Arrange
            var service = new AiScoringService(_mockLogger.Object);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                service.ScoreAsync(
                    "student answer",
                    "ideal answer",
                    10,
                    new List<string> { "test" },
                    cts.Token));
        }

        [Theory]
        [InlineData("The derivative of x^2 is 2x", 3)]
        [InlineData("x squared has derivative 2x by power rule", 3)]
        [InlineData("Not related to math at all", 0)]
        public async Task KeywordMatching_VariousInputs_ReturnsCorrectMatches(string studentText, int expectedMatches)
        {
            // Arrange
            var keywords = new List<string> { "derivative", "2x", "power rule" };
            var idealAnswer = "The derivative of x^2 is 2x using power rule.";
            var maxMarks = 10;

            Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", "https://invalid.openai.azure.com");
            Environment.SetEnvironmentVariable("AZURE_OPENAI_KEY", "invalid-key");
            
            var service = new AiScoringService(_mockLogger.Object);

            // Act
            var result = await service.ScoreAsync(studentText, idealAnswer, maxMarks, keywords);

            // Assert
            Assert.Equal(expectedMatches, result.KeywordsMatched.Count);
        }

        [Fact]
        public async Task ScoreAsync_WithLongAnswer_ProcessesSuccessfully()
        {
            // Arrange
            var studentText = string.Join(" ", Enumerable.Repeat("The derivative concept.", 100));
            var idealAnswer = "The derivative represents rate of change.";
            var maxMarks = 10;
            var keywords = new List<string> { "derivative", "rate of change" };

            Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", "https://invalid.openai.azure.com");
            Environment.SetEnvironmentVariable("AZURE_OPENAI_KEY", "invalid-key");
            
            var service = new AiScoringService(_mockLogger.Object);

            // Act
            var result = await service.ScoreAsync(studentText, idealAnswer, maxMarks, keywords);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.UsedFallback);
            Assert.InRange(result.Score, 0, maxMarks);
        }
    }

    /// <summary>
    /// Integration tests for complete evaluation workflow
    /// Requires database and Azure services
    /// </summary>
    public class EvaluationWorkflowIntegrationTests
    {
        [Fact(Skip = "Integration test - requires database and Azure services")]
        public async Task UploadAndEvaluate_EndToEnd_Success()
        {
            // This test would:
            // 1. Upload a test PDF
            // 2. Extract text via OCR
            // 3. Evaluate the answer
            // 4. Verify database insertion
            // 5. Check response structure
            
            Assert.True(true, "Integration test placeholder");
        }

        [Fact(Skip = "Integration test - requires database")]
        public async Task BatchEvaluate_MultipleAnswers_AllProcessed()
        {
            // This test would:
            // 1. Submit batch of 5 answers
            // 2. Verify concurrent processing (max 3)
            // 3. Check all results returned
            // 4. Verify database consistency
            
            Assert.True(true, "Integration test placeholder");
        }
    }
}
