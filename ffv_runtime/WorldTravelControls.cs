// Controller policy independent of Unity: edges, reconnect/focus rearming and repeat.
namespace ProjectBlues.Runtime
{
    internal readonly struct TravelPad
    {
        internal readonly int Device;
        internal readonly bool Focus, View, Y, A, B, Up, Down, Any;
        internal TravelPad(int device, bool focus, bool view, bool y, bool a, bool b, bool up, bool down, bool any)
        { Device = device; Focus = focus; View = view; Y = y; A = a; B = b; Up = up; Down = down; Any = any; }
    }

    internal sealed class WorldTravelControls
    {
        private TravelPad previous;
        private bool ready;
        private int direction;
        private long nextRepeat;
        internal bool Open { get; private set; }
        internal bool Confirm { get; private set; }
        internal bool Cancel { get; private set; }
        internal int Step { get; private set; }
        internal void Reset()
        {
            previous = default; ready = false; direction = 0; nextRepeat = 0;
            Open = Confirm = Cancel = false; Step = 0;
        }
        internal void Update(TravelPad pad, long now)
        {
            Open = Confirm = Cancel = false; Step = 0;
            if (!pad.Focus || pad.Device == 0 || pad.Device != previous.Device || !previous.Focus)
            { ready = false; direction = 0; }
            if (pad.Focus && pad.Device != 0)
            {
                if (!ready) { if (!pad.Any) ready = true; }
                else
                {
                    // View must be held first, or pressed together with Y; Y alone remains native.
                    Open = pad.View && pad.Y && !previous.Y;
                    Confirm = pad.A && !previous.A;
                    Cancel = pad.B && !previous.B;
                    int requested = pad.Up == pad.Down ? 0 : pad.Up ? -1 : 1;
                    if (requested != direction)
                    { Step = requested; nextRepeat = now + 350; }
                    else if (requested != 0 && now >= nextRepeat)
                    { Step = requested; nextRepeat = now + 140; }
                    direction = requested;
                }
            }
            previous = pad;
        }
    }
}
