using System.Text;
using System.Text.Json;
using Discord;
using Discord.Interactions;
using DotNetBungieAPI.Extensions;
using DotNetBungieAPI.Models;
using DotNetBungieAPI.Models.Destiny;
using DotNetBungieAPI.Models.Destiny.Definitions.Collectibles;
using DotNetBungieAPI.Models.Destiny.Definitions.InventoryItems;
using DotNetBungieAPI.Models.Destiny.Responses;
using DotNetBungieAPI.Service.Abstractions;
using Felicity.Models;
using Felicity.Util;
using Felicity.Util.Enums;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

namespace Felicity.DiscordCommands.Interactions;

[Group("emblem", "Various lookup commands for Destiny 2.")]
public class EmblemCommands : InteractionModuleBase<ShardedInteractionContext>
{
    private readonly IBungieClient _bungieClient;
    private readonly IConfiguration _configuration;
    private readonly UserDb _userDb;

    public EmblemCommands(IBungieClient bungieClient, UserDb userDb, IConfiguration configuration)
    {
        _bungieClient = bungieClient;
        _userDb = userDb;
        _configuration = configuration;
    }

    [SlashCommand("shares", "Look up account shared emblems of a player.")]
    public async Task EmblemShares(
        [Summary("bungie-name",
            "Bungie name of the requested user (name#1234).")]
        string bungieTag = "")
    {
        await DeferAsync();

        if (!await BungieApiUtils.CheckApi(_bungieClient))
            throw new Exception("Bungie API is down or unresponsive.");

        if (!string.IsNullOrEmpty(bungieTag) && !bungieTag.Contains('#'))
        {
            var errorEmbed = Embeds.MakeErrorEmbed();
            errorEmbed.Description =
                $"`{bungieTag}` is not a correct format for a Bungie name.\nTry again with the `<name>#<number>` format.";
            await FollowupAsync(embed: errorEmbed.Build());
            return;
        }

        var requestedProfile =
            await ProfileHelper.GetRequestedProfile(bungieTag, Context.User.Id, _userDb, _bungieClient);

        if (!string.IsNullOrEmpty(requestedProfile.Error))
        {
            var errorEmbed = Embeds.MakeErrorEmbed();
            errorEmbed.Description = requestedProfile.Error;
            await FollowupAsync(embed: errorEmbed.Build());
            return;
        }

        var profile = await _bungieClient.ApiAccess.Destiny2.GetProfile(requestedProfile.MembershipType,
            requestedProfile.MembershipId, new[]
            {
                DestinyComponentType.Characters, DestinyComponentType.Profiles, DestinyComponentType.Collectibles
            });

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        // Note to self, bungie privacy sucks ass and always reports privacy as private.
        if (profile.Response.ProfileCollectibles.Data == null)
        {
            var errorEmbed = Embeds.MakeErrorEmbed();
            errorEmbed.Description = $"`{bungieTag}` has their collections set to private, unable to parse emblems.";

            await FollowupAsync(embed: errorEmbed.Build());
            return;
        }

        var emblemCount = 0;
        var emblemList = new List<DestinyCollectibleDefinition>();

        var manifestInventoryItemIDs = profile.Response.Characters.Data
            .Select(destinyCharacterComponent => destinyCharacterComponent.Value.Emblem.Hash).ToList();
        var manifestCollectibleIDs =
            profile.Response.ProfileCollectibles.Data.Collectibles.Select(collectible => collectible.Key).ToList();

        var manifestInventoryItems = new List<DestinyInventoryItemDefinition>();
        foreach (var destinyInventoryItemDefinition in manifestInventoryItemIDs)
        {
            _bungieClient.Repository.TryGetDestinyDefinition<DestinyInventoryItemDefinition>(
                (uint)destinyInventoryItemDefinition!, out var result);

            manifestInventoryItems.Add(result);
        }

        var manifestCollectibles = new List<DestinyCollectibleDefinition>();
        foreach (var definitionHashPointer in manifestCollectibleIDs)
        {
            _bungieClient.Repository.TryGetDestinyDefinition<DestinyCollectibleDefinition>(
                (uint)definitionHashPointer.Hash!, out var result);

            manifestCollectibles.Add(result);
        }

        foreach (var collectible in from collectible in manifestCollectibles
                 where !collectible.Redacted
                 where !string.IsNullOrEmpty(collectible.DisplayProperties.Name)
                 from manifestCollectibleParentNodeHash in collectible.ParentNodes
                 where EmblemCats.EmblemCatList.Contains((EmblemCat)manifestCollectibleParentNodeHash.Hash!)
                 select collectible)
        {
            emblemCount++;

            var value = profile.Response.ProfileCollectibles.Data.Collectibles[collectible.Hash];

            foreach (var unused in from emblem in manifestInventoryItems
                     where emblem.Collectible.Hash == collectible.Hash
                     where value.State.HasFlag(DestinyCollectibleState.NotAcquired)
                     where !emblemList.Contains(collectible)
                     select emblem) emblemList.Add(collectible);

            if (value.State.HasFlag(DestinyCollectibleState.Invisible) &&
                !value.State.HasFlag(DestinyCollectibleState.NotAcquired))
                if (!emblemList.Contains(collectible))
                    emblemList.Add(collectible);

            // ReSharper disable once InvertIf
            if (value.State.HasFlag(DestinyCollectibleState.UniquenessViolation) &&
                value.State.HasFlag(DestinyCollectibleState.NotAcquired))
                if (!emblemList.Contains(collectible))
                    emblemList.Add(collectible);
        }

        var sortedList = emblemList.OrderBy(o => o.DisplayProperties.Name).ToList();

        var embed = Embeds.MakeBuilder();

        embed.Title = requestedProfile.BungieName;
        embed.Url =
            $"https://www.bungie.net/7/en/User/Profile/{(int)profile.Response.Profile.Data.UserInfo.MembershipType}/" +
            profile.Response.Profile.Data.UserInfo.MembershipId;
        embed.ThumbnailUrl = BotVariables.BungieBaseUrl + profile.Response.Characters.Data.First().Value.EmblemPath;

        if (sortedList.Count == 0)
        {
            embed.Description = "Account has no shared emblems.";
        }
        else
        {
            embed.Description = "**Account shared emblems:**\n";

            foreach (var emblemDefinition in sortedList)
                embed.Description +=
                    $"> [{emblemDefinition.DisplayProperties.Name}](https://emblem.report/{emblemDefinition.Item.Hash})\n";
        }

        embed.AddField("Parsed", $"> {emblemCount}", true);
        embed.AddField("Shared", $"> {sortedList.Count}", true);

        await FollowupAsync(embed: embed.Build());
    }

