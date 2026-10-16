using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.DB.Events;
using System.Globalization;
using System.IO;

namespace ChatGPTRevit;

public static class EditPlanExecutor
{
    public static string Stamp(Document doc)
    {
        var text = doc.CreationGUID + "|" + string.Join(";", State(doc).OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }
    public static EditExecutionResult Apply(Document doc, EditPlan plan, bool preview,
        ISet<string> allowedFamilies)
    {
        PlanRules.Validate(plan);
        if (plan.Actions.Count == 0) throw new InvalidOperationException("Plano sem ações.");
        if (doc.IsReadOnly || doc.IsLinked || doc.IsFamilyDocument)
            throw new InvalidOperationException("Abra um projeto editável.");
        foreach (var a in plan.Actions.Where(a => a.Operation == "load_family"))
            if (!allowedFamilies.Contains(a.FamilyPath))
                throw new InvalidOperationException("Selecione a família pelo botão Família .rfa antes de carregá-la.");

        var result = new EditExecutionResult();
        // DocumentChanged can be deferred until the outer TransactionGroup ends.
        // Compare model state BEFORE rolling back, so dependencies are visible in preview.
        var before = State(doc);
        using var group = new TransactionGroup(doc, "BIM Copiloto — " + (preview ? "simulação" : "edição"));
        if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Não foi possível iniciar o grupo.");
        try
        {
            using var tx = new Transaction(doc, "BIM Copiloto — plano revisado");
            if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Transação indisponível.");
            var failures = new FailureCollector(result.Warnings);
            tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures)
                .SetClearAfterRollback(true).SetForcedModalHandling(true));
            var keys = new Dictionary<string, ElementId>(StringComparer.Ordinal);
            for (var index = 0; index < plan.Actions.Count; index++)
            {
                try
                {
                    var action = plan.Actions[index];
                    var id = ApplyOne(doc, action, keys);
                    if (!string.IsNullOrEmpty(action.Key) && id != ElementId.InvalidElementId) keys.Add(action.Key, id);
                    result.Applied.Add($"{index + 1}. {action.Operation}: {(id == ElementId.InvalidElementId ? "concluído" : id.Value)}");
                    doc.Regenerate();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    throw new InvalidOperationException($"Ação {index + 1} ({plan.Actions[index].Operation}): {ex.Message}", ex);
                }
            }
            var status = tx.Commit();
            if (status != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit rejeitou o plano. " + string.Join("; ", failures.Errors));
            var after = State(doc);
            result.Created = after.Keys.Except(before.Keys).Order().ToList();
            result.Modified = before.Keys.Intersect(after.Keys).Where(id => before[id] != after[id]).Order().ToList();
            result.Deleted = before.Keys.Except(after.Keys).Order().ToList();
            if (preview)
            {
                if (group.RollBack() != TransactionStatus.RolledBack)
                    throw new InvalidOperationException("O Revit não confirmou a reversão da simulação.");
            }
            else
            {
                if (group.Assimilate() != TransactionStatus.Committed)
                    throw new InvalidOperationException("O Revit não confirmou a conclusão do grupo.");
                result.Committed = true;
            }
            return result;
        }
        catch
        {
            if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
            throw;
        }
    }

