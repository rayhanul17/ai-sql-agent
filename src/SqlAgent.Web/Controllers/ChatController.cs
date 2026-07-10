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
    private readonly ILogger<ChatController> _log;

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    public ChatController(IQueryAgentService agent, IModelManager models, ILogger<ChatController> log)
    {
        _agent = agent;
        _models = models;
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
        var result = await _models.WarmUpAsync(model, ct);
        return Json(result, JsonOpts);
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
            Model = dto.Model,
            History = dto.History
                .Select(t => new ConversationTurn { Question = t.Question, Sql = t.Sql })
                .ToList()
        };

        await foreach (var chunk in _agent.AskStreamAsync(request, ct))
        {
            var payload = JsonSerializer.Serialize(chunk, JsonOpts);
            await Response.WriteAsync($"data: {payload}\n\n", ct);
            await Response.Body.FlushAsync(ct);
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
            ws.Cell(1, c + 1).Value = dto.Columns[c];
        ws.Row(1).Style.Font.Bold = true;

        // Data rows.
        for (var r = 0; r < dto.Rows.Count; r++)
        {
            var row = dto.Rows[r];
            for (var c = 0; c < row.Count; c++)
                ws.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(row[c]);
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileName = $"query-result-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx";
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
