namespace Analytics.SystemTests.Models;

/// <summary>
/// Represents a single test query from the evaluation dataset.
/// </summary>
public sealed class TestQuery
{
    public string Id { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public string ExpectedBehavior { get; set; } = "success";
    public string? ExpectedResultContains { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Root object for deserializing EvaluationDataset.json.
/// </summary>
public sealed class EvaluationDataset
{
    public string Description { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public List<TestQuery> Queries { get; set; } = new();
}
