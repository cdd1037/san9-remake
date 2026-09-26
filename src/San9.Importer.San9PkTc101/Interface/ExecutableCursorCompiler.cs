using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using San9.Content.Images;
using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

internal static class ExecutableCursorCompiler
{
    public static Dictionary<string, CursorDefinition> Compile(PEReader pe, List<ImageAsset> assets,
        Func<string, string, PngImage, ImageAsset> save)
    {
        var table = pe.PEHeaders.PEHeader!.ResourceTableDirectory;
        byte[] directory = pe.GetSectionData(table.RelativeVirtualAddress).GetContent(0, table.Size).ToArray();
        int FindDirectory(int offset, int id)
        {
            int count = U16(directory, offset + 12) + U16(directory, offset + 14);
            for (int i = 0; i < count; i++)
            {
                int entry = checked(offset + 16 + 8 * i);
                if (U32(directory, entry) != id) continue;
                uint child = U32(directory, entry + 4);
                if ((child & 0x80000000) == 0) break;
                return checked((int)(child & 0x7fffffff));
            }
            throw new InvalidDataException($"Missing cursor resource directory {id}.");
        }
        byte[] Read(int type, int id)
        {
            int language = FindDirectory(FindDirectory(0, type), id);
            if (U16(directory, language + 12) != 0 || U16(directory, language + 14) != 1)
                throw new InvalidDataException("Ambiguous cursor resource language.");
            int data = checked((int)U32(directory, language + 20));
            int rva = checked((int)U32(directory, data)), size = checked((int)U32(directory, data + 4));
            return pe.GetSectionData(rva).GetContent(0, size).ToArray();
        }

        string[] directions = ["up", "right", "down", "left", "up-right", "down-right", "down-left", "up-left"];
        Dictionary<string, CursorDefinition> cursors = [];
        for (int state = 0; state < 2; state++)
        for (int direction = 0; direction < directions.Length; direction++)
        {
            int id = 12000 + state * 8 + direction;
            byte[] group = Read(12, id);
            if (group.Length != 20 || U16(group, 0) != 0 || U16(group, 2) != 2 || U16(group, 4) != 1)
                throw new InvalidDataException($"Unsupported cursor group {id}.");
            byte[] source = Read(1, U16(group, 18));
            var image = Decode(source);
            if (source.Length != U32(group, 14) || U16(group, 6) != image.Width ||
                U16(group, 8) != image.Height * 2 || U16(group, 10) != 1 || U16(group, 12) != 8)
                throw new InvalidDataException($"Cursor group/image mismatch {id}.");
            string role = $"scroll-{directions[direction]}-{(state == 0 ? "idle" : "active")}";
            string asset = "cursor-" + role;
            assets.Add(save(asset, $"executable/cursors/{id}.png", image));
            cursors.Add(role, new(asset, U16(source, 0), U16(source, 2)));
        }
        return cursors;
    }

    // TC101 uses bottom-up, uncompressed 8-bit DIBs followed by a 1-bit AND mask.
    private static PngImage Decode(byte[] source)
    {
        if (source.Length < 44 || U32(source, 4) != 40 || U16(source, 16) != 1 ||
            U16(source, 18) != 8 || U32(source, 20) != 0)
            throw new InvalidDataException("Unsupported cursor DIB.");
        int width = checked((int)U32(source, 8)), doubledHeight = checked((int)U32(source, 12));
        if (width is <= 0 or > 128 || doubledHeight is <= 0 or > 256 || doubledHeight % 2 != 0)
            throw new InvalidDataException("Invalid cursor dimensions.");
        int height = doubledHeight / 2;
        if (U16(source, 0) >= width || U16(source, 2) >= height)
            throw new InvalidDataException("Cursor hotspot outside image.");
        int colors = checked((int)U32(source, 36));
        if (colors == 0) colors = 256;
        if (colors > 256) throw new InvalidDataException("Invalid cursor palette.");
        int xorStart = 44 + colors * 4, xorStride = (width + 3) / 4 * 4;
        int andStart = xorStart + xorStride * height, andStride = (width + 31) / 32 * 4;
        if (source.Length != andStart + andStride * height)
            throw new InvalidDataException("Invalid cursor mask length.");
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int row = height - y - 1, index = source[xorStart + row * xorStride + x];
            if (index >= colors) throw new InvalidDataException("Cursor palette index out of range.");
            int palette = 44 + index * 4, target = (y * width + x) * 4;
            bool transparent = (source[andStart + row * andStride + x / 8] & (128 >> (x % 8))) != 0;
            if (transparent && (source[palette] != 0 || source[palette + 1] != 0 || source[palette + 2] != 0))
                throw new InvalidDataException("Inverting cursor pixels cannot be represented by RGBA.");
            pixels[target] = source[palette + 2]; pixels[target + 1] = source[palette + 1];
            pixels[target + 2] = source[palette]; pixels[target + 3] = transparent ? (byte)0 : (byte)255;
        }
        return new(width, height, 4, pixels);
    }

    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(Slice(bytes, offset, 2));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset, 4));
    private static ReadOnlySpan<byte> Slice(byte[] bytes, int offset, int size)
    {
        if (offset < 0 || offset > bytes.Length - size) throw new InvalidDataException("Truncated cursor resource.");
        return bytes.AsSpan(offset, size);
    }
}
