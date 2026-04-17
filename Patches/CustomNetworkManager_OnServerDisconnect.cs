using CustomPlayerEffects;
using HarmonyLib;
using Mirror;
using PlayerRoles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZombieOptOut.Patches;


// this simple patch just moves the logic for handling disconnected players to before they take damage
// this is as to cache their health before they died
[HarmonyPatch(typeof(CustomNetworkManager), nameof(CustomNetworkManager.OnServerDisconnect))]
internal class CustomNetworkManager_OnServerDisconnect
{
    public static void Prefix(CustomNetworkManager __instance, NetworkConnectionToClient conn)
    {
        if (__instance._disconnectDrop)
        {
            AFKReplacement.OnDisconnected(Player.Get(conn.identity));
        }
    }
}
