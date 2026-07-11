namespace SqlAgent.Domain.Models;

/// <summary>
/// Where the LLM runs. Ollama is the primary, local, private default;
/// Groq is an optional fast cloud provider (OpenAI-compatible).
/// </summary>
public enum LlmProvider
{
    Ollama = 0,
    Groq = 1
}
