namespace ChatGPTRevit;

public sealed class SnapshotOptions
{
    public bool IncludeParameters { get; set; } = true;
    public bool IncludeGeometry { get; set; } = true;
    public bool IncludeWarnings { get; set; } = true;
    public bool IncludeLinks { get; set; } = true;
    public int MaxElements { get; set; } = 50000;
}

public sealed class ProjectSnapshot
{
    public DateTime CollectedAtUtc { get; set; }
    public string ProjectTitle { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string RevitVersion { get; set; } = "";
    public string ActiveView { get; set; } = "";
    public string DocumentKey { get; set; } = "";
    public List<long> Selection { get; set; } = new();
    public List<LevelRecord> Levels { get; set; } = new();
    public List<TypeRecord> Types { get; set; } = new();
    public List<MaterialRecord> Materials { get; set; } = new();
    public int OmittedElements { get; set; }
    public List<CountRecord> CategoryCounts { get; set; } = new();
    public List<ElementRecord> Elements { get; set; } = new();
    public List<RoomRecord> Rooms { get; set; } = new();
    public List<SheetRecord> Sheets { get; set; } = new();
    public List<FamilyRecord> Families { get; set; } = new();
    public List<WarningRecord> Warnings { get; set; } = new();
    public List<LinkRecord> Links { get; set; } = new();
}

public sealed record CountRecord(string Category, int Count);

public sealed class ElementRecord
{
    public long Id { get; set; }
    public string UniqueId { get; set; } = "";
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string Level { get; set; } = "";
    public object? Location { get; set; }
    public object? BoundingBox { get; set; }
    public Dictionary<string, object?> Parameters { get; set; } = new();
}

public sealed record RoomRecord(long Id, string Name, string Number, string Level, double AreaM2, string Status);
public sealed record SheetRecord(long Id, string Number, string Name, int Viewports, int Schedules);
public sealed record FamilyRecord(string Name, string Category, string Kind);
public sealed record WarningRecord(string Description, string Severity, int FailingElementCount);
public sealed record LinkRecord(string Name, string Path, bool Loaded);
public sealed record LevelRecord(long Id, string Name, double ElevationMm);
public sealed record TypeRecord(long Id, string Name, string Family, string Category, string Kind, long CategoryId, string Layers);
public sealed record MaterialRecord(long Id, string Name);

public sealed class EditPlan
{
    public string Summary { get; set; } = "";
    public List<string> Notes { get; set; } = new();
    public List<EditAction> Actions { get; set; } = new();
}

/// <summary>
/// Comandos de edição aceitos pelo executor local. Distâncias são milímetros;
/// ângulos são graus. Os nomes de operação são deliberadamente explícitos.
/// </summary>
public sealed class EditAction
{
    public string Operation { get; set; } = "";
    public string Key { get; set; } = "";
    public string HostKey { get; set; } = "";
    public long TypeId { get; set; }
    public int LayerIndex { get; set; } = -1;
    public string ColorHex { get; set; } = "";
    public double X2Mm { get; set; }
    public double Y2Mm { get; set; }
    public List<PlanPoint> Points { get; set; } = new();
    public long ElementId { get; set; }
    public string ElementUniqueId { get; set; } = "";
    public bool TargetType { get; set; }
    public string ParameterName { get; set; } = "";
    public string Value { get; set; } = "";
    public double DxMm { get; set; }
    public double DyMm { get; set; }
    public double DzMm { get; set; }
    public double AngleDegrees { get; set; }
    public string FamilyPath { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string LevelName { get; set; } = "";
    public long HostWallId { get; set; }
    public double XMm { get; set; }
    public double YMm { get; set; }
    public double ZMm { get; set; }
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public double WallHeightMm { get; set; } = 2800;
    public string WallTypeName { get; set; } = "";
}

public sealed class EditExecutionResult
{
    public List<string> Applied { get; } = new();
    public List<string> Failed { get; } = new();
    public bool Committed { get; set; }
    public List<long> Created { get; set; } = new();
    public List<long> Modified { get; set; } = new();
    public List<long> Deleted { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class PlanPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}
