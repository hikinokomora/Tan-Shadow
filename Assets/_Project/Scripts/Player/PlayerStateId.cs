namespace ShadowTan.Player
{
    // Все состояния героини (§9.5). Имена попадают в отладочный оверлей.
    public enum PlayerStateId
    {
        Disabled,
        Locomote,
        Roll,
        Attack,
        Hang,
        HangAttack,
        HangStab,
        Fall,
        Dead
    }
}
