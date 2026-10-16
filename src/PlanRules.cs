namespace ChatGPTRevit;

public static class PlanRules
{
    public static readonly string[] Operations =
    [
        "set_parameter", "move", "rotate", "delete", "set_material", "create_material",
        "set_layer_material", "load_family", "change_type", "place_family", "place_door",
        "place_window", "place_furniture", "create_wall", "create_floor", "create_room",
        "create_level", "create_plan"
    ];
    public static void Validate(EditPlan plan)
    {
        if (plan.Actions == null || plan.Notes == null) throw new InvalidOperationException("Listas inválidas no plano.");
        if (plan.Actions.Count > 300) throw new InvalidOperationException("Máximo de 300 ações por plano.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in plan.Actions)
        {
            if (a == null || !Operations.Contains(a.Operation))
                throw new InvalidOperationException("Operação desconhecida no plano.");
            foreach (var property in typeof(EditAction).GetProperties())
            {
                if (property.PropertyType == typeof(string) && property.GetValue(a) == null)
                    throw new InvalidOperationException($"Campo nulo: {property.Name}.");
                if (property.GetValue(a) is double d && (!double.IsFinite(d) || Math.Abs(d) > 1e8))
                    throw new InvalidOperationException($"Número inválido: {property.Name}.");
            }
            if (a.Points == null || a.Points.Any(p => p == null || !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
                throw new InvalidOperationException("Contorno inválido.");
            if (!string.IsNullOrEmpty(a.HostKey) && !keys.Contains(a.HostKey))
                throw new InvalidOperationException("A parede hospedeira deve aparecer antes da abertura.");
            if (!string.IsNullOrEmpty(a.Key) && !keys.Add(a.Key))
                throw new InvalidOperationException("Chave repetida no plano: " + a.Key);
            if (a.Operation is "create_wall" or "create_floor" or "create_room" or "create_plan"
                or "place_door" or "place_window" or "place_family" or "place_furniture")
                Required(a.LevelName, "Informe o nível.");
            if (a.Operation is "place_door" or "place_window")
                if (a.HostWallId <= 0 && string.IsNullOrEmpty(a.HostKey))
                    throw new InvalidOperationException("Portas e janelas exigem uma parede hospedeira.");
            if (a.Operation is "place_door" or "place_window" or "place_family" or "place_furniture" or "change_type")
                if (a.TypeId <= 0) throw new InvalidOperationException("Escolha um tipo existente no catálogo.");
            if (a.Operation is "set_parameter" or "set_material" or "set_layer_material" or "move" or "rotate" or "delete" or "change_type")
                if (a.ElementId <= 0 && string.IsNullOrEmpty(a.ElementUniqueId))
                    throw new InvalidOperationException("Elemento de destino não informado.");
            if (a.Operation == "create_wall")
            {
                if (a.TypeId <= 0) throw new InvalidOperationException("Escolha um tipo de parede.");
                if (Math.Sqrt(Math.Pow(a.X2Mm - a.XMm, 2) + Math.Pow(a.Y2Mm - a.YMm, 2)) < 10 || a.WallHeightMm < 100)
                    throw new InvalidOperationException("Parede com comprimento ou altura inválidos.");
            }
            if (a.Operation == "create_floor" && (a.Points.Count < 3 || a.TypeId <= 0))
                throw new InvalidOperationException("Piso exige tipo e contorno com pelo menos 3 pontos.");
            if (a.Operation == "load_family")
                if (!System.IO.Path.IsPathFullyQualified(a.FamilyPath) ||
                    !a.FamilyPath.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Selecione um arquivo local .rfa.");
        }
    }
    private static void Required(string value, string message)
    { if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(message); }
}
