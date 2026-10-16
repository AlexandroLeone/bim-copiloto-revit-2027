using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfPoint = System.Windows.Point;
using WpfImage = System.Windows.Controls.Image;
using WpfColor = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using Color = System.Windows.Media.Color;
using Line = System.Windows.Shapes.Line;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace ChatGPTRevit;

public partial class ChatGPTWindow : Window
{
    private readonly Document _document;
    private readonly string _docKey;
    private readonly RevitRequestHandler _handler;
    private readonly ObservableCollection<ChatMessage> _messages = new();
    private readonly ObservableCollection<ImageAttachment> _attachments = new();
    private readonly HashSet<string> _families = new(StringComparer.OrdinalIgnoreCase);
    private ProjectSnapshot? _snapshot;
    private string _snapshotRevision = "", _planRevision = "", _previewRevision = "";
    private string? _previewJson;
    private EditExecutionResult? _preview;
    private ImageAttachment? _traceImage;
    private ImageTrace? _trace;
    private PlanPoint? _scaleA, _scaleB, _drawA;
    private string _clickMode = "";
    private bool _busy;
    private CancellationTokenSource? _cancel;

    public ChatGPTWindow(UIApplication app, RevitRequestHandler handler)
    {
        InitializeComponent();
        Width = Math.Max(MinWidth, Math.Min(1280, SystemParameters.WorkArea.Width - 30));
        Height = Math.Max(MinHeight, Math.Min(860, SystemParameters.WorkArea.Height - 30));
        _document = app.ActiveUIDocument.Document;
        _docKey = _document.CreationGUID.ToString();
        _handler = handler;
        ProjectText.Text = _document.Title;
        ContextText.Text = "Contexto atualizado a cada pedido. Para trocar de projeto, feche e reabra o assistente.";
        MessagesList.ItemsSource = _messages; AttachmentsList.ItemsSource = _attachments;
        try { foreach (var m in LocalServices.ReadHistory(_docKey)) _messages.Add(m); }
        catch (IOException) { StatusText.Text = "Histórico local indisponível; conversa iniciada em memória."; }
        if (_messages.Count == 0) Welcome();
        var providerPath = System.IO.Path.Combine(LocalServices.Root, "provider.txt");
        if (File.Exists(providerPath) && File.ReadAllText(providerPath).Trim().Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            ProviderBox.SelectedIndex = 1;
        var modelPath = System.IO.Path.Combine(LocalServices.Root, "model.txt");
        if (File.Exists(modelPath) && ProviderCode == "OpenAI") ModelBox.Text = File.ReadAllText(modelPath).Trim();
        ConfigureProviderUi();
        StatusText.Text = "Pronto. Cole uma imagem, selecione elementos no Revit ou escreva seu pedido.";
    }

    private void Welcome() => _messages.Add(new ChatMessage { Role = "assistant", Text =
        "Vamos trabalhar no seu projeto. Você pode anexar referências, perguntar sobre elementos selecionados ou pedir uma proposta de edição.\n\nPara criar uma planta a partir de imagem, abra Planta por imagem. Eu preparo o traçado, você ajusta a escala e revisa antes da modelagem." });

    private Document CheckedDocument(UIApplication app)
    {
        // Revit can return different managed wrappers for the same native document.
        // CreationGUID alone is insufficient: copies may share that GUID.
        if (!_document.IsValidObject || app.ActiveUIDocument == null ||
            !_document.Equals(app.ActiveUIDocument.Document))
            throw new InvalidOperationException("Volte ao projeto desta conversa ou feche o assistente e reabra no projeto desejado.");
        return _document;
    }

    private async Task RefreshContext()
    {
        var state = await _handler.RunAsync(app =>
        {
            CheckedDocument(app);
            var snapshot = ProjectSnapshotCollector.Collect(app, new SnapshotOptions { MaxElements = 800 });
            return (snapshot, revision: EditPlanExecutor.Stamp(_document));
        });
        _snapshot = state.snapshot; _snapshotRevision = state.revision;
        ContextText.Text = $"{_snapshot.Elements.Count} elementos · {_snapshot.Rooms.Count} ambientes\n"
            + $"{_snapshot.Selection.Count} selecionados · {_snapshot.ActiveView}"
            + (_snapshot.OmittedElements > 0 ? $"\n{_snapshot.OmittedElements} elementos fora deste resumo. Selecione os relevantes no Revit." : "");
        BindCatalog();
    }

    private void BindCatalog()
    {
        if (_snapshot == null) return;
        var levelName = (LevelCombo.SelectedItem as LevelRecord)?.Name;
        var wall = (WallCombo.SelectedItem as TypeRecord)?.Id;
        var door = (DoorCombo.SelectedItem as TypeRecord)?.Id;
        var window = (WindowCombo.SelectedItem as TypeRecord)?.Id;
        LevelCombo.ItemsSource = _snapshot.Levels;
        LevelCombo.SelectedItem = _snapshot.Levels.FirstOrDefault(l => l.Name == levelName) ?? _snapshot.Levels.FirstOrDefault();
        SetTypes(WallCombo, _snapshot.Types.Where(t => t.Kind == "WallType").ToList(), wall);
        var doors = _snapshot.Types.Where(t => t.CategoryId == (long)BuiltInCategory.OST_Doors).ToList();
        var windows = _snapshot.Types.Where(t => t.CategoryId == (long)BuiltInCategory.OST_Windows).ToList();
        SetTypes(DoorCombo, doors, door); SetTypes(WindowCombo, windows, window);
    }
    private static void SetTypes(ComboBox combo, List<TypeRecord> list, long? previous)
    {
        combo.ItemsSource = list;
        combo.SelectedItem = list.FirstOrDefault(x => x.Id == previous) ?? list.FirstOrDefault();
    }

    private async Task Work(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; _cancel = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        SetBusy(true);
        try { await action(); }
        catch (OperationCanceledException) { StatusText.Text = "Solicitação interrompida."; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally
        {
            _cancel.Dispose(); _cancel = null; _busy = false; SetBusy(false);
        }
    }
    private void SetBusy(bool busy)
    {
        SendButton.IsEnabled = ProposeButton.IsEnabled = RefreshButton.IsEnabled = CaptureButton.IsEnabled =
            FamilyButton.IsEnabled = NewChatButton.IsEnabled = RecognizeButton.IsEnabled = TracePlanButton.IsEnabled =
            SimulateButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        ApplyButton.IsEnabled = !busy && _previewJson != null;
        TraceCanvas.IsEnabled = !busy; TraceGrid.IsReadOnly = busy; PlanBox.IsReadOnly = busy;
        ModelBox.IsEnabled = !busy && ProviderCode == "OpenAI"; ProviderBox.IsEnabled = !busy; ContextCheck.IsEnabled = !busy;
    }
    private string ProviderCode => (ProviderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "OpenAI";
    private bool IsGemini => ProviderCode == "Gemini";

    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || ModelBox == null) return;
        ConfigureProviderUi();
    }

    private void ConfigureProviderUi()
    {
        if (ModelBox == null || ProviderBox == null) return;
        if (IsGemini)
        {
            ModelBox.Text = GeminiClient.DefaultModel;
            ModelBox.IsReadOnly = true;
            KeyLabel.Text = "Chave Gemini API";
            PlatformButton.Content = "Abrir Google AI Studio ↗";
            ProviderDisclosure.Text = "Dados e imagens são enviados ao Google Gemini ao Enviar, Propor edição ou Reconhecer. As alterações exigem revisão e confirmação.";
            KeyStatus.Text = "Configure uma chave Gemini nova aqui ou use GEMINI_API_KEY do Windows.";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(ModelBox.Text) || ModelBox.Text.Equals(GeminiClient.DefaultModel, StringComparison.OrdinalIgnoreCase))
                ModelBox.Text = "gpt-5";
            ModelBox.IsReadOnly = false;
            KeyLabel.Text = "Chave OpenAI API";
            PlatformButton.Content = "Abrir OpenAI Platform ↗";
            ProviderDisclosure.Text = "Dados e imagens são enviados à OpenAI ao Enviar, Propor edição ou Reconhecer. As alterações exigem revisão e confirmação.";
            KeyStatus.Text = "Configure uma chave OpenAI nova aqui ou use OPENAI_API_KEY do Windows.";
        }
    }

