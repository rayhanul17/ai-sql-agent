namespace SqlAgent.Domain.Models;

/// <summary>
/// The result of analysing a user message up front. Unlike a single intent
/// label, this captures that ONE message can carry more than one thing — most
/// importantly a data request AND a language instruction together
/// ("how many tables, answer in Bangla"). The app decides what to do from these
/// fields rather than trusting a single classification.
/// </summary>
public sealed class MessageAnalysis
{
    /// <summary>The primary thing the message asks for. Drives the branch taken.</summary>
    public required QueryIntent Intent { get; init; }

    /// <summary>
    /// A language the user asked the reply to be in, if the message contains such
    /// an instruction (e.g. "in Bangla", "answer in English") — otherwise null.
    /// Applied to the answer even when the primary intent is a data query, so a
    /// combined "count the rows, in Bangla" is answered in Bangla.
    /// </summary>
    public string? RequestedLanguage { get; init; }
}
