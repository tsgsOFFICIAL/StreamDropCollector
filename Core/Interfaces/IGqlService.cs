using System.Text.Json.Nodes;

namespace Core.Interfaces
{
    public interface IGqlService : IDisposable
    {
        Task<JsonObject> QueryAsync(string operationName, object? variables = null, CancellationToken ct = default);
    }
}