    private IAssistantClient Client()
    {
        var envName = IsGemini ? "GEMINI_API_KEY" : "OPENAI_API_KEY";
        var key = CredentialStore.Get(ProviderCode) ??
            Environment.GetEnvironmentVariable(envName, EnvironmentVariableTarget.User) ??
            Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(IsGemini ? "Configure uma chave Gemini na área Conexão." : "Configure uma chave OpenAI na área Conexão.");
        if (string.IsNullOrWhiteSpace(ModelBox.Text)) throw new InvalidOperationException("Informe o modelo da API.");
        return IsGemini ? new GeminiClient(key) : new OpenAIClient(key);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Work(async () =>
    { await RefreshContext(); StatusText.Text = "Contexto atualizado; nenhum dado enviado."; });

    private async void Send_Click(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var question = QuestionBox.Text.Trim();
        if (question.Length == 0 && _attachments.Count == 0) return;
        var client = Client();
        var context = "Usuário desativou o envio de dados do projeto.";
        if (ContextCheck.IsChecked == true) { await RefreshContext(); context = LocalServices.Context(_snapshot!); }
        if (context.Length > 600_000) throw new InvalidOperationException("Contexto grande demais. Selecione menos elementos ou desative o contexto para conversar sobre uma imagem.");
        var message = new ChatMessage { Text = string.IsNullOrEmpty(question) ? "Analise esta imagem." : question, Images = _attachments.ToList() };
        _messages.Add(message); _attachments.Clear(); QuestionBox.Clear();
        var answer = new ChatMessage { Role = "assistant", Text = "", Complete = false };
        _messages.Add(answer);
        StatusText.Text = "Respondendo…";
        try
        {
            var text = await client.ChatAsync(_messages, context, ModelBox.Text.Trim(), delta =>
                Dispatcher.Invoke(() => { answer.Text += delta; ChatScroll.ScrollToEnd(); }), _cancel!.Token);
            answer.Text = text; answer.Complete = true;
            StatusText.Text = "Resposta concluída.";
        }
        catch
        {
            answer.Text += "\n[Resposta interrompida. Você pode tentar novamente.]";
            throw;
        }
        finally { SaveHistory(); }
    });

