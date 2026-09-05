using QuickRun.App.Daemon;

namespace QuickRun.App.Tests;

public class DashboardTests
{
    [Fact]
    public void The_page_carries_its_token_and_port()
    {
        var dashboard = new Dashboard();
        var html = dashboard.Render(9876);

        Assert.Contains(dashboard.Token, html);
        Assert.Contains("9876", html);
        Assert.DoesNotContain("{{TOKEN}}", html);
        Assert.DoesNotContain("{{PORT}}", html);
        Assert.DoesNotContain("{{VERSION}}", html);
    }

    [Fact]
    public void The_token_is_long_and_hex()
    {
        var token = new Dashboard().Token;

        Assert.Equal(48, token.Length);
        Assert.Matches("^[0-9a-f]+$", token);
    }

    [Fact]
    public void Each_dashboard_gets_its_own_token()
        => Assert.NotEqual(new Dashboard().Token, new Dashboard().Token);

    /// <summary>
    /// The reason this token exists: CORS stops another origin reading a response but not sending
    /// the request, so a page could otherwise POST to the dashboard's endpoints.
    /// </summary>
    [Fact]
    public void Only_the_page_own_token_is_accepted()
    {
        var dashboard = new Dashboard();

        Assert.True(dashboard.Authorized(dashboard.Token));
        Assert.False(dashboard.Authorized(null));
        Assert.False(dashboard.Authorized(""));
        Assert.False(dashboard.Authorized("wrong"));
        Assert.False(dashboard.Authorized(dashboard.Token + "a"));
        Assert.False(dashboard.Authorized(new Dashboard().Token));
    }

    [Fact]
    public void The_page_renders_the_sections_a_user_needs()
    {
        var html = new Dashboard().Render(9876);

        foreach (var expected in new[] { "Runs", "Workspaces", "Browser extension", "About" })
            Assert.Contains(expected, html);
    }

    /// <summary>
    /// Pairing is gone, so the page must not still promise it - and what replaced it, registering
    /// the scheme the extension falls back to, has to be reachable from here.
    /// </summary>
    [Fact]
    public void The_page_offers_the_protocol_registration_and_no_pairing()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("quickrun://", html);
        Assert.Contains("schemeButton", html);
        Assert.DoesNotContain("pairing window", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://quickrun.org/download", html);
    }

