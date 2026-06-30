using NeoAdapter.Application.SqlEditor;
using Xunit;
using FluentAssertions;

namespace NeoAdapter.IntegrationTests;

public class SqlValidationTests
{
    [Theory]
    [InlineData("SELECT * FROM Products", true, "")]
    [InlineData("select id, title from products where id = 5", true, "")]
    [InlineData("SELECT * FROM Products -- delete this record", true, "")]
    [InlineData("SELECT * FROM Products /* delete this record */", true, "")]
    [InlineData("SELECT * FROM Products /* nested /* delete */ comments */", true, "")]
    [InlineData("SELECT 'This is a delete query' AS description", true, "")]
    [InlineData("SELECT \"delete\" FROM products", true, "")]
    [InlineData("EXEC GetProductById 5", true, "")]
    [InlineData("CALL ProcessData()", true, "")]
    [InlineData("DELETE FROM Products", false, "DELETE")]
    [InlineData("delete from products", false, "DELETE")]
    [InlineData("UPDATE Products SET price = 10", false, "UPDATE")]
    [InlineData("INSERT INTO Products VALUES (1, 'Widget')", false, "INSERT")]
    [InlineData("DROP TABLE Products", false, "DROP")]
    [InlineData("ALTER TABLE Products ADD COLUMN descr VARCHAR(10)", false, "ALTER")]
    [InlineData("TRUNCATE TABLE Products", false, "TRUNCATE")]
    [InlineData("CREATE TABLE Test (id INT)", false, "CREATE")]
    [InlineData("MERGE INTO Products AS Target ...", false, "MERGE")]
    [InlineData("REPLACE INTO Products VALUES ...", false, "REPLACE")]
    [InlineData("RENAME TABLE Products TO Items", false, "RENAME")]
    [InlineData("GRANT SELECT ON Products TO Guest", false, "GRANT")]
    [InlineData("REVOKE SELECT ON Products FROM Guest", false, "REVOKE")]
    [InlineData("SELECT * FROM Products; DELETE FROM Products;", false, "DELETE")]
    [InlineData("/* comment */ DELETE FROM Products;", false, "DELETE")]
    [InlineData("EXPLAIN SELECT * FROM Products", true, "")]
    [InlineData("EXPLAIN (ANALYZE, VERBOSE) SELECT * FROM Products", true, "")]
    [InlineData("EXPLAIN DELETE FROM Products", false, "DELETE")]
    [InlineData("EXPLAIN ANALYZE INSERT INTO Products VALUES (1)", false, "INSERT")]
    public void IsQueryAllowed_ShouldValidateCorrectly(string query, bool expectedAllowed, string expectedForbiddenKeyword)
    {
        var result = SqlValidation.IsQueryAllowed(query, out var forbiddenKeyword);
        result.Should().Be(expectedAllowed);
        forbiddenKeyword.Should().Be(expectedForbiddenKeyword);
    }
}
