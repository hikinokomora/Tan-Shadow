namespace ShadowTan.Core
{
    // Имена слоёв из ARCHITECTURE §7.5. Индексы живут в ProjectSettings и проверяются редакторским Setup.
    public static class Layers
    {
        public const string Ground = "Ground";
        public const string Wall = "Wall";
        public const string Cover = "Cover";
        public const string Player = "Player";
        public const string PlayerHitbox = "PlayerHitbox";
        public const string Colossus = "Colossus";
        public const string WeakPoint = "WeakPoint";
        public const string GrabPoint = "GrabPoint";
        public const string KillVolume = "KillVolume";
        public const string Trigger = "Trigger";
        public const string Lava = "Lava";
        public const string CameraOnly = "CameraOnly";
        public const string Interactable = "Interactable";

        public const int GroundIndex = 3;
        public const int WallIndex = 6;
        public const int CoverIndex = 7;
        public const int PlayerIndex = 8;
        public const int PlayerHitboxIndex = 9;
        public const int ColossusIndex = 10;
        public const int WeakPointIndex = 11;
        public const int GrabPointIndex = 12;
        public const int KillVolumeIndex = 13;
        public const int TriggerIndex = 14;
        public const int LavaIndex = 15;
        public const int CameraOnlyIndex = 16;
        public const int InteractableIndex = 17;
    }
}
