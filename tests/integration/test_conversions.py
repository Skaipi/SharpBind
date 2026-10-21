"""Checks against the freshly installed fixture wheel."""

import inspect
import unittest
import numpy as np
import sharpbind_fixture as fixture


class ConversionTests(unittest.TestCase):
    def test_arrays_are_copied_into_managed_storage(self):
        for step in (1, 2):
            with self.subTest(step=step):
                source = np.arange(8, dtype=np.int32)[::step]
                expected = source.copy()
                result = fixture.AccumulatorInt32.echo(source)
                source[:] = -1
                np.testing.assert_array_equal(result, expected)
                self.assertTrue(result.flags.owndata)

    def test_empty_and_null_arrays(self):
        self.assertEqual(fixture.AccumulatorDouble.echo([]).size, 0)
        self.assertIsNone(fixture.Functions.null_array())

    def test_copied_inputs_can_normalize_unaligned_storage(self):
        values = np.ndarray((4,), dtype=np.float64, buffer=bytearray(33), offset=1)
        values[:] = np.arange(4)
        self.assertFalse(values.flags.aligned)
        np.testing.assert_array_equal(fixture.AccumulatorDouble.echo(values), values)

    def test_array_conversions_reject_invalid_values(self):
        for value, error in [
            ([2**31], OverflowError),
            ([1.5], TypeError),
            (None, TypeError),
        ]:
            with self.subTest(value=value), self.assertRaises(error):
                fixture.AccumulatorInt32.echo(value)

    def test_nullable_unicode_strings_round_trip(self):
        for text in (None, "", "żółć\0hello 🌍"):
            with self.subTest(text=text):
                self.assertEqual(fixture.Functions.echo_text(text), text)

    def test_optional_string_defaults_to_none(self):
        self.assertIsNone(fixture.Functions.echo_text())
        self.assertIsNone(
            inspect.signature(fixture.Functions.echo_text).parameters["value"].default
        )

    def test_boolean_conversion(self):
        self.assertIs(fixture.Functions.negate(True), False)
        with self.assertRaises(ValueError):
            fixture.Functions.negate(2)

    def test_managed_errors_preserve_type_and_message(self):
        with self.assertRaises(RuntimeError) as raised:
            fixture.Functions.fail()
        self.assertEqual(raised.exception.category, "managed")
        self.assertEqual(
            raised.exception.managed_type, "System.InvalidOperationException"
        )
        self.assertIn("fixture failure", str(raised.exception))

    def test_numeric_abi_widths(self):
        for name, value in [
            ("byte", 255),
            ("s_byte", -128),
            ("short", -32768),
            ("u_short", 65535),
            ("u_int", 2**32 - 1),
            ("long", -(2**63)),
            ("u_long", 2**64 - 1),
            ("float", 1.25),
        ]:
            with self.subTest(type=name, value=value):
                self.assertEqual(getattr(fixture.Functions, name)(value), value)

    def test_integer_conversion_rejects_overflow_and_floats(self):
        with self.assertRaises(OverflowError):
            fixture.Functions.identity_int(2**31)
        with self.assertRaises(TypeError):
            fixture.Functions.identity_int(1.5)
