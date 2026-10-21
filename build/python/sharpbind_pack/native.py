"""Validate staged library architecture before assigning a wheel tag."""

import struct


def validate_native_library(path, target):
    try:
        data = path.read_bytes()
        if target.rid == "win-x64":
            _pe(data)
        elif target.rid == "linux-x64":
            _elf(data)
        elif target.rid == "osx-arm64":
            _macho(data)
        else:
            raise ValueError(f"Unsupported native target: {target.rid}")
    except (struct.error, IndexError) as error:
        raise ValueError(f"Truncated native library: {path}") from error


def _pe(data):
    if data[:2] != b"MZ":
        raise ValueError("Not a Windows DLL")
    (offset,) = struct.unpack_from("<I", data, 0x3C)
    signature, machine = struct.unpack_from("<4sH", data, offset)
    if signature != b"PE\0\0" or machine != 0x8664:
        raise ValueError("Expected a Windows x64 DLL")
    if struct.unpack_from("<H", data, offset + 24)[0] != 0x20B:
        raise ValueError("Expected a PE32+ DLL")
    clr = struct.unpack_from("<II", data, offset + 24 + 112 + 14 * 8)
    if clr != (0, 0):
        raise ValueError("Expected Native AOT output, received a managed DLL")


def _elf(data):
    if data[:6] != b"\x7fELF\x02\x01":
        raise ValueError("Expected a little-endian ELF64 library")
    kind, machine = struct.unpack_from("<HH", data, 16)
    if kind != 3 or machine != 62:
        raise ValueError("Expected a Linux x64 shared library")


def _macho(data):
    magic, cpu, subtype, kind = struct.unpack_from("<IIII", data)
    if magic != 0xFEEDFACF or cpu != 0x0100000C or kind != 6:
        raise ValueError("Expected an ARM64 Mach-O dynamic library")
