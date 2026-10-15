using CommunityToolkit.Mvvm.ComponentModel;
using Silkfly.App.Localization;
using Silkfly.Core.Infrastructure;

namespace Silkfly.App.ViewModels;

public sealed partial class MarkdownDialogViewModel(
    string title,
    string subtitle,
    Func<CancellationToken, Task<string?>> load,
    string missingWhat,
    ILocalizer localizer) : ObservableObject
{
    public string Title { get; } = title;

    public string Subtitle { get; } = subtitle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMarkdown))]
    private string? _markdown;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _message;

    public bool HasMarkdown => !string.IsNullOrEmpty(Markdown);

    public async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            Markdown = await load(ct);
            if (Markdown is null)
                Message = localizer.Format("Readme_Empty", missingWhat);
        }
        catch (OperationCanceledException)
        {
        }
        catch (SilkflyException ex)
        {
            Message = localizer.Format("Readme_Error", localizer.Format(ex.MessageKey, [.. ex.Args]));
        }
        finally
        {
            IsLoading = false;
        }
    }
}
