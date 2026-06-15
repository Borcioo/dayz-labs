using Dzl.Core.Economy;
using FluentAssertions;

/// <summary>Pure parse + in-place edit of db/messages.xml (server message scheduler).</summary>
public class MessagesXmlTests
{
    private const string Xml = """
        <messages>
          <message><delay>2</delay><repeat>0</repeat><deadline>0</deadline><onConnect>1</onConnect><shutdown>0</shutdown><text>Welcome to #name</text></message>
          <message><delay>0</delay><repeat>0</repeat><deadline>600</deadline><onconnect>0</onconnect><shutdown>1</shutdown><text>#name restarts in #tmin min</text></message>
        </messages>
        """;

    [Fact]
    public void Parse_reads_messages_case_insensitive_flags()
    {
        var m = MessagesXml.Parse(Xml);
        m.Should().HaveCount(2);
        m[0].OnConnect.Should().BeTrue();
        m[0].Text.Should().Be("Welcome to #name");
        m[1].Deadline.Should().Be(600);
        m[1].Shutdown.Should().BeTrue();
        m[1].OnConnect.Should().BeFalse("<onconnect>0</onconnect> read case-insensitively");
    }

    [Fact]
    public void Add_RemoveAt_SetAt_by_index()
    {
        var doc = MessagesXml.ParseDoc(Xml);
        MessagesXml.Add(doc, new ServerMessage(5, 15, 0, false, false, "hello")).Should().BeTrue();
        MessagesXml.Parse(MessagesXml.ToXml(doc)).Should().HaveCount(3);

        MessagesXml.SetAt(doc, 0, new ServerMessage(9, 0, 0, false, false, "edited")).Should().BeTrue();
        MessagesXml.Parse(MessagesXml.ToXml(doc))[0].Text.Should().Be("edited");

        MessagesXml.RemoveAt(doc, 1).Should().BeTrue();
        MessagesXml.Parse(MessagesXml.ToXml(doc)).Should().HaveCount(2);
    }

    [Fact]
    public void Parse_is_empty_on_malformed()
    {
        MessagesXml.Parse("garbage").Should().BeEmpty();
    }
}
