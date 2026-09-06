import struct
import unittest
import hashlib
import io
import tempfile
from contextlib import redirect_stderr
from pathlib import Path
from unittest.mock import patch
from PIL import Image
from verify_initial_ui import (ASSET_GROUPS, expected_roles, parse_args, parse_combined,
                               verify_frame, verify_role_coverage, verify_skin_assets)


class InitialUiAuditTests(unittest.TestCase):
    def test_combined_stream_rejects_wrong_stride_and_length(self):
        image = struct.pack("<4I", 18, 2, 2, 1) + bytes([0, 1])
        data = struct.pack("<I", 1) + bytes(1064) + struct.pack("<I", 22) + image
        palettes, images = parse_combined(data)
        self.assertEqual(1, len(palettes))
        self.assertEqual((2, 1, bytes([0, 1])), images[0])
        with self.assertRaises(ValueError):
            parse_combined(data[:-1])
        wrong = bytearray(data)
        struct.pack_into("<I", wrong, 4 + 1064 + 4 + 4, 3)
        with self.assertRaises(ValueError):
            parse_combined(wrong)

    def test_frames_check_visible_color_and_alpha_but_allow_hidden_color_extension(self):
        palette = bytes([0, 248, 0, 0, 50, 80, 120, 128]) + bytes(1016)
        source = (4, 1, bytes([0, 1, 1, 0]))
        image = Image.frombytes("RGBA", (2, 1), bytes([50, 80, 120, 0, 50, 80, 120, 255]))
        verify_frame(image, source, palette, 2, 0)
        image.putpixel((1, 0), (50, 80, 121, 255))
        with self.assertRaises(AssertionError):
            verify_frame(image, source, palette, 2, 0)

    def test_palette_alpha_allows_hidden_rgb_but_checks_partial_alpha_pixels(self):
        palette = bytes([0, 248, 0, 0, 10, 20, 30, 0, 50, 80, 120, 21]) + bytes(1012)
        source = (2, 1, bytes([1, 2]))
        image = Image.frombytes("RGBA", (2, 1), bytes([50, 80, 120, 0, 50, 80, 120, 42]))
        verify_frame(image, source, palette, 1, 0, True)
        image.putpixel((1, 0), (51, 80, 120, 42))
        with self.assertRaises(AssertionError):
            verify_frame(image, source, palette, 1, 0, True)
        image.putpixel((1, 0), (50, 80, 120, 43))
        with self.assertRaises(AssertionError):
            verify_frame(image, source, palette, 1, 0, True)

    def test_scope_is_explicit_and_baseline_requires_full_audit(self):
        paths = ["sample.exe", "data", "package"]
        for options in [[], ["--scope", "unknown"], ["--scope", "scrollbar", "--all"],
                        ["--scope", "scrollbar", "--baseline", "old"]]:
            with self.subTest(options=options), redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                parse_args(paths + options)
        self.assertEqual(["scrollbar", "roster"], parse_args(paths + ["--scope", "scrollbar", "--scope", "roster"]).scope)
        self.assertTrue(parse_args(paths + ["--all", "--baseline", "old"]).all)

    def test_full_coverage_checks_role_identity_not_just_count(self):
        roles = dict.fromkeys(expected_roles(ASSET_GROUPS), "asset")
        verify_role_coverage({"roles": roles}, ASSET_GROUPS)
        roles["new-unreviewed-role"] = roles.pop("top-frame")
        with self.assertRaisesRegex(AssertionError, "top-frame"):
            verify_role_coverage({"roles": roles}, ASSET_GROUPS)
        # Unselected roles do not change the scope of a local audit.
        verify_role_coverage({"roles": roles}, ["scrollbar"])
        roles["top-frame"] = "asset"
        with self.assertRaisesRegex(AssertionError, "new-unreviewed-role"):
            verify_role_coverage({"roles": roles}, ASSET_GROUPS)

    def test_local_audit_reads_only_selected_assets_and_still_checks_source_pixels(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            # No G_Common, G_Marker, scenario, map or unselected output files exist.
            (root / "G_Main.s9").write_bytes(b"fixture")
            palette = bytes([0, 0, 0, 0, 50, 80, 120, 128]) + bytes(1016)
            images, bindings, assets, roles = [(1, 1, b"\1")] * 98, {}, [], {}
            for name, resource, columns, _ in ASSET_GROUPS["scrollbar"][3]:
                images[resource - 5975] = (columns, 1, b"\1" * columns)
                bindings[resource] = 66 if resource != 6060 else 76
                for frame in range(columns):
                    role = name if frame == 0 else f"{name}-{frame}"
                    path = root / f"{role}.png"
                    Image.new("RGBA", (1, 1), (50, 80, 120, 255)).save(path)
                    roles[role] = role
                    assets.append({"id": role, "path": path.name,
                                   "sha256": hashlib.sha256(path.read_bytes()).hexdigest().upper()})
            roles["city-prefix"] = "absent-unselected-file"
            skin = {"roles": roles, "assets": assets}
            with patch("verify_initial_ui.parse_combined", return_value=([palette] * 21, images)) as parser:
                result = verify_skin_assets(root, root, skin, bindings, ["scrollbar"])
                self.assertEqual(len(expected_roles(["scrollbar"])), result["scrollbar"])
                parser.assert_called_once_with(b"fixture")
                target = next(a for a in assets if a["id"] == "button-scroll-up-3")
                path = root / target["path"]
                Image.new("RGBA", (1, 1), (51, 80, 120, 255)).save(path)
                with self.assertRaises(AssertionError):
                    verify_skin_assets(root, root, skin, bindings, ["scrollbar"])
                target["sha256"] = hashlib.sha256(path.read_bytes()).hexdigest().upper()
                with self.assertRaisesRegex(AssertionError, "Source pixels mismatch: button-scroll-up-3"):
                    verify_skin_assets(root, root, skin, bindings, ["scrollbar"])


if __name__ == "__main__":
    unittest.main()
