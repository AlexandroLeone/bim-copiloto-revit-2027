using Autodesk.Revit.UI;

namespace ChatGPTRevit;

public sealed class RevitRequestHandler : IExternalEventHandler, IDisposable
{
    private Action<UIApplication>? _pending;
    private readonly ExternalEvent _event;
    public RevitRequestHandler() => _event = ExternalEvent.Create(this);
    public Task<T> RunAsync<T>(Func<UIApplication, T> action)
    {
        if (_pending != null) throw new InvalidOperationException("O Revit já tem uma solicitação pendente.");
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = app =>
        {
            try { completion.TrySetResult(action(app)); }
            catch (Exception ex) { completion.TrySetException(ex); }
        };
        try
        {
            var status = _event.Raise();
            if (status != ExternalEventRequest.Accepted && status != ExternalEventRequest.Pending)
                throw new InvalidOperationException("Revit indisponível. Termine o comando ativo e tente novamente.");
        }
        catch (Exception ex) { _pending = null; completion.TrySetException(ex); }
        return completion.Task;
    }
    public void Execute(UIApplication app)
    {
        var action = _pending;
        _pending = null;
        action?.Invoke(app);
    }
    public string GetName() => "BIM Copiloto";
    public void Dispose() => _event.Dispose();
}
