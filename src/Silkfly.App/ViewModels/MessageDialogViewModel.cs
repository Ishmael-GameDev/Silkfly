using CommunityToolkit.Mvvm.ComponentModel;

namespace Silkfly.App.ViewModels;

public sealed partial class DialogItem(string text, bool checkable = false, bool isChecked = true) : ObservableObject
{
    public string Text { get; } = text;

    public bool Checkable { get; } = checkable;

    public bool NotCheckable => !Checkable;

    [ObservableProperty]
    private bool _isChecked = isChecked;
}

public sealed partial class MessageDialogViewModel : ObservableObject
{
    public IReadOnlyList<DialogItem> Items { get; init; } = [];

    public bool HasItems => Items.Count > 0;

    public string? Note { get; init; }

    public int InputMaxLength { get; init; }

    public bool HasNote => Note is not null;

    private readonly Func<string, string?>? _validate;

    public MessageDialogViewModel(
        string title,
        string? message,
        string confirmText,
        string? cancelText,
        bool isDanger = false,
        string? inputLabel = null,
        string? initialInput = null,
        Func<string, string?>? validate = null,
        string? details = null)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
        IsDanger = isDanger;
        InputLabel = inputLabel;
        _input = initialInput ?? string.Empty;
        _validate = validate;
        Details = details;
        Validate();
    }

    public string Title { get; }

    public string? Message { get; }

    public string ConfirmText { get; }

    public string? CancelText { get; }

    public bool HasCancel => CancelText is not null;

    public bool IsDanger { get; }

    public string? InputLabel { get; }

    public bool HasInput => InputLabel is not null;

    public string? Details { get; }

    public bool HasDetails => Details is not null;

    public bool ShowOpenLog { get; init; } = true;

    public bool HasOpenLog => HasDetails && ShowOpenLog;

    [ObservableProperty]
    private string _input;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _canConfirm = true;

    partial void OnInputChanged(string value) => Validate();

    private void Validate()
    {
        if (!HasInput)
            return;
        Error = string.IsNullOrWhiteSpace(Input) ? null : _validate?.Invoke(Input);
        CanConfirm = Error is null && !string.IsNullOrWhiteSpace(Input);
    }
}
