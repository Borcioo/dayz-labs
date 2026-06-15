using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="MessagesEditor"/> control (Server "Messages" tab): edits db/messages.xml — the server
/// message scheduler (broadcasts, on-connect welcome, scheduled restart/shutdown countdowns). A list of message
/// cards (text + delay/repeat/deadline + on-connect/shutdown). Messages have no key, so they're addressed by
/// index. Per-tab undo/redo + status from <see cref="RawXmlEditorVm"/>.
/// </summary>
public sealed partial class MessagesVm : RawXmlEditorVm
{
    private readonly MessagesService _svc;
    private readonly Func<string, bool> _confirm;
    private bool _suspend;

    public MessagesVm(string configPath, Func<string, bool> confirm)
        : this(new MessagesService(configPath), confirm) { }

    private MessagesVm(MessagesService svc, Func<string, bool> confirm)
        : base(svc.ReadRaw, svc.WriteRaw, svc.MessagesPath,
               "(no db/messages.xml — pick/scaffold a server mission)", confirm)
    {
        _svc = svc;
        _confirm = confirm;
    }

    public ObservableCollection<ServerMessageVm> Messages { get; } = new();
    public bool HasMessages => Messages.Count > 0;

    protected override void ReloadView()
    {
        _suspend = true;
        try
        {
            foreach (var m in Messages) m.Edited -= OnMessageEdited;
            Messages.Clear();
            var i = 0;
            foreach (var m in _svc.Load())
            {
                var vm = new ServerMessageVm(i++, m);
                vm.Edited += OnMessageEdited;
                Messages.Add(vm);
            }
        }
        finally { _suspend = false; }
        OnPropertyChanged(nameof(HasMessages));
    }

    private static bool TryInt(string raw, out int v) =>
        int.TryParse(string.IsNullOrWhiteSpace(raw) ? "0" : raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private void OnMessageEdited(ServerMessageVm m)
    {
        if (_suspend) return;
        if (!TryInt(m.DelayText, out var delay) || !TryInt(m.RepeatText, out var repeat) || !TryInt(m.DeadlineText, out var deadline))
        { Status = "✗ delay / repeat / deadline must be whole numbers"; ReloadView(); return; }
        PushUndo();
        Report(_svc.SetAt(m.Index, new ServerMessage(delay, repeat, deadline, m.OnConnect, m.Shutdown, m.Text ?? "")));
    }

    public void AddMessage()
    {
        PushUndo();
        if (Report(_svc.Add(new ServerMessage(0, 0, 0, false, false, "")))) ReloadView();
    }

    public void RemoveMessage(ServerMessageVm? m)
    {
        if (m is null) { Status = "✗ select a message to remove"; return; }
        if (!_confirm("Remove this scheduled message?")) return;
        PushUndo();
        if (Report(_svc.RemoveAt(m.Index))) ReloadView();
    }
}

/// <summary>One editable scheduled message. Toggles persist immediately; text/number fields on commit.</summary>
public sealed partial class ServerMessageVm : ObservableObject
{
    public ServerMessageVm(int index, ServerMessage m)
    {
        Index = index;
        _text = m.Text;
        _delayText = m.Delay.ToString(CultureInfo.InvariantCulture);
        _repeatText = m.Repeat.ToString(CultureInfo.InvariantCulture);
        _deadlineText = m.Deadline.ToString(CultureInfo.InvariantCulture);
        _onConnect = m.OnConnect;
        _shutdown = m.Shutdown;
    }

    public int Index { get; }
    [ObservableProperty] private string _text;
    [ObservableProperty] private string _delayText;
    [ObservableProperty] private string _repeatText;
    [ObservableProperty] private string _deadlineText;
    [ObservableProperty] private bool _onConnect;
    [ObservableProperty] private bool _shutdown;

    partial void OnOnConnectChanged(bool value) => Edited?.Invoke(this);
    partial void OnShutdownChanged(bool value) => Edited?.Invoke(this);

    public void Commit() => Edited?.Invoke(this);
    public event Action<ServerMessageVm>? Edited;
}
