using Avalonia.Controls;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;

namespace StarBreaker.Screens;

public partial class TextPreviewView : UserControl
{
    private TextEditor? _textEditor;
    private TextMate.Installation? _textMateInstallation;
    private RegistryOptions? _registryOptions;
    private string? _currentLanguage;
    
    public TextPreviewView()
    {
        InitializeComponent();
        _textEditor = this.FindControl<TextEditor>("TextEditor");
        
        // Initialize TextMate for syntax highlighting
        if (_textEditor != null)
        {
            _registryOptions = new RegistryOptions(ThemeName.DarkPlus);
            _textMateInstallation = _textEditor.InstallTextMate(_registryOptions);
        }
        
        // Subscribe to DataContext changes
        DataContextChanged += OnDataContextChanged;
    }
    
    private CancellationTokenSource? _debounceCts;

    private async void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_textEditor != null && DataContext is TextPreviewViewModel vm)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            try
            {
                await Task.Delay(100, token);
                
                if (token.IsCancellationRequested) return;
                if (_textMateInstallation != null && _registryOptions != null)
                {
                    var language = DetectLanguageFromExtension(vm.FileExtension) ?? DetectLanguageFromContent(vm.Text);
                    
                    if (language != _currentLanguage)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(language))
                            {
                                var scope = _registryOptions.GetScopeByLanguageId(language);
                                if (scope != null)
                                {
                                    _textMateInstallation.SetGrammar(scope);
                                    _currentLanguage = language;
                                }
                            }
                            else
                            {
                                _textMateInstallation.SetGrammar(null);
                                _currentLanguage = null;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"TextMate error: {ex.Message}");
                            try
                            {
                                _textMateInstallation.SetGrammar(null);
                            }
                            catch { }
                            _currentLanguage = null;
                        }
                    }
                }
                
                if (!token.IsCancellationRequested)
                {
                    _textEditor.Text = vm.Text ?? string.Empty;
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnDataContextChanged: {ex.Message}");
            }
        }
    }
    
    private static string? DetectLanguageFromExtension(string? fileExtension)
    {
        if (string.IsNullOrEmpty(fileExtension))
            return null;
            
        return fileExtension.ToLowerInvariant() switch
        {
            ".xml" => "xml",
            ".json" => "json",
            ".cfg" => "ini",
            ".ini" => "ini",
            ".txt" => "plaintext",
            ".mtl" => "xml",
            ".eco" => "xml",
            ".ale" => "json",
            ".lua" => "lua",
            ".js" => "javascript",
            ".hlsl" => "hlsl",
            ".fx" => "hlsl",
            ".shader" => "hlsl",
            ".cryxml" => "xml",
            _ => null
        };
    }
    
    private static bool IsXmlContent(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
            
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("<", StringComparison.OrdinalIgnoreCase);
    }
    
    private static string? DetectLanguageFromContent(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
            
        var trimmed = text.TrimStart();
        
        // XML detection
        if (IsXmlContent(text))
            return "xml";
        
        // JSON detection
        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            return "json";
            
        // Check for common file type indicators in the content
        if (trimmed.Contains("shader") || trimmed.Contains("technique") || trimmed.Contains("Technique"))
            return "hlsl"; // For shader/material files
            
        return null; // No specific language detected
    }
}