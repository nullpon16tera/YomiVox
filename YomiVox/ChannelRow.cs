using System.ComponentModel;

namespace YomiVox;

/// <summary>接続チャンネル一覧の1行（先頭が入力フィールド）。</summary>
public sealed class ChannelRow : INotifyPropertyChanged
{
    private readonly Action? _onTextChanged;
    private string _text = "";
    private bool _isConnected;
    private bool _isBusy;

    public ChannelRow(Action? onTextChanged = null) => _onTextChanged = onTextChanged;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            _onTextChanged?.Invoke();
        }
    }

    /// <summary>この行のチャットに参加済みか。</summary>
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected == value) return;
            _isConnected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
            NotifyConnectUi();
        }
    }

    /// <summary>接続・切断の非同期処理中。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            NotifyConnectUi();
        }
    }

    private void NotifyConnectUi()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChannelUiEditable)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanDisconnect)));
    }

    /// <summary>参加中は URL を編集しない（誤操作防止）。</summary>
    public bool IsChannelUiEditable => !IsConnected && !IsBusy;

    /// <summary>この行のチャットから抜けられるとき true。</summary>
    public bool CanDisconnect => IsConnected && !IsBusy;

    public event PropertyChangedEventHandler? PropertyChanged;
}
