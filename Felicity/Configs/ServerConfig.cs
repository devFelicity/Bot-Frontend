﻿using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Discord.WebSocket;
using Felicity.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using J = Newtonsoft.Json.JsonPropertyAttribute;

// ReSharper disable UnusedMember.Global
// ReSharper disable PartialTypeWithSinglePart

namespace Felicity.Configs;

public partial class ServerConfig
{
    [J("Settings")]
    public Dictionary<string, ServerSetting> Settings { get; set; } = new();
}

public partial class ServerSetting
{
    [J("language")] public Lang Language { get; set; } = Lang.Undefined;
    [J("moderatorRole")] public ulong ModeratorRole { get; set; }
    [J("announcementChannel")] public ulong AnnouncementChannel { get; set; }
    [J("staffChannel")] public ulong StaffChannel { get; set; }
    [J("memberCountChannel")] public ulong MemberCountChannel { get; set; }
    [J("boostCountChannel")] public ulong BoostCountChannel { get; set; }
    [J("memberEvents")] public MemberEvents MemberEvents { get; set; } = new();
    [J("subscriptions")] public Dictionary<string, Subscription> Subscriptions { get; set; } = new();
    [J("twitchStreams")] public Dictionary<string, TwitchStream> TwitchStreams { get; set; } = new();
    [J("destiny")] public Destiny Destiny { get; set; } = new();
    [J("tmpVCs")] public List<ulong> TmpVCs { get; set; } = new();

}

public class TwitchStream
{
    [J("ChannelId")] public ulong ChannelId { get; set; }
    [J("UserId")] public ulong UserId { get; set; }
    [J("Mention")] public ulong Mention { get; set; }
    [J("MentionEveryone")] public bool MentionEveryone { get; set; }
}

public partial class Destiny
{
    [J("dailyReset")] public Vendor DailyReset { get; set; } = new();
    [J("weeklyReset")] public Vendor WeeklyReset { get; set; } = new();
    [J("ada")] public Vendor Ada { get; set; } = new();
    [J("gunsmith")] public Vendor Gunsmith { get; set; } = new();
    [J("xur")] public Vendor Xur { get; set; } = new();
}

public partial class Vendor
{
    [J("channelId")] public ulong ChannelId { get; set; }
    [J("enabled")] public bool Enabled { get; set; }
}

public partial class MemberEvents
{
    [J("logChannel")] public ulong LogChannel { get; set; }
    [J("memberJoined")] public bool MemberJoined { get; set; }
    [J("memberLeft")] public bool MemberLeft { get; set; }
    [J("memberKicked")] public bool MemberKicked { get; set; }
    [J("memberBanned")] public bool MemberBanned { get; set; }
}

public partial class Subscription
{
    [J("type")] public string Type { get; set; }
    [J("url")] public string Url { get; set; }
    [J("channelId")] public ulong ChannelId { get; set; }
}

public enum Lang
{
    En,
    De,
    Es,
    Fr,
    It,
    Ja,
    Ko,
    Nl,
    Pl,
    PtBr,
    Ru,
    ZhChs,
    ZhCht,
    Undefined
}

public partial class ServerConfig
{
    public static string GetTextChannel(SocketGuild contextGuild, ulong channelId)
    {
        var channel = contextGuild.GetTextChannel(channelId);
        return channel == null ? "None." : channel.Mention;
    }

    public static ServerConfig GetServerSettings(ulong guildId)
    {
        var serverSettings = FromJson();
        if (serverSettings.Settings.ContainsKey(guildId.ToString()))
            return serverSettings;

        serverSettings.Settings.Add(guildId.ToString(), new ServerSetting());
        return serverSettings;
    }

    public static ServerConfig FromJson()
    {
        return JsonConvert.DeserializeObject<ServerConfig>(File.ReadAllText(ConfigHelper.ServerConfigPath), Converter.Settings);
    }

    public static string ToJson(ServerConfig self)
    {
        return JsonConvert.SerializeObject(self, Converter.Settings);
    }
}

internal static class Converter
{
    public static readonly JsonSerializerSettings Settings = new()
    {
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        Formatting = Formatting.Indented,
        Converters =
        {
            new IsoDateTimeConverter {DateTimeStyles = DateTimeStyles.AssumeUniversal}
        }
    };
}