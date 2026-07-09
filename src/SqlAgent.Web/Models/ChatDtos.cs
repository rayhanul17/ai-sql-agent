using SqlAgent.Domain.Models;

namespace SqlAgent.Web.Models;

/// <summary>Posted from the chat UI. Mirrors <see cref="AskRequest"/> but is a plain form/JSON DTO.</summary>
public sealed class AskDto
{
    public string Question { get; set; } = string.Empty;
    public string? ConnectionString { get; set; }
    public DbDialect? Dialect { get; set; }
    public string? Model { get; set; }
}

/// <summary>Posted to /Chat/Export to build an .xlsx from an already-returned result.</summary>
public sealed class ExportDto
{
    public string? Question { get; set; }
    public string? Sql { get; set; }
    public List<string> Columns { get; set; } = [];
    public List<List<object?>> Rows { get; set; } = [];
}
