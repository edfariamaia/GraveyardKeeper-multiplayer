using System;
using HarmonyLib;

namespace GraveyardKeeperMultiplayer
{
    // Harmony postfix patch on MainGame.GeneralInit.
    // GeneralInit is the game's primary initialisation method — it runs once when a new
    // game session starts. We use it as the earliest safe moment to create all mod
    // managers, because at this point the game engine is fully set up but no gameplay
    // logic has run yet.
    [HarmonyPatch(typeof(MainGame), "GeneralInit")]
    public class PatchStartGame
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                // Spin up every manager as a persistent Unity GameObject.
                // Order matters: NetworkManager must exist before the others,
                // because they may call NetworkManager.SendRaw during Init.
                NetworkManager.Init();
                RequestManager.Create();
                PacketQueue.Create();
                ReliablePacketSender.Create();
                WorldStateSync.Create();
                RemotePlayerAvatar.Create();
                OnlineSettingsMenu.Create();
                SaveTransferManager.Create();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Erro: " + ex.Message);
            }
        }
    }
}
