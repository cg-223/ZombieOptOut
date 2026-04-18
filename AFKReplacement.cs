using CustomPlayerEffects;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Features.Extensions;
using MEC;
using PlayerRoles;
using SimpleCustomRoles.RoleYaml;

namespace ZombieOptOut;

public class AFKReplacement
{
    private static bool withinRoundStart = true;
    public static Dictionary<RoleTypeId, CustomRoleBaseInfo> cachedCustomRole = new Dictionary<RoleTypeId, CustomRoleBaseInfo>();
    public static Dictionary<RoleTypeId, float> disconnectedRoleQueue = new Dictionary<RoleTypeId, float>();
    public static bool canReplace = false;
    //Uses IP instead of player info directly, otherwise references would be lost on disconnect
    public static List<string> offendingPlayers = new();
    private static PlayerChangingRoleEventArgs cachedArgs;
    private static CoroutineHandle fillTimerCoroutine;

    //TODO: Queue disconnected players and roles, framework is already mostly in place

    public static void OnServerRoundStarted()
    {
        if (!Main.Instance.Config.AFKReplacement)
            return;

        withinRoundStart = true;
        canReplace = false;
        cachedCustomRole.Clear();
        offendingPlayers.Clear();
        disconnectedRoleQueue.Clear();

        Timing.CallDelayed(Main.Instance.Config.AFKReplacementValidTime, () => withinRoundStart = false);
    }

    //Caching information before disconnect or when a main SCP spawns
    public static void OnRoleChanging(PlayerChangingRoleEventArgs ev)
    {
        if (!Main.Instance.Config.AFKReplacement)
            return;
        if (!withinRoundStart)
            return;
        if (ev.Player == null)
            return;
        if (ev.Player.IsDummy)
            return;

        cachedArgs = ev;

        //Caches custom role when it initially spawns (Anything -> SCP), needed to save custom role info
        if (cachedArgs.NewRole.IsScp() && cachedArgs.NewRole != RoleTypeId.Scp0492)
        {
            CustomRoleBaseInfo savedCustomRole = null;

            if (cachedCustomRole.ContainsKey(cachedArgs.NewRole))
                cachedCustomRole.Remove(cachedArgs.NewRole);

            if (SimpleCustomRoles.Helpers.CustomRoleHelpers.TryGetCustomRole(cachedArgs.Player, out savedCustomRole))
                cachedCustomRole.Add(cachedArgs.NewRole, savedCustomRole);
            else
                cachedCustomRole.Add(cachedArgs.NewRole, null);
        }
    }

    internal static void OnPlayerDying(PlayerDyingEventArgs ev)
    {
        if (!withinRoundStart)
            return;

        if (ev.Player.Role.IsScp() && ev.Player.Role != RoleTypeId.Scp0492)
        {
            if (ev.Attacker != null)
                return;
            if (ev.Player.IsDummy)
                return;
            if (ev.Player.HasEffect<PitDeath>())
                return;

            if (disconnectedRoleQueue.ContainsKey(ev.Player.Role))
                disconnectedRoleQueue.Remove(ev.Player.Role);

            if (!Main.Instance.Config.DisableXPLoss)
                XPSystem.BackEnd.XpSystemAPI.AddXP(ev.Player, -500, "<b>Disconnected as an SCP</b>", "red");

            if (!offendingPlayers.Contains(ev.Player.IpAddress))
                offendingPlayers.Add(ev.Player.IpAddress);

            disconnectedRoleQueue.Add(ev.Player.Role, CacheHealth(ev.Player));
            AllowReplacement();
        }
    }

    //Caches health if the player suicides off the map
    internal static void OnUpdatingEffects(PlayerEffectUpdatingEventArgs ev)
    {
        if (!Main.Instance.Config.AFKReplacement)
            return;
        if (!ev.Player.Role.IsScp())
            return;
        if (ev.Player.IsDummy)
            return;
        if (!withinRoundStart)
            return;
        if (ev.Player.Role == RoleTypeId.Scp0492)
            return;

        if (ev.Effect.name.ToLower() == "pitdeath")
        {
            if (disconnectedRoleQueue.ContainsKey(ev.Player.Role))
                disconnectedRoleQueue.Remove(ev.Player.Role);

            disconnectedRoleQueue.Add(ev.Player.Role, CacheHealth(ev.Player));

            if (!Main.Instance.Config.DisableXPLoss)
                XPSystem.BackEnd.XpSystemAPI.AddXP(ev.Player, -500, "<b>Suicided as an SCP</b>", "red");

            if (!offendingPlayers.Contains(ev.Player.IpAddress))
                offendingPlayers.Add(ev.Player.IpAddress);

            AllowReplacement();
        }
    }

