using AICommentModerator.Application.Models;
using AICommentModerator.Infrastructure.Ai;
using Xunit;

namespace AICommentModerator.Tests;

public class OpenAiVerdictParsingTests
{
    [Fact]
    public void Reads_a_well_formed_verdict()
    {
        var result = OpenAiModerationService.Parse(
            """{"decision":"block","reason":"Personal insult","categories":["insult"],"confidence":0.93}""");

        Assert.NotNull(result);
        Assert.Equal(ModerationDecision.Block, result!.Decision);
        Assert.Equal("Personal insult", result.Reason);
        Assert.Contains("insult", result.Categories);
        Assert.Equal(0.93, result.Confidence, 3);
        Assert.Equal("openai", result.Source);
    }

    [Theory]
    [InlineData("allow", ModerationDecision.Allow)]
    [InlineData("FLAG", ModerationDecision.Flag)]
    [InlineData(" Block ", ModerationDecision.Block)]
    public void Decision_is_case_and_space_insensitive(string value, ModerationDecision expected)
    {
        var result = OpenAiModerationService.Parse($$"""{"decision":"{{value}}","reason":"r"}""");

        Assert.NotNull(result);
        Assert.Equal(expected, result!.Decision);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"reason":"missing decision"}""")]
    [InlineData("""{"decision":"maybe","reason":"unknown verdict"}""")]
    public void Unusable_answers_return_null(string json)
    {
        Assert.Null(OpenAiModerationService.Parse(json));
    }

    [Fact]
    public void Confidence_is_clamped_to_the_zero_one_range()
    {
        var result = OpenAiModerationService.Parse("""{"decision":"allow","reason":"ok","confidence":4.2}""");

        Assert.NotNull(result);
        Assert.Equal(1.0, result!.Confidence);
    }

    [Fact]
    public void Missing_reason_gets_a_placeholder()
    {
        var result = OpenAiModerationService.Parse("""{"decision":"flag"}""");

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result!.Reason));
    }
}
