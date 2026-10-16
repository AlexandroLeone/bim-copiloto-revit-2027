namespace ChatGPTRevit;

public sealed class ImageTrace
{
    public string Summary { get; set; } = "";
    public List<string> Notes { get; set; } = new();
    public List<TraceSegment> Segments { get; set; } = new();
}

public sealed class TraceSegment
{
    public bool Include { get; set; } = true;
    public string Key { get; set; } = "";
    public string Kind { get; set; } = "wall";
    public string HostKey { get; set; } = "";
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public double Confidence { get; set; }
}

public sealed class TraceCalibration
{
    public PlanPoint A { get; set; } = new();
    public PlanPoint B { get; set; } = new();
    public double ImageWidth { get; set; }
    public double ImageHeight { get; set; }
    public double RealLengthMm { get; set; }
    public double OriginX { get; set; }
    public double OriginY { get; set; }
    public double RotationDegrees { get; set; }
    public PlanPoint Convert(double x, double y)
    {
        var dx = (B.X - A.X) * ImageWidth / 1000;
        var dy = (B.Y - A.Y) * ImageHeight / 1000;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 2 || RealLengthMm <= 0 || !double.IsFinite(RealLengthMm))
            throw new InvalidOperationException("Marque dois pontos distintos e informe a medida real em mm.");
        var scale = RealLengthMm / length;
        var px = (x - A.X) * ImageWidth / 1000 * scale;
        var py = -(y - A.Y) * ImageHeight / 1000 * scale;
        var angle = RotationDegrees * Math.PI / 180;
        return new PlanPoint { X = OriginX + px * Math.Cos(angle) - py * Math.Sin(angle),
            Y = OriginY + px * Math.Sin(angle) + py * Math.Cos(angle) };
    }
}

public static class TraceConverter
{
    public static void Validate(ImageTrace trace)
    {
        if (trace.Segments == null || trace.Notes == null || trace.Segments.Count > 300)
            throw new InvalidOperationException("Desenho inválido ou maior que 300 segmentos.");
        var keys = new HashSet<string>();
        foreach (var s in trace.Segments)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.Key) || !keys.Add(s.Key) ||
                s.Kind is not ("wall" or "door" or "window") ||
                new[] { s.X1, s.Y1, s.X2, s.Y2 }.Any(n => !double.IsFinite(n) || n < 0 || n > 1000) ||
                !double.IsFinite(s.Confidence) || s.Confidence < 0 || s.Confidence > 1)
                throw new InvalidOperationException("Segmento inválido na leitura da imagem.");
        }
    }
    public static EditPlan ToPlan(ImageTrace trace, TraceCalibration c, string level,
        long wallType, long doorType, long windowType, double heightMm)
    {
        Validate(trace);
        var plan = new EditPlan { Summary = "Modelar a planta revisada a partir da imagem.", Notes = trace.Notes.ToList() };
        var walls = trace.Segments.Where(s => s.Include && s.Kind == "wall").ToList();
        foreach (var s in walls)
        {
            var a = c.Convert(s.X1, s.Y1); var b = c.Convert(s.X2, s.Y2);
            plan.Actions.Add(new EditAction { Operation = "create_wall", Key = s.Key, TypeId = wallType,
                LevelName = level, XMm = a.X, YMm = a.Y, X2Mm = b.X, Y2Mm = b.Y, WallHeightMm = heightMm });
        }
        foreach (var s in trace.Segments.Where(s => s.Include && s.Kind != "wall"))
        {
            if (!walls.Any(w => w.Key == s.HostKey))
                throw new InvalidOperationException("A abertura " + s.Key + " não tem uma parede incluída.");
            var p = c.Convert((s.X1 + s.X2) / 2, (s.Y1 + s.Y2) / 2);
            plan.Actions.Add(new EditAction { Operation = s.Kind == "door" ? "place_door" : "place_window",
                Key = s.Key, HostKey = s.HostKey, LevelName = level, XMm = p.X, YMm = p.Y,
                ZMm = s.Kind == "window" ? 1000 : 0, TypeId = s.Kind == "door" ? doorType : windowType });
        }
        plan.Notes.Add("Alturas, espessuras e larguras das aberturas dependem dos tipos escolhidos; revise no Revit. Peitoril inicial das janelas: 1000 mm.");
        PlanRules.Validate(plan);
        return plan;
    }
}
