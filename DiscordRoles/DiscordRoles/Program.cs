using System.Collections.Concurrent;
using System.Text.Json;
using Discord;
using Discord.Net;
using Discord.WebSocket;

// Env vars.
string token = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ??
               throw new Exception("environment variable DISCORD_TOKEN not found");

// Discord client.
DiscordSocketClient client = new (
    new DiscordSocketConfig
    {
        // Intent for receiving slash commands and component interactions.
        GatewayIntents = GatewayIntents.Guilds
    }
);

// A dict containing the current component a user is working on.
ConcurrentDictionary<(ulong, ulong), Dictionary<string, List<SocketRole>>> dict = new ();

client.Log += Log;
client.Ready += Ready;
client.SlashCommandExecuted += SlashCommandExecuted;
client.SelectMenuExecuted += SelectMenuExecuted;
client.ButtonExecuted += ButtonExecuted;

await client.LoginAsync(TokenType.Bot, token);

await client.StartAsync();

await Task.Delay(-1);

static Task Log(LogMessage msg)
{
    Console.WriteLine(msg.ToString());
    return Task.CompletedTask;
}

async Task Ready()
{
    try
    {
        SlashCommandProperties command = new SlashCommandBuilder().WithName("roleselector")
            .WithDescription("Role Selector")
            .AddOption(
                new SlashCommandOptionBuilder().WithName("create")
                    .WithDescription("Create a role component")
                    .WithType(ApplicationCommandOptionType.SubCommand)
            )
            .AddOption(
                new SlashCommandOptionBuilder().WithName("delete")
                    .WithDescription("Delete a role component")
                    .WithType(ApplicationCommandOptionType.SubCommand)
            )
            .AddOption(
                Enumerable.Range(1, 20)
                    .Aggregate(
                        new SlashCommandOptionBuilder().WithName("add")
                            .WithDescription("Add a select select component")
                            .AddOption("name", ApplicationCommandOptionType.String, "name", true)
                            .WithType(ApplicationCommandOptionType.SubCommand),
                        (builder, number) => builder.AddOption(
                            $"role-{number}",
                            ApplicationCommandOptionType.Role,
                            $"role {number}"
                        )
                    )
            )
            .AddOption(
                new SlashCommandOptionBuilder().WithName("finish")
                    .WithDescription("Finish a role component")
                    .AddOption("message", ApplicationCommandOptionType.String, "The id of the message to replace.")
                    .WithType(ApplicationCommandOptionType.SubCommand)
            )
            .Build();

        await client.BulkOverwriteGlobalApplicationCommandsAsync(new ApplicationCommandProperties[] { command });
    }
    catch (HttpException exception)
    {
        await Console.Error.WriteLineAsync(JsonSerializer.Serialize(exception.Errors));
    }
}

