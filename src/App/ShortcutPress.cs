sealed class ShortcutPress
{
    const int DuplicateWindowMs = 60;
    bool rawKeyDown, havePrevious, previousWasRaw;
    uint previousTime;

    internal void Reset()
    {
        rawKeyDown = havePrevious = false;
    }

    internal bool FromKeyboard(bool down, bool modifiersMatch, uint time)
    {
        if (!down)
        {
            rawKeyDown = false;
            return false;
        }
        if (rawKeyDown) return false;
        rawKeyDown = true;
        return modifiersMatch && Accept(true, time);
    }

    internal bool FromWindows(uint time) { return Accept(false, time); }

    bool Accept(bool raw, uint time)
    {
        int elapsed = unchecked((int)(time - previousTime));
        if (havePrevious && raw != previousWasRaw && elapsed >= -DuplicateWindowMs && elapsed <= DuplicateWindowMs)
            return false;
        havePrevious = true;
        previousWasRaw = raw;
        previousTime = time;
        return true;
    }

    internal static bool SelfTest()
    {
        var press = new ShortcutPress();
        if (!press.FromKeyboard(true, true, 100) || press.FromWindows(101) ||
            press.FromKeyboard(true, true, 450) || press.FromKeyboard(false, true, 500) ||
            !press.FromWindows(600) || press.FromKeyboard(true, true, 599) ||
            press.FromKeyboard(false, true, 610)) return false;
        press.Reset();
        return !press.FromKeyboard(true, false, 700) && !press.FromKeyboard(false, false, 750) &&
            press.FromKeyboard(true, true, uint.MaxValue - 1) && !press.FromWindows(1);
    }
}