    [SlashCommand("rarest", "Gets the top rarest emblems in collections.")]
    public async Task EmblemRarest(
        [Summary("count", "Number of rarest emblems to fetch. (default = 5, between 1 and 50)")]
        int count = 5,
        [Summary("bungie-name",
            "Bungie name of the requested user. (name#1234)")]
        string bungieTag = "")
    {
        await DeferAsync();

        if (count is <= 0 or > 50)
        {
            var errorEmbed = Embeds.MakeErrorEmbed();
            errorEmbed.Description =
                $"`{count}` is not a valid choice.\nTry again with a value between 1 and 50.";
            await FollowupAsync(embed: errorEmbed.Build());
            return;
        }

        if (!await BungieApiUtils.CheckApi(_bungieClient))
            throw new Exception("Bungie API is down or unresponsive.");

        if (!string.IsNullOrEmpty(bungieTag) && !bungieTag.Contains('#'))
        {
            var errorEmbed = Embeds.MakeErrorEmbed();
            errorEmbed.Description =
                $"`{bungieTag}` is not a correct format for a Bungie name.\nTry again with the `<name>#<number>` format.";
            await FollowupAsync(embed: errorEmbed.Build());
            return;
        }

        var requestedProfile =
            await ProfileHelper.GetRequestedProfile(bungieTag, Context.User.Id, _userDb, _bungieClient);

        if (!string.IsNullOrEmpty(requestedProfile.Error))
        {
            var errorEmbed = Embeds.MakeErrorEmbed();
            errorEmbed.Description = requestedProfile.Error;
            await FollowupAsync(embed: errorEmbed.Build());
            return;
        }

        var profile = await _bungieClient.ApiAccess.Destiny2.GetProfile(requestedProfile.MembershipType,
            requestedProfile.MembershipId, new[]
            {
                DestinyComponentType.Collectibles, DestinyComponentType.Profiles
            });

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        // Note to self, bungie privacy sucks ass and always reports privacy as private.
        if (profile.Response.ProfileCollectibles.Data == null)
        {
            var errorEmbed = Embeds.MakeErrorEmbed();
            errorEmbed.Description = $"`{bungieTag}` has their collections set to private, unable to parse emblems.";
            await FollowupAsync(embed: errorEmbed.Build());
            return;
        }

        var manifestCollectibleIDs = AddEmblems(profile.Response);

        switch (requestedProfile.MembershipId)
        {
            // Moonie
            case 4611686018471516071:
                profile = await _bungieClient.ApiAccess.Destiny2.GetProfile(
                    BungieMembershipType.TigerSteam, 4611686018500337909,
                    new[]
                    {
                        DestinyComponentType.Collectibles, DestinyComponentType.Profiles
                    });

                manifestCollectibleIDs.AddRange(AddEmblems(profile.Response));
                break;
            // Zempp
            case 4611686018432393645:
                profile = await _bungieClient.ApiAccess.Destiny2.GetProfile(
                    BungieMembershipType.TigerPsn, 4611686018475371052,
                    new[]
                    {
                        DestinyComponentType.Collectibles, DestinyComponentType.Profiles
                    });

                manifestCollectibleIDs.AddRange(AddEmblems(profile.Response));

                profile = await _bungieClient.ApiAccess.Destiny2.GetProfile(
                    BungieMembershipType.TigerSteam, 4611686018483360936,
                    new[]
                    {
                        DestinyComponentType.Collectibles, DestinyComponentType.Profiles
                    });

                manifestCollectibleIDs.AddRange(AddEmblems(profile.Response));
                break;
        }

        var manifestCollectibles = new List<DestinyCollectibleDefinition>();
        foreach (var definitionHashPointer in manifestCollectibleIDs)
        {
            _bungieClient.Repository.TryGetDestinyDefinition<DestinyCollectibleDefinition>(
                (uint)definitionHashPointer.Hash!, out var result);

            if (!result.Redacted && !string.IsNullOrEmpty(result.DisplayProperties.Name))
                manifestCollectibles.AddRange(from definitionParentNode in result.ParentNodes
                    where EmblemCats.EmblemCatList.Contains((EmblemCat)definitionParentNode.Hash!)
                    select result);
        }

        string jsonString;

        try
        {
            var collectiblesData = new
            {
                collectibles = manifestCollectibles.Select(x => x.Hash).ToList()
            };

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Felicity/1.0)");
            client.DefaultRequestHeaders.Add("x-api-key", _configuration["Bungie:EmblemReportApiKey"]);

            var json = JsonSerializer.Serialize(collectiblesData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"https://emblem.report/api/getRarestEmblems?limit={count}", content);
            response.EnsureSuccessStatusCode();

            var responseData = await response.Content.ReadAsStringAsync();
            jsonString = responseData;
        }
        catch (Exception error)
        {
            var errorEmbed = Embeds.MakeErrorEmbed();
            errorEmbed.Description = error.Message;
            if (error.StackTrace != null) {
                errorEmbed.Description += "\n" + error.StackTrace.Split(Environment.NewLine).FirstOrDefault();
            }
            await FollowupAsync(embed: errorEmbed.Build());
            return;
        }

