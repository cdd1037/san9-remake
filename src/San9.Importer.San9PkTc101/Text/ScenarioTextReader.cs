using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Text;

public sealed class ScenarioTextReader(byte[] messages, byte[] executable) : IDisposable
{
    private readonly San9MessageTable _messages = new(messages);
    private readonly PEReader _executable = new(new MemoryStream(executable));
    private readonly Dictionary<string, TextEntry> _entries = [];

    public TextCatalog Catalog => new(1, new(_entries));
    private string Add(string key, string source)
    {
        _entries.Add(key, new(NameLocalizer.ToSimplified(source), source)); return key;
    }
    public string City(string key, int code) => Add(key, _messages.Name(721 + 2 * code));
    public string Region(string key, int code) => Add(key, _messages.Name(166 + 2 * code));
    public string Settlement(string key, int code, int type) => Add(key, _messages.Name(166 + 2 * code) + TypeSuffix(type));
    public string Person(string key, int family, int given)
    {
        if (family >= 700 || given >= 700) throw new InvalidDataException("Unsupported generated leader name.");
        return Add(key, _messages.Name(1678 + 4 * family) + _messages.Name(1679 + 4 * given));
    }
    private string TypeSuffix(int type)
    {
        int label = type switch { 1 => 1, 6 => 3, 7 => 4, 8 => 5, 9 => 6, 10 or 11 => 7, 12 or 13 => 8, 14 or 15 => 9, 16 => 10, _ => -1 };
        if (label < 0) return "";
        var table = _executable.GetSectionData(0x26a90c).GetContent(4 * label, 4);
        int pointer = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan());
        int rva = checked(pointer - (int)_executable.PEHeaders.PEHeader!.ImageBase);
        var data = _executable.GetSectionData(rva).GetContent(0, 64).ToArray();
        int end = Array.IndexOf(data, (byte)0);
        if (end < 0) throw new InvalidDataException("Unterminated building label.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(data, 0, end);
    }
    public void Dispose() => _executable.Dispose();
}
