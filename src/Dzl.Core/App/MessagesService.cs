using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>Facade for editing the active mission's <c>db/messages.xml</c> — the server message scheduler
/// (broadcasts, on-connect, scheduled restart/shutdown). Not a CE file; lives in the Server module.</summary>
public sealed class MessagesService : CeFileService
{
    public MessagesService(string configPath) : base(configPath) { }

    protected override string RelativePath => System.IO.Path.Combine("db", "messages.xml");
    protected override string? SeedRootName => "messages";

    public string? MessagesPath() => FilePath();

    public List<ServerMessage> Load() => LoadList(MessagesXml.Parse);

    public (bool ok, string msg) Add(ServerMessage message) =>
        Edit(doc => MessagesXml.Add(doc, message), "added message", "could not add message");

    public (bool ok, string msg) RemoveAt(int index) =>
        Edit(doc => MessagesXml.RemoveAt(doc, index), $"removed message #{index}", "message not removed");

    public (bool ok, string msg) SetAt(int index, ServerMessage message) =>
        Edit(doc => MessagesXml.SetAt(doc, index, message), $"updated message #{index}", "message not updated");
}
