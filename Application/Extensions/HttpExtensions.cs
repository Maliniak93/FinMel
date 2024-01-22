using Application.Common;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace Application.Extensions;
public static class HttpExtensions
{
    public static void AddPaginationheader(this HttpResponse response, PaginationHeader header)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        response.Headers.Append("Pagination", JsonSerializer.Serialize(header, jsonOptions));
        response.Headers.Append("Access-Control-Expose-Headers", "Pagination");
    }
}
