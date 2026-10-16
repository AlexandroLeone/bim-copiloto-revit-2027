using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using System.Reflection;

namespace ChatGPTRevit;

public sealed class App : IExternalApplication
{
    private static readonly Dictionary<Guid, long> Revisions = new();
    public static long Revision(Document doc) => Revisions.GetValueOrDefault(doc.CreationGUID);
    private static void Changed(object? sender, DocumentChangedEventArgs e)
    {
        var key = e.GetDocument().CreationGUID;
        Revisions[key] = Revisions.GetValueOrDefault(key) + 1;
    }
    public Result OnStartup(UIControlledApplication application)
    {
        const string tab = "ChatGPT";
        try { application.CreateRibbonTab(tab); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { }
        var panel = application.CreateRibbonPanel(tab, "BIM Copiloto");
        var button = new PushButtonData("ChatGPTRevitCommand", "BIM\nCopiloto",
            Assembly.GetExecutingAssembly().Location, typeof(Command).FullName!);
        button.ToolTip = "Conversa, imagens, plantas por imagem e edição assistida do modelo.";
        panel.AddItem(button);
        application.ControlledApplication.DocumentChanged += Changed;
        return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication application)
    {
        application.ControlledApplication.DocumentChanged -= Changed;
        return Result.Succeeded;
    }
}
