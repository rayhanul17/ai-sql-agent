using SqlAgent.Application.Services;
using SqlAgent.Infrastructure.Dialects;
using Xunit;

namespace SqlAgent.Tests;

/// <summary>
/// The SQL safety layer is security-critical, so it gets the most coverage:
/// valid read-only queries must pass, and anything that writes / chains / calls
/// out must be rejected.
/// </summary>
public class SqlSafetyValidatorTests
{
    private readonly SqlSafetyValidator _validator = new();
    private readonly PostgreSqlDialect _pg = new();

    // ---- valid read-only queries are accepted ----

    [Theory]
    [InlineData("SELECT * FROM students")]
    [InlineData("SELECT COUNT(*) FROM teachers")]
    [InlineData("select name, salary from teachers order by salary desc")]
    [InlineData("SELECT name, REPLACE(name, ' ', '_') AS clean FROM students")] // REPLACE() is a read-only function
    [InlineData("WITH c AS (SELECT class_id, COUNT(*) n FROM students GROUP BY class_id) SELECT * FROM c")]
    [InlineData("SELECT 'students' AS t UNION ALL SELECT 'teachers'")]
    public void Accepts_valid_readonly_select(string sql)
    {
        var result = _validator.Validate(sql, _pg);
        Assert.True(result.IsValid, $"expected valid, got: {result.Reason}");
        Assert.NotNull(result.SafeSql);
    }

    // ---- write / DDL / privilege / exec statements are rejected ----

    [Theory]
    [InlineData("INSERT INTO students VALUES (1, 'x')")]
    [InlineData("UPDATE fees SET amount = 0")]
    [InlineData("DELETE FROM students")]
    [InlineData("DROP TABLE teachers")]
    [InlineData("ALTER TABLE students ADD COLUMN x int")]
    [InlineData("TRUNCATE TABLE students")]
    [InlineData("CREATE TABLE x (id int)")]
    [InlineData("GRANT ALL ON students TO public")]
    [InlineData("EXEC sp_who")]
    [InlineData("CALL some_proc()")]
    [InlineData("REPLACE INTO students VALUES (1)")] // dangerous REPLACE form -> not a SELECT
    public void Rejects_write_and_ddl(string sql)
    {
        var result = _validator.Validate(sql, _pg);
        Assert.False(result.IsValid);
    }

    // ---- a chained payload never executes a second statement ----

    [Fact]
    public void Chained_statement_does_not_execute_the_drop()
    {
        var result = _validator.Validate("SELECT * FROM students; DROP TABLE teachers", _pg);
        // Whatever the outcome, the DROP must never survive into the executed SQL.
        if (result.IsValid)
            Assert.DoesNotContain("DROP", result.SafeSql!, System.StringComparison.OrdinalIgnoreCase);
    }

    // ---- must start with SELECT / WITH ----

    [Theory]
    [InlineData("EXPLAIN SELECT * FROM students")]
    [InlineData("PRAGMA table_info(students)")]
    [InlineData("just some text")]
    [InlineData("")]
    public void Rejects_non_select_start(string sql)
    {
        Assert.False(_validator.Validate(sql, _pg).IsValid);
    }

    // ---- model formatting is cleaned before validation ----

    [Theory]
    [InlineData("```sql\nSELECT * FROM students\n```")]
    [InlineData("<think>let me think</think>SELECT * FROM students")]
    [InlineData("sql: SELECT * FROM students")]
    public void Cleans_model_formatting_and_accepts(string sql)
    {
        var result = _validator.Validate(sql, _pg);
        Assert.True(result.IsValid, $"expected valid, got: {result.Reason}");
        Assert.StartsWith("SELECT", result.SafeSql!, System.StringComparison.OrdinalIgnoreCase);
    }
}
