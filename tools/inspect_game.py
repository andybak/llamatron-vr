"""Read-only DOS Llamatron inventory and LZEXE 0.91 load-image extraction.

Generated .bin files are unrelocated analysis images, not runnable EXEs.
LZEXE format reference: https://github.com/mywave82/unlzexe
"""

import argparse
import hashlib
import json
import re
import struct
from pathlib import Path


class Reader:
    def __init__(self, data, position=0):
        self.data = data
        self.position = position

    def byte(self):
        if self.position >= len(self.data):
            raise ValueError(f"Unexpected end at {self.position:#x}")
        value = self.data[self.position]
        self.position += 1
        return value

    def word(self):
        return self.byte() | (self.byte() << 8)


def unpack_lz91(data):
    if data[:2] != b"MZ" or data[28:32] != b"LZ91":
        raise ValueError("Expected an MZ executable packed with LZEXE 0.91")
    header = struct.unpack_from("<14H", data)
    stub = (header[4] + header[11]) * 16
    info = struct.unpack_from("<8H", data, stub)
    start = stub - info[4] * 16
    stream = Reader(data, start)
    bits, remaining = stream.word(), 16

    def bit():
        nonlocal bits, remaining
        value = bits & 1
        remaining -= 1
        # The stub prefetches the next word before consuming more payload.
        if remaining == 0:
            bits, remaining = stream.word(), 16
        else:
            bits >>= 1
        return value

    image = bytearray()
    while len(image) < 1024 * 1024:
        if bit():
            image.append(stream.byte())
            continue
        if bit():
            low, high = stream.byte(), stream.byte()
            distance = (low | ((high & 0xF8) << 5) | 0xE000) - 65536
            length = (high & 7) + 2
            if length == 2:
                extension = stream.byte()
                if extension == 0:
                    break
                if extension == 1:
                    continue
                length = extension + 1
        else:
            length = (bit() << 1) | bit()
            length += 2
            distance = stream.byte() - 256
        if len(image) + distance < 0:
            raise ValueError(f"Invalid backreference at {stream.position:#x}")
        for _ in range(length):
            image.append(image[len(image) + distance])
    else:
        raise ValueError("Decompression exceeded the DOS image size limit")

    # LZ91 relocation deltas; retained for a future emulated-memory loader.
    reloc_stream = Reader(data, stub + 0x158)
    relocation_addresses = []
    segment, offset = 0, 0
    for _ in range(65536):
        delta = reloc_stream.byte()
        if delta == 0:
            delta = reloc_stream.word()
            if delta == 0:
                segment += 0xFFF
                continue
            if delta == 1:
                break
        offset += delta
        segment += offset >> 4
        offset &= 15
        address = segment * 16 + offset
        if address + 2 > len(image):
            raise ValueError(f"Relocation outside image: {address:#x}")
        relocation_addresses.append(address)
    else:
        raise ValueError("Unterminated relocation table")

    entry = info[1] * 16 + info[0]
    if entry >= len(image):
        raise ValueError(f"Entry outside image: {entry:#x}")
    return bytes(image), {
        "entry_cs": info[1], "entry_ip": info[0], "entry_image_offset": entry,
        "stack_ss": info[3], "stack_sp": info[2],
        "compressed_stream_offset": start, "compressed_stream_end": stream.position,
        "relocation_addresses": relocation_addresses,
    }


def iff_chunks(data):
    if data[:4] != b"FORM":
        return None
    chunks, offset = [], 12
    limit = struct.unpack_from(">I", data, 4)[0] + 8
    if limit > len(data):
        raise ValueError("Truncated IFF file")
    while offset + 8 <= limit:
        kind = data[offset:offset + 4].decode("ascii")
        size = struct.unpack_from(">I", data, offset + 4)[0]
        chunk = {"type": kind, "offset": offset + 8, "size": size}
        if kind == "BMHD":
            width, height, _, _, planes, mask, compression = struct.unpack_from(">HHhhBBB", data, offset + 8)
            chunk.update(width=width, height=height, planes=planes,
                         masking=mask, compression=compression)
        chunks.append(chunk)
        offset += 8 + size + (size & 1)
    return {"type": data[8:12].decode("ascii"), "chunks": chunks}


def unpack_exepack(image, metadata):
    """Decode the backward RLE stub found inside the outer LZEXE image."""
    stub = metadata["entry_cs"] * 16
    if image[stub + 14:stub + 16] != b"RB":
        return image, metadata
    ip, cs, _, stub_size, sp, ss, paragraphs, _ = struct.unpack_from("<8H", image, stub)
    size = paragraphs * 16
    if not 0 < size <= 1024 * 1024:
        raise ValueError("Invalid EXEPACK output size")
    result = bytearray(image[:size])
    result.extend(b"\0" * (size - len(result)))
    source, destination = stub - 1, size - 1
    while image[source] == 255:
        source -= 1
    while True:
        control = image[source]
        count = struct.unpack_from("<H", image, source - 2)[0]
        source -= 3
        if count == 0 or destination - count + 1 < 0:
            raise ValueError("Invalid EXEPACK run length")
        if control & 0xFE == 0xB0:
            result[destination - count + 1:destination + 1] = bytes([image[source]]) * count
            source -= 1
        elif control & 0xFE == 0xB2:
            if source - count + 1 < 0:
                raise ValueError("EXEPACK literal outside source")
            result[destination - count + 1:destination + 1] = image[source - count + 1:source + 1]
            source -= count
        else:
            raise ValueError(f"Unknown EXEPACK command {control:#x} at {source + 3:#x}")
        destination -= count
        if control & 1:
            break
    if source != destination:
        raise ValueError(f"EXEPACK prefix mismatch: {source:#x} versus {destination:#x}")
    cursor = Reader(image, stub + 0x132)
    relocations = []
    for segment in range(0, 0x10000, 0x1000):
        for _ in range(cursor.word()):
            address = segment * 16 + cursor.word()
            if address + 2 > len(result):
                raise ValueError("EXEPACK relocation outside output")
            relocations.append(address)
    if cursor.position > stub + stub_size:
        raise ValueError("EXEPACK relocation table exceeds stub")
    return bytes(result), {
        "entry_cs": cs, "entry_ip": ip, "entry_image_offset": cs * 16 + ip,
        "stack_ss": ss, "stack_sp": sp, "relocation_addresses": relocations,
        "inner_packer": "EXEPACK", "outer_lzexe_metadata": metadata,
    }


