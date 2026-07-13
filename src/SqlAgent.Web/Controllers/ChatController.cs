using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;
using SqlAgent.Web.Models;

namespace SqlAgent.Web.Controllers;

public sealed class ChatController : Controller
{
    private readonly IQueryAgentService _agent;
    private readonly IModelManager _models;
    private readonly ISqlExecutor _executor;
    private readonly ILogger<ChatController> _log;

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    public ChatController(
        IQueryAgentService agent, IModelManager models,
        ISqlExecutor executor, ILogger<ChatController> log)
    {
        _agent = agent;
        _models = models;
        _executor = executor;
        _log = log;
    }

    [HttpGet]
    public IActionResult Index() => View();

    /// <summary>Model catalog for the dropdown (id, name, tier, available, loaded).</summary>
    [HttpGet]
    public async Task<IActionResult> Models(CancellationToken ct)
    {
        var models = await _models.GetModelsAsync(ct);
        return Json(models, JsonOpts);
    }

    /// <summary>Warm a model into RAM; returns load time so the UI can hide its loader.</summary>
    [HttpPost]
    public async Task<IActionResult> Warmup([FromBody] string model, CancellationToken ct)
    {
        // Logged here (on Save) rather than per query, so the log shows when the
        // active model actually changed.
        _log.LogInformation("Settings applied: model set to {Model}", model);
        var result = await _models.WarmUpAsync(model, ct);
        return Json(result, JsonOpts);
    }

    /// <summary>
    /// Introspect + cache the full schema for a data source. Called on Save (to
    /// read the structure once) and by the refresh-schema button (force=true).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> LoadSchema([FromBody] LoadSchemaDto dto, CancellationToken ct)
    {
        // Logged on Save so the log records when the data source changed.
        var source = string.IsNullOrWhiteSpace(dto.ConnectionString)
            ? "demo DB (default)"
            : $"{dto.Dialect} [{Mask(dto.ConnectionString)}]";
        _log.LogInformation("Settings applied: data source set to {Source}", source);

        var result = await _agent.LoadSchemaAsync(dto.ConnectionString, dto.Dialect, dto.Force, ct);
        _log.LogInformation("Schema loaded: success={Success} tables={Tables} columns={Columns}",
            result.Success, result.TableCount, result.ColumnCount);
        return Json(result, JsonOpts);
    }

    private static string Mask(string conn) =>
        System.Text.RegularExpressions.Regex.Replace(
            conn, @"(?i)(password|pwd)\s*=\s*[^;]*", "$1=***");

    /// <summary>Validate a runtime connection string before it is applied (Save flow).</summary>
    [HttpPost]
    public async Task<IActionResult> TestConnection([FromBody] TestConnectionDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ConnectionString) || dto.Dialect is null)
            return Json(new { ok = false, error = "Connection string and database type are required." }, JsonOpts);

        try
        {
            var ok = await _executor.TestConnectionAsync(dto.ConnectionString, dto.Dialect.Value, ct);
            return ok
                ? Json(new { ok = true }, JsonOpts)
                : Json(new { ok = false, error = "Could not connect with the given connection string." }, JsonOpts);
        }
        catch (Exception ex)
        {
            // Don't echo the exception: it can contain the connection string the
            // user typed (password included). Log the detail, return a generic hint.
            _log.LogWarning(ex, "TestConnection failed");
            return Json(new { ok = false, error = "Could not connect. Check the host, port, database name and credentials." }, JsonOpts);
        }
    }

    /// <summary>
    /// Streams the whole answer over Server-Sent Events: status -> sql -> rows
    /// -> explanation tokens -> done. One-directional streaming; simpler than SignalR.
    /// </summary>
    [HttpPost]
    public async Task Ask([FromBody] AskDto dto, CancellationToken ct)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        var request = new AskRequest
        {
            Question = dto.Question,
            ConnectionString = dto.ConnectionString,
            Dialect = dto.Dialect,
            Provider = dto.Provider,
            Model = dto.Model,
            History = dto.History
                .Select(t => new ConversationTurn { Question = t.Question, Sql = t.Sql })
                .ToList(),
            StandingLanguage = dto.StandingLanguage
        };

        try
        {
            await foreach (var chunk in _agent.AskStreamAsync(request, ct))
            {
                var payload = JsonSerializer.Serialize(chunk, JsonOpts);
                await Response.WriteAsync($"data: {payload}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client navigated away / cancelled — normal, not an error.
            _log.LogDebug("Ask stream cancelled by the client.");
        }
        catch (Exception ex)
        {
            // Surface a generic failure to the UI instead of crashing — the full
            // exception (which may hold connection/driver internals) is only logged.
            _log.LogError(ex, "Ask stream failed");
            var msg = "Something went wrong while answering. Please check the log file (logs/agent-*.log) for details.";
            var payload = JsonSerializer.Serialize(
                new StreamChunk { Type = "error", Content = msg }, JsonOpts);
            try { await Response.WriteAsync($"data: {payload}\n\n"); } catch { /* connection gone */ }
        }
    }

    /// <summary>Builds an .xlsx (ClosedXML, MIT-licensed) from the returned result grid.</summary>
    [HttpPost]
    public IActionResult Export([FromBody] ExportDto dto)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Result");

        // Header row.
        for (var c = 0; c < dto.Columns.Count; c++)
            WriteCell(ws.Cell(1, c + 1), dto.Columns[c]);
        ws.Row(1).Style.Font.Bold = true;

        // Data rows.
        for (var r = 0; r < dto.Rows.Count; r++)
        {
            var row = dto.Rows[r];
            for (var c = 0; c < row.Count; c++)
                WriteCell(ws.Cell(r + 2, c + 1), row[c]);
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileName = $"query-result-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx";
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // Guard against spreadsheet formula injection (CWE-1236). A string value that
    // starts with = + - @ (or a leading tab/CR/LF) is executed by Excel/Sheets as
    // a formula when the file is opened; a hostile value like =HYPERLINK(...) or
    // =cmd|'...' could run. For those we force the cell to TEXT and mark it with a
    // quote-prefix so Excel keeps it literal (and the marker persists in the file).
    // Non-string values (numbers, dates, bools, null) can't be formulas -> as-is.
    internal static void WriteCell(IXLCell cell, object? value)
    {
        // Rows arrive as JsonElement (the DTO is List<List<object?>> and
        // System.Text.Json boxes each value), so unwrap those to a real CLR value
        // first — otherwise a hostile string would never be seen as a string here.
        var v = Unwrap(value);

        if (v is string s && s.Length > 0 &&
            s[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n')
        {
            // Assigning a value that starts with an apostrophe makes ClosedXML store
            // the cell with Excel's quote-prefix flag (text marker) and drop the
            // apostrophe from the visible value — so Excel shows the original text
            // and never runs it as a formula.
            cell.Value = "'" + s;
            return;
        }
        cell.Value = XLCellValue.FromObject(v);
    }

    // Convert a System.Text.Json JsonElement (how object? values arrive) into a
    // plain CLR value ClosedXML understands, so strings are seen as strings (and
    // can be checked for formula-injection). Non-JsonElement values pass through.
    internal static object? Unwrap(object? value) => value switch
    {
        JsonElement je => je.ValueKind switch
        {
            JsonValueKind.String => je.GetString(),
            // Box each branch separately so an integer stays a long (a shared
            // ternary would promote both to double).
            JsonValueKind.Number => je.TryGetInt64(out var l) ? l : (object)je.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => je.ToString(),
        },
        _ => value,
    };
}
