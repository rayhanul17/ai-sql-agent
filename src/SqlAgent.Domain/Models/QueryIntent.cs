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
}
