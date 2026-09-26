namespace San9.Application.Presentation;

public sealed class BoundedNumberInput(uint minimum, uint maximum, uint initial)
{
    public uint Minimum { get; } = minimum <= maximum ? minimum : throw new ArgumentOutOfRangeException(nameof(minimum));
    public uint Maximum { get; } = maximum;
    public uint Value { get; private set; } = Math.Clamp(initial, minimum, maximum);
    public bool CanConfirm => Value >= Minimum && Value <= Maximum;
    public void Append(string digits)
    {
        foreach (char digit in digits)
        {
            if (digit is < '0' or > '9') throw new ArgumentException("Digits required.", nameof(digits));
            Value = (uint)Math.Min(Maximum, (ulong)Value * 10 + (uint)(digit - '0'));
        }
    }
    public void Backspace() => Value /= 10;
    public void Clear() => Value = 0;
    public void Set(uint value) => Value = Math.Clamp(value, Minimum, Maximum);
    public void Step(int amount) => Value = (uint)Math.Clamp((long)Value + amount, Minimum, Maximum);
}
