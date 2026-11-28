using System.Collections.Concurrent;
using System.Globalization;
using Il2CppScheduleOne.Networking;
using MelonLoader;

namespace TVPlus;

/// <summary>
/// Group watch: every player keeps the video in the same folder, and only short "play this file from here" messages
/// travel between players, as Steam lobby chat messages through the game's own lobby.
/// </summary>
static class GroupWatch
{
    const string Tag = "TVPLUS1";
    static readonly string[] Commands = { "play", "pause", "resume", "stop", "sync" };
    static readonly string Me = Guid.NewGuid().ToString("N").Substring(0, 8); // Steam also hands us our own messages: skip them

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "TV+ Group Watch");
    public const string FolderLabel = @"Videos\TV+ Group Watch";

    public record Msg(string Command, float Time, string File);

    static readonly ConcurrentQueue<Msg> Inbox = new();
    static ILobbyService _lobby;
    static float _nextLook;

    /// <summary>Hooks into the game's lobby once it exists. Called every frame; cheap once hooked.</summary>
    public static void Poll()
    {
        if (_lobby != null || UnityEngine.Time.unscaledTime < _nextLook) return;
        _nextLook = UnityEngine.Time.unscaledTime + 2f;
        try
        {
            var lobby = UnityEngine.Object.FindObjectOfType<Lobby>();
            if (lobby == null || lobby._lobbyService == null) return;
            _lobby = lobby._lobbyService;
            _lobby.add_OnLobbyMessage((Il2CppSystem.Action<string>)new Action<string>(Receive));
            Directory.CreateDirectory(Folder);
            MelonLogger.Msg("TV+: group watch ready. Shared videos go in " + Folder);
        }
        catch (Exception e) { _lobby = null; MelonLogger.Warning("TV+ group watch: " + e.Message); }
    }

    public static bool InMultiplayer
    {
        get { try { return _lobby != null && _lobby.IsInLobby && _lobby.PlayerCount > 1; } catch { return false; } }
    }

    public static void Send(string command, double time, string file)
    {
        if (!InMultiplayer) return;
        try { _lobby.SendMessage(string.Join("|", Tag, Me, command, time.ToString("0.00", CultureInfo.InvariantCulture), file)); }
        catch (Exception e) { MelonLogger.Warning("TV+ group watch could not send: " + e.Message); }
    }

    public static bool TryTake(out Msg m) => Inbox.TryDequeue(out m);

    static void Receive(string text)
    {
        var m = Parse(text, Me);
        if (m != null) Inbox.Enqueue(m); // handled on the next frame, by the Videos app
    }

    /// <summary>
    /// These come from other players, so only our exact format is accepted: a known command, a sane time,
    /// and a plain .mp4 file name (no folders), which is only ever looked up inside the group watch folder.
    /// </summary>
    internal static Msg Parse(string text, string me)
    {
        var p = text?.Split('|');
        if (p == null || p.Length != 5 || p[0] != Tag || p[1] == me || Array.IndexOf(Commands, p[2]) < 0) return null;
        if (!float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var t) || !(t >= 0 && t <= 86400)) return null;
        var file = p[4];
        if (file.Length == 0 || file.Length > 200 || Path.GetFileName(file) != file || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || file.StartsWith(".") || !file.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) return null;
        return new Msg(p[2], t, file);
    }
}
