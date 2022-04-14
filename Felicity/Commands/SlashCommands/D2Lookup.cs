﻿using System;
using System.Linq;
using System.Threading.Tasks;
using BungieSharper.Client;
using BungieSharper.Entities;
using BungieSharper.Entities.Destiny;
using BungieSharper.Entities.Exceptions;
using BungieSharper.Entities.User;
using Ceen;
using Discord.Interactions;
using Felicity.Helpers;
using Felicity.Services;

// ReSharper disable UnusedMember.Global

namespace Felicity.Commands.SlashCommands;

[Group("lookup", "Various lookup commands for Destiny2")]
public class D2Lookup : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("guardian", "Look up a profile of a player")]
    public async Task Guardian(
        [Summary("bungiename", "Bungie name of the requested user (name#1234)")]
        string bungieTag)
    {
        await DeferAsync();

        long membershipId;
        BungieMembershipType membershipType;
        string bungieName;

        if (bungieTag.StartsWith("https://www.bungie.net/7/en/User/Profile/"))
        {
            var url = bungieTag.Split("Profile/").Last();
            if (url.Contains('?')) url = url.Split("?").First();

            var urlMemId = url.Split("/").Last();
            var urlMemType = url.Split("/").First();

            var userCard = APIService.GetApiClient().Api
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

                var userInfoCard = APIService.GetApiClient().Api.Destiny2_SearchDestinyPlayerByBungieName(
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
                if (((BungieBaseApiResponseException) ex.InnerException)?.ApiResponseMsg
                    .ErrorCode == PlatformErrorCodes.UserCannotResolveCentralAccount)
                {
                    await Log.ErrorAsync($"Failed to lookup: {bungieTag}");
                    await FollowupAsync("Failed to lookup profile, try using the full Bungie.net profile link.\n-> https://www.bungie.net/7/en/User/Profile/");
                }
                else
                {
                    var log = $"{ex.GetType()}: {ex.Message}";
                    await Log.ErrorAsync(log);
                    await FollowupAsync(log);
                }

                return;
            }
        }

        var player = APIService.GetApiClient().Api.Destiny2_GetProfile(membershipId,
            membershipType, new[]
            {
                DestinyComponentType.Characters
            }).Result;

        await FollowupAsync("", new[] {player.GenerateLookupEmbed(bungieName, membershipId, membershipType)});
    }
}