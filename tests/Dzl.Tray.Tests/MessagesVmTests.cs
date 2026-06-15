using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="MessagesVm"/> (Server "Messages" tab = db/messages.xml): list, add/remove,
/// edit a message + numeric validation.</summary>
public class MessagesVmTests
{
    private const string Fixture = """
        <messages>
          <message><delay>2</delay><onConnect>1</onConnect><text>Welcome to #name</text></message>
        </messages>
        """;

    private static MessagesVm Load(out string cfg)
    {
        cfg = CeScaffold.Mission(("db/messages.xml", Fixture));
        var vm = new MessagesVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    private static MessagesVm Reloaded(string cfg)
    {
        var vm = new MessagesVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    [Fact]
    public void Reload_lists_messages()
    {
        var vm = Load(out _);
        vm.HasMessages.Should().BeTrue();
        vm.Messages.Should().ContainSingle().Which.Text.Should().Be("Welcome to #name");
    }

    [Fact]
    public void AddMessage_appends_an_empty_one()
    {
        var vm = Load(out var cfg);
        vm.AddMessage();
        Reloaded(cfg).Messages.Should().HaveCount(2);
    }

    [Fact]
    public void Editing_a_message_persists()
    {
        var vm = Load(out var cfg);
        var m = vm.Messages.Single();
        m.Text = "Changed";
        m.DeadlineText = "300";
        m.Commit();

        var saved = Reloaded(cfg).Messages.Single();
        saved.Text.Should().Be("Changed");
        saved.DeadlineText.Should().Be("300");
    }

    [Fact]
    public void Editing_rejects_a_non_numeric_timer()
    {
        var vm = Load(out var cfg);
        var m = vm.Messages.Single();
        m.DelayText = "soon";
        m.Commit();
        vm.Status.Should().StartWith("✗");
        Reloaded(cfg).Messages.Single().Text.Should().Be("Welcome to #name", "the bad edit was not persisted");
    }

    [Fact]
    public void RemoveMessage_deletes_it()
    {
        var vm = Load(out var cfg);
        vm.RemoveMessage(vm.Messages.Single());   // confirm => true
        Reloaded(cfg).Messages.Should().BeEmpty();
    }
}
