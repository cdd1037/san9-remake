namespace San9.Rendering;

public static class ButtonVisualState
{
    public static (int X, int Y) TextOffset(bool selected) => selected ? (2, 2) : (0, 0);

    public static int Frame(bool territory, bool hovered, bool selected, bool disabled)
    {
        if (disabled) return territory ? (selected ? 5 : 4) : 3;
        if (selected) return territory && hovered ? 3 : 2;
        return hovered ? 1 : 0;
    }
}
