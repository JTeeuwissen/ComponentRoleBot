﻿using System.Collections.Concurrent;
using System.Text.Json;
using Discord;
using Discord.Net;
using Discord.WebSocket;

namespace DiscordRoles;

public class Program
{
    // Env vars.
    private readonly string _token;
    private readonly ulong _guildId;

    // Discord client.
    private readonly DiscordSocketClient _client = new();

    // A dict containing the current component a user is working on.
    private readonly ConcurrentDictionary<ulong, Dictionary<string, List<SocketRole>>> _dict = new();

    /// <summary>
    /// Read env vars.
    /// </summary>
    public Program()
    {
        _token = GetEnvironmentVariable("DISCORD_TOKEN");
        _guildId = ulong.Parse(GetEnvironmentVariable("DISCORD_GUILD"));
    }

    /// <summary>
    /// Main.
    /// </summary>
    public static void Main() =>
        new Program().MainAsync()
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// Async main.
    /// </summary>
    /// <returns>A task.</returns>
    public async Task MainAsync()
    {
        _client.Log += Log;
        _client.Ready += Ready;
        _client.SlashCommandExecuted += SlashCommandExecuted;
        _client.SelectMenuExecuted += SelectMenuExecuted;
        _client.ButtonExecuted += ButtonExecuted;

        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();

        await Task.Delay(-1);
    }

    /// <summary>
    /// Log
    /// </summary>
    /// <param name="msg">Log message.</param>
    /// <returns>A task.</returns>
    private static Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Run on read, adds application commands.
    /// Only needs to be done once, but easier to do on start.
    /// </summary>
    /// <returns>A task.</returns>
    private async Task Ready()
    {
        try
        {
            SocketGuild guild = _client.GetGuild(_guildId);

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
                        .WithType(ApplicationCommandOptionType.SubCommand)
                )
                .Build();

            await guild.BulkOverwriteApplicationCommandAsync(new ApplicationCommandProperties[] { command });
        }
        catch (HttpException exception)
        {
            await Console.Error.WriteLineAsync(JsonSerializer.Serialize(exception.Errors));
        }
    }

    /// <summary>
    /// Slash command event handler.
    /// </summary>
    /// <param name="arg">A slash event.</param>
    /// <returns>A task.</returns>
    private async Task SlashCommandExecuted(SocketSlashCommand arg)
    {
        try
        {
            if (arg.User.Id != 342314893367705601)
            {
                await arg.RespondAsync(
                    embed: new EmbedBuilder().WithTitle("Sike!")
                        .WithDescription("Only <:HisGloriousHat:650325636602134529> can do that!")
                        .Build(),
                    ephemeral: true
                );
                return;
            }

            switch (arg.Data.Options.First()
                        .Name)
            {
                case "create":
                {
                    bool added = _dict.TryAdd(arg.User.Id, new Dictionary<string, List<SocketRole>>());
                    if (added)
                        await arg.RespondAsync("Created.");
                    else
                        await arg.RespondAsync("Already exists.");

                    break;
                }
                case "delete":
                {
                    bool removed = _dict.TryRemove(arg.User.Id, out _);
                    if (removed)
                        await arg.RespondAsync("Deleted");
                    else
                        await arg.RespondAsync("Does not exist.");
                    break;
                }
                case "add":
                {
                    bool got = _dict.TryGetValue(arg.User.Id, out Dictionary<string, List<SocketRole>>? value);
                    if (!got)
                    {
                        await arg.RespondAsync("Does not exist.");
                        break;
                    }

                    if (value!.Count >= 4)
                    {
                        await arg.RespondAsync("Already max length.");
                        break;
                    }

                    List<object> vals = arg.Data.Options.Single()
                        .Options.Select(option => option.Value)
                        .ToList();
                    value.Add(
                        vals.OfType<string>()
                            .Single(),
                        vals.OfType<SocketRole>()
                            .ToList()
                    );

                    await arg.RespondAsync("Added.");
                    break;
                }
                case "finish":
                {
                    bool got = _dict.TryGetValue(arg.User.Id, out Dictionary<string, List<SocketRole>>? value);
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

                    await arg.Channel.SendMessageAsync("Select Roles(s)", component: component);
                    await arg.RespondAsync("Finished.");
                    break;
                }
                default:
                    await arg.RespondAsync(
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

    /// <summary>
    /// Select event handler.
    /// </summary>
    /// <param name="arg">A select event.</param>
    /// <returns>A task.</returns>
    /// <exception cref="InvalidOperationException">An exception throw when a non user presses a button.</exception>
    private static async Task SelectMenuExecuted(SocketMessageComponent arg)
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

            await Task.WhenAll(
                unselectedValues.Count == 0 ? Task.CompletedTask : user.RemoveRolesAsync(unselectedValues),
                selectedValues.Count == 0 ? Task.CompletedTask : user.AddRolesAsync(selectedValues)
            );

            await arg.RespondAsync(
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

    /// <summary>
    /// Button press event handler.
    /// </summary>
    /// <param name="arg">A button press event.</param>
    /// <returns>A task.</returns>
    /// <exception cref="InvalidOperationException">An exception throw when a non user presses a button.</exception>
    private static async Task ButtonExecuted(SocketMessageComponent arg)
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
                    if (allValues.Count != 0) await user.RemoveRolesAsync(allValues);
                    await arg.RespondAsync(
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

    /// <summary>
    /// Get an environment variable, and throw an exception if it is not found.
    /// </summary>
    /// <param name="name">The name of the environment variable.</param>
    /// <returns>The value from the environment variable.</returns>
    /// <exception cref="Exception">An exception thrown when the environment variable cannot be found.</exception>
    public static string GetEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new Exception($"environment variable {name} not found");
}