async Task SlashCommandExecuted(SocketSlashCommand arg)
{
    try
    {
        (ulong, ulong) key = ((arg.Channel as IGuildChannel ?? throw new InvalidOperationException()).GuildId, arg.User.Id);

        switch (arg.Data.Options.First().Name)
        {
            case "create":
            {
                bool added = dict.TryAdd(key, new Dictionary<string, List<SocketRole>>());
                if (added)
                    await arg.RespondAsync("Created.", ephemeral: true);
                else
                    await arg.RespondAsync("Already exists.", ephemeral: true);

                break;
            }
            case "delete":
            {
                bool removed = dict.TryRemove(key, out _);
                if (removed)
                    await arg.RespondAsync("Deleted", ephemeral: true);
                else
                    await arg.RespondAsync("Does not exist.", ephemeral: true);
                break;
            }
            case "add":
            {
                bool got = dict.TryGetValue(key, out Dictionary<string, List<SocketRole>>? value);
                if (!got)
                {
                    await arg.RespondAsync("Does not exist.", ephemeral: true);
                    break;
                }

                if (value!.Count >= 4)
                {
                    await arg.RespondAsync("Already max length.", ephemeral: true);
                    break;
                }

                List<SocketSlashCommandDataOption> options = arg.Data.Options.Single().Options.ToList();

                string name = (string)options.Single(option => option.Name == "name");

                if (value.ContainsKey(name))
                {
                    await arg.RespondAsync($"Name already exists: {name}", ephemeral: true);
                    break;
                }

                List<SocketRole> roles = options.Select(option => option.Value).OfType<SocketRole>().ToList();

                List<IGrouping<string, SocketRole>> duplicates =
                    roles.GroupBy(role => role.Name).Where(group => group.Count() > 1).ToList();

                if (duplicates.Any())
                {
                    await arg.RespondAsync(
                        $"Can't have duplicate values: {string.Join(' ', duplicates.Select(duplicate => duplicate.Key))}",
                        ephemeral: true
                    );
                    break;
                }

                value.Add(name, roles);

                await arg.RespondAsync("Added.", ephemeral: true);
                break;
            }
            case "finish":
            {
                bool got = dict.TryGetValue(key, out Dictionary<string, List<SocketRole>>? value);
                if (!got)
                {
                    await arg.RespondAsync("Does not exist.");
                    break;
                }

                MessageComponent component = value!.Aggregate(
                        new ComponentBuilder(),
                        (component, kvp) => component.WithSelectMenu(
                            kvp.Value.Aggregate(
                                    new SelectMenuBuilder(),
                                    (select, role) => select.AddOption(role.Name, role.Id.ToString())
                                )
                                .WithPlaceholder($"Select {kvp.Key}(s)")
                                .WithCustomId(kvp.Key)
                                .WithMinValues(0)
                                .WithMaxValues(kvp.Value.Count)
                        )
                    )
                    .WithButton("list all", "list")
                    .WithButton("remove all", "remove", ButtonStyle.Danger)
                    .Build();

                await arg.DeferAsync(true);

                // If a message id was supplied, replace that message. Otherwise create a new message.
                string? messageIdMaybe = arg.Data.Options.Single()
                    .Options.Select(option => option.Value)
                    .OfType<string>()
                    .SingleOrDefault();
                if (messageIdMaybe is { } messageId)
                    await arg.Channel.ModifyMessageAsync(
                        ulong.Parse(messageId),
                        properties => properties.Components = component
                    );
                else
                    await arg.Channel.SendMessageAsync("Select Roles(s)", components: component);

                await arg.FollowupAsync("Finished.", ephemeral: true);
                break;
            }
            default:
                await arg.RespondAsync(
                    ephemeral: true,
                    embed: new EmbedBuilder().WithTitle("Unknown Command")
                        .WithCurrentTimestamp()
                        .Build()
                );
                break;
        }
    }
    catch (HttpException e)
    {
        Console.WriteLine(JsonSerializer.Serialize(e.Errors));
        throw;
    }
}

static async Task SelectMenuExecuted(SocketMessageComponent arg)
{
    try
    {
        IGuildUser user = arg.User as IGuildUser ?? throw new InvalidOperationException();

        List<ulong> allValues = arg.Message.Components.SelectMany(component => component.Components)
            .OfType<SelectMenuComponent>()
            .Single(component => component.CustomId == arg.Data.CustomId)
            .Options.Select(option => ulong.Parse(option.Value))
            .ToList();
        List<ulong> selectedValues = arg.Data.Values.Select(ulong.Parse)
            .ToList();
        List<ulong> unselectedValues = allValues.Except(selectedValues)
            .ToList();

        await arg.DeferAsync(true);

        if (unselectedValues.Count != 0) await user.RemoveRolesAsync(unselectedValues);
        if (selectedValues.Count != 0) await user.AddRolesAsync(selectedValues);

        await arg.FollowupAsync(
            embed: new EmbedBuilder().WithTitle("Updated roles")
                .WithDescription($"For {arg.Data.CustomId}")
                .Build(),
            ephemeral: true
        );
    }
    catch (HttpException e)
    {
        Console.WriteLine(JsonSerializer.Serialize(e.Errors));
        throw;
    }
}

static async Task ButtonExecuted(SocketMessageComponent arg)
{
    try
    {
        IGuildUser user = arg.User as IGuildUser ?? throw new InvalidOperationException();

        List<ulong> allValues = arg.Message.Components.SelectMany(component => component.Components)
            .OfType<SelectMenuComponent>()
            .SelectMany(component => component.Options)
            .Select(option => ulong.Parse(option.Value))
            .ToList();

        switch (arg.Data.CustomId)
        {
            case "list":
                IEnumerable<ulong> selectedValues = allValues.Intersect(user.RoleIds);
                IEnumerable<IRole> selectedRoles = user.Guild.Roles.Where(role => selectedValues.Contains(role.Id));
                await arg.RespondAsync(
                    embed: new EmbedBuilder().WithTitle("Current roles")
                        .WithDescription(string.Join("\n", selectedRoles.Select(role => role.Mention)))
                        .Build(),
                    ephemeral: true
                );
                break;
            case "remove":
                await arg.DeferAsync(true);
                if (allValues.Count != 0) await user.RemoveRolesAsync(allValues);
                await arg.FollowupAsync(
                    embed: new EmbedBuilder().WithTitle("Removed all roles")
                        .Build(),
                    ephemeral: true
                );
                break;
        }
    }
    catch (HttpException e)
    {
        Console.WriteLine(JsonSerializer.Serialize(e.Errors));
        throw;
    }
}