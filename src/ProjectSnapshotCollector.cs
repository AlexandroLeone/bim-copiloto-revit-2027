using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ChatGPTRevit;

public static class ProjectSnapshotCollector
{
    public static ProjectSnapshot Collect(UIApplication uiapp, SnapshotOptions options)
    {
        var doc = uiapp.ActiveUIDocument.Document;
        var snapshot = new ProjectSnapshot
        {
            CollectedAtUtc = DateTime.UtcNow,
            ProjectTitle = doc.Title,
            FilePath = doc.PathName,
            RevitVersion = doc.Application.VersionNumber,
            ActiveView = doc.ActiveView?.Name ?? ""
        };
        snapshot.DocumentKey = doc.CreationGUID.ToString();
        snapshot.Selection = uiapp.ActiveUIDocument.Selection.GetElementIds().Select(x => x.Value).ToList();
        snapshot.Levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .Select(x => new LevelRecord(x.Id.Value, x.Name, Mm(x.Elevation))).OrderBy(x => x.ElevationMm).ToList();
        snapshot.Types = new FilteredElementCollector(doc).WhereElementIsElementType().Cast<ElementType>()
            .Where(x => x is WallType { Kind: WallKind.Basic } or FloorType or FamilySymbol)
            .Select(x => new TypeRecord(x.Id.Value, x.Name, x.FamilyName, x.Category?.Name ?? "",
                x is FamilySymbol fs ? fs.Family.FamilyPlacementType.ToString() : x.GetType().Name,
                x.Category?.Id.Value ?? 0, ReadLayers(x))).ToList();
        snapshot.Materials = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
            .Select(x => new MaterialRecord(x.Id.Value, x.Name)).ToList();

        var elements = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .ToElements();

        var categoryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in elements)
        {
            var category = element.Category?.Name ?? "Sem categoria";
            categoryCounts[category] = categoryCounts.TryGetValue(category, out var count) ? count + 1 : 1;
        }
        snapshot.CategoryCounts = categoryCounts
            .OrderByDescending(x => x.Value)
            .Select(x => new CountRecord(x.Key, x.Value))
            .ToList();

        var selection = snapshot.Selection.ToHashSet();
        var candidates = elements.Where(e => e.Category?.CategoryType == CategoryType.Model || selection.Contains(e.Id.Value))
            .OrderByDescending(e => selection.Contains(e.Id.Value)).ThenBy(e => e.Id.Value).ToList();
        snapshot.OmittedElements = Math.Max(0, candidates.Count - options.MaxElements);
        foreach (var element in candidates.Take(options.MaxElements))
        {
            snapshot.Elements.Add(ReadElement(doc, element, options));
        }

