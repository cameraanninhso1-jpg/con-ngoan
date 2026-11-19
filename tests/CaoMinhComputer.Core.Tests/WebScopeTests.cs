using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;

namespace CaoMinhComputer.Core.Tests;

public class WebScopeTests
{
    private const string Child = "S-1-5-21-1-1-1-1001";
    private const string Parent = "S-1-5-21-1-1-1-1000";
    private static readonly string[] Children = [Child];

    private static UserSession S(string sid, string name, bool active, bool locked = false) => new(1, name, sid, active, locked);

    [Fact]
    public void Blocks_when_child_session_is_active() =>
        Assert.True(WebScopePolicy.Decide(WebBlockScope.Children, Children, [S(Parent, "bo", false), S(Child, "con", true)]).Block);

    [Fact]
    public void Does_not_block_when_parent_session_is_active()
    {
        var d = WebScopePolicy.Decide(WebBlockScope.Children, Children, [S(Parent, "bo", true), S(Child, "con", false)]);
        Assert.False(d.Block);
        Assert.Contains("bo", d.Reason);
    }

    [Fact]
    public void Blocks_when_child_session_is_active_even_if_locked() =>
        Assert.True(WebScopePolicy.Decide(WebBlockScope.Children, Children, [S(Child, "con", true, locked: true)]).Block);

    [Fact]
    public void Blocks_when_nobody_is_logged_in_or_unknown_user()
    {
        Assert.True(WebScopePolicy.Decide(WebBlockScope.Children, Children, []).Block);
        Assert.True(WebScopePolicy.Decide(WebBlockScope.Children, Children, [S(Parent, "bo", false)]).Block);
        Assert.True(WebScopePolicy.Decide(WebBlockScope.Children, Children, [new UserSession(2, "?", null, true, false)]).Block);
    }

    [Fact]
    public void All_scope_or_no_child_configured_always_blocks()
    {
        Assert.True(WebScopePolicy.Decide(WebBlockScope.All, Children, [S(Parent, "bo", true)]).Block);
        Assert.True(WebScopePolicy.Decide(WebBlockScope.Children, [], [S(Parent, "bo", true)]).Block);
    }

    [Fact]
    public void Sid_comparison_is_case_insensitive() =>
        Assert.True(WebScopePolicy.Decide(WebBlockScope.Children, [Child.ToLowerInvariant()], [S(Child, "con", true)]).Block);
}
