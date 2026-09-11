import unittest
from executable_strings import scan_null_terminated_big5
from ls11_archive import split_indexed_records


class ExecutableStringTests(unittest.TestCase):
    def test_scanner_requires_strict_encoding_and_terminator(self):
        raw = "洛陽".encode("cp950")
        rows = scan_null_terminated_big5(b"ASCII\0" + raw + b"\0\xff\0" + raw)
        self.assertEqual([(r.offset, r.text) for r in rows], [(6, "洛陽")])

    def test_independent_container_reader_keeps_internal_delimiters(self):
        import struct
        raw = b"A\x05\x05\x05B\x05\x05\x05"
        result = split_indexed_records(struct.pack("<HI", 1, 6) + raw)
        self.assertEqual(result[2], [raw])


if __name__ == "__main__":
    unittest.main()
