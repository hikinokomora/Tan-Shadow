namespace ShadowTan.Core.Contracts
{
    public enum DeathCause
    {
        DragonThrow,
        TigerPounce,
        SnakeBite,
        Lava,
        Overheat,
        Slip,
        Fall,
        Other
    }

    public interface IKillSource
    {
        DeathCause Cause { get; }
    }
}
