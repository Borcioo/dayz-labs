using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>One scheduled server message: an optional delay/repeat/deadline (seconds/minutes per the engine),
/// on-connect + shutdown flags, and the broadcast text (placeholders like <c>#name</c>/<c>#tmin</c> allowed).</summary>
public sealed record ServerMessage(int Delay, int Repeat, int Deadline, bool OnConnect, bool Shutdown, string Text);

/// <summary>
/// Pure parse + in-place edit of a mission's <c>db/messages.xml</c> — the SERVER message scheduler (periodic
/// broadcasts, on-connect welcome, scheduled restart/shutdown countdowns). NOT a Central Economy file; it lives
/// in the Server module. Messages have no key, so edits address them by index. <see cref="Parse"/> never throws.
/// </summary>
public static class MessagesXml
{
    public static List<ServerMessage> Parse(string xml)
    {
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null) return new List<ServerMessage>();
            return root.Elements("message").Select(m => new ServerMessage(
                IntChild(m, "delay"), IntChild(m, "repeat"), IntChild(m, "deadline"),
                BoolChild(m, "onconnect"), BoolChild(m, "shutdown"), TextChild(m, "text"))).ToList();
        }
        catch { return new List<ServerMessage>(); }
    }

    private static XElement? Child(XElement m, string name) =>
        m.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
    private static int IntChild(XElement m, string name) => CeNum.Int(Child(m, name)?.Value, 0);
    private static bool BoolChild(XElement m, string name) => CeNum.Bool01(Child(m, name)?.Value ?? "0");
    private static string TextChild(XElement m, string name) => (Child(m, name)?.Value ?? "").Trim();

    public static XDocument ParseDoc(string xml) => CeXml.ParseDoc(xml);

    public static bool Add(XDocument doc, ServerMessage msg)
    {
        if (doc.Root is not { } root) return false;
        root.Add(Build(msg));
        return true;
    }

    public static bool RemoveAt(XDocument doc, int index)
    {
        var list = doc.Root?.Elements("message").ToList();
        if (list is null || index < 0 || index >= list.Count) return false;
        list[index].Remove();
        return true;
    }

    public static bool SetAt(XDocument doc, int index, ServerMessage msg)
    {
        var list = doc.Root?.Elements("message").ToList();
        if (list is null || index < 0 || index >= list.Count) return false;
        var m = list[index];
        Set(m, "delay", msg.Delay); Set(m, "repeat", msg.Repeat); Set(m, "deadline", msg.Deadline);
        Set(m, "onConnect", msg.OnConnect ? 1 : 0); Set(m, "shutdown", msg.Shutdown ? 1 : 0);
        SetText(m, "text", msg.Text);
        return true;
    }

    private static void Set(XElement m, string name, int value)
    {
        var el = Child(m, name);
        if (el is null) m.Add(new XElement(name, value)); else el.Value = value.ToString();
    }

    private static void SetText(XElement m, string name, string value)
    {
        var el = Child(m, name);
        if (el is null) m.Add(new XElement(name, value)); else el.Value = value;
    }

    private static XElement Build(ServerMessage msg) => new("message",
        new XElement("delay", msg.Delay), new XElement("repeat", msg.Repeat), new XElement("deadline", msg.Deadline),
        new XElement("onConnect", msg.OnConnect ? 1 : 0), new XElement("shutdown", msg.Shutdown ? 1 : 0),
        new XElement("text", msg.Text));

    public static string ToXml(XDocument doc) => CeXml.Serialize(doc);
}
