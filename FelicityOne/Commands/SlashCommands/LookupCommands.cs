﻿using BungieSharper.Entities;
using BungieSharper.Entities.Destiny;
using BungieSharper.Entities.Destiny.Definitions;
using BungieSharper.Entities.Destiny.Definitions.Collectibles;
using BungieSharper.Entities.User;
using Discord;
using Discord.Interactions;
using FelicityOne.Enums;
using FelicityOne.Helpers;
using FelicityOne.Services;
using Serilog;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global

namespace FelicityOne.Commands.SlashCommands;

[Group("lookup", "Various lookup commands for Destiny 2.")]
public class Lookup : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("guardian", "Look up a profile of a player.")]
    public async Task LookupGuardian(
        [Summary("bungiename", "Bungie name of the requested user (name#1234)")]
        string bungieTag = "")
    {
        await DeferAsync();

        long membershipId;
        BungieMembershipType membershipType;
        string bungieName;

        if (string.IsNullOrEmpty(bungieTag))
        {
            var linkedUser = OAuthService.GetUser(Context.User.Id).Result;
            var linkedProfile = BungieAPI.GetApiClient().Api.Destiny2_GetLinkedProfiles(linkedUser.MembershipId,
                BungieMembershipType.BungieNext, true, linkedUser.AccessToken).Result;

            membershipId = linkedProfile.Profiles.First().MembershipId;
            membershipType = linkedProfile.Profiles.First().MembershipType;
            bungieName =
                $"{linkedProfile.BnetMembership.BungieGlobalDisplayName}#{linkedProfile.BnetMembership.BungieGlobalDisplayNameCode}";
        }
        else
        {
            if (bungieTag.StartsWith("https://www.bungie.net/7/en/User/Profile/"))
            {
                var url = bungieTag.Split("Profile/").Last();
                if (url.Contains('?')) url = url.Split("?").First();

                var urlMemId = url.Split("/").Last();
                var urlMemType = url.Split("/").First();

                var userCard = BungieAPI.GetApiClient().Api
                    .User_GetMembershipDataById(Convert.ToInt64(urlMemId),
                        Enum.Parse<BungieMembershipType>(urlMemType)).Result.DestinyMemberships.First();

                membershipId = userCard.MembershipId;
                membershipType = userCard.MembershipType;
                bungieName = $"{userCard.BungieGlobalDisplayName}#{userCard.BungieGlobalDisplayNameCode}";
            }
            else
            {
                try
                {
                    var name = bungieTag.Split("#").First();
                    var code = Convert.ToInt16(bungieTag.Split("#").Last());

                    var userInfoCard = BungieAPI.GetApiClient().Api.Destiny2_SearchDestinyPlayerByBungieName(
                        BungieMembershipType.All,
                        new ExactSearchRequest
                        {
                            DisplayName = name,
                            DisplayNameCode = code
                        }).Result.First();

                    membershipId = userInfoCard.MembershipId;
                    membershipType = userInfoCard.MembershipType;
                    bungieName = $"{userInfoCard.BungieGlobalDisplayName}#{userInfoCard.BungieGlobalDisplayNameCode}";
                }
                catch (Exception ex)
                {
                    var msg = $"Failed to lookup: {bungieTag}\n{ex.GetType()}: {ex.Message}";
                    Log.Error(ex, msg);

                    await FollowupAsync(
                        "Failed to lookup profile, try using the full Bungie.net profile link.\n-> https://www.bungie.net/7/en/User/Profile/");
                    return;
                }
            }
        }

        var player = BungieAPI.GetApiClient().Api.Destiny2_GetProfile(membershipId,
            membershipType, new[]
            {
                DestinyComponentType.Characters
            }).Result;

        await FollowupAsync(embed: player.GenerateLookupEmbed(bungieName, membershipId, membershipType));
    }

    [SlashCommand("accountshare", "Look up account shared emblems of a player.")]
    public async Task LookupAccountShare(
        [Summary("bungiename",
            "Bungie name of the requested user (name#1234). If absent, registered profile will be used.")]
        string bungieTag)
    {
        await DeferAsync();

        long membershipId;
        BungieMembershipType membershipType;
        string bungieName;

        try
        {
            var name = bungieTag.Split("#").First();
            var code = Convert.ToInt16(bungieTag.Split("#").Last());

            var userInfoCard = BungieAPI.GetApiClient().Api.Destiny2_SearchDestinyPlayerByBungieName(
                BungieMembershipType.All,
                new ExactSearchRequest
                {
                    DisplayName = name,
                    DisplayNameCode = code
                }).Result.First();

            membershipId = userInfoCard.MembershipId;
            membershipType = userInfoCard.MembershipType;
            bungieName = $"{userInfoCard.BungieGlobalDisplayName}#{userInfoCard.BungieGlobalDisplayNameCode}";
        }
        catch (Exception ex)
        {
            var msg = $"Failed to lookup: {bungieTag}\n{ex.GetType()}: {ex.Message}";
            Log.Error(ex, msg);

            await FollowupAsync("Failed to lookup player profile.");
            return;
        }

        var profile = BungieAPI.GetApiClient().Api.Destiny2_GetProfile(membershipId,
            membershipType, new[]
            {
                DestinyComponentType.Characters, DestinyComponentType.Profiles, DestinyComponentType.Collectibles
            }).Result;

        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        // ReSharper disable HeuristicUnreachableCode
        if (profile.ProfileCollectibles.Data == null!)
        {
            var privateEmbed = Extensions.GenerateMessageEmbed(bungieName, "",
                "User has their collections set to private, unable to parse emblems.",
                "https://www.bungie.net/7/en/User/Profile/254/" + profile.Profile.Data.UserInfo.MembershipId);

            await FollowupAsync(embed: privateEmbed.Build());
            return;
        }
        // ReSharper restore HeuristicUnreachableCode

        var emblemCount = 0;
        var emblemList = new List<DestinyCollectibleDefinition>();

        var manifestInventoryItemIDs = profile.Characters.Data
            .Select(destinyCharacterComponent => destinyCharacterComponent.Value.EmblemHash).ToList();
        var manifestCollectibleIDs =
            profile.ProfileCollectibles.Data.Collectibles.Select(collectible => collectible.Key).ToList();

        var manifestInventoryItems =
            BungieAPI.GetManifestDefinition<DestinyInventoryItemDefinition>(
                ConfigService.GetServerSettings(Context.Guild.Id).Language, manifestInventoryItemIDs);

        var manifestCollectibles =
            BungieAPI.GetManifestDefinition<DestinyCollectibleDefinition>(
                ConfigService.GetServerSettings(Context.Guild.Id).Language, manifestCollectibleIDs);

        foreach (var collectible in from collectible in manifestCollectibles
                 where !collectible.Redacted
                 where !string.IsNullOrEmpty(collectible.DisplayProperties.Name)
                 from manifestCollectibleParentNodeHash in collectible.ParentNodeHashes
                 where EmblemCats.EmblemCatList.Contains((EmblemCat) manifestCollectibleParentNodeHash)
                 select collectible)
        {
            emblemCount++;

            var value = profile.ProfileCollectibles.Data.Collectibles[collectible.Hash];

            foreach (var unused in from emblem in manifestInventoryItems
                     where emblem.CollectibleHash == collectible.Hash
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

        var embed = new EmbedBuilder
        {
            Author = new EmbedAuthorBuilder
            {
                Name = bungieName,
                Url = "https://www.bungie.net/7/en/User/Profile/254/" +
                      profile.Profile.Data.UserInfo.MembershipId
            },
            Color = Color.Purple,
            ThumbnailUrl = BungieAPI.BaseUrl + profile.Characters.Data.First().Value.EmblemPath,
            Footer = new EmbedFooterBuilder
            {
                Text = $"{Strings.FelicityVersion} | Parsed {emblemCount} emblems.",
                IconUrl = Images.FelicityLogo
            }
        };

        if (sortedList.Count == 0)
        {
            embed.Description = "Account has no shared emblems.";
        }
        else
        {
            embed.Description = "**Account shared emblems:**\n\n";

            foreach (var emblemDefinition in sortedList)
                embed.Description +=
                    $"[{emblemDefinition.DisplayProperties.Name}](https://destinyemblemcollector.com/emblem?id={emblemDefinition.ItemHash})\n";
        }

        await FollowupAsync(embed: embed.Build());
    }
}