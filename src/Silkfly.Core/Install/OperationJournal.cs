using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Silkfly.Core.Install;

public sealed record JournalMove(string From, string To);

public sealed class OperationJournal
{
    public Guid OperationId { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<JournalMove> Moves { get; set; } = [];

    public bool Committed { get; set; }

    public static string PathFor(IFileSystem fs, string stagingRoot, Guid operationId) =>
        fs.Path.Combine(stagingRoot, $"{operationId}.journal.json");

    public Task SaveAsync(IFileSystem fs, string stagingRoot, CancellationToken ct) =>
        Infrastructure.AtomicFile.WriteAllTextAsync(
            fs, PathFor(fs, stagingRoot, OperationId), JsonSerializer.Serialize(this, JournalJsonContext.Default.OperationJournal), ct: ct);

    public static OperationJournal? Load(IFileSystem fs, string path)
    {
        try
        {
            return JsonSerializer.Deserialize(fs.File.ReadAllText(path), JournalJsonContext.Default.OperationJournal);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OperationJournal))]
internal sealed partial class JournalJsonContext : JsonSerializerContext;
