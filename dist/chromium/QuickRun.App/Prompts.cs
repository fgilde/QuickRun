using QuickRun.Core.Config;
using Spectre.Console;

namespace QuickRun.App;

public static class Prompts
{
    /// <summary>Asks for the values a config declares but the command line did not supply.</summary>
    public static IReadOnlyDictionary<string, string?> Collect(
        IReadOnlyList<InputDef> defs, IReadOnlyDictionary<string, string?> provided)
    {
        var values = new Dictionary<string, string?>(provided);

        foreach (var def in defs)
        {
            if (values.TryGetValue(def.Id, out var existing) && !string.IsNullOrWhiteSpace(existing)) continue;

            // Never block on a prompt nobody can answer.
            if (!AnsiConsole.Profile.Capabilities.Interactive)
            {
                if (!def.Required) continue;
                return values;
            }

            values[def.Id] = def.Type switch
            {
                InputType.Bool => AnsiConsole
                    .Confirm(Label(def), bool.TryParse(def.Default, out var flag) && flag)
                    .ToString(),
                InputType.Select => AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title(Label(def))
                        .AddChoices(def.Options.Select(o => o.Value))),
                InputType.Password => Ask(def, secret: true),
                _ => Ask(def, secret: false),
            };
        }

        return values;
    }

    private static string Label(InputDef def)
    {
        var label = Markup.Escape(def.Label ?? def.Id);
        return string.IsNullOrWhiteSpace(def.Description)
            ? label
            : $"{label} [grey]({Markup.Escape(def.Description)})[/]";
    }

    private static string Ask(InputDef def, bool secret)
    {
        var prompt = new TextPrompt<string>(Label(def)).AllowEmpty();
        if (secret) prompt = prompt.Secret();
        if (!string.IsNullOrWhiteSpace(def.Default)) prompt = prompt.DefaultValue(def.Default!);
        return AnsiConsole.Prompt(prompt);
    }
}