    private static void AllowReplacement()
    {
        if (!withinRoundStart)
            return;
        if (Warhead.IsDetonated)
            return;

        canReplace = true;

        foreach (Player player in Player.ReadyList)
        {
            if (player.IsSCP)
                continue;

            if (SimpleCustomRoles.Helpers.CustomRoleHelpers.TryGetCustomRole(player, out _))
                continue;

            if (player.IsDummy)
                continue;

            player.ClearBroadcasts();
            player.SendBroadcast(MakeBroadcast(), 10);
        }

        if (fillTimerCoroutine != null || !fillTimerCoroutine.IsValid)
            Timing.KillCoroutines(fillTimerCoroutine);

        fillTimerCoroutine = Timing.RunCoroutine(fillTimeout());
    }

    private static float CacheHealth(Player player)
    {
        //Health is 0 when they die and 200 when they disconnect, setting it to -1 here so we don't bother changing health in the future if the role is filled
        if ((int)player.Health == 0 || (int)player.Health == 200)
            return -1f;
        else
            return player.Health;
    }

    private static string MakeBroadcast()
    {
        string broadcast = $"<size=40>[AFK Replacement] <b>{disconnectedRoleQueue.FirstOrDefault().Key}</b>";

        if (cachedCustomRole[disconnectedRoleQueue.FirstOrDefault().Key] == null)
        {
            if (disconnectedRoleQueue.FirstOrDefault().Value != -1f)
                broadcast += $" ({disconnectedRoleQueue.FirstOrDefault().Value}hp) ";
        }
        else
        {
            if (disconnectedRoleQueue.FirstOrDefault().Value != -1f)
                broadcast += $" ({cachedCustomRole[disconnectedRoleQueue.FirstOrDefault().Key].Rolename} | {disconnectedRoleQueue.FirstOrDefault().Value}hp) ";
            else
                broadcast += $" ({cachedCustomRole[disconnectedRoleQueue.FirstOrDefault().Key].Rolename}) ";
        }


        return (broadcast + "has disconnected!\n </size><size=34> You can take their spot by typing <b>.fill</b> in your console (`)!</size>");
    }

    public static void OnFilling(Player fillingPlayer)
    {
        canReplace = false;

        if (cachedCustomRole[disconnectedRoleQueue.FirstOrDefault().Key] != null)
        {
            Server.RunCommand($"/scr set {cachedCustomRole[disconnectedRoleQueue.FirstOrDefault().Key].Rolename} {fillingPlayer.PlayerId}");
        }
        else
        {
            fillingPlayer.SetRole(disconnectedRoleQueue.FirstOrDefault().Key);
        }

        Server.ClearBroadcasts();
        Server.SendBroadcast($"[AFK Replacement] {disconnectedRoleQueue.FirstOrDefault().Key} has been replaced!", 5);

        if (!Main.Instance.Config.DisableXPLoss)
            XPSystem.BackEnd.XpSystemAPI.AddXP(fillingPlayer, 150, "Filled for an SCP [+150]");

        Timing.CallDelayed(3f, () =>
        {
            if (disconnectedRoleQueue.FirstOrDefault().Value != -1f)
                fillingPlayer.Health = disconnectedRoleQueue.FirstOrDefault().Value;

            disconnectedRoleQueue.Clear();
        });

        if (fillTimerCoroutine != null || fillTimerCoroutine.IsValid)
            Timing.KillCoroutines(fillTimerCoroutine);
    }

    private static IEnumerator<float> fillTimeout()
    {
        yield return Timing.WaitForSeconds(Main.Instance.Config.SCPFillDuration);
        disconnectedRoleQueue.Clear();
        canReplace = false;
    }
}