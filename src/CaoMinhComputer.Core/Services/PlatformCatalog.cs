using CaoMinhComputer.Core.Models;

namespace CaoMinhComputer.Core.Services;

/// <summary>
/// Danh mục nền tảng mẫu. HOSTS không hỗ trợ wildcard nên mỗi nền tảng liệt kê
/// tên miền gốc và các subdomain chính mà trình duyệt/ứng dụng thường dùng.
/// Danh sách này không thể bao phủ 100% biến thể; xem docs/SECURITY.md.
/// </summary>
public static class PlatformCatalog
{
    private static readonly PlatformGroup[] Social = [PlatformGroup.Social];
    private static readonly PlatformGroup[] Game = [PlatformGroup.Game];
    private static readonly PlatformGroup[] WebGame = [PlatformGroup.WebGame];
    private static readonly PlatformGroup[] Bypass = [PlatformGroup.BypassTools];
    private static readonly PlatformGroup[] Entertainment = [PlatformGroup.Entertainment];

    public static IReadOnlyList<PlatformDefinition> All { get; } =
    [
        // ------------------ MẠNG XÃ HỘI & GIAO TIẾP ------------------
        new("facebook", "Facebook", "f", "#1877F2", Social,
        [
            "facebook.com", "www.facebook.com", "m.facebook.com", "web.facebook.com", "mbasic.facebook.com",
            "touch.facebook.com", "business.facebook.com", "graph.facebook.com", "api.facebook.com",
            "edge-mqtt.facebook.com", "star.c10r.facebook.com", "fb.com", "www.fb.com", "fb.me", "fb.watch", "www.fb.watch",
            "messenger.com", "www.messenger.com", "static.xx.fbcdn.net", "scontent.xx.fbcdn.net"
        ]),
        new("tiktok", "TikTok", "Tt", "#111111", Social,
        [
            "tiktok.com", "www.tiktok.com", "m.tiktok.com", "vt.tiktok.com", "vm.tiktok.com", "t.tiktok.com",
            "us.tiktok.com", "webcast.tiktok.com", "tiktokv.com", "www.tiktokv.com", "api.tiktokv.com",
            "tiktokcdn.com", "lf16-tiktok-web.tiktokcdn-us.com", "sf16-website-login.neutral.ttwstatic.com"
        ]),
        new("youtube", "YouTube", "▶", "#E62117", Social,
        [
            "youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "studio.youtube.com",
            "tv.youtube.com", "kids.youtube.com", "www.youtubekids.com", "youtubekids.com",
            "youtu.be", "www.youtu.be", "youtube-nocookie.com", "www.youtube-nocookie.com",
            "youtubei.googleapis.com", "youtube.googleapis.com", "yt3.ggpht.com", "i.ytimg.com", "s.ytimg.com"
        ]),
        new("instagram", "Instagram", "Ig", "#D6246E", Social,
        [
            "instagram.com", "www.instagram.com", "i.instagram.com", "l.instagram.com", "graph.instagram.com",
            "api.instagram.com", "help.instagram.com", "about.instagram.com", "instagr.am", "www.instagr.am",
            "static.cdninstagram.com", "scontent.cdninstagram.com"
        ]),
        new("threads", "Threads", "@", "#111111", Social,
        [
            "threads.net", "www.threads.net", "threads.com", "www.threads.com"
        ]),
        new("x", "X (Twitter)", "X", "#111111", Social,
        [
            "x.com", "www.x.com", "mobile.x.com", "api.x.com", "twitter.com", "www.twitter.com", "mobile.twitter.com",
            "api.twitter.com", "t.co", "abs.twimg.com", "pbs.twimg.com", "video.twimg.com"
        ]),
        new("linkedin", "LinkedIn", "in", "#0A66C2", Social,
        [
            "linkedin.com", "www.linkedin.com", "m.linkedin.com", "lnkd.in", "www.lnkd.in", "static.licdn.com", "media.licdn.com"
        ]),
        new("telegram", "Telegram", "Tg", "#29A9EB", Social,
        [
            "telegram.org", "www.telegram.org", "web.telegram.org", "desktop.telegram.org", "core.telegram.org",
            "telegram.me", "www.telegram.me", "t.me", "www.t.me"
        ]),
        new("discord", "Discord", "Dc", "#5865F2", [PlatformGroup.Social, PlatformGroup.Game],
        [
            "discord.com", "www.discord.com", "discord.gg", "www.discord.gg", "gateway.discord.gg",
            "discordapp.com", "www.discordapp.com", "cdn.discordapp.com", "media.discordapp.net",
            "discord.media", "discord.new", "dl.discordapp.net", "status.discord.com"
        ]),

        // ------------------ GAME PC & LAUNCHER ------------------
        new("roblox", "Roblox", "Rb", "#393B3D", Game,
        [
            "roblox.com", "www.roblox.com", "web.roblox.com", "auth.roblox.com", "apis.roblox.com",
            "games.roblox.com", "users.roblox.com", "economy.roblox.com", "clientsettings.roblox.com",
            "clientsettingscdn.roblox.com", "assetdelivery.roblox.com", "setup.rbxcdn.com", "rbxcdn.com", "rbx.com"
        ]),
        new("garena", "Garena", "G", "#EE4D2D", Game,
        [
            "garena.com", "www.garena.com", "garena.vn", "www.garena.vn", "account.garena.com", "sso.garena.com",
            "connect.garena.com", "shop.garena.vn", "napthe.vn", "www.napthe.vn",
            "lienquan.garena.vn", "ff.garena.vn", "fo4.garena.vn", "auth.garena.com"
        ]),
        new("riot", "Riot Games", "R", "#D13639", Game,
        [
            "riotgames.com", "www.riotgames.com", "auth.riotgames.com", "authenticate.riotgames.com",
            "account.riotgames.com", "clientconfig.rpg.riotgames.com", "lol.secure.dyn.riotcdn.net",
            "valorant.secure.dyn.riotcdn.net", "riotcdn.net", "leagueoflegends.com", "www.leagueoflegends.com",
            "playvalorant.com", "www.playvalorant.com", "lolesports.com", "www.lolesports.com"
        ]),
        new("minecraft", "Minecraft", "MC", "#5D8A3A", Game,
        [
            "minecraft.net", "www.minecraft.net", "tlauncher.org", "www.tlauncher.org", "api.tlauncher.org",
            "minecraftservices.com", "mojang.com", "authserver.mojang.com", "sessionserver.mojang.com"
        ]),
        new("steam", "Steam", "St", "#1B2838", Game,
        [
            "steampowered.com", "www.steampowered.com", "store.steampowered.com", "steamcommunity.com",
            "www.steamcommunity.com", "steamcontent.com", "steamstatic.com", "api.steampowered.com"
        ]),
        new("epicgames", "Epic Games", "Eg", "#2F2F2F", Game,
        [
            "epicgames.com", "www.epicgames.com", "store.epicgames.com", "launcher.epicgames.com",
            "unrealengine.com", "www.unrealengine.com"
        ]),

        // ------------------ WEB GAME (CHƠI TRÊN TRÌNH DUYỆT) ------------------
        new("poki", "Poki Games", "Pk", "#00B2FF", WebGame,
        [
            "poki.com", "www.poki.com", "poki.vn", "www.poki.vn", "kids.poki.com", "poki-cdn.com"
        ]),
        new("y8", "Y8 Games", "Y8", "#E61B23", WebGame,
        [
            "y8.com", "www.y8.com", "vi.y8.com", "account.y8.com", "cdn.y8.com", "static.y8.com"
        ]),
        new("gamevui", "GameVui & Game24h", "Gv", "#F37021", WebGame,
        [
            "gamevui.vn", "www.gamevui.vn", "m.gamevui.vn", "game24h.vn", "www.game24h.vn", "m.game24h.vn"
        ]),
        new("crazygames", "CrazyGames", "Cg", "#6B21A8", WebGame,
        [
            "crazygames.com", "www.crazygames.com", "kids.crazygames.com", "assets.crazygames.com"
        ]),
        new("webgames_io", "Game .IO (Slither/Agar)", "IO", "#10B981", WebGame,
        [
            "slither.io", "www.slither.io", "agar.io", "www.agar.io", "krunker.io", "www.krunker.io",
            "paper-io.com", "www.paper-io.com", "venge.io", "www.venge.io", "surviv.io", "www.surviv.io"
        ]),

        // ------------------ CÔNG CỤ VƯỢT RÀO & WEB PROXY ------------------
        new("croxyproxy", "CroxyProxy", "Cp", "#4F46E5", Bypass,
        [
            "croxyproxy.com", "www.croxyproxy.com", "croxyproxy.rocks", "www.croxyproxy.rocks",
            "croxy.network", "croxy.org", "www.croxy.org"
        ]),
        new("kproxy", "KProxy & ProxySite", "Kp", "#D97706", Bypass,
        [
            "kproxy.com", "www.kproxy.com", "proxysite.com", "www.proxysite.com",
            "blockaway.net", "www.blockaway.net", "hidemyass.com", "www.hidemyass.com",
            "whoer.net", "www.whoer.net", "4everproxy.com", "www.4everproxy.com"
        ]),
        new("ultrasurf", "UltraSurf", "Us", "#DC2626", Bypass,
        [
            "ultrasurf.us", "www.ultrasurf.us", "ultrasurfing.com", "www.ultrasurfing.com"
        ]),

        // ------------------ LIVESTREAM & STREAMING GAME ------------------
        new("twitch", "Twitch", "Tw", "#9146FF", Entertainment,
        [
            "twitch.tv", "www.twitch.tv", "m.twitch.tv", "api.twitch.tv",
            "static-cdn.jtvnw.net", "usher.ttvnw.net"
        ]),
        new("nimo_bigo", "Nimo TV & Bigo Live", "Nm", "#F59E0B", Entertainment,
        [
            "nimo.tv", "www.nimo.tv", "bigo.tv", "www.bigo.tv", "web.bigo.tv",
            "kick.com", "www.kick.com"
        ])
    ];

