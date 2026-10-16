namespace ChatGPTRevit;

/// <summary>Provider-neutral contract used by the Revit UI.</summary>
public interface IAssistantClient
{
    Task<string> ChatAsync(IEnumerable<ChatMessage> history, string context, string model,
        Action<string> onDelta, CancellationToken cancel);

    Task<EditPlan> PlanAsync(string request, IEnumerable<ChatMessage> history, string context,
        IEnumerable<ImageAttachment> images, string model, CancellationToken cancel);

    Task<ImageTrace> TraceAsync(ImageAttachment image, string model, CancellationToken cancel);
}
