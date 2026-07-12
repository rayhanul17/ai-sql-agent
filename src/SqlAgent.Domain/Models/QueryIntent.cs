namespace SqlAgent.Domain.Models;

/// <summary>
/// What the user's message is actually asking for. Classified up-front so the
/// agent can take the right branch instead of forcing every message through the
/// SQL writer. Drives both the reply strategy and the UI status text.
/// </summary>
public enum QueryIntent
{
    /// <summary>Retrieve specific rows/values from the tables (or a follow-up that refines a prior query). Generates SQL.</summary>
    DataQuery = 0,

    /// <summary>A general SQL/DB concept or how-to question ("what is a JOIN", "how do I GROUP BY"). Answered as a schema-aware SQL tutor — no query is run.</summary>
    SqlGeneral = 1,

    /// <summary>Asking what the agent can do, for prompt ideas, or how to use it. Answered with schema-grounded help — no query is run.</summary>
    MetaHelp = 2,

    /// <summary>Greeting, small talk, or something unrelated to the database. Politely redirected — no query is run.</summary>
    OffTopic = 3,

    /// <summary>
    /// Tells the agent HOW to behave rather than asking for data — most commonly a
    /// standing language preference ("from now on answer in English", "banglay
    /// bolo"), but also reply-style requests ("always keep answers short").
    /// Acknowledged conversationally; no query is run. The instruction itself is
    /// honoured on subsequent turns because the client replays recent history.
    /// </summary>
    Instruction = 4,
}