        snapshot.Rooms = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .Select(room => new RoomRecord(
                room.Id.Value,
                room.Name ?? "",
                room.Number ?? "",
                room.Level?.Name ?? "",
                UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters),
                room.Location == null ? "unplaced" : "placed"))
            .ToList();

        snapshot.Sheets = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Select(sheet => new SheetRecord(
                sheet.Id.Value,
                sheet.SheetNumber ?? "",
                sheet.Name ?? "",
                sheet.GetAllViewports().Count,
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ScheduleSheetInstance))
                    .Cast<ScheduleSheetInstance>()
                    .Count(x => x.OwnerViewId == sheet.Id)))
            .ToList();

        snapshot.Families = new FilteredElementCollector(doc)
            .OfClass(typeof(Family))
            .Cast<Family>()
            .Select(f => new FamilyRecord(f.Name ?? "", f.FamilyCategory?.Name ?? "", "loadable"))
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name)
            .ToList();

        if (options.IncludeWarnings)
        {
            snapshot.Warnings = doc.GetWarnings()
                .Select(w => new WarningRecord(
                    w.GetDescriptionText(),
                    w.GetSeverity().ToString(),
                    w.GetFailingElements().Count))
                .ToList();
        }

        if (options.IncludeLinks)
        {
            snapshot.Links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Select(link => new LinkRecord(
                    link.Name ?? "",
                    link.GetLinkDocument()?.PathName ?? "",
                    link.GetLinkDocument() != null))
                .ToList();
        }

        return snapshot;
    }

    public static string ToJson(ProjectSnapshot snapshot)
    {
        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static ElementRecord ReadElement(Document doc, Element element, SnapshotOptions options)
    {
        var record = new ElementRecord
        {
            Id = element.Id.Value,
            UniqueId = element.UniqueId,
            Category = element.Category?.Name ?? "",
            Name = element.Name ?? "",
            Level = TryGetLevel(element) ?? ""
        };

        var type = element.Document.GetElement(element.GetTypeId()) as ElementType;
        record.TypeName = type?.Name ?? "";
        if (type is FamilySymbol symbol)
            record.FamilyName = symbol.FamilyName ?? "";

        if (options.IncludeGeometry)
        {
            record.Location = ReadLocation(element.Location);
            record.BoundingBox = ReadBoundingBox(element.get_BoundingBox(null));
        }

        if (options.IncludeParameters)
        {
            foreach (Parameter parameter in element.Parameters.Cast<Parameter>().Take(40))
            {
                if (!parameter.HasValue || parameter.Definition == null) continue;
                var name = parameter.Definition.Name;
                if (string.IsNullOrWhiteSpace(name) || record.Parameters.ContainsKey(name)) continue;
                var value = ReadParameterValue(doc, parameter);
                if (value is string text && text.Length > 1000) value = text[..1000];
                record.Parameters[name] = value;
            }
        }
        return record;
    }

    private static string? TryGetLevel(Element element)
    {
        var levelId = element.LevelId;
        if (levelId == ElementId.InvalidElementId) return null;
        return element.Document.GetElement(levelId)?.Name;
    }

    private static object? ReadParameterValue(Document doc, Parameter parameter)
    {
        return parameter.StorageType switch
        {
            StorageType.String => parameter.AsString(),
            StorageType.Double => parameter.AsValueString() is string doubleText ? doubleText : parameter.AsDouble(),
            StorageType.Integer => parameter.AsValueString() is string integerText ? integerText : parameter.AsInteger(),
            StorageType.ElementId => ReadElementIdValue(doc, parameter.AsElementId()),
            _ => null
        };
    }

    private static object ReadElementIdValue(Document doc, ElementId id)
    {
        var element = id == ElementId.InvalidElementId ? null : doc.GetElement(id);
        return element == null ? id.Value : new { id = id.Value, name = element.Name };
    }

    private static object? ReadLocation(Location? location)
    {
        if (location is LocationPoint point)
            return new { kind = "point", x_mm = Mm(point.Point.X), y_mm = Mm(point.Point.Y), z_mm = Mm(point.Point.Z) };
        if (location is LocationCurve curve && curve.Curve.IsBound)
            return new
            {
                kind = "curve",
                start = new { x_mm = Mm(curve.Curve.GetEndPoint(0).X), y_mm = Mm(curve.Curve.GetEndPoint(0).Y), z_mm = Mm(curve.Curve.GetEndPoint(0).Z) },
                end = new { x_mm = Mm(curve.Curve.GetEndPoint(1).X), y_mm = Mm(curve.Curve.GetEndPoint(1).Y), z_mm = Mm(curve.Curve.GetEndPoint(1).Z) }
            };
        return null;
    }

    private static object? ReadBoundingBox(BoundingBoxXYZ? box)
    {
        if (box == null) return null;
        return new
        {
            min = new { x_mm = Mm(box.Min.X), y_mm = Mm(box.Min.Y), z_mm = Mm(box.Min.Z) },
            max = new { x_mm = Mm(box.Max.X), y_mm = Mm(box.Max.Y), z_mm = Mm(box.Max.Z) }
        };
    }

    private static double Mm(double internalValue) => UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.Millimeters);
    private static string ReadLayers(ElementType type)
    {
        if (type is not HostObjAttributes host || host.GetCompoundStructure() is not { } structure) return "";
        return string.Join("; ", structure.GetLayers().Select((l, i) =>
            $"{i}: {Mm(l.Width):0.##} mm, materialId={l.MaterialId.Value}"));
    }
}
