using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dota2Dispenser.Person;

namespace Dota2Dispenser.Routes;

public static class AccountRoutes
{
    public static async Task<IResult> DeleteAsync(HttpContext context, ulong id, TargetsContainer targetsContainer)
    {
        string? identity = context.Request.Headers.Authorization.FirstOrDefault();
        if (identity == null)
            return TypedResults.BadRequest();

        bool result = await targetsContainer.RemoveAsync(id, identity);

        if (result)
        {
            return TypedResults.Ok();
        }
        else
        {
            return TypedResults.NotFound();
        }
    }

    public static async Task<IResult> PostAsync(HttpContext context, TargetsContainer targetsContainer)
    {
        string? identity = context.Request.Headers.Authorization.FirstOrDefault();
        if (identity == null)
            return TypedResults.BadRequest();

        if (context.Request.ContentLength > 1024)
        {
            context.Abort();
            return TypedResults.Empty;
        }

        using var reader = new StreamReader(context.Request.Body);

        string bodyContent = await reader.ReadToEndAsync();

        var content = JsonSerializer.Deserialize<Shared.Models.PostAccountModel>(bodyContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });

        if (content == null)
        {
            return TypedResults.BadRequest();
        }

        bool result = await targetsContainer.AddAsync(content.Id, identity, content.Note);

        if (result)
        {
            return TypedResults.Ok();
        }
        else
        {
            return TypedResults.Conflict();
        }
    }
}
