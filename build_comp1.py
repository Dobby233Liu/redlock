import gzip
import sys
from array import array
from io import BytesIO
from os import chdir
from os.path import dirname

import lief.PE

chdir(dirname(__file__))


def bytes_delta(b_from: bytes, b_to: bytes, out_ofs: int = 0) -> list[tuple[int, int]]:
    if len(b_from) < len(b_to):
        raise ValueError("size of b_from < b_to")
    deltas = []
    for i, (c_from, c_to) in enumerate(zip(b_from, b_to)):
        if c_from != c_to:
            deltas.append((out_ofs + i, c_to))
    return deltas


def export_swap_delta(pe_data: bytes, swaps: list[tuple[bytes, bytes]]) -> list[tuple[int, int]]:
    pe = lief.PE.parse(pe_data)
    if pe is None:
        raise Exception("failed to load dll")

    exports = pe.get_export()
    with BytesIO(pe_data) as pe_f:
        pe_f.seek(pe.rva_to_offset(exports.names_addr_table_rva))
        export_names_rva = array("I")
        export_names_rva.fromfile(pe_f, exports.names_addr_table_cnt)
        if sys.byteorder == "big":
            export_names_rva.byteswap()

        export_names_ptr = sorted(pe.rva_to_offset(ptr) for ptr in export_names_rva)
        export_names = []
        for ptr in export_names_ptr:
            pe_f.seek(ptr)
            name_buf = bytearray()
            while True:
                name_byte = pe_f.read(1)
                if name_byte is None or name_byte == b"\x00":
                    break
                name_buf.extend(name_byte)
            export_names.append(bytes(name_buf))

    deltas = []
    for swap_a, swap_b in swaps:
        swap_order = [
            (swap_a + b"\x00", export_names_ptr[export_names.index(swap_a)]),
            (swap_b + b"\x00", export_names_ptr[export_names.index(swap_b)]),
        ]
        if swap_order[0][1] < swap_order[1][1]:
            swap1, swap1_ptr = swap_order[0]
            swap2, swap2_ptr = swap_order[1]
        else:
            swap2, swap2_ptr = swap_order[0]
            swap1, swap1_ptr = swap_order[1]

        deltas += bytes_delta(swap1, swap2, swap1_ptr)
        deltas += bytes_delta(swap2, swap1, swap2_ptr)

    return deltas


SHSXS_FILES = (r"../obj/X64/shsxs.dll", r"../obj/X86/shsxs.dll", r"../obj/ARM/shsxs.dll")
# two implementations with different signatures; RP_InitLauncherDataLaye0 has a more primitive one
USE_OLD_ILDL_PATCH_EXPORT_SWAPS = (b"RP_InitLauncherDataLayer", b"RP_InitLauncherDataLaye0")

comp_data = []
for file in SHSXS_FILES:
    with open(file, "rb") as inf:
        print(file)

        data = inf.read()
        print(f"Size: {len(data)}")
        comp_data.append(data)

        # at runtime, if both RP_GetLayoutManagerBandDependencies and RP_InitLauncherDataLayer are referenced
        # in system32/twinui.dll (idk how relevant the wow64 one is), the unlocker will apply this extra patch
        # we don't know that ahead of time, so here we just generate deltas
        use_old_ildl_patch_deltas = export_swap_delta(data, [USE_OLD_ILDL_PATCH_EXPORT_SWAPS])
        if len(use_old_ildl_patch_deltas) > 0:
            print("Old ILDL patch:")
            for ptr, char_code in use_old_ildl_patch_deltas:
                print(f"    data[{hex(ptr)}] = {hex(char_code)}")
        print()

print("Creating comp1")
with gzip.open("comp1.bin", "wb") as f:
    f.write(b"".join(comp_data))
