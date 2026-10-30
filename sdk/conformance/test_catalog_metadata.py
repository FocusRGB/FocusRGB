"""Regression tests for optional catalog progress metadata; standard library only."""
import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location(
    "conformance", pathlib.Path(__file__).with_name("plugin-conformance.py"))
conformance = importlib.util.module_from_spec(spec)
spec.loader.exec_module(conformance)


class CatalogMetadataTests(unittest.TestCase):
    def test_partial_catalog_accepts_empty_items_and_extensible_reasons(self):
        for support in ("supported", "unverified"):
            conformance.require_catalog_metadata({"support": support, "items": [], "reason": {
                "code": "catalog-scan-incomplete", "message": "Scan continues."}})
        conformance.require_catalog_metadata({"support": "supported", "items": []})
        conformance.require_catalog_metadata({"reason": {"code": "plugin.custom", "message": "Details"}})

    def test_partial_catalog_rejects_fresh_timestamp_unsupported_or_empty_message(self):
        base = {"support": "supported", "items": [], "reason": {
            "code": "catalog-scan-incomplete", "message": "Scan continues."}}
        for changed in ({"refreshedAt": "2026-10-26T00:00:00Z"}, {"refreshedAt": None},
                        {"support": "unsupported"}, {"reason": {
                            "code": "catalog-scan-incomplete", "message": ""}}):
            with self.subTest(changed=changed), self.assertRaises(conformance.ProtocolError):
                conformance.require_catalog_metadata(base | changed)
