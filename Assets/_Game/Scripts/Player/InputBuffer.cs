namespace TanShadow.Player
{
    public enum BufferedAction
    {
        None,
        Attack,
        Dash
    }

    // Помнит последнее нажатие, пока оно не устарело: атака, нажатая чуть раньше конца анимации, не теряется.
    public class InputBuffer
    {
        BufferedAction action;
        float pressTime;

        public void Press(BufferedAction pressed, float now)
        {
            action = pressed;
            pressTime = now;
        }

        public bool Consume(BufferedAction wanted, float now, float window)
        {
            if (action != wanted || now - pressTime > window) return false;
            action = BufferedAction.None;
            return true;
        }

        public void Clear() => action = BufferedAction.None;
    }
}
