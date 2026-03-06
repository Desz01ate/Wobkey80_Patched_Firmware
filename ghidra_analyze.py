# Ghidra headless script — run via analyzeHeadless
# Searches for the VIA custom channel handler (RGB color/hue logic)
#
# Key insight: we know the handler at ~0x0DC9C does:
#   lbu a4, 2(s1)          # load parameter ID from struct
#   addi a2, zero, 0x12    # max effect = 18
#   lbu a3, 1(a5)          # load value from HID report
#   bltu a2, a4, skip      # if param_id > 18, skip
#
# We want to find all functions that reference this area
# and trace the color (ID=4) handler specifically.

from ghidra.program.model.listing import CodeUnit
from ghidra.program.flatapi import FlatProgramAPI
from ghidra.app.decompiler import DecompInterface

api = FlatProgramAPI(currentProgram)
decomp = DecompInterface()
decomp.openProgram(currentProgram)

listing = currentProgram.getListing()
fm = currentProgram.getFunctionManager()

print("=" * 70)
print("FIRMWARE ANALYSIS — Wobkey Crush 80")
print("=" * 70)

# List all functions found by auto-analysis
print("\n--- All functions ---")
funcs = list(fm.getFunctions(True))
print(f"Total functions: {len(funcs)}")
for fn in funcs:
    print(f"  0x{fn.getEntryPoint().getOffset():05X}  {fn.getName():40s}  size={fn.getBody().getNumAddresses()}")

# Decompile functions near our targets of interest
targets = [
    0x0D5E0,   # near RGB handler
    0x0DCA0,   # effect bounds check (addi a2, zero, 18)
    0x0DC70,   # effect table area
    0x15220,   # VIA dispatch candidate
    0x04488,   # addi x15, x0, 18
    0x03B0C,   # addi x15, x0, 19
]

print("\n--- Decompilation of target functions ---")
for addr_int in targets:
    addr = api.toAddr(addr_int)
    fn = fm.getFunctionContaining(addr)
    if fn is None:
        print(f"\n0x{addr_int:05X}: No function found at this address")
        continue

    entry = fn.getEntryPoint().getOffset()
    print(f"\n{'='*70}")
    print(f"Function at 0x{entry:05X} (contains 0x{addr_int:05X})")
    print(f"Name: {fn.getName()}, Size: {fn.getBody().getNumAddresses()}")
    print(f"{'='*70}")

    result = decomp.decompileFunction(fn, 60, None)
    if result and result.decompileCompleted():
        code = result.getDecompiledFunction().getC()
        # Print first 200 lines
        lines = code.split('\n')
        for line in lines[:200]:
            print(f"  {line}")
        if len(lines) > 200:
            print(f"  ... ({len(lines) - 200} more lines)")
    else:
        print("  (decompilation failed)")

# Search for references to byte comparisons with small constants (VIA cmd IDs)
print("\n--- Searching for functions with multiple VIA-like comparisons ---")
for fn in funcs:
    result = decomp.decompileFunction(fn, 30, None)
    if not result or not result.decompileCompleted():
        continue
    code = result.getDecompiledFunction().getC()
    # Look for functions that compare with 7 and 8 (CUSTOM_SET/GET)
    # or that reference both "color" patterns and effect=18
    has_7 = "== 7" in code or "== 0x7" in code or "!= 7" in code
    has_8 = "== 8" in code or "== 0x8" in code or "!= 8" in code
    has_3 = "== 3" in code or "== 0x3" in code
    has_18 = "== 0x12" in code or "< 0x13" in code or "> 0x12" in code or "== 18" in code
    has_9 = "== 9" in code or "< 10" in code or "< 0xa" in code

    score = sum([has_7, has_8, has_3, has_18, has_9])
    if score >= 3:
        entry = fn.getEntryPoint().getOffset()
        print(f"\n{'='*70}")
        print(f"HIGH SCORE ({score}/5) — Function at 0x{entry:05X} ({fn.getName()})")
        print(f"  has_7={has_7} has_8={has_8} has_3={has_3} has_18={has_18} has_9={has_9}")
        print(f"{'='*70}")
        lines = code.split('\n')
        for line in lines[:300]:
            print(f"  {line}")
        if len(lines) > 300:
            print(f"  ... ({len(lines) - 300} more lines)")

decomp.dispose()
print("\n--- Analysis complete ---")