        var emblemResponse = EmblemReport.EmblemResponse.FromJson(jsonString);
        if (emblemResponse?.Data == null)
        {
            var errorEmbed = Embeds.MakeErrorEmbed();
            errorEmbed.Description = "Failed to parse response from server.";
            await FollowupAsync(embed: errorEmbed.Build());
            return;
        }

        var embed = new EmbedBuilder
        {
            Title = requestedProfile.BungieName,
            Url =
                $"https://www.bungie.net/7/en/User/Profile/{(int)profile.Response.Profile.Data.UserInfo.MembershipType}/" +
                profile.Response.Profile.Data.UserInfo.MembershipId,
            Color = Color.Purple,
            Footer = Embeds.MakeFooter(),
            Description =
                $"Here are the {count} rarest emblems in collections for this user.\nData provided by [emblem.report](https://emblem.report).\n\n"
        };

        var i = 1;

        foreach (var emblem in emblemResponse.Data)
            if (_bungieClient.Repository.TryGetDestinyDefinition<DestinyCollectibleDefinition>(emblem.CollectibleHash,
                    out var emblemDef))
            {
                var sb = new StringBuilder();
                embed.Description += count.ToString().Length switch
                {
                    < 10 => $"> {i}. ",
                    _ => $"> {i,2}. "
                };

                sb.Append(emblem.Acquisition.ToString("N0").PadLeft(7));

                embed.Description +=
                    $"`{sb}` - " +
                    $"[{emblemDef.DisplayProperties.Name}](https://emblem.report/{emblemDef.Item.Select(x => x.Hash)}) " +
                    $"({emblem.Percentage}%)\n";

                if (embed.Description.Length <= 3850)
                {
                    i++;
                    continue;
                }

                embed.Description += $"\n\n**Requested size is too long, response has been truncated to {i} emblems**.";
                embed.Description =
                    embed.Description.Replace($"Here are the {count} rarest",
                        $"Here are the {i} rarest"); // Don't mind me.
                break;
            }

