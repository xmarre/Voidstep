#!/usr/bin/env python3
import unittest
from metadata_reader import CLR


class MetadataReaderTests(unittest.TestCase):
    def test_compressed_integer_variants(self):
        reader = CLR.__new__(CLR)
        self.assertEqual((0x7F, 1), reader.comp(bytes([0x7F])))
        self.assertEqual((0x1234, 2), reader.comp(bytes([0x92, 0x34])))
        self.assertEqual((0x1234567, 4), reader.comp(bytes([0xC1, 0x23, 0x45, 0x67])))

    def test_invalid_metadata_signature_raises_value_error(self):
        reader = CLR.__new__(CLR)
        reader.data = b'NOPE' + b'\0' * 32
        reader.meta_off = 0
        with self.assertRaisesRegex(ValueError, 'BSJB'):
            reader._parse_meta()


if __name__ == '__main__':
    unittest.main(verbosity=2)
