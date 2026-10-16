using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace ChatGPTRevit;

public sealed class ChatMessage : INotifyPropertyChanged
{
    public string Role { get; set; } = "user";
    private string _text = "";
    public string Text { get => _text; set { _text = value; Notify(); } }
    public List<ImageAttachment> Images { get; set; } = new();
    public bool Complete { get; set; } = true;
    [JsonIgnore] public string Label => Role == "user" ? "VOCÊ" : "BIM COPILOTO";
    [JsonIgnore] public Brush Bubble => new SolidColorBrush(Role == "user" ? Color.FromRgb(224, 237, 255) : Colors.White);
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class ImageAttachment
{
    public string Name { get; set; } = "";
    public string DataUrl { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    [JsonIgnore] public ImageSource? Preview { get; set; }
}
