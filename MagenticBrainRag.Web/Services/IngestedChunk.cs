using System.Text.Json.Serialization;
using Microsoft.Extensions.VectorData;

namespace MagenticBrainRag.Web.Services;

public class IngestedChunk
{
    public const int VectorDimensions = 768; // nomic-embed-text produces 768-dimensional embeddings
    public const string VectorDistanceFunction = DistanceFunction.CosineDistance;
    public const string CollectionName = "data-magenticbrainrag-chunks";

    [VectorStoreKey(StorageName = "key")]
    [JsonPropertyName("key")]
    public required Guid Key { get; set; }

    [VectorStoreData(StorageName = "documentid")]
    [JsonPropertyName("documentid")]
    public required string DocumentId { get; set; }

    [VectorStoreData(StorageName = "content")]
    [JsonPropertyName("content")]
    public required string Text { get; set; }

    [VectorStoreData(StorageName = "context")]
    [JsonPropertyName("context")]
    public string? Context { get; set; }

    [VectorStoreVector(VectorDimensions, DistanceFunction = VectorDistanceFunction, StorageName = "embedding")]
    [JsonPropertyName("embedding")]
    public string? Vector => Text;
}
