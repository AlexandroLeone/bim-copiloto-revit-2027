using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatGPTRevit;

public static class EditPlanParser
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
    public static EditPlan Parse(string text)
    {
        var plan = JsonSerializer.Deserialize<EditPlan>(text, Options)
            ?? throw new InvalidOperationException("Plano vazio.");
        PlanRules.Validate(plan);
        return plan;
    }
}
