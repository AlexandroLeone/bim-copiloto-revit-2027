using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using System.Windows.Interop;

namespace ChatGPTRevit;

[Transaction(TransactionMode.Manual)]
public sealed class Command : IExternalCommand
{
    internal static ChatGPTWindow? Window;
    public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
    {
        if (commandData.Application.ActiveUIDocument == null)
        { TaskDialog.Show("BIM Copiloto", "Abra um projeto no Revit."); return Result.Cancelled; }
        if (Window != null) { Window.Activate(); return Result.Succeeded; }
        var handler = new RevitRequestHandler();
        try
        {
            Window = new ChatGPTWindow(commandData.Application, handler);
            new WindowInteropHelper(Window).Owner = commandData.Application.MainWindowHandle;
            Window.Closed += (_, _) => { handler.Dispose(); Window = null; };
            Window.Show();
            return Result.Succeeded;
        }
        catch (Exception ex) { handler.Dispose(); Window = null; message = ex.Message; return Result.Failed; }
    }
}