        await FollowupAsync(embed: embed.Build());
    }

    private static List<DefinitionHashPointer<DestinyCollectibleDefinition>> AddEmblems(
        DestinyProfileResponse profileResponse)
    {
        var manifestCollectibleIDs =
            (from destinyCollectibleComponent in profileResponse.ProfileCollectibles.Data.Collectibles
                where !destinyCollectibleComponent.Value.State.HasFlag(DestinyCollectibleState.UniquenessViolation) ||
                      !destinyCollectibleComponent.Value.State.HasFlag(DestinyCollectibleState.NotAcquired)
                where !destinyCollectibleComponent.Value.State.HasFlag(DestinyCollectibleState.Invisible) ||
                      destinyCollectibleComponent.Value.State.HasFlag(DestinyCollectibleState.NotAcquired)
                where !destinyCollectibleComponent.Value.State.HasFlag(DestinyCollectibleState.NotAcquired)
                select destinyCollectibleComponent.Key).ToList();

        foreach (var destinyCollectibleComponent in profileResponse.CharacterCollectibles.Data)
            manifestCollectibleIDs.AddRange(from collectibleComponent in destinyCollectibleComponent.Value.Collectibles
                where !collectibleComponent.Value.State.HasFlag(DestinyCollectibleState.UniquenessViolation) ||
                      !collectibleComponent.Value.State.HasFlag(DestinyCollectibleState.NotAcquired)
                where !collectibleComponent.Value.State.HasFlag(DestinyCollectibleState.Invisible) ||
                      collectibleComponent.Value.State.HasFlag(DestinyCollectibleState.NotAcquired)
                where !collectibleComponent.Value.State.HasFlag(DestinyCollectibleState.NotAcquired)
                select collectibleComponent.Key);

        return manifestCollectibleIDs;
    }
}
