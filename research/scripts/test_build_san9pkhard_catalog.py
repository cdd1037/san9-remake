import json
import tempfile
import unittest
from pathlib import Path

from build_san9pkhard_catalog import build_catalog, build_status


class BuildSan9PkHardCatalogTests(unittest.TestCase):
    def test_extracts_address_thunk_old_bytes_write_and_config(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "sample.h").write_text(
                """
#define BYTE_SIZE 1
BYTE codeFooThunk[] = {0x90, 0x90};
BYTE codeFooOldThunk[] = {0x75, 0x04};
ASM_PATCH patchFooThunk[] = {
  {0x401000, sizeof(codeFooThunk), codeFooThunk},
  {0x401000, sizeof(codeFooOldThunk), codeFooOldThunk}};
""",
                encoding="utf-8",
            )
            (root / "sample.cpp").write_text(
                """
Thing::Thing() : m_dwBarAddr(0x401020), m_dwOldBar(0x75) {}
void f() {
  DDX_Check(pDX, IDC_BAR, m_bBar);
  WriteProcessMemory(h, (LPVOID)m_dwBarAddr, &m_dwOldBar, BYTE_SIZE, 0);
}
""",
                encoding="utf-8",
            )
            for name in ("San9PKHardPara.txt", "San9PKHardPerson.txt", "San9PKHardCity.txt", "San9PKHardArray.txt", "San9PKHardSkill.txt"):
                (root / name).write_text("1\n2\n", encoding="utf-8")
            catalog = build_catalog(root)
        self.assertEqual(catalog["summary"]["patch_entries"], 2)
        self.assertEqual(catalog["sections"]["old_byte_records"][0]["expected_old_hex"], "75 04")
        self.assertEqual(catalog["sections"]["config_bindings"][0]["field"], "m_bBar")
        self.assertEqual(catalog["sections"]["write_calls"][0]["role"], "restore-old-bytes")
        scalar = [item for item in catalog["sections"]["old_byte_records"] if item.get("old_symbol")]
        self.assertEqual(scalar[0]["expected_old_hex"], "75")
        self.assertFalse(catalog["issues"].get("registration_errors"))
        status = build_status(catalog)
        self.assertEqual(status["section_status"]["patch_entries"]["count"], 2)
        self.assertEqual(len(status["catalog_sha256"]), 64)

    def test_detects_mismatched_registration_and_missing_old_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "sample.h").write_text(
                """
BYTE codeBadThunk[] = {0x90, 0x90};
BYTE codeBadOldThunk[] = {0x75};
ASM_PATCH patchBadThunk[] = {
  {0x401000, sizeof(codeBadThunk), codeBadThunk},
  {0x401001, sizeof(codeBadOldThunk), codeBadOldThunk}};
BYTE codeMissingThunk[] = {0x90};
ASM_PATCH patchMissingThunk[] = {
  {0x402000, sizeof(codeMissingThunk), codeMissingThunk}};
""",
                encoding="utf-8",
            )
            for name in ("San9PKHardPara.txt", "San9PKHardPerson.txt", "San9PKHardCity.txt", "San9PKHardArray.txt", "San9PKHardSkill.txt"):
                (root / name).write_text("1\n", encoding="utf-8")
            catalog = build_catalog(root)
        reasons = {item["reason"] for item in catalog["issues"]["registration_errors"]}
        self.assertIn("install-restore-address-mismatch", reasons)
        self.assertEqual(catalog["issues"]["missing_old_bytes"][0]["patch"], "patchMissingThunk")

    def test_registers_runtime_captured_old_bytes_and_parameter_bits(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "sample.h").write_text(
                """
BYTE codeCaptureThunk[] = {0x90};
BYTE codeCaptureOldThunk[sizeof(codeCaptureThunk)];
ASM_PATCH patchCaptureThunk[] = {
  {0x401000, sizeof(codeCaptureThunk), codeCaptureThunk},
  {0x401000, sizeof(codeCaptureOldThunk), codeCaptureOldThunk}};
""",
                encoding="utf-8",
            )
            (root / "sample.cpp").write_text(
                "m_dwComboData |= m_bOption << 7;\nm_bOption = (m_dwComboData>>7) & 1;\n",
                encoding="utf-8",
            )
            for name in ("San9PKHardPara.txt", "San9PKHardPerson.txt", "San9PKHardCity.txt", "San9PKHardArray.txt", "San9PKHardSkill.txt"):
                (root / name).write_text("1\n", encoding="utf-8")
            catalog = build_catalog(root)
        old = catalog["sections"]["old_byte_records"][0]
        self.assertEqual(old["status"], "runtime-captured")
        self.assertEqual(len(catalog["sections"]["config_bit_mappings"]), 2)

    def test_preserves_block_commented_observation_as_inactive(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "sample.h").write_text(
                """/*
DWORD dwAddrDisabled[] = {0x401000};
BYTE codeDisabledThunk[] = {0x90};
*/
""",
                encoding="utf-8",
            )
            for name in ("San9PKHardPara.txt", "San9PKHardPerson.txt", "San9PKHardCity.txt", "San9PKHardArray.txt", "San9PKHardSkill.txt"):
                (root / name).write_text("1\n", encoding="utf-8")
            catalog = build_catalog(root)
        address = catalog["sections"]["address_array_entries"][0]
        thunk = catalog["sections"]["byte_arrays"][0]
        self.assertFalse(address["active"])
        self.assertEqual(address["exclusion_reason"], "commented-out")
        self.assertFalse(thunk["active"])


if __name__ == "__main__":
    unittest.main()
