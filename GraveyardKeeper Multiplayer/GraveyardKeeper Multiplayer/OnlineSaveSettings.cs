using System;

namespace GraveyardKeeperMultiplayer
{
    // Plain data class that holds the configurable settings for an online session.
    // An instance of this class is serialised to JSON and stored in the .gkmp companion
    // file alongside each online save. It is read back when the host loads the save so
    // the correct rules are applied for that session.
    [Serializable]
    public class OnlineSaveSettings
    {
        // Always true for online saves — used as a quick presence check when reading the file
        public bool isOnline = true;

        // Whether both players draw from a shared coin pool (true) or keep separate wallets (false)
        public bool sharedMoney = false;

        // Controls how experience points and skill unlocks are handled:
        //   0 = Individual — each player earns and spends XP independently (default)
        //   1 = Shared XP  — both players gain the same XP from any source
        //   2 = Shared XP + Skills — XP and skill points are pooled together
        public int experienceMode = 0;

        // Whether players can damage each other (true) or are immune to each other (false)
        public bool friendlyFire = false;
    }
}
