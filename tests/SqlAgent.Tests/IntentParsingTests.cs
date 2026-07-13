using SqlAgent.Application.Services;
using SqlAgent.Domain.Models;
using Xunit;

namespace SqlAgent.Tests;

/// <summary>
/// The analysis output (INTENT + LANGUAGE lines) is produced by the LLM, so the
/// parser must be robust: pick the right label, read the language, and fall back
/// safely to DataQuery when the output is garbled or empty.
/// </summary>
public class IntentParsingTests
{
    [Theory]
    [InlineData("INTENT: DATA_QUERY\nLANGUAGE: none", QueryIntent.DataQuery)]
    [InlineData("INTENT: SQL_GENERAL\nLANGUAGE: none", QueryIntent.SqlGeneral)]
    [InlineData("INTENT: META_HELP\nLANGUAGE: none", QueryIntent.MetaHelp)]
    [InlineData("INTENT: INSTRUCTION\nLANGUAGE: bangla", QueryIntent.Instruction)]
    [InlineData("INTENT: OFF_TOPIC\nLANGUAGE: none", QueryIntent.OffTopic)]
    public void Parses_each_intent(string raw, QueryIntent expected)
    {
        Assert.Equal(expected, QueryAgentService.ParseAnalysis(raw).Intent);
    }

    [Theory]
    [InlineData("INTENT: DATA_QUERY\nLANGUAGE: bangla", "bangla")]
    [InlineData("INTENT: DATA_QUERY\nLANGUAGE: english", "english")]
    [InlineData("INTENT: INSTRUCTION\nLANGUAGE: spanish", "spanish")]
    [InlineData("INTENT: DATA_QUERY\nLANGUAGE: none", null)]
    [InlineData("INTENT: DATA_QUERY", null)]
    public void Parses_requested_language(string raw, string? expected)
    {
        Assert.Equal(expected, QueryAgentService.ParseAnalysis(raw).RequestedLanguage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("some unrelated text with no label")]
    public void Falls_back_to_DataQuery_when_unclear(string? raw)
    {
        // An unclear/empty analysis must never block a real data question.
        Assert.Equal(QueryIntent.DataQuery, QueryAgentService.ParseAnalysis(raw).Intent);
    }

    [Fact]
    public void Reads_the_last_label_when_several_appear()
    {
        // A reasoning model may restate options; the final verdict should win.
        var raw = "Could be DATA_QUERY or META_HELP...\nINTENT: OFF_TOPIC\nLANGUAGE: none";
        Assert.Equal(QueryIntent.OffTopic, QueryAgentService.ParseAnalysis(raw).Intent);
    }
}
