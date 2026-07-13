using System.Text.Json;
using ClosedXML.Excel;
using SqlAgent.Web.Controllers;
using Xunit;

namespace SqlAgent.Tests;

/// <summary>
/// Excel/CSV formula injection (CWE-1236): a DB value starting with = + - @ must
/// not be written as a live formula. We verify the actual cell in a built workbook.
/// </summary>
public class ExcelExportTests
{
    // Write a value and assert on the resulting cell WHILE the workbook is alive
    // (ClosedXML disposes cell access with the workbook).
    private static void OnCell(object? value, Action<IXLCell> assert)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("t");
        var cell = ws.Cell(1, 1);
        ChatController.WriteCell(cell, value);
        assert(cell);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    public void Dangerous_values_are_not_formulas(string dangerous) => OnCell(dangerous, cell =>
    {
        // Never a live formula, and marked as quote-prefixed text so Excel won't run it.
        Assert.False(cell.HasFormula);
        Assert.True(cell.Style.IncludeQuotePrefix);
        Assert.Equal(dangerous, cell.GetString()); // original text preserved
    });

    [Theory]
    [InlineData("normal text")]
    [InlineData("students")]
    public void Ordinary_strings_pass_through_untouched(string safe) => OnCell(safe, cell =>
    {
        Assert.False(cell.Style.IncludeQuotePrefix);
        Assert.Equal(safe, cell.GetString());
    });

    [Fact]
    public void JsonElement_string_is_unwrapped_and_still_guarded()
    {
        // Values arrive from the client as JsonElement; a dangerous one must still
        // be caught after unwrapping (this was a real miss before the fix).
        var je = JsonDocument.Parse("\"=cmd|'/c calc'\"").RootElement;
        OnCell(je, cell =>
        {
            Assert.False(cell.HasFormula);
            Assert.True(cell.Style.IncludeQuotePrefix);
        });
    }

    [Fact]
    public void Unwrap_converts_json_scalars()
    {
        Assert.Equal("hi", ChatController.Unwrap(JsonDocument.Parse("\"hi\"").RootElement));
        Assert.Equal(42L, Assert.IsType<long>(ChatController.Unwrap(JsonDocument.Parse("42").RootElement)));
        Assert.Equal(true, ChatController.Unwrap(JsonDocument.Parse("true").RootElement));
        Assert.Null(ChatController.Unwrap(JsonDocument.Parse("null").RootElement));
    }
}