    private static Dictionary<long, int> State(Document doc)
    {
        var result = new Dictionary<long, int>();
        foreach (var e in new FilteredElementCollector(doc).WherePasses(new LogicalOrFilter(
            new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true))))
        {
            var hash = new HashCode();
            hash.Add(e.VersionGuid); hash.Add(e.Name); hash.Add(e.GetTypeId().Value);
            foreach (Parameter p in e.Parameters)
            {
                hash.Add(p.Id.Value);
                switch (p.StorageType)
                {
                    case StorageType.String: hash.Add(p.AsString()); break;
                    case StorageType.Double: hash.Add(p.AsDouble()); break;
                    case StorageType.Integer: hash.Add(p.AsInteger()); break;
                    case StorageType.ElementId: hash.Add(p.AsElementId().Value); break;
                }
            }
            if (e.Location is LocationPoint point)
            {
                hash.Add(point.Point.X); hash.Add(point.Point.Y); hash.Add(point.Point.Z);
                if (e is FamilyInstance instance)
                {
                    // Some imported/hosted families have no extractable scalar Rotation.
                    // The full transform also captures their orientation and mirroring.
                    var transform = instance.GetTransform();
                    foreach (var axis in new[] { transform.BasisX, transform.BasisY, transform.BasisZ })
                    { hash.Add(axis.X); hash.Add(axis.Y); hash.Add(axis.Z); }
                }
                else
                {
                    try { hash.Add(point.Rotation); }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException) { /* Rooms have no rotation. */ }
                    catch (Autodesk.Revit.Exceptions.InternalException) { /* Some point locations do not expose rotation. */ }
                }
            }
            if (e.Location is LocationCurve curve && curve.Curve.IsBound)
                foreach (var point3 in new[] { curve.Curve.GetEndPoint(0), curve.Curve.GetEndPoint(1) })
                { hash.Add(point3.X); hash.Add(point3.Y); hash.Add(point3.Z); }
            result[e.Id.Value] = hash.ToHashCode();
        }
        return result;
    }

    private static ElementId ApplyOne(Document doc, EditAction a, Dictionary<string, ElementId> keys)
    {
        switch (a.Operation)
        {
            case "set_parameter":
                var target = Target(doc, a);
                var parameters = target.GetParameters(a.ParameterName);
                if (parameters.Count != 1 || parameters[0].IsReadOnly)
                    throw new InvalidOperationException("Parâmetro inexistente, ambíguo ou somente leitura: " + a.ParameterName);
                var p = parameters[0];
                var ok = p.StorageType switch
                {
                    StorageType.String => p.Set(a.Value),
                    StorageType.Integer => p.Set(int.Parse(a.Value, CultureInfo.InvariantCulture)),
                    StorageType.ElementId => SetElementId(doc, p, a.Value),
                    StorageType.Double => SetDouble(p, a.Value),
                    _ => false
                };
                if (!ok) throw new InvalidOperationException("Revit não aceitou o valor do parâmetro.");
                return target.Id;
            case "move":
                var moved = Find(doc, a);
                ElementTransformUtils.MoveElement(doc, moved.Id, Point(a.DxMm, a.DyMm, a.DzMm));
                return moved.Id;
            case "rotate":
                var rotated = Find(doc, a);
                var location = rotated.Location as LocationPoint
                    ?? throw new InvalidOperationException("Este elemento não tem ponto de rotação.");
                ElementTransformUtils.RotateElement(doc, rotated.Id,
                    Line.CreateBound(location.Point, location.Point + XYZ.BasisZ), a.AngleDegrees * Math.PI / 180);
                return rotated.Id;
            case "delete":
                doc.Delete(Find(doc, a).Id);
                return ElementId.InvalidElementId;
            case "change_type":
                var element = Find(doc, a);
                var newType = new ElementId(a.TypeId);
                if (!element.IsValidType(newType)) throw new InvalidOperationException("Tipo incompatível com o elemento.");
                element.ChangeTypeId(newType);
                return element.IsValidObject ? element.Id : ElementId.InvalidElementId;
            case "create_material":
                if (string.IsNullOrWhiteSpace(a.MaterialName)) throw new InvalidOperationException("Nome do material vazio.");
                if (Materials(doc, a.MaterialName).Any()) throw new InvalidOperationException("Já existe um material com esse nome.");
                var material = (Material)doc.GetElement(Material.Create(doc, a.MaterialName));
                if (!string.IsNullOrEmpty(a.ColorHex))
                {
                    var hex = a.ColorHex.TrimStart('#');
                    if (hex.Length != 6) throw new InvalidOperationException("Use cor #RRGGBB.");
                    material.Color = new Color(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
                }
                return material.Id;
            case "set_material":
                var t = Target(doc, a);
                var mp = string.IsNullOrWhiteSpace(a.ParameterName)
                    ? t.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)
                    : UniqueParameter(t, a.ParameterName);
                if (mp == null || mp.IsReadOnly || mp.Definition.GetDataType() != SpecTypeId.Reference.Material)
                    throw new InvalidOperationException("Escolha um parâmetro editável do tipo Material.");
                if (!mp.Set(MaterialByName(doc, a.MaterialName).Id)) throw new InvalidOperationException("Material rejeitado.");
                return t.Id;
            case "set_layer_material":
                var hostType = Target(doc, a) as HostObjAttributes
                    ?? throw new InvalidOperationException("Informe um tipo de parede/piso e TargetType=true.");
                var structure = hostType.GetCompoundStructure()
                    ?? throw new InvalidOperationException("O tipo não possui camadas.");
                if (a.LayerIndex < 0 || a.LayerIndex >= structure.LayerCount)
                    throw new InvalidOperationException("Índice de camada inválido.");
                structure.SetMaterialId(a.LayerIndex, MaterialByName(doc, a.MaterialName).Id);
                hostType.SetCompoundStructure(structure);
                return hostType.Id;
            case "load_family":
                if (!File.Exists(a.FamilyPath)) throw new FileNotFoundException("Família não encontrada.");
                if (new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                    .Any(f => string.Equals(f.Name, Path.GetFileNameWithoutExtension(a.FamilyPath), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Esta família já está carregada; substituição automática está desabilitada.");
                if (!doc.LoadFamily(a.FamilyPath, new KeepExistingFamily(), out var family) || family == null)
                    throw new InvalidOperationException("Família não carregada; confira versão, formato ou duplicidade.");
                return family.Id;
            case "place_family":
            case "place_door":
            case "place_window":
            case "place_furniture":
                return Place(doc, a, keys).Id;
            case "create_wall":
                var level = LevelByName(doc, a.LevelName);
                var type = doc.GetElement(new ElementId(a.TypeId)) as WallType;
                if (type == null || type.Kind != WallKind.Basic) throw new InvalidOperationException("Escolha uma parede básica.");
                var start = new XYZ(Mm(a.XMm), Mm(a.YMm), level.Elevation);
                var end = new XYZ(Mm(a.X2Mm), Mm(a.Y2Mm), level.Elevation);
                return Wall.Create(doc, Line.CreateBound(start, end), type.Id, level.Id,
                    Mm(a.WallHeightMm), Mm(a.ZMm), false, false).Id;
            case "create_floor":
                var fl = LevelByName(doc, a.LevelName);
                if (doc.GetElement(new ElementId(a.TypeId)) is not FloorType) throw new InvalidOperationException("Tipo de piso inválido.");
                var loop = new CurveLoop();
                for (var i = 0; i < a.Points.Count; i++)
                {
                    var from = a.Points[i]; var to = a.Points[(i + 1) % a.Points.Count];
                    loop.Append(Line.CreateBound(new XYZ(Mm(from.X), Mm(from.Y), fl.Elevation),
                        new XYZ(Mm(to.X), Mm(to.Y), fl.Elevation)));
                }
                var floor = Floor.Create(doc, new List<CurveLoop> { loop }, new ElementId(a.TypeId), fl.Id);
                floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(Mm(a.ZMm));
                return floor.Id;
            case "create_room":
                var room = doc.Create.NewRoom(LevelByName(doc, a.LevelName), new UV(Mm(a.XMm), Mm(a.YMm)));
                if (!string.IsNullOrWhiteSpace(a.Value)) room.Name = a.Value;
                doc.Regenerate();
                if (room.Area <= 0) throw new InvalidOperationException("O ponto do ambiente não está em um recinto fechado.");
                return room.Id;
            case "create_level":
                if (new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Any(l => l.Name == a.LevelName))
                    throw new InvalidOperationException("Nível já existente.");
                if (string.IsNullOrWhiteSpace(a.LevelName)) throw new InvalidOperationException("Informe um nome para o nível.");
                var newLevel = Level.Create(doc, Mm(a.ZMm)); newLevel.Name = a.LevelName; return newLevel.Id;
            case "create_plan":
                var vtype = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan)
                    ?? throw new InvalidOperationException("Projeto sem tipo de vista de planta.");
                return ViewPlan.Create(doc, vtype.Id, LevelByName(doc, a.LevelName).Id).Id;
            default: throw new InvalidOperationException("Operação não implementada.");
        }
    }

    private static FamilyInstance Place(Document doc, EditAction a, Dictionary<string, ElementId> keys)
    {
        var symbol = doc.GetElement(new ElementId(a.TypeId)) as FamilySymbol
            ?? throw new InvalidOperationException("Tipo de família inexistente.");
        var expected = a.Operation switch
        {
            "place_door" => BuiltInCategory.OST_Doors, "place_window" => BuiltInCategory.OST_Windows,
            "place_furniture" => BuiltInCategory.OST_Furniture, _ => (BuiltInCategory)0
        };
        if (expected != 0 && symbol.Category.Id.Value != (long)expected)
            throw new InvalidOperationException("A categoria da família não corresponde à operação.");
        var level = LevelByName(doc, a.LevelName);
        if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }
        var point = new XYZ(Mm(a.XMm), Mm(a.YMm), level.Elevation + Mm(a.ZMm));
        FamilyInstance instance;
        var hostId = !string.IsNullOrEmpty(a.HostKey) ? keys[a.HostKey] : new ElementId(a.HostWallId);
        if (hostId.Value > 0)
        {
            var host = doc.GetElement(hostId) as Wall ?? throw new InvalidOperationException("Parede hospedeira inválida.");
            if (host.LevelId != level.Id) throw new InvalidOperationException("Nível da abertura difere do nível da parede.");
            if (host.Location is not LocationCurve curve) throw new InvalidOperationException("Parede sem eixo.");
            var projection = curve.Curve.Project(point) ?? throw new InvalidOperationException("Ponto fora da parede.");
            if (!curve.Curve.IsInside(projection.Parameter)) throw new InvalidOperationException("Abertura fora das extremidades da parede.");
            var projected = projection.XYZPoint;
            if (Math.Sqrt(Math.Pow(point.X - projected.X, 2) + Math.Pow(point.Y - projected.Y, 2)) > Mm(100))
                throw new InvalidOperationException("Abertura a mais de 100 mm do eixo da parede. Revise o desenho.");
            point = new XYZ(projected.X, projected.Y, point.Z);
            instance = doc.Create.NewFamilyInstance(point, symbol, host, level, StructuralType.NonStructural);
        }
        else
        {
            if (symbol.Family.FamilyPlacementType != FamilyPlacementType.OneLevelBased)
                throw new InvalidOperationException("Esta família exige face, plano ou outro hospedeiro.");
            instance = doc.Create.NewFamilyInstance(point, symbol, level, StructuralType.NonStructural);
        }
        if (a.AngleDegrees != 0)
            ElementTransformUtils.RotateElement(doc, instance.Id, Line.CreateBound(point, point + XYZ.BasisZ),
                a.AngleDegrees * Math.PI / 180);
        return instance;
    }

    private static bool SetDouble(Parameter p, string value)
    {
        if (p.Definition.GetDataType() == SpecTypeId.Number)
            return p.Set(double.Parse(value, CultureInfo.InvariantCulture));
        // Explicit units prevent accidental interpretation of millimetres as internal feet.
        if (!value.Any(char.IsLetter) && !value.Contains('°') && !value.Contains('%'))
            throw new InvalidOperationException("Inclua a unidade no valor: exemplo 900 mm.");
        return p.SetValueString(value);
    }
    private static bool SetElementId(Document doc, Parameter p, string value)
    {
        var id = new ElementId(long.Parse(value, CultureInfo.InvariantCulture));
        if (doc.GetElement(id) == null) throw new InvalidOperationException("ID de parâmetro não existe.");
        return p.Set(id);
    }
    private static Parameter? UniqueParameter(Element e, string name)
    {
        var list = e.GetParameters(name);
        if (list.Count > 1) throw new InvalidOperationException("Nome de parâmetro ambíguo.");
        return list.SingleOrDefault();
    }
    private static Element Find(Document doc, EditAction a)
    {
        var e = !string.IsNullOrEmpty(a.ElementUniqueId) ? doc.GetElement(a.ElementUniqueId) : doc.GetElement(new ElementId(a.ElementId));
        if (e == null || (!string.IsNullOrEmpty(a.ElementUniqueId) && a.ElementId > 0 && e.Id.Value != a.ElementId))
            throw new InvalidOperationException("Elemento não existe ou IDs não correspondem.");
        if (e.Pinned || e.GroupId != ElementId.InvalidElementId)
            throw new InvalidOperationException("Elemento fixado ou pertencente a grupo. Revise no Revit.");
        return e;
    }
    private static Element Target(Document doc, EditAction a)
    {
        var e = Find(doc, a);
        return a.TargetType ? doc.GetElement(e.GetTypeId()) ?? throw new InvalidOperationException("Tipo não encontrado.") : e;
    }
    private static IEnumerable<Material> Materials(Document doc, string name) =>
        new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    private static Material MaterialByName(Document doc, string name) =>
        Materials(doc, name).SingleOrDefault() ?? throw new InvalidOperationException("Material inexistente: " + name);
    private static Level LevelByName(Document doc, string name) =>
        new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().SingleOrDefault(l => l.Name == name)
        ?? throw new InvalidOperationException("Nível não encontrado: " + name);
    private static XYZ Point(double x, double y, double z) => new(Mm(x), Mm(y), Mm(z));
    private static double Mm(double value) => UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Millimeters);
    private sealed class KeepExistingFamily : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool inUse, out bool overwrite) { overwrite = false; return false; }
        public bool OnSharedFamilyFound(Family f, bool inUse, out FamilySource source, out bool overwrite)
        { source = FamilySource.Project; overwrite = false; return false; }
    }
    private sealed class FailureCollector(List<string> warnings) : IFailuresPreprocessor
    {
        public List<string> Errors { get; } = new();
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var f in accessor.GetFailureMessages())
                if (f.GetSeverity() == FailureSeverity.Warning)
                { warnings.Add(f.GetDescriptionText()); accessor.DeleteWarning(f); }
                else Errors.Add(f.GetDescriptionText());
            return Errors.Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