    private async void Propose_Click(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var request = QuestionBox.Text.Trim();
        if (request.Length == 0) throw new InvalidOperationException("Escreva a alteração desejada antes de propor uma edição.");
        var client = Client();
        if (ContextCheck.IsChecked != true) throw new InvalidOperationException("Ative Incluir dados do projeto para propor uma edição.");
        await RefreshContext();
        var revision = _snapshotRevision;
        var context = LocalServices.Context(_snapshot!);
        if (context.Length > 600_000)
            throw new InvalidOperationException("Contexto grande demais para uma proposta. Trabalhe em uma cópia simplificada do modelo ou use um plano local revisado.");
        StatusText.Text = "Preparando proposta de edição…";
        var plan = await client.PlanAsync(request, _messages, context,
            _attachments.ToList(), ModelBox.Text.Trim(), _cancel!.Token);
        PresentPlan(plan, revision);
        _messages.Add(new ChatMessage { Text = request, Images = _attachments.ToList() });
        _messages.Add(new ChatMessage { Role = "assistant", Text = plan.Summary + "\n" + string.Join("\n", plan.Notes) + "\nProposta preparada; ainda não aplicada." });
        _attachments.Clear(); SaveHistory();
        StatusText.Text = plan.Actions.Count == 0 ? plan.Summary : "Proposta pronta. Simule para verificar o resultado no Revit.";
    });

    private void PresentPlan(EditPlan plan, string revision)
    {
        _planRevision = revision;
        _previewJson = null; _preview = null;
        PlanBox.Text = JsonSerializer.Serialize(plan, EditPlanParser.Options);
        PreviewText.Text = plan.Summary + "\n" + string.Join("\n", plan.Notes);
        Tabs.SelectedIndex = 2;
    }

    private async void Simulate_Click(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var plan = EditPlanParser.Parse(PlanBox.Text);
        var canonical = JsonSerializer.Serialize(plan, EditPlanParser.Options);
        StatusText.Text = "Simulando no Revit e verificando impactos…";
        _previewJson = null;
        var preview = await _handler.RunAsync(app =>
        {
            var doc = CheckedDocument(app);
            if (EditPlanExecutor.Stamp(doc) != _planRevision)
                throw new InvalidOperationException("O modelo mudou desde a proposta. Gere ou importe o plano novamente com o contexto atual.");
            var result = EditPlanExecutor.Apply(doc, plan, true, _families);
            return (result, revision: EditPlanExecutor.Stamp(doc));
        });
        _preview = preview.result; _previewRevision = preview.revision; _planRevision = preview.revision;
        _previewJson = canonical;
        PreviewText.Text = $"SIMULAÇÃO REVERTIDA — {plan.Actions.Count} ações\n"
            + $"{_preview.Created.Count} criados · {_preview.Modified.Count} modificados · {_preview.Deleted.Count} excluídos (incluindo dependências).\n"
            + (_preview.Deleted.Count > 0 ? "IDs excluídos: " + string.Join(", ", _preview.Deleted) + "\n" : "")
            + (plan.Actions.Any(a => a.TargetType || a.Operation == "set_layer_material") ? "Atenção: alterações de tipo/camadas afetam todas as instâncias desse tipo.\n" : "")
            + string.Join("\n", _preview.Warnings);
        StatusText.Text = "Revisão pronta. A simulação foi revertida. Confirme para aplicar.";
    });

    private async void Apply_Click(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var plan = EditPlanParser.Parse(PlanBox.Text);
        if (_previewJson == null || _previewJson != JsonSerializer.Serialize(plan, EditPlanParser.Options))
            throw new InvalidOperationException("Simule a versão atual do plano antes de aplicar.");
        if (MessageBox.Show(this, PreviewText.Text + "\n\nAplicar estas alterações ao projeto " + ProjectText.Text + "?",
            "Aplicar plano revisado", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _previewJson = null;
        var result = await _handler.RunAsync(app =>
        {
            var doc = CheckedDocument(app);
            if (EditPlanExecutor.Stamp(doc) != _previewRevision)
                throw new InvalidOperationException("O modelo mudou após a simulação. Gere um plano atualizado.");
            return EditPlanExecutor.Apply(doc, plan, false, _families);
        });
        // Reporting failures cannot undo a committed Revit transaction.
        var message = $"Aplicado: {result.Created.Count} criados, {result.Modified.Count} modificados, {result.Deleted.Count} excluídos.\n"
            + "Use Desfazer no Revit para reverter o grupo. Salve o .rvt quando desejar.";
        try { LocalServices.Log(_docKey, plan, result); }
        catch (Exception ex) { message += "\nAlterações aplicadas, mas não foi possível salvar o registro: " + ex.Message; }
        PreviewText.Text = message;
        _messages.Add(new ChatMessage { Role = "assistant", Text = message });
        SaveHistory(); StatusText.Text = "Alterações aplicadas ao modelo. O arquivo .rvt não foi salvo automaticamente.";
    });

    private void PlanBox_TextChanged(object sender, TextChangedEventArgs e)
    { _previewJson = null; if (ApplyButton != null) ApplyButton.IsEnabled = false; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancel?.Cancel();

    private async void Capture_Click(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        if (_attachments.Count >= 4) throw new InvalidOperationException("Máximo de 4 imagens por mensagem.");
        var image = await _handler.RunAsync(app => LocalServices.Capture(CheckedDocument(app)));
        _attachments.Add(image); Tabs.SelectedIndex = 0;
        StatusText.Text = "Vista anexada. Envie quando desejar.";
    });

    private void Attach_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFileDialog { Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp", Multiselect = true };
        if (dialog.ShowDialog(this) == true) AddFiles(dialog.FileNames);
    }
    private void AddFiles(IEnumerable<string> paths)
    {
        try
        {
            foreach (var path in paths)
            {
                if (_attachments.Count >= 4) throw new InvalidOperationException("Máximo de 4 imagens por mensagem.");
                _attachments.Add(LocalServices.Image(path));
            }
            Tabs.SelectedIndex = 0;
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            if (_attachments.Count >= 4) throw new InvalidOperationException("Máximo de 4 imagens por mensagem.");
            var bitmap = Clipboard.GetImage() ?? throw new InvalidOperationException("Copie uma imagem antes de colar.");
            _attachments.Add(LocalServices.Image(bitmap, "Imagem colada.png")); Tabs.SelectedIndex = 0;
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    { if (!_busy && (sender as Button)?.Tag is ImageAttachment image) _attachments.Remove(image); }
    private void Window_Drop(object sender, DragEventArgs e)
    { if (!_busy && e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths); }
    private void Question_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
        { e.Handled = true; Send_Click(sender, e); }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V && Clipboard.ContainsImage())
        { e.Handled = true; Paste_Click(sender, e); }
    }

    private async void Family_Click(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "Família Revit|*.rfa" };
        if (dialog.ShowDialog(this) != true) return;
        await RefreshContext();
        _families.Add(dialog.FileName);
        PresentPlan(new EditPlan { Summary = "Carregar a família selecionada sem substituir famílias existentes.",
            Actions = new() { new EditAction { Operation = "load_family", FamilyPath = dialog.FileName } } }, _snapshotRevision);
    });

    private void ChooseTrace_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFileDialog { Filter = "Planta em imagem|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _traceImage = LocalServices.Image(dialog.FileName);
            _trace = null; _scaleA = _scaleB = _drawA = null; _clickMode = "";
            TraceGrid.ItemsSource = null; Redraw();
            TraceHint.Text = "Imagem carregada. Reconheça o desenho e marque os dois pontos da escala.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private async void Recognize_Click(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        if (_traceImage == null) throw new InvalidOperationException("Escolha uma imagem de planta.");
        var client = Client();
        StatusText.Text = "Reconhecendo paredes e aberturas da imagem…";
        _trace = await client.TraceAsync(_traceImage, ModelBox.Text.Trim(), _cancel!.Token);
        TraceGrid.ItemsSource = _trace.Segments;
        Redraw();
        TraceHint.Text = _trace.Summary + "\n" + string.Join(" · ", _trace.Notes);
        StatusText.Text = $"{_trace.Segments.Count} segmentos. Revise a sobreposição e calibre a escala.";
        if (ContextCheck.IsChecked == true) await RefreshContext();
    });
    private void Calibrate_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _traceImage == null) return;
        _clickMode = "scale"; _scaleA = _scaleB = null;
        TraceHint.Text = "Clique nas duas extremidades de uma medida conhecida e informe o comprimento real em mm.";
        Redraw();
    }
    private void DrawWall_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _traceImage == null) return;
        _clickMode = "wall"; _drawA = null;
        TraceHint.Text = "Clique no início e no fim do eixo da parede.";
    }
    private void TraceCanvas_Click(object sender, MouseButtonEventArgs e)
    {
        if (_busy || _traceImage == null || _clickMode.Length == 0) return;
        var p = e.GetPosition(TraceCanvas);
        var point = new PlanPoint { X = Math.Clamp(p.X / TraceCanvas.Width * 1000, 0, 1000),
            Y = Math.Clamp(p.Y / TraceCanvas.Height * 1000, 0, 1000) };
        if (_clickMode == "scale")
        {
            if (_scaleA == null) _scaleA = point;
            else { _scaleB = point; _clickMode = ""; TraceHint.Text = "Escala marcada. Confira a medida, o nível e os tipos escolhidos."; }
        }
        else
        {
            if (_drawA == null) _drawA = point;
            else
            {
                _trace ??= new ImageTrace { Summary = "Traçado manual revisado." };
                _trace.Segments.Add(new TraceSegment { Key = "w" + Guid.NewGuid().ToString("N")[..8],
                    X1 = _drawA.X, Y1 = _drawA.Y, X2 = point.X, Y2 = point.Y, Confidence = 1 });
                _drawA = null; _clickMode = "";
                TraceGrid.ItemsSource = _trace.Segments; TraceGrid.Items.Refresh();
            }
        }
        Redraw();
    }

    private void Redraw()
    {
        TraceCanvas.Children.Clear();
        if (_traceImage == null) return;
        TraceCanvas.Height = 1000d * _traceImage.Height / _traceImage.Width;
        TraceCanvas.Children.Add(new WpfImage { Source = _traceImage.Preview, Width = 1000, Height = TraceCanvas.Height, IsHitTestVisible = false });
        if (_trace != null)
            foreach (var s in _trace.Segments.Where(s => s.Include))
            {
                var color = s.Kind == "wall" ? Brushes.DodgerBlue : s.Kind == "door" ? Brushes.OrangeRed : Brushes.MediumSeaGreen;
                TraceCanvas.Children.Add(new Line { X1 = s.X1, Y1 = s.Y1 / 1000 * TraceCanvas.Height,
                    X2 = s.X2, Y2 = s.Y2 / 1000 * TraceCanvas.Height, Stroke = color, StrokeThickness = 5,
                    Opacity = .8, IsHitTestVisible = false });
                var label = new TextBlock { Text = s.Key, Foreground = color, Background = Brushes.White, FontSize = 12, IsHitTestVisible = false };
                Canvas.SetLeft(label, (s.X1 + s.X2) / 2); Canvas.SetTop(label, (s.Y1 + s.Y2) / 2000 * TraceCanvas.Height);
                TraceCanvas.Children.Add(label);
            }
        foreach (var p in new[] { _scaleA, _scaleB, _drawA }.Where(p => p != null))
        {
            var marker = new Ellipse { Width = 14, Height = 14, Fill = Brushes.Magenta, IsHitTestVisible = false };
            Canvas.SetLeft(marker, p!.X - 7); Canvas.SetTop(marker, p.Y / 1000 * TraceCanvas.Height - 7); TraceCanvas.Children.Add(marker);
        }
    }
    private void Redraw_Click(object sender, RoutedEventArgs e)
    {
        TraceGrid.CommitEdit(DataGridEditingUnit.Cell, true); TraceGrid.CommitEdit(DataGridEditingUnit.Row, true);
        Redraw();
    }
    private void TraceGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(Redraw), System.Windows.Threading.DispatcherPriority.Background);

    private async void TracePlan_Click(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        if (_trace == null || _traceImage == null || _scaleA == null || _scaleB == null)
            throw new InvalidOperationException("Reconheça/desenhe a planta e marque dois pontos de escala.");
        var calibration = new TraceCalibration { A = _scaleA, B = _scaleB, ImageWidth = _traceImage.Width, ImageHeight = _traceImage.Height,
            RealLengthMm = Number(ScaleBox.Text), OriginX = Number(OriginXBox.Text), OriginY = Number(OriginYBox.Text), RotationDegrees = Number(RotationBox.Text) };
        TraceGrid.CommitEdit(DataGridEditingUnit.Cell, true); TraceGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (_snapshot == null) await RefreshContext();
        var level = (LevelCombo.SelectedItem as LevelRecord)?.Name ?? "";
        var plan = TraceConverter.ToPlan(_trace, calibration, level, (WallCombo.SelectedItem as TypeRecord)?.Id ?? 0,
            (DoorCombo.SelectedItem as TypeRecord)?.Id ?? 0, (WindowCombo.SelectedItem as TypeRecord)?.Id ?? 0, Number(WallHeightBox.Text));
        var revision = await _handler.RunAsync(app => EditPlanExecutor.Stamp(CheckedDocument(app)));
        PresentPlan(plan, revision);
        StatusText.Text = "Planta convertida em proposta. Simule e revise antes de aplicar.";
    });

    private static double Number(string value)
    {
        if (!double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n))
            throw new InvalidOperationException("Valor numérico inválido.");
        return n;
    }
    private void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var provider = ProviderCode;
            CredentialStore.Save(provider, KeyBox.Password); KeyBox.Clear();
            Directory.CreateDirectory(LocalServices.Root);
            File.WriteAllText(System.IO.Path.Combine(LocalServices.Root, "model.txt"), ModelBox.Text.Trim());
            File.WriteAllText(System.IO.Path.Combine(LocalServices.Root, "provider.txt"), provider);
            KeyStatus.Text = $"Chave {provider} salva no Gerenciador de Credenciais do Windows.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void RemoveKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var provider = ProviderCode;
            CredentialStore.Remove(provider); KeyBox.Clear();
            KeyStatus.Text = $"Chave {provider} local removida. A variável de ambiente correspondente, se definida, continua disponível.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void Platform_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(IsGemini ? "https://aistudio.google.com/apikey" : "https://platform.openai.com/api-keys") { UseShellExecute = true });
    private void SaveHistory()
    {
        try { LocalServices.SaveHistory(_docKey, _messages); }
        catch (Exception) { StatusText.Text += " Histórico permanece somente nesta sessão."; }
    }
    private void NewChat_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _messages.Clear(); _attachments.Clear(); Welcome(); SaveHistory();
    }
    private void ExportChat_Click(object sender, RoutedEventArgs e) =>
        SaveJson("conversa-bim.json", JsonSerializer.Serialize(_messages.Select(m => new { m.Role, m.Text, Images = m.Images.Select(i => i.Name) }), EditPlanParser.Options));
    private void ExportPlan_Click(object sender, RoutedEventArgs e) => SaveJson("plano-revit.json", PlanBox.Text);
    private void ExportTrace_Click(object sender, RoutedEventArgs e)
    {
        if (_trace == null) return;
        SaveJson("tracado-planta.json", JsonSerializer.Serialize(new { trace = _trace, scaleA = _scaleA, scaleB = _scaleB,
            realLengthMm = ScaleBox.Text, originX = OriginXBox.Text, originY = OriginYBox.Text, rotation = RotationBox.Text }, EditPlanParser.Options));
    }
    private void SaveJson(string name, string json)
    {
        try
        {
            var dialog = new SaveFileDialog { FileName = name, Filter = "JSON|*.json" };
            if (dialog.ShowDialog(this) == true) { File.WriteAllText(dialog.FileName, json); StatusText.Text = "Arquivo exportado."; }
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private async void ImportPlan_Click(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "Plano JSON|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        if (new FileInfo(dialog.FileName).Length > 2_000_000) throw new InvalidOperationException("Plano grande demais.");
        var plan = EditPlanParser.Parse(File.ReadAllText(dialog.FileName));
        await RefreshContext();
        PresentPlan(plan, _snapshotRevision);
    });
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_busy) { _cancel?.Cancel(); e.Cancel = true; StatusText.Text = "Cancelando. Aguarde terminar e feche novamente."; return; }
        SaveHistory();
    }
}