    /// <summary>
    /// A stopped run has to read as stopped, and what the repository says it is belongs next to it.
    /// </summary>
    [Fact]
    public void The_page_renders_a_description_and_a_stopped_run()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("run.description", html);
        Assert.Contains("cancelled: ['stopped', 'warn']", html);
        Assert.Contains("Stopping", html);
    }

    /// <summary>
    /// The form a config declares has to be on the page, and built as text: a label or an option
    /// comes out of someone's repository.
    /// </summary>
    [Fact]
    public void The_page_can_ask_for_a_configs_inputs()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("function inputForm(run)", html);
        Assert.Contains("/inputs`", html);
        Assert.Contains("awaitingInput", html);

        // A secret must not be put back into a field.
        Assert.Contains("def.type === 'password' ? '' :", html);
        Assert.DoesNotContain("innerHTML = def.label", html);
    }

    /// <summary>
    /// A run says what it was answered with, for as long as its card exists.
    /// <para>
    /// The form is gone once a run starts, and with it went the only record of which values it had
    /// been given - so two runs of the same repository looked identical while doing different
    /// things. The card keeps them, and keeps them readable: a choice is shown by its option's
    /// label rather than its value, because a value is often a flag and the label is the sentence
    /// somebody picked.
    /// </para>
    /// </summary>
    [Fact]
    public void The_page_says_what_a_run_was_answered_with()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("function chosenValues(run)", html);
        Assert.Contains("function valueChips(run)", html);
        Assert.Contains("[data-values]", html);

        // A choice reads as its label.
        Assert.Contains("option?.label", html);

        // And a secret never reads as anything. Both halves matter: the word that is shown, and the
        // absence of any path from the value to the page.
        Assert.Contains("text: 'hidden', secret: true", html);
        Assert.DoesNotContain("textContent = values[def.id]", html);

        // Built with DOM calls, because a label and a value are both text out of somebody's config.
        Assert.Contains("value.textContent = text", html);
        Assert.DoesNotContain("innerHTML = text", html);
    }

    /// <summary>
    /// The page can be a window opened on one plan, rather than the whole interface.
    /// <para>
    /// A repository handed over by the website or a quickrun:// link used to land in the big window
    /// with a panel somewhere in it, while the browser extension opened a window showing that plan
    /// and nothing else. Same page, same controls, same log - the difference is what is taken away,
    /// which is why this is a stylesheet and a flag rather than a second copy of the window living
    /// somewhere else.
    /// </para>
    /// </summary>
    [Fact]
    public void The_page_can_be_a_window_opened_on_one_plan()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("shell === 'confirm'", html);
        Assert.Contains("body[data-shell=\"confirm\"] > nav", html);
        Assert.Contains("body[data-shell=\"confirm\"] #runList", html);

        // The plan is what is left, so it must not be among what the shell hides.
        Assert.DoesNotContain("body[data-shell=\"confirm\"] #planPanel { display: none; }", html);

        // And once it starts, the run stays in that window - its card, its log and its Stop.
        Assert.Contains("panel === builder.plan || confirming()", html);
    }

    /// <summary>Per-task state and the address each task reports, as a link.</summary>
    [Fact]
    public void The_page_shows_what_each_task_is_doing()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("function taskRows(run)", html);
        Assert.Contains("run.tasks", html);
        Assert.Contains("addressLink(task.url)", html);
    }

    /// <summary>A run can be started from this page, which is what makes the extension optional.</summary>
    [Fact]
    public void The_page_can_start_a_run_itself()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("repoInput", html);
        Assert.Contains("refSelect", html);
        Assert.Contains("/api/dashboard/branches", html);
        Assert.Contains("/api/dashboard/run", html);

        // The gate: the page confirms separately, it does not start on prepare.
        Assert.Contains("/api/dashboard/runs/${run.id}/confirm", html);
    }

    /// <summary>
    /// Downloads always go through the project's own page, never a GitHub release page or a
    /// hand-written asset link that would rot when the assets change.
    /// </summary>
    [Fact]
    public void The_page_links_no_release_assets_directly()
    {
        var html = new Dashboard().Render(9876);

        Assert.DoesNotContain("releases/latest/download", html);
        Assert.DoesNotContain("releases/tag/", html);
    }

    /// <summary>
    /// A changed value must not cost a second click, and applying it must not rebuild the form: the
    /// field being typed in has to keep the cursor.
    /// </summary>
    [Fact]
    public void Changing_a_value_applies_itself_and_leaves_the_form_alone()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("settle:", html);
        Assert.Contains("form.settle();", html);
        Assert.Contains("[data-commands]", html);

        // The whole panel is no longer redrawn while someone is typing in it.
        Assert.DoesNotContain("renderPlan(updated, panel)", html);
    }

    /// <summary>A finished run can be taken off the list, and nothing else can.</summary>
    [Fact]
    public void The_page_can_remove_a_finished_run()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("/forget`", html);
        Assert.Contains("Remove", html);
    }

    /// <summary>
    /// The settings a program is expected to have: coming back after a reboot, being a command in a
    /// terminal, and saying what that command can do.
    /// </summary>
    [Fact]
    public void The_page_offers_the_system_settings_and_the_cli()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("data-tab=\"settings\"", html);
        Assert.Contains("autostartToggle", html);
        Assert.Contains("pathToggle", html);
        Assert.Contains("/api/dashboard/settings", html);
        Assert.Contains("quickrun run acme/app", html);
    }

    /// <summary>
    /// A Stop rendered while nothing was running yet stayed disabled for the rest of the run, which
    /// made a run unstoppable from the page. The buttons follow the live task count as well.
    /// </summary>
    [Fact]
    public void The_buttons_follow_whether_anything_is_still_running()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("${run.state}:${(run.liveTasks ?? 0) > 0}", html);
        Assert.Contains("seen.actions", html);
    }

    /// <summary>A test run in the config builder is watched and stopped where it was started.</summary>
    [Fact]
    public void A_builder_test_run_stays_in_the_builder()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("function mountRun(", html);
        Assert.Contains("mountRun(run.id, panel)", html);
    }
}