    public static PlatformDefinition? Find(string key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ứng dụng mẫu (tắt mặc định) để phụ huynh bật nhanh.</summary>
    public static IReadOnlyList<(string Name, string Process)> SampleApps { get; } =
    [
        // Giao tiếp & Chat
        ("Discord", "Discord.exe"),
        ("Telegram Desktop", "Telegram.exe"),

        // Game PC & Launcher
        ("Roblox Player", "RobloxPlayerBeta.exe"),
        ("Riot Client", "RiotClientServices.exe"),
        ("Liên Minh Huyền Thoại", "LeagueClient.exe"),
        ("VALORANT", "VALORANT.exe"),
        ("Garena", "Garena.exe"),
        ("Steam", "steam.exe"),
        ("Epic Games Launcher", "EpicGamesLauncher.exe"),
        ("FC Online (FIFA Online 4)", "FC_Online.exe"),
        ("Đột Kích (CrossFire)", "crossfire.exe"),

        // Minecraft
        ("Java – javaw.exe (Minecraft; chặn cả phần mềm Java khác)", "javaw.exe"),
        ("TLauncher (Minecraft)", "TLauncher.exe"),
        ("Minecraft Launcher", "Minecraft.exe"),
        ("Badlion Client (Minecraft)", "Badlion Client.exe"),
        ("Lunar Client (Minecraft)", "Lunar Client.exe"),

        // Giả lập Android (chơi Free Fire, Liên Quân...)
        ("Giả lập LDPlayer", "dnplayer.exe"),
        ("Giả lập LDPlayer (Core)", "LDPlayer.exe"),
        ("Giả lập BlueStacks", "HD-Player.exe"),
        ("Giả lập BlueStacks (Core)", "BlueStacks.exe"),
        ("Giả lập NoxPlayer", "Nox.exe"),
        ("Giả lập MuMu Player", "MuMuPlayer.exe"),
        ("Giả lập MEmu Play", "MEmu.exe"),

        // VPN & Công cụ vượt rào
        ("Cloudflare WARP (1.1.1.1)", "Cloudflare WARP.exe"),
        ("ProtonVPN", "ProtonVPN.exe")
    ];
}