def coff_wrapper(image):
    """Analysis-only COFF container for LLVM's 16-bit x86 disassembler."""
    header = struct.pack("<HHIIIHH", 0x14C, 1, 0, 0, 0, 0, 0)
    section = struct.pack("<8sIIIIIIHHI", b".text", 0, 0, len(image),
                          60, 0, 0, 0, 0, 0x60000020)
    return header + section + image


def rebuild_executable(original, image, metadata):
    """Rebuild a local debugger copy with both packing stubs removed."""
    old = struct.unpack_from("<14H", original)
    addresses = metadata["relocation_addresses"]
    relocations = b"".join(struct.pack("<HH", address & 15, address >> 4)
                           for address in addresses)
    header_size = (28 + len(relocations) + 15) & ~15
    file_size = header_size + len(image)
    old_allocation = (len(original) - old[4] * 16 + 15) // 16 + old[5]
    image_paragraphs = (len(image) + 15) // 16
    stack_paragraphs = (metadata["stack_ss"] * 16 + metadata["stack_sp"] + 15) // 16
    minimum = max(old_allocation, stack_paragraphs, image_paragraphs) - image_paragraphs
    header = struct.pack("<14H", 0x5A4D, file_size % 512, (file_size + 511) // 512,
                         len(addresses), header_size // 16, minimum, 0xFFFF,
                         metadata["stack_ss"], metadata["stack_sp"], 0,
                         metadata["entry_ip"], metadata["entry_cs"], 28, 0)
    return (header + relocations).ljust(header_size, b"\0") + image


def unpack_cmp(data):
    """Llamatron's LZSS resource stream, traced from GAME86 CS:e6bb."""
    stream = Reader(data)
    dictionary = bytearray(4096)
    write = 0xFEE
    output = bytearray()
    flags = 0
    while len(output) < 1024 * 1024:
        flags >>= 1
        if flags & 0x100 == 0:
            flags = stream.byte() | 0xFF00
        if flags & 1:
            values = [stream.byte()]
            for value in values:
                output.append(value)
                dictionary[write] = value
                write = (write + 1) & 0xFFF
        else:
            low, high = stream.byte(), stream.byte()
            if low == 0 and high == 0:
                marker = stream.byte()
                if marker == 0:
                    if stream.position != len(data):
                        raise ValueError("Trailing data after CMP terminator")
                    return bytes(output)
                stream.position -= 1  # Decoder's single-byte pushback.
            read = low | ((high >> 4) << 8)
            for _ in range((high & 15) + 3):
                value = dictionary[read]
                output.append(value)
                dictionary[write] = value
                write = (write + 1) & 0xFFF
                read = (read + 1) & 0xFFF
    raise ValueError("CMP decompression exceeded size limit")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("game", type=Path)
    parser.add_argument("--output", type=Path, default=Path("artifacts/analysis"))
    args = parser.parse_args()
    if not args.game.is_dir():
        parser.error(f"Not a game directory: {args.game}")
    if args.output.resolve().is_relative_to(args.game.resolve()):
        parser.error("Output must be outside the original game directory")
    args.output.mkdir(parents=True, exist_ok=True)
    report = {"source": str(args.game.resolve()), "files": []}
    for path in sorted(args.game.iterdir()):
        if not path.is_file():
            continue
        data = path.read_bytes()
        item = {"name": path.name, "size": len(data),
                "sha256": hashlib.sha256(data).hexdigest()}
        if data[:2] == b"MZ" and data[28:32] == b"LZ91":
            image, metadata = unpack_lz91(data)
            image, metadata = unpack_exepack(image, metadata)
            item.update(lzexe="0.91", unpacked_size=len(image), **metadata)
            (args.output / f"{path.stem}.image.bin").write_bytes(image)
            (args.output / f"{path.stem}.analysis.obj").write_bytes(coff_wrapper(image))
            (args.output / f"{path.stem}.unpacked.exe").write_bytes(
                rebuild_executable(data, image, metadata))
            strings = [f"{match.start():06x}  {match.group().decode('ascii')}"
                       for match in re.finditer(rb"[\x20-\x7e]{5,}", image)]
            (args.output / f"{path.stem}.strings.txt").write_text(
                "\n".join(strings), encoding="utf-8")
            print(f"{path.name}: {len(data):,} -> {len(image):,} bytes; "
                  f"entry {metadata['entry_cs']:04x}:{metadata['entry_ip']:04x}; "
                  f"{len(metadata['relocation_addresses'])} relocations")
        elif data[:4] == b"FORM":
            item["iff"] = iff_chunks(data)
        elif path.suffix.upper() == ".CMP":
            resource = unpack_cmp(data)
            item["decoded_size"] = len(resource)
            (args.output / f"{path.stem}.decoded.bin").write_bytes(resource)
            print(f"{path.name}: {len(data):,} -> {len(resource):,} resource bytes")
        report["files"].append(item)
    (args.output / "inventory.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"Saved analysis to {args.output}")


if __name__ == "__main__":
    main()
